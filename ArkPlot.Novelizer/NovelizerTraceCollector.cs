using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArkPlot.Novelizer;

/// <summary>
/// 一次 LLM 调用的完整复盘记录（单轮/多轮 Turn/上下文压缩共用同一结构）。
/// onThought 回调（四参：轮次标签 / 完整输入 prompt / 思考过程 / 输出）的落盘集合。
/// </summary>
public sealed class NovelizerTraceCollector
{
    private readonly List<TurnTrace> _turns = new();
    private readonly object _sync = new();

    /// <summary>当前运行元信息（开始时间、模型，由 BeginRun 设置）。</summary>
    private string _model = "";
    private DateTime _startedAt;
    /// <summary>本次运行的落盘目录（BeginRun 时由调用方传入），每轮 Add 后立即增量重写 trace 文件。</summary>
    private string? _storyOutputDir;

    /// <summary>每次 LLM 调用的结构化记录。</summary>
    public sealed record TurnTrace(
        string Label,            // 例如「第 1/3 章「孤星」思考 Turn 2/5」或「上下文压缩」
        string Prompt,           // 完整输入 prompt（system + user 拼接）
        string Thinking,         // 思考过程（ReasoningContent）
        string Answer,           // 输出（AnswerContent）
        string ChapterTitle,     // 所属章节标题
        bool IsCompress,         // 是否为压缩调用
        DateTime Timestamp,      // 调用发生时间
        string? FilePath = null  // 输出文件路径（非序列化，仅 UI 提示用）
    );

    /// <summary>当前记录的线程安全快照，避免并发章节写入时 UI 枚举原始 List。</summary>
    public IReadOnlyList<TurnTrace> Turns
    {
        get { lock (_sync) return _turns.ToArray(); }
    }

    /// <summary>本次运行的唯一标识（开始时间 yyyyMMdd_HHmmss），作为 turnKey 前缀。</summary>
    public string RunId => _startedAt == default ? "" : _startedAt.ToString("yyyyMMdd_HHmmss");

    /// <summary>
    /// 标记一次运行的开始（清空上次运行的残留 turns，保证一次 Save 对应一次运行）。
    /// 传入 <paramref name="storyOutputDir"/> 后，每轮 Add 都会立即增量重写 trace 文件，
    /// 复盘文件在生成过程中就实时可读（取消/崩溃也不丢已完成轮次）。
    /// </summary>
    public void BeginRun(string model, string? storyOutputDir = null)
    {
        lock (_sync)
        {
            (_model, _startedAt, _storyOutputDir) = (model, DateTime.Now, storyOutputDir);
            _turns.Clear();
        }
    }

    /// <summary>追加一次 LLM 调用记录，返回该记录在本运行内的序号（0 起），供日志与复盘面板定位。</summary>
    public int Add(string label, string prompt, string thinking, string answer, string chapterTitle = "", bool isCompress = false)
    {
        lock (_sync)
        {
            _turns.Add(new TurnTrace(label, prompt, thinking, answer, chapterTitle, isCompress, DateTime.Now));
            FlushCore();
            return _turns.Count - 1;
        }
    }

    /// <summary>
    /// 把当前已收集的 turns 写入 trace 文件（每轮 Add 后调用，实现生成过程中的实时落盘）。
    /// 持有 <see cref="_sync"/> 时调用；落盘失败静默吞掉——trace 是附属产物，不能阻塞生成。
    /// </summary>
    private void FlushCore()
    {
        if (_turns.Count == 0 || string.IsNullOrWhiteSpace(_storyOutputDir))
            return;
        try
        {
            WriteTraceFile();
        }
        catch
        {
            // 目录不可写等 IO 异常不阻塞生成；若后续目录恢复，下一轮 Add / 最终 Save 仍会重试。
        }
    }

    /// <summary>把全部 turns 序列化为 trace 文件内容（持有 <see cref="_sync"/> 时调用）。</summary>
    private void WriteTraceFile()
    {
        var dir = Path.Combine(_storyOutputDir!, "novelizer-traces");
        Directory.CreateDirectory(dir);
        var fullPath = Path.Combine(dir, $"{_startedAt:yyyyMMdd_HHmmss}_novelizer-trace.json");

        var runDoc = new
        {
            model = _model,
            startedAt = _startedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            turnCount = _turns.Count,
            turns = _turns.ToArray(),
        };

        var json = JsonSerializer.Serialize(runDoc, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        File.WriteAllText(fullPath, json);
    }

    /// <summary>
    /// 运行结束：把本次运行的全部调用落盘到 <paramref name="storyOutputDir"/>/novelizer-traces/ 下，
    /// 文件名带时间戳，支持多次运行可回看。与增量落盘写同一个文件（按 RunId 命名），收尾重写一遍。
    /// </summary>
    /// <returns>落盘文件完整路径；无记录或落盘失败（与 <see cref="FlushCore"/> 同策略：trace 是
    /// 附属产物，不能把一次成功的生成报成失败）时返回 null。</returns>
    public string? Save(string storyOutputDir)
    {
        lock (_sync)
        {
            if (_turns.Count == 0 || string.IsNullOrWhiteSpace(storyOutputDir))
                return null;

            _storyOutputDir = storyOutputDir;
            try
            {
                WriteTraceFile();
                return Path.Combine(storyOutputDir, "novelizer-traces", $"{_startedAt:yyyyMMdd_HHmmss}_novelizer-trace.json");
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>读取指定目录下所有历史运行记录（按文件名倒序，最新在前）。</summary>
    public static IReadOnlyList<RunDocument> LoadAll(string storyOutputDir)
    {
        var dir = Path.Combine(storyOutputDir, "novelizer-traces");
        if (!Directory.Exists(dir))
            return Array.Empty<RunDocument>();

        return Directory.GetFiles(dir, "*.json")
            .OrderByDescending(f => f)
            .Select(f => LoadFile(f))
            .Where(doc => doc != null)
            .Select(doc => doc!)
            .ToList();
    }

    private static RunDocument? LoadFile(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<RunDocument>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>一次小说化运行（trace 文件根对象）。</summary>
public sealed class RunDocument
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("startedAt")]
    public string StartedAt { get; set; } = "";

    [JsonPropertyName("turnCount")]
    public int TurnCount { get; set; }

    [JsonPropertyName("turns")]
    public List<NovelizerTraceCollector.TurnTrace> Turns { get; set; } = new();
}
