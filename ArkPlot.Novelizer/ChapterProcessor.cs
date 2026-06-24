namespace ArkPlot.Novelizer;

/// <summary>
/// 章节处理器：负责单章 LLM 调用、并发控制、错误处理
/// </summary>
public class ChapterProcessor
{
    private readonly BailianClient _client;
    private readonly string _systemPrompt;
    private readonly Action<string> _log;
    private readonly Action<string> _logError;
    private readonly int _maxConcurrency;
    private readonly bool _enableMultiTurn;
    private readonly int _chunkSize;
    private readonly int _compressInterval;
    private readonly int _compressThresholdTokens;

    private const string FirstTurnPrefix = "请小说化以下内容：\n\n---\n\n";
    private const string ContinuePrefix = "请继续小说化下一段：\n\n---\n\n";

    private const string CompressPrompt = """
请将以上对话中已生成的小说内容压缩为一份紧凑的情节摘要，要求：

1. 保留所有已出现的角色名称及其关键特征
2. 按顺序列出已发生的场景和关键事件
3. 保留重要的对话要点和情节转折
4. 保留场景转换和时空信息（年代、地点）
5. 压缩后不超过 300 字

这份摘要将作为后续小说生成的上下文参考，不可遗漏重要信息。直接输出摘要，不要任何开场白。
""";

    public ChapterProcessor(
        BailianClient client,
        string systemPrompt,
        Action<string> log,
        Action<string> logError,
        int maxConcurrency = 3,
        bool enableMultiTurn = false,
        int chunkSize = 5_000,
        int compressInterval = 0,
        int compressThresholdTokens = 0)
    {
        _client = client;
        _systemPrompt = systemPrompt;
        _log = log;
        _logError = logError;
        _maxConcurrency = maxConcurrency;
        _enableMultiTurn = enableMultiTurn;
        _chunkSize = chunkSize;
        _compressInterval = compressInterval;
        _compressThresholdTokens = compressThresholdTokens;
    }

    /// <summary>
    /// 并发处理所有章节，返回按索引排序的处理结果
    /// </summary>
    public async Task<IReadOnlyList<ChapterResult>> ProcessAllAsync(
        IReadOnlyList<Chapter> chapters,
        string model,
        CancellationToken ct = default)
    {
        var semaphore = new SemaphoreSlim(_maxConcurrency);
        var results = new Dictionary<int, ChapterResult>();
        var tasks = new List<Task>();
        var tokenTracker = new TokenTracker();

        foreach (var chapter in chapters)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(chapter.Body))
            {
                _log($"⏭️  第 {chapter.Index + 1}/{chapters.Count} 章「{chapter.Title}」无正文，跳过。");
                results[chapter.Index] = ChapterResult.Skipped(chapter.Index, chapter.Title, chapters.Count);
                continue;
            }

