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
    private const int CurrentAlignmentCacheVersion = 14;
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
    public const int WindowSize = 5;
    public const double WindowMatchThreshold = 0.4;
    public const double NarratorMatchThreshold = 0.15;

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

            // 构建搜索单元：同段落连续对话合并
            var units = BuildSearchUnits(dialogs);

            #region Phase 1: 锚点匹配（高置信度文本匹配）
            var anchors = FindAnchors(units, plotEntries);
            anchorMatches += anchors.Count;
            #endregion

            #region Phase 2: 构建对齐映射（unit idx → DB entry idx → 展开为 dialog idx）
            var unitAlignMap = new Dictionary<int, int>();
            foreach (var (ui, di) in anchors)
            {
                if (di < 0 || di >= plotEntries.Count)
                    throw new IndexOutOfRangeException(
                        $"Anchor DbIdx out of range: di={di}, plotEntries.Count={plotEntries.Count}, " +
                        $"chapter='{novelChapter.Title}', plot='{plot.Title}', plotId={plot.Id}");
                unitAlignMap[ui] = di;
            }

            // 展开到 dialog 级别
            var alignmentMap = new Dictionary<int, int>();
            foreach (var (ui, dbIdx) in unitAlignMap)
                foreach (var dIdx in units[ui].DialogIndices)
                    alignmentMap[dIdx] = dbIdx;
            #endregion

            #region Phase 3: 锚点间的窗口匹配
            var anchorBounds = new List<(int Ni, int Di)> { (-1, -1) };
            anchorBounds.AddRange(anchors);
            anchorBounds.Add((units.Count, plotEntries.Count));

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
                    int unitIdx = prevNi + 1 + i;
                    var novelNorm = NormalizeLoose(units[unitIdx].MergedText);
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
                        var matchedDbIdx = prevDi + 1 + bestJ;
                        // 展开到 dialog 级别
                        foreach (var dIdx in units[unitIdx].DialogIndices)
                            alignmentMap[dIdx] = matchedDbIdx;
                        windowMatches++;
                        dbCursor = bestJ + 1;
                    }
                }
            }
            #endregion

            #region Phase 3.1: 高压缩间隙全局回退
            // 当间隙压缩比 > 2 时，对未对齐的 unit 在整个 plotEntries 中搜索
            for (int k = 0; k < anchorBounds.Count - 1; k++)
            {
                var (prevNi, prevDi) = anchorBounds[k];
                var (nextNi, nextDi) = anchorBounds[k + 1];
                int nGap = nextNi - prevNi - 1;
                int dGap = nextDi - prevDi - 1;
                if (nGap <= 0 || dGap <= 0) continue;
                if ((double)nGap / dGap <= 2.0) continue; // 压缩比 <= 2，跳过

                for (int i = 0; i < nGap; i++)
                {
                    int unitIdx = prevNi + 1 + i;
                    if (units[unitIdx].DialogIndices.Any(dIdx => alignmentMap.ContainsKey(dIdx)))
                        continue; // 已对齐，跳过

                    var unitNorm = NormalizeNoPunct(units[unitIdx].MergedText);
                    if (unitNorm.Length < 3) continue;

                    // 全局搜索
                    for (int di = 0; di < plotEntries.Count; di++)
                    {
                        var dbNorm = NormalizeNoPunct(plotEntries[di].Dialog ?? "");
                        if (dbNorm.Length < 3) continue;
                        if (IsAnchorMatch(unitNorm, dbNorm))
                        {
                            foreach (var dIdx in units[unitIdx].DialogIndices)
                                alignmentMap[dIdx] = di;
                            anchorMatches++;
                            break;
                        }
                    }
                }
            }
            #endregion

            #region Phase 3.5: 修复被旁白切断的对话碎片
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
                        // 验证：当前片段必须是匹配到的 DB 条目的子串
                        var matchedDbNorm = NormalizeNoPunct(plotEntries[mergedDbIdx].Dialog ?? "");
                        if (!string.IsNullOrEmpty(matchedDbNorm) && matchedDbNorm.Contains(NormalizeNoPunct(dialogs[di].Text)))
                        {
                            alignmentMap[di] = mergedDbIdx;
                            mergeMatches++;
                        }
                    }
                }
            }

            if (mergeMatches > 0)
                Console.WriteLine($"[Phase3.5] 碎片修复: {mergeMatches} 条 ({novelChapter.Title})");
            #endregion

            #region Phase 4: 构建结果
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
            #endregion

            #region Phase 5: 旁白对齐 + 继承兜底
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
            #endregion
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

    // ── SearchUnit 构建 ──

    /// <summary>
    /// 将同段落连续对话合并为搜索单元。
    /// 限制：合并后的文本长度不超过第一个片段长度的 2 倍，避免过度合并。
    /// </summary>
    internal static List<SearchUnit> BuildSearchUnits(List<NovelSegment> dialogs)
    {
        var units = new List<SearchUnit>();
        int i = 0;
        while (i < dialogs.Count)
        {
            var paragraph = dialogs[i].Paragraph;
            var indices = new List<int>();
            var texts = new List<string>();
            var firstLen = dialogs[i].Text.Length;
            var maxLen = firstLen * 2; // 限制合并长度

            while (i < dialogs.Count && dialogs[i].Paragraph == paragraph)
            {
                // 检查合并后是否会超长
                var newMerged = string.Join("", texts) + dialogs[i].Text;
                if (indices.Count > 0 && newMerged.Length > maxLen)
                {
                    // 超过限制，停止合并（但不包括当前这个）
                    break;
                }

                indices.Add(i);
                texts.Add(dialogs[i].Text);
                i++;
            }

            units.Add(new SearchUnit(string.Join("", texts), paragraph, indices));
        }
        return units;
    }

    // ── 锚点查找 ──

    internal static List<(int UnitIdx, int DbIdx)> FindAnchors(
        List<SearchUnit> units,
        List<FormattedTextEntry> dbEntries)
    {
        var anchors = new List<(int, int)>();
        int dbCursor = 0;

        for (int ui = 0; ui < units.Count; ui++)
        {
            var novelNorm = NormalizeNoPunct(units[ui].MergedText);
            if (novelNorm.Length < 3) continue;

            for (int di = dbCursor; di < dbEntries.Count; di++)
            {
                var dbNorm = NormalizeNoPunct(dbEntries[di].Dialog ?? "");
                if (dbNorm.Length < 3) continue;

                if (IsAnchorMatch(novelNorm, dbNorm))
                {
                    anchors.Add((ui, di));
                    dbCursor = di + 1;
                    break;
                }
            }
        }

        return anchors;
    }

    // ── 文本标准化 ──

    /// <summary>
    /// 去除所有标点符号，只保留字母、数字和中文字符。
    /// 用于锚点匹配前的预处理，避免标点差异导致匹配失败。
    /// </summary>
    internal static string NormalizeNoPunct(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            // 保留字母、数字、中文字符（CJK Unified Ideographs）
            if (char.IsLetterOrDigit(c) || (c >= 0x4E00 && c <= 0x9FFF) ||
                (c >= 0x3400 && c <= 0x4DBF) || (c >= 0x20000 && c <= 0x2A6DF))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

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

    // ═══════════════════════════════════════════════════════════════
    //  Diagnostic: 追踪单个片段在各 Phase 中的匹配过程
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 诊断单个小说片段的对齐过程：追踪目标文本在 Phase 1/3/3.5 中的匹配细节。
    /// </summary>
    /// <param name="novelFilePath">小说 md 文件路径。</param>
    /// <param name="chapterTitle">目标章节标题（如 "CW-1 迷雾重重 行动前"）。</param>
    /// <param name="targetText">目标小说文本（如 "真有趣，"）。</param>
    public async Task<AlignmentDiagnostic> DiagnoseChapterAsync(
        string novelFilePath, string chapterTitle, string targetText)
    {
        var fileName = Path.GetFileNameWithoutExtension(novelFilePath);
        var actName = ExtractActName(fileName);
        var novelText = await File.ReadAllTextAsync(novelFilePath);
        var novelChapters = DialogExtractor.ExtractChapters(novelText);

        var novelChapter = novelChapters.FirstOrDefault(c => c.Title == chapterTitle);
        if (novelChapter == null)
            throw new InvalidOperationException($"章节 '{chapterTitle}' 未找到。可用章节: {string.Join(", ", novelChapters.Select(c => c.Title))}");

        var dialogs = novelChapter.Segments.Where(s => s.IsDialog).ToList();
        var targetIdx = dialogs.FindIndex(d => d.Text == targetText || d.Text.Contains(targetText));
        if (targetIdx < 0)
            throw new InvalidOperationException($"文本 '{targetText}' 在章节 '{chapterTitle}' 的对话中未找到。");

        var (plots, entriesByPlot, allEntriesByPlot) = await LoadActDataAsync(actName);
        var plot = plots.FirstOrDefault(p =>
            p.Title.Contains(chapterTitle) || chapterTitle.Contains(p.Title));
        if (plot == null)
            throw new InvalidOperationException($"章节 '{chapterTitle}' 在 DB 中未找到对应 Plot。");

        if (!entriesByPlot.TryGetValue(plot.Id, out var plotEntries))
            throw new InvalidOperationException($"Plot '{plot.Title}' (Id={plot.Id}) 无 FormattedTextEntry 数据。");

        var diag = new AlignmentDiagnostic
        {
            TargetText = targetText,
            ChapterTitle = chapterTitle,
            NovelFilePath = novelFilePath,
            NovelDialogIdx = targetIdx
        };

        // 构建搜索单元
        var units = BuildSearchUnits(dialogs);
        var targetUnitIdx = -1;
        for (int ui = 0; ui < units.Count; ui++)
        {
            if (units[ui].DialogIndices.Contains(targetIdx))
            {
                targetUnitIdx = ui;
                break;
            }
        }

        var novelNormStrict = NormalizeStrict(targetText);
        var novelNormLoose = NormalizeLoose(targetText);
        var targetUnitNormStrict = targetUnitIdx >= 0 ? NormalizeStrict(units[targetUnitIdx].MergedText) : novelNormStrict;
        var targetUnitNormLoose = targetUnitIdx >= 0 ? NormalizeLoose(units[targetUnitIdx].MergedText) : novelNormLoose;

        // ── Phase 1: 锚点匹配 ──
        var anchors = FindAnchors(units, plotEntries);
        var anchorMatch = anchors.FirstOrDefault(a => a.UnitIdx == targetUnitIdx);
        var p1 = new Phase1Diag();

        if (anchorMatch != default)
        {
            p1.Matched = true;
            p1.MatchedDbIdx = anchorMatch.DbIdx;
        }
        else if (targetUnitNormStrict.Length < 3)
        {
            p1.SkipReason = $"NormalizeStrict 后长度 = {targetUnitNormStrict.Length} (< 3)，跳过";
            if (units[targetUnitIdx].DialogIndices.Count > 1)
                p1.SkipReason += $" (合并了 {units[targetUnitIdx].DialogIndices.Count} 个同段落对话)";
        }
        else
        {
            p1.SkipReason = "未命中任何锚点";
            if (units[targetUnitIdx].DialogIndices.Count > 1)
                p1.SkipReason += $" (合并文本: \"{TruncateForDiag(units[targetUnitIdx].MergedText, 60)}\")";
        }
        diag.Phase1 = p1;

        // ── Phase 2: 构建对齐映射 ──
        var unitAlignMap = new Dictionary<int, int>();
        foreach (var (ui, di) in anchors)
            unitAlignMap[ui] = di;

        var alignmentMap = new Dictionary<int, int>();
        foreach (var (ui, dbIdx) in unitAlignMap)
            foreach (var dIdx in units[ui].DialogIndices)
                alignmentMap[dIdx] = dbIdx;

        // ── Phase 3: 窗口匹配 ──
        var anchorBounds = new List<(int Ni, int Di)> { (-1, -1) };
        anchorBounds.AddRange(anchors);
        anchorBounds.Add((units.Count, plotEntries.Count));

        var p3 = new Phase3Diag();
        for (int k = 0; k < anchorBounds.Count - 1; k++)
        {
            var (prevNi, prevDi) = anchorBounds[k];
            var (nextNi, nextDi) = anchorBounds[k + 1];

            if (targetUnitIdx <= prevNi || targetUnitIdx >= nextNi)
                continue;

            int nGap = nextNi - prevNi - 1;
            int dGap = nextDi - prevDi - 1;

            p3.GapIndex = k;
            p3.PrevAnchorNi = prevNi;
            p3.PrevAnchorDi = prevDi;
            p3.NextAnchorNi = nextNi;
            p3.NextAnchorDi = nextDi;
            p3.NGap = nGap;
            p3.DGap = dGap;

            int i = targetUnitIdx - prevNi - 1;
            p3.GapPosition = i;

            if (nGap <= 0 || dGap <= 0)
            {
                p3.SkipReason = $"间隙为空 (nGap={nGap}, dGap={dGap})";
                break;
            }

            int expectedPos = dGap > 1 ? (int)((long)i * dGap / nGap) : 0;
            p3.ExpectedPos = expectedPos;

            int dbCursor = 0;
            for (int ii = 0; ii < i; ii++)
            {
                var prevNorm = NormalizeLoose(units[prevNi + 1 + ii].MergedText);
                if (string.IsNullOrEmpty(prevNorm)) continue;
                int prevExpected = dGap > 1 ? (int)((long)ii * dGap / nGap) : 0;
                int prevStart = Math.Max(dbCursor, prevExpected - WindowSize);
                int prevEnd = Math.Min(dGap - 1, prevExpected + WindowSize);
                for (int j = prevStart; j <= prevEnd; j++)
                {
                    int dbIdx = prevDi + 1 + j;
                    var dbNorm = NormalizeLoose(plotEntries[dbIdx].Dialog ?? "");
                    if (string.IsNullOrEmpty(dbNorm)) continue;
                    double score = ComputeSimilarity(prevNorm, dbNorm);
                    if (score >= WindowMatchThreshold) { dbCursor = j + 1; break; }
                }
            }

            int searchStart = Math.Max(dbCursor, expectedPos - WindowSize);
            int searchEnd = Math.Min(dGap - 1, expectedPos + WindowSize);
            p3.SearchStart = searchStart;
            p3.SearchEnd = searchEnd;

            int bestJ = -1;
            double bestScore = 0;
            for (int j = searchStart; j <= searchEnd; j++)
            {
                int dbIdx = prevDi + 1 + j;
                var dbNorm = NormalizeLoose(plotEntries[dbIdx].Dialog ?? "");
                if (string.IsNullOrEmpty(dbNorm)) continue;
                double score = ComputeSimilarity(targetUnitNormLoose, dbNorm);
                p3.Candidates.Add(new CandidateScore
                {
                    DbIdx = dbIdx,
                    DbText = plotEntries[dbIdx].Dialog ?? "",
                    Score = score
                });
                if (score > bestScore && score >= WindowMatchThreshold)
                {
                    bestJ = j;
                    bestScore = score;
                }
            }

            foreach (var c in p3.Candidates)
            {
                if (c.DbIdx == prevDi + 1 + bestJ)
                    c.IsSelected = true;
            }

            if (bestJ >= 0)
            {
                p3.Matched = true;
                p3.MatchedDbIdx = prevDi + 1 + bestJ;
                p3.MatchedScore = bestScore;
                foreach (var dIdx in units[targetUnitIdx].DialogIndices)
                    alignmentMap[dIdx] = p3.MatchedDbIdx;
            }

            int globalStart = Math.Max(0, prevDi + 1);
            int globalEnd = Math.Min(plotEntries.Count - 1, nextDi - 1);
            for (int j = globalStart; j <= globalEnd; j++)
            {
                int gapJ = j - prevDi - 1;
                if (gapJ >= searchStart && gapJ <= searchEnd) continue;
                var dbNorm = NormalizeLoose(plotEntries[j].Dialog ?? "");
                if (string.IsNullOrEmpty(dbNorm)) continue;
                double score = ComputeSimilarity(targetUnitNormLoose, dbNorm);
                if (score >= WindowMatchThreshold)
                {
                    p3.OutOfWindowCandidates.Add(new CandidateScore
                    {
                        DbIdx = j,
                        DbText = plotEntries[j].Dialog ?? "",
                        Score = score
                    });
                }
            }

            break;
        }
        diag.Phase3 = p3;

        // ── Phase 3.1: 高压缩间隙全局回退 ──
        if (!alignmentMap.ContainsKey(targetIdx) && targetUnitIdx >= 0)
        {
            // 找到目标 unit 所在的间隙
            for (int k = 0; k < anchorBounds.Count - 1; k++)
            {
                var (prevNi, prevDi) = anchorBounds[k];
                var (nextNi, nextDi) = anchorBounds[k + 1];
                if (targetUnitIdx <= prevNi || targetUnitIdx >= nextNi) continue;

                int nGap = nextNi - prevNi - 1;
                int dGap = nextDi - prevDi - 1;
                if (nGap > 0 && dGap > 0 && (double)nGap / dGap > 2.0)
                {
                    var unitNorm = NormalizeNoPunct(units[targetUnitIdx].MergedText);
                    if (unitNorm.Length >= 3)
                    {
                        for (int di = 0; di < plotEntries.Count; di++)
                        {
                            var dbNorm = NormalizeNoPunct(plotEntries[di].Dialog ?? "");
                            if (dbNorm.Length < 3) continue;
                            if (IsAnchorMatch(unitNorm, dbNorm))
                            {
                                foreach (var dIdx in units[targetUnitIdx].DialogIndices)
                                    alignmentMap[dIdx] = di;
                                diag.Phase31Matched = true;
                                diag.MatchedDbEntryIndex = plotEntries[di].Index;
                                break;
                            }
                        }
                    }
                }
                break;
            }
        }

        // ── Phase 3.5: 碎片修复（仍基于原始 dialogs）──
        bool wasAligned = alignmentMap.ContainsKey(targetIdx);
        int currentDbIdx = wasAligned ? alignmentMap[targetIdx] : -1;
        var p35 = new Phase35Diag { WasAligned = wasAligned };

        if (targetIdx > 0 && alignmentMap.TryGetValue(targetIdx - 1, out int prevDbIdx35)
            && prevDbIdx35 != currentDbIdx)
        {
            var prevDbNorm = NormalizeLoose(plotEntries[prevDbIdx35].Dialog ?? "");
            var c1 = new Check1Diag
            {
                PrevDbIdx = prevDbIdx35,
                PrevDbText = plotEntries[prevDbIdx35].Dialog ?? "",
                IsSubstring = !string.IsNullOrEmpty(prevDbNorm) && prevDbNorm.Contains(novelNormLoose)
            };
            if (!c1.IsSubstring) c1.SkipReason = "前邻 DB entry 不包含此文本";
            p35.Check1 = c1;
            if (c1.IsSubstring) { p35.Fixed = true; p35.FixedToDbIdx = prevDbIdx35; diag.FinalEntryIndex = prevDbIdx35; }
        }

        if (!p35.Fixed && targetIdx + 1 < dialogs.Count
            && alignmentMap.TryGetValue(targetIdx + 1, out int nextDbIdx35)
            && nextDbIdx35 != currentDbIdx)
        {
            var nextDbNorm = NormalizeLoose(plotEntries[nextDbIdx35].Dialog ?? "");
            var c2 = new Check2Diag
            {
                NextDbIdx = nextDbIdx35,
                NextDbText = plotEntries[nextDbIdx35].Dialog ?? "",
                IsSubstring = !string.IsNullOrEmpty(nextDbNorm) && nextDbNorm.Contains(novelNormLoose)
            };
            if (!c2.IsSubstring) c2.SkipReason = "后邻 DB entry 不包含此文本";
            p35.Check2 = c2;
            if (c2.IsSubstring) { p35.Fixed = true; p35.FixedToDbIdx = nextDbIdx35; diag.FinalEntryIndex = nextDbIdx35; }
        }

        if (!p35.Fixed && !wasAligned && novelNormLoose.Length <= 30
            && targetIdx > 0 && alignmentMap.TryGetValue(targetIdx - 1, out int prevDbIdx3))
        {
            var mergedNorm = NormalizeLoose(dialogs[targetIdx - 1].Text + targetText);
            var c3 = new Check3Diag { MergedText = dialogs[targetIdx - 1].Text + targetText };
            if (TryFindMergedMatch(mergedNorm, prevDbIdx3, plotEntries, out int mergedDbIdx))
            {
                // 验证：当前片段必须是匹配到的 DB 条目的子串
                var matchedDbNorm = NormalizeNoPunct(plotEntries[mergedDbIdx].Dialog ?? "");
                if (!string.IsNullOrEmpty(matchedDbNorm) && matchedDbNorm.Contains(NormalizeNoPunct(targetText)))
                {
                    c3.Matched = true; c3.MatchedDbIdx = mergedDbIdx;
                    p35.Fixed = true; p35.FixedToDbIdx = mergedDbIdx; diag.FinalEntryIndex = mergedDbIdx;
                }
                else
                {
                    c3.SkipReason = "合并后匹配到的 DB entry 不包含当前片段";
                }
            }
            else { c3.SkipReason = "合并后未匹配到任何 DB entry"; }
            p35.Check3 = c3;
        }

        diag.Phase35 = p35;
        if (!p35.Fixed)
            diag.FinalEntryIndex = wasAligned ? currentDbIdx : -1;

        return diag;
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

    private static string TruncateForDiag(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return "(空)";
        return text.Length <= maxLen ? text : text[..maxLen] + "…";
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
