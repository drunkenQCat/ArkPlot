using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArkPlot.Core.Infrastructure;
using ArkPlot.Core.Model;
using SqlSugar;

namespace ArkPlot.Tts.Alignment;

/// <summary>
/// 将小说化文本中的对话与原始 FormattedTextEntry 按顺序对齐。
///
/// 核心假设：LLM 小说化时保持对话顺序不变，
/// 因此小说中引号内对话的出现顺序 == DB 中 Dialog 字段的顺序。
/// </summary>
public class NovelAligner
{
    private const int CurrentAlignmentCacheVersion = 8;
    private readonly SqlSugarClient _db;
    private readonly GenderOverrideProvider? _genderOverrides;

    public NovelAligner(GenderOverrideProvider? genderOverrides = null)
        : this(DbFactory.GetClient(), genderOverrides) { }

    public NovelAligner(SqlSugarClient db, GenderOverrideProvider? genderOverrides = null)
    {
        _db = db;
        _genderOverrides = genderOverrides;
    }

    /// <summary>
    /// 从小说文件名推断活动名，执行完整对齐。
    /// 文件名格式：{活动名}_novel_{model}.md
    /// 结果缓存到 {cacheDirOverride ?? 文件目录}/_align_cache/{contentHash}.json
    /// </summary>
    /// <param name="novelFilePath">小说 md 文件路径。</param>
    /// <param name="cacheDirOverride">
    /// 自定义对齐缓存目录。若为 null 则默认使用小说文件所在目录下的 _align_cache。
    /// GUI 场景通常传 TtsOutputDir/_align_cache 以集中管理缓存。
    /// </param>
    public async Task<(List<AlignmentEntry> Entries, AlignmentStats Stats)> AlignByFileNameAsync(
        string novelFilePath, string? cacheDirOverride = null)
    {
        var fileName = Path.GetFileNameWithoutExtension(novelFilePath);
        var actName = ExtractActName(fileName);
        var novelText = await File.ReadAllTextAsync(novelFilePath);

        // 离线缓存：按文件内容哈希缓存对齐结果
        var cacheDir = cacheDirOverride
            ?? Path.Combine(Path.GetDirectoryName(novelFilePath) ?? ".", "_align_cache");
        var contentHash = Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes(novelText))).ToLowerInvariant();
        var cacheFile = Path.Combine(cacheDir, $"{contentHash}.json");

        if (File.Exists(cacheFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(cacheFile);
                var cached = JsonSerializer.Deserialize<AlignmentCacheEntry>(json);
                if (cached != null
                    && cached.Version == CurrentAlignmentCacheVersion
                    && cached.Entries.Count > 0)
                    return (cached.Entries, cached.Stats);
            }
            catch { /* 缓存损坏，重新对齐 */ }
        }

        var result = await AlignAsync(novelText, actName);

        // 写入缓存
        try
        {
            Directory.CreateDirectory(cacheDir);
            var cacheEntry = new AlignmentCacheEntry(CurrentAlignmentCacheVersion, result.Entries, result.Stats);
            var cacheJson = JsonSerializer.Serialize(cacheEntry, new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            await File.WriteAllTextAsync(cacheFile, cacheJson);
        }
        catch { /* 缓存写入失败不影响主流程 */ }

        return result;
    }

    /// <summary>
    /// 执行对齐：小说文本 + 活动名 → 对齐结果。
    /// </summary>
    public async Task<(List<AlignmentEntry> Entries, AlignmentStats Stats)> AlignAsync(
        string novelText, string actName)
    {
        var novelChapters = DialogExtractor.ExtractChapters(novelText);
        var (plots, entriesByPlot, allEntriesByPlot) = await LoadActDataAsync(actName);
        var nameToCode = BuildNameToCodeMap(allEntriesByPlot);
        var charCodeAtEntry = BuildCharCodeAtEntry(allEntriesByPlot, nameToCode);
        var picDescByCode = await LoadPicDescMapAsync();
        return AlignChapters(novelChapters, plots, entriesByPlot, charCodeAtEntry, picDescByCode, _genderOverrides);
    }

    private async Task<(List<Plot> Plots,
        Dictionary<long, List<FormattedTextEntry>> EntriesByPlot,
        Dictionary<long, List<FormattedTextEntry>> AllEntriesByPlot)>
        LoadActDataAsync(string actName)
    {
        var act = await _db.Queryable<Act>()
            .FirstAsync(a => a.Name == actName && a.Lang == "zh_CN");

        if (act == null)
            throw new InvalidOperationException($"活动 '{actName}' 未找到。请先运行 CLI 生成原始 Markdown。");

        var plots = await _db.Queryable<Plot>()
            .Where(p => p.ActId == act.Id && p.StoryChapterId > 0)
            .ToListAsync();

        var plotIds = plots.Select(p => p.Id).ToList();
        var allEntriesRaw = await _db.Queryable<FormattedTextEntry>()
            .Where(e => plotIds.Contains(e.PlotId))
            .OrderBy(e => e.PlotId)
            .OrderBy(e => e.Index)
            .ToListAsync();

        var entriesByPlot = new Dictionary<long, List<FormattedTextEntry>>();
        var allEntriesByPlot = new Dictionary<long, List<FormattedTextEntry>>();

        foreach (var group in allEntriesRaw.GroupBy(e => e.PlotId))
        {
            var sorted = group.OrderBy(e => e.Index).ToList();
            entriesByPlot[group.Key] = sorted.Where(e => !string.IsNullOrEmpty(e.Dialog)).ToList();
            allEntriesByPlot[group.Key] = sorted;
        }

        return (plots, entriesByPlot, allEntriesByPlot);
    }

    internal static Dictionary<string, string> BuildNameToCodeMap(
        Dictionary<long, List<FormattedTextEntry>> allEntriesByPlot)
    {
        var nameToCode = new Dictionary<string, string>();

        foreach (var (_, entries) in allEntriesByPlot)
        {
            FormattedTextEntry? lastCharSlot = null;
            foreach (var entry in entries)
            {
                if (IsCharacterSlotEntry(entry))
                {
                    lastCharSlot = IsEffectiveCharSlot(entry) ? entry : null;
                    continue;
                }

                if (string.IsNullOrEmpty(entry.Dialog) || string.IsNullOrEmpty(entry.CharacterName))
                    continue;

                if (lastCharSlot != null && !nameToCode.ContainsKey(entry.CharacterName))
                {
                    var code = ExtractCodeFromCharSlot(lastCharSlot);
                    if (code != null)
                        nameToCode[entry.CharacterName] = code;
                }
                lastCharSlot = null;
            }
        }

        return nameToCode;
    }

    internal static bool IsCharacterSlotEntry(FormattedTextEntry entry)
    {
        return string.Equals(entry.Type, "charslot", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Type, "character", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsEffectiveCharSlot(FormattedTextEntry entry)
    {
        if (entry.CommandSet == null || entry.CommandSet.Count == 0)
            return false;
        if (!entry.CommandSet.ContainsKey("name"))
            return false;
        var focus = entry.CommandSet.TryGetValue("focus", out var f) ? f : null;
        return focus != "none" && focus != "-1";
    }

    internal static Dictionary<(long PlotId, int Index), string?> BuildCharCodeAtEntry(
        Dictionary<long, List<FormattedTextEntry>> allEntriesByPlot,
        Dictionary<string, string> nameToCode)
    {
        var charCodeAtEntry = new Dictionary<(long PlotId, int Index), string?>();

        foreach (var (plotId, entries) in allEntriesByPlot)
        {
            string? lastCharSlotCode = null;
            foreach (var entry in entries)
            {
                if (IsCharacterSlotEntry(entry))
                {
                    lastCharSlotCode = IsEffectiveCharSlot(entry) ? ExtractCodeFromCharSlot(entry) : null;
                    continue;
                }

                if (string.IsNullOrEmpty(entry.Dialog))
                    continue;

                var nameCode = !string.IsNullOrEmpty(entry.CharacterName)
                    ? nameToCode.GetValueOrDefault(entry.CharacterName)
                    : null;
                charCodeAtEntry[(plotId, entry.Index)] = nameCode ?? lastCharSlotCode;
            }
        }

        return charCodeAtEntry;
    }

    private async Task<Dictionary<string, string>> LoadPicDescMapAsync()
    {
        var allPicDescs = await _db.Queryable<PicDescription>().ToListAsync();
        return allPicDescs
            .Where(p => !string.IsNullOrEmpty(p.DedupKey))
            .ToDictionary(p => p.DedupKey, p => p.PicDesc);
    }

    // ── 锚点 + 窗口对齐 配置 ──
    internal const int WindowSize = 5;
    internal const double WindowMatchThreshold = 0.4;
    internal const double NarratorMatchThreshold = 0.15;

    private static (List<AlignmentEntry> Entries, AlignmentStats Stats) AlignChapters(
        List<NovelChapter> novelChapters,
        List<Plot> plots,
        Dictionary<long, List<FormattedTextEntry>> entriesByPlot,
        Dictionary<(long PlotId, int Index), string?> charCodeAtEntry,
        Dictionary<string, string> picDescByCode,
        GenderOverrideProvider? genderOverrides = null)
    {
        var results = new List<AlignmentEntry>();
        int totalDialogs = 0;
        int alignedDialogs = 0;
        int anchorMatches = 0;
        int windowMatches = 0;
        int matchedChapters = 0;

        foreach (var novelChapter in novelChapters)
        {
            try
            {
            var plot = plots.FirstOrDefault(p =>
                p.Title.Contains(novelChapter.Title) || novelChapter.Title.Contains(p.Title));
            if (plot == null) continue;
            matchedChapters++;

            if (!entriesByPlot.TryGetValue(plot.Id, out var plotEntries)) continue;

            var dialogs = novelChapter.Segments.Where(s => s.IsDialog).ToList();
            totalDialogs += dialogs.Count;

            // Phase 1: 找锚点（高置信度文本匹配）
            var anchors = FindAnchors(dialogs, plotEntries);
            anchorMatches += anchors.Count;

            // Phase 2: 构建对齐映射（novel dialog idx → DB entry idx）
            var alignmentMap = new Dictionary<int, int>();
            foreach (var (ni, di) in anchors)
            {
                if (di < 0 || di >= plotEntries.Count)
                    throw new IndexOutOfRangeException(
                        $"Anchor DbIdx out of range: di={di}, plotEntries.Count={plotEntries.Count}, " +
                        $"chapter='{novelChapter.Title}', plot='{plot.Title}', plotId={plot.Id}");
                alignmentMap[ni] = di;
            }

            // Phase 3: 锚点间的窗口匹配
            var anchorBounds = new List<(int Ni, int Di)> { (-1, -1) };
            anchorBounds.AddRange(anchors);
            anchorBounds.Add((dialogs.Count, plotEntries.Count));

            for (int k = 0; k < anchorBounds.Count - 1; k++)
            {
                var (prevNi, prevDi) = anchorBounds[k];
                var (nextNi, nextDi) = anchorBounds[k + 1];

                int nGap = nextNi - prevNi - 1;
                int dGap = nextDi - prevDi - 1;
                if (nGap <= 0 || dGap <= 0) continue;

                int dbCursor = 0;
                for (int i = 0; i < nGap; i++)
                {
                    int novelIdx = prevNi + 1 + i;
                    var novelNorm = NormalizeLoose(dialogs[novelIdx].Text);
                    if (string.IsNullOrEmpty(novelNorm)) continue;

                    int expectedPos = dGap > 1 ? (int)((long)i * dGap / nGap) : 0;
                    int searchStart = Math.Max(dbCursor, expectedPos - WindowSize);
                    int searchEnd = Math.Min(dGap - 1, expectedPos + WindowSize);

                    int bestJ = -1;
                    double bestScore = 0;

                    for (int j = searchStart; j <= searchEnd; j++)
                    {
                        int dbIdx = prevDi + 1 + j;
                        if (dbIdx < 0 || dbIdx >= plotEntries.Count)
                            throw new IndexOutOfRangeException(
                                $"Window dbIdx out of range: dbIdx={dbIdx}, plotEntries.Count={plotEntries.Count}, " +
                                $"j={j}, prevDi={prevDi}, nextDi={nextDi}, dGap={dGap}, " +
                                $"chapter='{novelChapter.Title}', plot='{plot.Title}', plotId={plot.Id}");
                        var dbNorm = NormalizeLoose(plotEntries[dbIdx].Dialog ?? "");
                        if (string.IsNullOrEmpty(dbNorm)) continue;

                        double score = ComputeSimilarity(novelNorm, dbNorm);
                        if (score > bestScore && score >= WindowMatchThreshold)
                        {
                            bestJ = j;
                            bestScore = score;
                        }
                    }

                    if (bestJ >= 0)
                    {
                        alignmentMap[novelIdx] = prevDi + 1 + bestJ;
                        windowMatches++;
                        dbCursor = bestJ + 1;
                    }
                }
            }

            // Phase 3.5: 修复被旁白切断的对话碎片
            // 1) 未对齐碎片 → 检查是否为相邻 DB entry 的子串
            // 2) 已对齐的短对话 → 检查是否被错配（应为相邻 DB entry 的一部分）
            int mergeMatches = 0;
            for (int di = 0; di < dialogs.Count; di++)
            {
                var novelNorm = NormalizeLoose(dialogs[di].Text);
                if (string.IsNullOrEmpty(novelNorm)) continue;
                bool wasAligned = alignmentMap.ContainsKey(di);
                int currentDbIdx = wasAligned ? alignmentMap[di] : -1;

                // ── 检查 1: 碎片是否为前一个已对齐 DB entry 的子串 ──
                if (di > 0 && alignmentMap.TryGetValue(di - 1, out int prevDbIdx)
                    && prevDbIdx != currentDbIdx)
                {
                    var prevDbNorm = NormalizeLoose(plotEntries[prevDbIdx].Dialog ?? "");
                    if (!string.IsNullOrEmpty(prevDbNorm) && prevDbNorm.Contains(novelNorm))
                    {
                        alignmentMap[di] = prevDbIdx;
                        mergeMatches++;
                        continue;
                    }
                }

                // ── 检查 2: 碎片是否为后一个已对齐 DB entry 的子串 ──
                if (di + 1 < dialogs.Count && alignmentMap.TryGetValue(di + 1, out int nextDbIdx)
                    && nextDbIdx != currentDbIdx)
                {
                    var nextDbNorm = NormalizeLoose(plotEntries[nextDbIdx].Dialog ?? "");
                    if (!string.IsNullOrEmpty(nextDbNorm) && nextDbNorm.Contains(novelNorm))
                    {
                        alignmentMap[di] = nextDbIdx;
                        mergeMatches++;
                        continue;
                    }
                }

                // ── 检查 3（仅未对齐）: 合并搜索 ──
                if (!wasAligned && novelNorm.Length <= 30
                    && di > 0 && alignmentMap.TryGetValue(di - 1, out int prevDbIdx2))
                {
                    var mergedNorm = NormalizeLoose(dialogs[di - 1].Text + dialogs[di].Text);
                    if (TryFindMergedMatch(mergedNorm, prevDbIdx2, plotEntries, out int mergedDbIdx))
                    {
                        alignmentMap[di] = mergedDbIdx;
                        mergeMatches++;
                    }
                }
            }

            if (mergeMatches > 0)
                Console.WriteLine($"[Phase3.5] 碎片修复: {mergeMatches} 条 ({novelChapter.Title})");

            // Phase 4: 构建结果
            var chapterStart = results.Count;
            int dialogIdx = 0;
            foreach (var segment in novelChapter.Segments)
            {
                if (!segment.IsDialog)
                {
                    results.Add(MakeNarrationEntry(segment, novelChapter.Title));
                    continue;
                }

                if (alignmentMap.TryGetValue(dialogIdx, out int dbEntryIdx))
                {
                    if (dbEntryIdx < 0 || dbEntryIdx >= plotEntries.Count)
                        throw new IndexOutOfRangeException(
                            $"Phase4 dbEntryIdx out of range: dbEntryIdx={dbEntryIdx}, plotEntries.Count={plotEntries.Count}, " +
                            $"dialogIdx={dialogIdx}, chapter='{novelChapter.Title}', plot='{plot.Title}', plotId={plot.Id}");
                    alignedDialogs++;
                    results.Add(MakeAlignedDialogEntry(
                        segment, plotEntries[dbEntryIdx], plot.Id, novelChapter.Title,
                        charCodeAtEntry, picDescByCode, genderOverrides));
                }
                else
                {
                    results.Add(MakeUnalignedDialogEntry(segment, novelChapter.Title));
                }

                dialogIdx++;
            }

            // Phase 5: 旁白对齐 + 继承兜底
            // ── Step 5a: 用 BigramJaccard 匹配旁白到 DB 旁白条目 ──
            var dbNarrators = plotEntries
                .Where(e => string.IsNullOrEmpty(e.CharacterName) && !string.IsNullOrEmpty(e.Dialog))
                .ToList();

            if (dbNarrators.Count > 0)
            {
                // 收集围栏柱（已对齐的对话 EntryIndex，按在 results 中的位置排序）
                var fences = new List<(int ResultIdx, int EntryIdx)>();
                for (int i = chapterStart; i < results.Count; i++)
                {
                    if (results[i].IsDialog && results[i].EntryIndex >= 0)
                        fences.Add((i, results[i].EntryIndex));
                }

                // 构建区间：(leftFenceResultIdx, rightFenceResultIdx, leftEntryIdx, rightEntryIdx)
                // 每个区间内的旁白只能匹配该区间内的 DB 旁白条目
                var intervals = new List<(int LeftRes, int RightRes, int LeftEntry, int RightEntry)>();
                int prevResIdx = chapterStart - 1;
                int prevEntryIdx = -1;
                foreach (var (resIdx, entryIdx) in fences)
                {
                    intervals.Add((prevResIdx, resIdx, prevEntryIdx, entryIdx));
                    prevResIdx = resIdx;
                    prevEntryIdx = entryIdx;
                }
                intervals.Add((prevResIdx, results.Count, prevEntryIdx, int.MaxValue));

                foreach (var (leftRes, rightRes, leftEntry, rightEntry) in intervals)
                {
                    // 该区间内的 DB 旁白条目
                    var candidates = dbNarrators
                        .Where(e => e.Index > leftEntry && e.Index < rightEntry)
                        .ToList();
                    if (candidates.Count == 0) continue;

                    // 该区间内的旁白 results
                    int cursor = 0;
                    int lastMatchedCursor = -1;

                    for (int i = leftRes + 1; i < rightRes; i++)
                    {
                        if (results[i].IsDialog || results[i].EntryIndex >= 0) continue;

                        var narratorNorm = NormalizeLoose(results[i].NovelText ?? "");
                        if (string.IsNullOrEmpty(narratorNorm)) continue;

                        double bestScore = 0;
                        FormattedTextEntry? bestEntry = null;
                        int bestCursor = cursor;

                        for (int ci = cursor; ci < candidates.Count; ci++)
                        {
                            var entryNorm = NormalizeLoose(candidates[ci].Dialog ?? "");
                            var score = BigramJaccard(narratorNorm, entryNorm);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestEntry = candidates[ci];
                                bestCursor = ci;
                            }
                        }

                        // 允许匹配上一句相同的 DB 条目（多句共享同一 entry）
                        if (lastMatchedCursor >= 0 && lastMatchedCursor < candidates.Count)
                        {
                            var prevNorm = NormalizeLoose(candidates[lastMatchedCursor].Dialog ?? "");
                            var prevScore = BigramJaccard(narratorNorm, prevNorm);
                            if (prevScore > bestScore)
                            {
                                bestScore = prevScore;
                                bestEntry = candidates[lastMatchedCursor];
                                bestCursor = lastMatchedCursor;
                            }
                        }

                        if (bestScore >= NarratorMatchThreshold && bestEntry != null)
                        {
                            cursor = bestCursor;
                            lastMatchedCursor = bestCursor;
                            results[i] = results[i] with { EntryIndex = bestEntry.Index };
                        }
                    }
                }
            }

            // ── Step 5b: 前向继承（匹配不上的走继承兜底）──
            {
                int lastIdx = -1;
                for (int i = chapterStart; i < results.Count; i++)
                {
                    if (results[i].EntryIndex >= 0)
                        lastIdx = results[i].EntryIndex;
                    else if (lastIdx >= 0)
                        results[i] = results[i] with { EntryIndex = lastIdx };
                }
            }

            // ── Step 5c: 后向继承（章节开头的 -1）──
            {
                int nextIdx = -1;
                for (int i = results.Count - 1; i >= chapterStart; i--)
                {
                    if (results[i].EntryIndex >= 0)
                        nextIdx = results[i].EntryIndex;
                    else if (nextIdx >= 0 && results[i].EntryIndex < 0)
                        results[i] = results[i] with { EntryIndex = nextIdx };
                }
            }
            }
            catch (IndexOutOfRangeException ex)
            {
                throw new InvalidOperationException(
                    $"AlignChapters failed at chapter '{novelChapter.Title}': {ex.Message}", ex);
            }
        }

        var stats = new AlignmentStats(
            TotalNovelChapters: novelChapters.Count,
            MatchedChapters: matchedChapters,
            TotalDialogs: totalDialogs,
            AlignedDialogs: alignedDialogs,
            UnalignedDialogs: totalDialogs - alignedDialogs,
            AnchorMatches: anchorMatches,
            WindowMatches: windowMatches);

        return (results, stats);
    }

    // ── 锚点查找 ──

    internal static List<(int NovelIdx, int DbIdx)> FindAnchors(
        List<NovelSegment> novelDialogs,
        List<FormattedTextEntry> dbEntries)
    {
        var anchors = new List<(int, int)>();
        int dbCursor = 0;

        for (int ni = 0; ni < novelDialogs.Count; ni++)
        {
            var novelNorm = NormalizeStrict(novelDialogs[ni].Text);
            if (novelNorm.Length < 3) continue;

            for (int di = dbCursor; di < dbEntries.Count; di++)
            {
                var dbText = dbEntries[di].Dialog ?? "";
                var dbNorm = NormalizeStrict(dbText);
                if (dbNorm.Length < 3) continue;

                if (IsAnchorMatch(novelNorm, dbNorm))
                {
                    anchors.Add((ni, di));
                    dbCursor = di + 1;
                    break;
                }
            }
        }

        return anchors;
    }

    // ── 文本标准化 ──

    internal static string NormalizeStrict(string text)
    {
        var s = DialogExtractor.Normalize(text);
        s = s.Replace('，', ',').Replace('。', '.').Replace('！', '!').Replace('？', '?');
        s = s.Replace('；', ';').Replace('：', ':').Replace('、', ',');
        s = s.Replace('\u201C', '"').Replace('\u201D', '"');
        s = s.Replace('\u2018', '\'').Replace('\u2019', '\'');
        return s.Trim();
    }

    internal static string NormalizeLoose(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    internal static bool IsAnchorMatch(string novelNorm, string dbNorm)
    {
        if (novelNorm == dbNorm) return true;

        int minLen = Math.Min(novelNorm.Length, dbNorm.Length);
        if (minLen < 4) return false;

        if (novelNorm.Contains(dbNorm) || dbNorm.Contains(novelNorm))
        {
            double ratio = (double)minLen / Math.Max(novelNorm.Length, dbNorm.Length);
            return ratio >= 0.7;
        }

        return false;
    }

    internal static double ComputeSimilarity(string a, string b)
    {
        if (a == b) return 1.0;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;

        if (a.Contains(b)) return (double)b.Length / a.Length;
        if (b.Contains(a)) return (double)a.Length / b.Length;

        int commonLen = 0;
        int maxLen = Math.Min(a.Length, b.Length);
        while (commonLen < maxLen && a[commonLen] == b[commonLen])
            commonLen++;

        return (double)commonLen / Math.Max(a.Length, b.Length);
    }

    /// <summary>
    /// 在前一个 DB entry 附近搜索合并后的文本是否匹配。
    /// </summary>
    private static bool TryFindMergedMatch(
        string mergedNorm, int prevDbIdx, List<FormattedTextEntry> plotEntries,
        out int matchedDbIdx)
    {
        matchedDbIdx = -1;
        // 在前一个 DB entry 及其后续几个 entry 中搜索
        for (int offset = 0; offset <= 3; offset++)
        {
            int candidateIdx = prevDbIdx + offset;
            if (candidateIdx >= plotEntries.Count) break;
            var dbNorm = NormalizeLoose(plotEntries[candidateIdx].Dialog ?? "");
            if (string.IsNullOrEmpty(dbNorm)) continue;

            double score = ComputeSimilarity(mergedNorm, dbNorm);
            if (score >= WindowMatchThreshold)
            {
                matchedDbIdx = candidateIdx;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Character bigram Jaccard 相似度。对句首改写免疫，适合旁白匹配。
    /// </summary>
    internal static double BigramJaccard(string a, string b)
    {
        if (a.Length < 2 || b.Length < 2) return 0;

        var setA = new HashSet<string>();
        for (int i = 0; i < a.Length - 1; i++)
            setA.Add(a[i..(i + 2)]);

        var setB = new HashSet<string>();
        for (int i = 0; i < b.Length - 1; i++)
            setB.Add(b[i..(i + 2)]);

        int intersection = 0;
        foreach (var bg in setA)
            if (setB.Contains(bg)) intersection++;

        int union = setA.Count + setB.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static AlignmentEntry MakeNarrationEntry(NovelSegment segment, string chapterTitle)
        => new(segment.Text, false, null, null, -1, chapterTitle, null);

    private static AlignmentEntry MakeUnalignedDialogEntry(NovelSegment segment, string chapterTitle)
        => new(segment.Text, true, null, null, -1, chapterTitle, null);

    private static AlignmentEntry MakeAlignedDialogEntry(
        NovelSegment segment, FormattedTextEntry entry, long plotId, string chapterTitle,
        Dictionary<(long PlotId, int Index), string?> charCodeAtEntry,
        Dictionary<string, string> picDescByCode,
        GenderOverrideProvider? genderOverrides = null)
    {
        segment.CharacterName = entry.CharacterName;
        segment.EntryIndex = entry.Index;

        var effectiveCode = charCodeAtEntry.GetValueOrDefault((plotId, entry.Index))
                            ?? entry.CharacterCode;
        segment.CharacterCode = effectiveCode;

        var gender = InferGender(effectiveCode, picDescByCode, genderOverrides, entry.CharacterName);
        return new AlignmentEntry(
            segment.Text, true,
            entry.CharacterName, effectiveCode,
            entry.Index, chapterTitle, gender, entry.Portraits);
    }

    internal static string ExtractActName(string fileNameWithoutExt)
    {
        var novelIdx = fileNameWithoutExt.IndexOf("_novel_", StringComparison.Ordinal);
        if (novelIdx > 0)
            return fileNameWithoutExt[..novelIdx];
        return fileNameWithoutExt;
    }

    internal static string? ExtractCodeFromCharSlot(FormattedTextEntry entry)
    {
        if (entry.CommandSet == null || entry.CommandSet.Count == 0)
            return entry.CharacterCode;

        var nameKey = "name";
        if (entry.CommandSet.TryGetValue("focus", out var focusVal) &&
            focusVal == "2" && entry.CommandSet.ContainsKey("name2"))
        {
            nameKey = "name2";
        }

        if (!entry.CommandSet.TryGetValue(nameKey, out var rawName) || string.IsNullOrEmpty(rawName))
            return entry.CharacterCode;

        var code = rawName.ToLower();
        var hashIdx = code.IndexOf('#');
        if (hashIdx >= 0) code = code[..hashIdx];

        return code;
    }

    /// <summary>
    /// 推断性别：优先查 override → fallback PicDescription 推断。
    /// </summary>
    internal static string? InferGender(
        string? characterCode,
        Dictionary<string, string> picDescByCode,
        GenderOverrideProvider? genderOverrides = null,
        string? characterName = null)
    {
        // 1. 优先查 override（按角色名）
        var overrideGender = genderOverrides?.GetOverride(characterName);
        if (!string.IsNullOrEmpty(overrideGender))
            return overrideGender;

        // 2. Fallback: PicDescription 推断
        return InferGenderFromPicDesc(characterCode, picDescByCode);
    }

    /// <summary>
    /// 从 PicDescription 推断性别（原始逻辑）。
    /// </summary>
    internal static string? InferGenderFromPicDesc(string? characterCode, Dictionary<string, string> picDescByCode)
    {
        if (string.IsNullOrEmpty(characterCode))
            return null;

        var baseCode = characterCode.Split('#')[0];
        if (!picDescByCode.TryGetValue(baseCode, out var desc))
            return null;

        var head = desc.Length > 100 ? desc[..100] : desc;
        if (head.Contains("她"))
            return "女";
        if (head.Contains("他"))
            return "男";

        if (desc.Contains("女性") || desc.Contains("女人") || desc.Contains("女孩") || desc.Contains("少女"))
            return "女";
        if (desc.Contains("男性") || desc.Contains("男人") || desc.Contains("男孩") || desc.Contains("少年"))
            return "男";

        return null;
    }

    /// <summary>
    /// 对齐缓存条目，用于 JSON 序列化。
    /// </summary>
    private record AlignmentCacheEntry(
        int Version,
        List<AlignmentEntry> Entries,
        AlignmentStats Stats
    );
}