            tasks.Add(ProcessChapterAsync(chapter, chapters.Count, model, semaphore, results, tokenTracker, ct));
        }

        await Task.WhenAll(tasks);
        ct.ThrowIfCancellationRequested();

        return results.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
    }

    private async Task ProcessChapterAsync(
        Chapter chapter,
        int totalCount,
        string model,
        SemaphoreSlim semaphore,
        Dictionary<int, ChapterResult> results,
        TokenTracker tokenTracker,
        CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            // 多轮模式 && 正文超过 chunkSize → 走多轮路径
            if (_enableMultiTurn && chapter.Body.Length > _chunkSize)
            {
                await ProcessChapterMultiTurnAsync(chapter, totalCount, model, results, tokenTracker);
                return;
            }

            _log($"\n--- 第 {chapter.Index + 1}/{totalCount} 章: {chapter.Title} ({chapter.Body.Length} 字符) ---");
            _log($"[DIAG] 即将调用 ChatAsync for 第 {chapter.Index + 1} 章「{chapter.Title}」");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var chatResult = await _client.ChatAsync(model, _systemPrompt, chapter.Body);
                sw.Stop();
                _log($"[DIAG] ChatAsync 返回，耗时 {sw.Elapsed.TotalSeconds:F1}s");

                var strippedContent = ChapterSplitter.StripHeadings(chatResult.AnswerContent);
                results[chapter.Index] = ChapterResult.FromSuccess(
                    chapter.Index,
                    chapter.Title,
                    strippedContent,
                    totalCount);

                if (chatResult.Usage is not null)
                {
                    tokenTracker.Add(chatResult.Usage.PromptTokens, chatResult.Usage.CompletionTokens);
                    _log($"✅ Token: 入 {chatResult.Usage.PromptTokens} / 出 {chatResult.Usage.CompletionTokens}");
                    _log($"[DIAG] 第 {chapter.Index + 1} 章 token: prompt={chatResult.Usage.PromptTokens}, completion={chatResult.Usage.CompletionTokens}");
                }
            }
            catch (BailianException ex)
            {
                sw.Stop();
                _log($"[DIAG] ChatAsync 抛出 BailianException（{sw.Elapsed.TotalSeconds:F1}s）: {ex.Message}");
                _logError($"❌ 第 {chapter.Index + 1} 章失败: {ex.Message}");
                results[chapter.Index] = ChapterResult.FromFailure(chapter.Index, chapter.Title, ex.Message, totalCount);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// 多轮对话处理：拆 chunk → 逐轮带完整历史调用 LLM → 拼接结果
    /// </summary>
    private async Task ProcessChapterMultiTurnAsync(
        Chapter chapter,
        int totalCount,
        string model,
        Dictionary<int, ChapterResult> results,
        TokenTracker tokenTracker)
    {
        _log($"\n--- 🔄 第 {chapter.Index + 1}/{totalCount} 章（多轮）: {chapter.Title} ({chapter.Body.Length} 字符) ---");
        _log($"[DIAG] 拆块中 (chunkSize={_chunkSize})...");

        var chunks = ChapterChunker.ChunkChapter(chapter.Body, _chunkSize);
        _log($"[DIAG] 拆为 {chunks.Count} 个 chunk");

        if (chunks.Count <= 1)
        {
            // 拆完只剩一块 → 兜底走单次调用
            _log("  ↪ 仅一块，降级为单次调用");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var chatResult = await _client.ChatAsync(model, _systemPrompt, chapter.Body);
                sw.Stop();
                var strippedContent = ChapterSplitter.StripHeadings(chatResult.AnswerContent);
                results[chapter.Index] = ChapterResult.FromSuccess(chapter.Index, chapter.Title, strippedContent, totalCount);
                if (chatResult.Usage is not null)
                {
                    tokenTracker.Add(chatResult.Usage.PromptTokens, chatResult.Usage.CompletionTokens);
                }
            }
            catch (BailianException ex)
            {
                _logError($"❌ 第 {chapter.Index + 1} 章（多轮降级）失败: {ex.Message}");
                results[chapter.Index] = ChapterResult.FromFailure(chapter.Index, chapter.Title, ex.Message, totalCount);
            }
            return;
        }

        // 多轮对话：构建历史，逐轮调用，必要时压缩
        var history = new List<ChatMessage> { new("system", _systemPrompt) };
        var turnOutputs = new List<string>();
        var totalPromptTokens = 0;
        var totalCompletionTokens = 0;
        var overallSw = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < chunks.Count; i++)
        {
            // 检查是否需要压缩历史（compressInterval > 0 且已处理 compressInterval 轮之后）
            bool shouldCompress = _compressInterval > 0
                && i > 0
                && i % _compressInterval == 0
                && i < chunks.Count - 1; // 最后一轮不压缩

            if (shouldCompress)
            {
                _log($"\n  --- 🔄 上下文压缩 (第 {i + 1} 轮前) ---");
                try
                {
                    var compressSw = System.Diagnostics.Stopwatch.StartNew();
                    var compressResult = await _client.ChatWithHistoryAsync(model, history);
                    compressSw.Stop();

                    var compressed = ChapterSplitter.StripHeadings(compressResult.AnswerContent);
                    _log($"  压缩完成: {compressed.Length} 字符, 耗时 {compressSw.Elapsed.TotalSeconds:F1}s");
                    _log($"  📋 压缩摘要:\n{compressed}");

                    if (compressResult.Usage is not null)
                    {
                        totalPromptTokens += compressResult.Usage.PromptTokens;
                        totalCompletionTokens += compressResult.Usage.CompletionTokens;
                    }

                    // 重置历史：system prompt + 压缩摘要作为 system 上下文
                    history.Clear();
                    history.Add(new ChatMessage("system", _systemPrompt));
                    history.Add(new ChatMessage("system", $"此前已生成的小说情节摘要：\n{compressed}"));
                }
                catch (BailianException ex)
                {
                    _logError($"  ⚠️ 压缩失败: {ex.Message}，继续使用未压缩历史");
                }
            }

            var prefix = i == 0 ? FirstTurnPrefix : ContinuePrefix;
            var userMsg = prefix + chunks[i];
            var turnLabel = $"Turn {i + 1}/{chunks.Count}";

            _log($"\n  --- {turnLabel} ({chunks[i].Length} 字符, history={history.Count} 条消息) ---");

            history.Add(new ChatMessage("user", userMsg));

            var turnSw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var chatResult = await _client.ChatWithHistoryAsync(model, history);
                turnSw.Stop();

                var answer = ChapterSplitter.StripHeadings(chatResult.AnswerContent);
                turnOutputs.Add(answer);

                if (chatResult.Usage is not null)
                {
                    totalPromptTokens += chatResult.Usage.PromptTokens;
                    totalCompletionTokens += chatResult.Usage.CompletionTokens;
                    _log($"  ✅ {turnLabel}: 入 {chatResult.Usage.PromptTokens} / 出 {chatResult.Usage.CompletionTokens}，耗时 {turnSw.Elapsed.TotalSeconds:F1}s");
                }
                else
                {
                    _log($"  ✅ {turnLabel}: 耗时 {turnSw.Elapsed.TotalSeconds:F1}s");
                }

                // FullHistory 模式：将 assistant 回复追加到历史
                history.Add(new ChatMessage("assistant", answer));
            }
            catch (BailianException ex)
            {
                turnSw.Stop();
                _logError($"  ❌ {turnLabel} 失败: {ex.Message}");
                // 某轮失败，用占位符继续
                turnOutputs.Add($"\n> *（第 {i + 1} 段生成失败：{ex.Message}）*");
            }
        }

        overallSw.Stop();
        tokenTracker.Add(totalPromptTokens, totalCompletionTokens);

        var combinedContent = string.Join("\n\n", turnOutputs);
        results[chapter.Index] = ChapterResult.FromSuccess(
            chapter.Index,
            chapter.Title,
            combinedContent,
            totalCount);

        _log($"\n  ✅ 多轮完成: {chunks.Count} 轮, {overallSw.Elapsed.TotalSeconds:F1}s 总耗时, 入 {totalPromptTokens} / 出 {totalCompletionTokens} tokens");
    }
}

/// <summary>
/// 单章处理结果
/// </summary>
public record ChapterResult
{
    public int Index { get; init; }
    public string Title { get; init; } = "";
    public string Content { get; init; } = "";
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public int TotalChapters { get; init; }

    public static ChapterResult Skipped(int index, string title, int total) => new()
    {
        Index = index,
        Title = title,
        Content = $"## {title}\n\n> *（本章无正文）*",
        IsSuccess = true,
        TotalChapters = total
    };

    public static ChapterResult FromSuccess(int index, string title, string content, int total) => new()
    {
        Index = index,
        Title = title,
        Content = $"## {title}\n\n{content}",
        IsSuccess = true,
        TotalChapters = total
    };

    public static ChapterResult FromFailure(int index, string title, string error, int total) => new()
    {
        Index = index,
        Title = title,
        Content = $"## {title}\n\n> *（本章生成失败：{error}）*",
        IsSuccess = false,
        ErrorMessage = error,
        TotalChapters = total
    };
}

/// <summary>
/// Token 统计追踪器（线程安全）
/// </summary>
public class TokenTracker
{
    private readonly object _lock = new();
    public int TotalPromptTokens { get; private set; }
    public int TotalCompletionTokens { get; private set; }

    public void Add(int prompt, int completion)
    {
        lock (_lock)
        {
            TotalPromptTokens += prompt;
            TotalCompletionTokens += completion;
        }
    }
}
