using System.Text.Json;
using ArkPlot.Core.Interfaces;
using ArkPlot.Core.Model;
using ArkPlot.Tts.Alignment;

namespace ArkPlot.Tts;

/// <summary>
/// TTS 统一编排器。
/// 整合 ITtsEngine + VoiceManager + TtsCacheService + AudioMerger + TextSanitizer，
/// 提供统一的 TTS 生成入口，消除三条旧管线的重复。
/// </summary>
public class TtsPipeline : IDisposable
{
    private readonly ITtsEngine _engine;
    private readonly VoiceManager _voices;
    private readonly TtsCacheService _cache;
    private readonly ILoudnessNormalizer? _normalizer;
    private readonly string _rate;
    private readonly string _volume;
    private readonly string _tempDir;

    public TtsPipeline(
        ITtsEngine engine,
        VoiceManager voices,
        TtsCacheService cache,
        string rate = "+0%",
        string volume = "+0%",
        ILoudnessNormalizer? normalizer = null)
    {
        _engine = engine;
        _voices = voices;
        _cache = cache;
        _normalizer = normalizer;
        _rate = rate;
        _volume = volume;
        _tempDir = Path.Combine(Path.GetTempPath(), $"arkplot_pipeline_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// 执行 TTS 管线。
    /// </summary>
    public async Task<TtsPipelineResult> GenerateAsync(
        TtsRequest request,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        Directory.CreateDirectory(request.OutputDir);
        progress?.Report($"开始 TTS 生成: {request.Mode} → {Path.GetFileName(request.InputPath)}");

        // 1. 解析输入为片段列表
        var segments = request.Mode switch
        {
            TtsInputMode.NovelChapter => await LoadNovelChapterSegments(request, progress),
            TtsInputMode.AlignedJson => LoadAlignedJsonSegments(request, progress),
            TtsInputMode.RawEntries => LoadRawEntrySegments(request, progress),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Mode))
        };

        // 应用片段限制
        if (request.SegmentLimit.HasValue && segments.Count > request.SegmentLimit.Value)
        {
            progress?.Report($"⚠️ 限制为前 {request.SegmentLimit.Value} 个片段");
            segments = segments.Take(request.SegmentLimit.Value).ToList();
        }

        progress?.Report($"共 {segments.Count} 个片段待合成");

        // 调试模式：只输出音色分配表
        if (request.DebugVoiceOnly)
        {
            foreach (var seg in segments)
                progress?.Report($"  {seg.Label} → {seg.Voice}");
            return new TtsPipelineResult(segments.Count, 0, 0, 0, []);
        }

        // 2. 按章节分组（如有）并逐组生成
        var hasChapters = segments.Any(s => s.ChapterTitle != null);
        var outputFiles = new List<string>();

        if (hasChapters)
        {
            outputFiles = await GenerateByChapter(segments, request, cancellationToken, progress);
        }
        else
        {
            var singleOutput = Path.Combine(request.OutputDir,
                $"{Path.GetFileNameWithoutExtension(request.InputPath)}_tts.mp3");
            var result = await SynthesizeAndMerge(segments, singleOutput, request.RequestDelayMs,
                cancellationToken, progress);
            if (result != null)
                outputFiles.Add(singleOutput);
        }

        var synthesized = outputFiles.Count > 0 ? segments.Count(s => !IsSkipped(s)) : 0;
        progress?.Report($"🎵 完成: {outputFiles.Count} 个文件, {segments.Count} 个片段");

        return new TtsPipelineResult(
            segments.Count, synthesized, 0, 0, outputFiles);
    }

    private async Task<List<string>> GenerateByChapter(
        List<TtsSegment> segments,
        TtsRequest request,
        CancellationToken ct,
        IProgress<string>? progress)
    {
        var chapters = segments.GroupBy(s => s.ChapterTitle ?? "(无标题)").ToList();
        var outputFiles = new List<string>();
        var baseName = Path.GetFileNameWithoutExtension(request.InputPath);

        for (int i = 0; i < chapters.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var chapter = chapters[i];
            var chapterSegments = chapter.ToList();
            var safeName = SanitizeFileName(chapter.Key);
            var outputPath = Path.Combine(request.OutputDir, $"{baseName}_{i + 1:D2}_{safeName}.mp3");

            progress?.Report($"\n📖 [{i + 1}/{chapters.Count}] {chapter.Key} ({chapterSegments.Count} 段)");

            var result = await SynthesizeAndMerge(chapterSegments, outputPath,
                request.RequestDelayMs, ct, progress);
            if (result != null)
                outputFiles.Add(outputPath);
        }

        return outputFiles;
    }

    /// <summary>
    /// 合成片段列表并合并为单个 MP3。返回实际合成的文件数，失败返回 null。
    /// </summary>
    private async Task<int?> SynthesizeAndMerge(
        List<TtsSegment> segments,
        string outputPath,
        int delayMs,
        CancellationToken ct,
        IProgress<string>? progress)
    {
        var tempFiles = new List<string>();
        int cacheHits = 0;
        int synthesized = 0;
        int skipped = 0;

        try
        {
            bool isFirst = true;
            for (int i = 0; i < segments.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var seg = segments[i];
                var text = TextSanitizer.Sanitize(seg.Text);
                if (string.IsNullOrWhiteSpace(text))
                {
                    progress?.Report($"  [{i + 1}/{segments.Count}] 跳过（空文本）");
                    skipped++;
                    continue;
                }

                // 请求间隔
                if (!isFirst && delayMs > 0)
                    await Task.Delay(delayMs, ct);
                isFirst = false;

                // 缓存查询
                var cacheKey = TtsCacheService.GetCacheKey(text, seg.Voice, _rate, _volume);
                var (hit, cachedPath) = _cache.TryGetCachedAudio(cacheKey);
                if (hit)
                {
                    var tempFile = Path.Combine(_tempDir, $"seg_{i:D4}.mp3");
                    File.Copy(cachedPath!, tempFile, overwrite: true);
                    tempFiles.Add(tempFile);
                    cacheHits++;
                    progress?.Report($"  [{i + 1}/{segments.Count}] 📦 缓存命中 | {seg.Label}");
                    continue;
                }

                // 合成
                var synthFile = Path.Combine(_tempDir, $"seg_{i:D4}_raw.mp3");
                try
                {
                    await _engine.SynthesizeAsync(text, seg.Voice, synthFile, _rate, _volume, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var msg = ex.Message.Length > 80 ? ex.Message[..80] : ex.Message;
                    progress?.Report($"  [{i + 1}/{segments.Count}] ⚠️ 跳过（{ex.GetType().Name}: {msg}）");
                    skipped++;
                    continue;
                }

                if (!File.Exists(synthFile) || new FileInfo(synthFile).Length == 0)
                {
                    progress?.Report($"  [{i + 1}/{segments.Count}] ⚠️ 跳过（空文件）");
                    skipped++;
                    continue;
                }

                // 响度均衡（如有 normalizer）
                var finalFile = synthFile;
                if (_normalizer != null)
                {
                    var normalizedFile = Path.Combine(_tempDir, $"seg_{i:D4}.mp3");
                    try
                    {
                        await _normalizer.NormalizeAsync(synthFile, normalizedFile, ct);
                        finalFile = normalizedFile;
                        try { File.Delete(synthFile); } catch { }
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"  [{i + 1}/{segments.Count}] ⚠️ 归一化失败，使用原始音频（{ex.Message[..Math.Min(40, ex.Message.Length)]}）");
                    }
                }

                // 写入缓存
                try { _cache.SaveToCache(cacheKey, finalFile); } catch { /* 缓存写入失败不影响 */ }

                tempFiles.Add(finalFile);
                synthesized++;
                progress?.Report($"  [{i + 1}/{segments.Count}] 🎤 {seg.Voice.Replace("Neural", "")} | {seg.Label}");
            }

            if (tempFiles.Count == 0)
            {
                progress?.Report("  ⚠️ 无有效片段，跳过合并");
                return null;
            }

            // 合并
            AudioMerger.MergeFiles(tempFiles, outputPath);
            progress?.Report($"  ✅ → {Path.GetFileName(outputPath)} ({tempFiles.Count} 段, 缓存 {cacheHits}, 合成 {synthesized}, 跳过 {skipped})");
            return tempFiles.Count;
        }
        finally
        {
            foreach (var f in tempFiles)
            {
                try { if (File.Exists(f)) File.Delete(f); } catch { }
            }
        }
    }

    /// <summary>
    /// 合成指定片段列表，每个片段输出为独立的 MP3 文件（不合并）。
    /// 返回每个片段的输出文件路径列表。
    /// </summary>
    /// <param name="segments">待合成的片段列表。</param>
    /// <param name="segmentIndices">每个片段对应的行号（1-based），用于 fileProgress 回调索引。若为 null 则使用 1,2,3...。</param>
    /// <param name="outputDir">输出目录。</param>
    /// <param name="delayMs">请求间隔毫秒。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="progress">进度回调。</param>
    /// <param name="fileProgress">逐文件完成回调：(segmentIndex_0based, filePath)。filePath 始终为缓存路径。</param>
    public async Task<List<string>> SynthesizeSegmentsAsync(
        List<TtsSegment> segments,
        List<int>? segmentIndices,
        string outputDir,
        int delayMs = 1000,
        CancellationToken ct = default,
        IProgress<string>? progress = null,
        IProgress<(int Index, string FilePath)>? fileProgress = null)
    {
        Directory.CreateDirectory(outputDir);
        var outputFiles = new List<string>();
        bool isFirst = true;

        for (int i = 0; i < segments.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var seg = segments[i];
            var text = TextSanitizer.Sanitize(seg.Text);
            if (string.IsNullOrWhiteSpace(text))
            {
                progress?.Report($"  [{i + 1}/{segments.Count}] 跳过（空文本）");
                continue;
            }

            if (!isFirst && delayMs > 0)
                await Task.Delay(delayMs, ct);
            isFirst = false;

            // 缓存查询 — 输出路径统一为缓存目录下的哈希文件名
            var cacheKey = TtsCacheService.GetCacheKey(text, seg.Voice, _rate, _volume);
            var (hit, cachedPath) = _cache.TryGetCachedAudio(cacheKey);
            var cachePath = _cache.GetCachePath(cacheKey);

            if (hit)
            {
                outputFiles.Add(cachedPath!);
                fileProgress?.Report((i, cachedPath!));
                progress?.Report($"  [{i + 1}/{segments.Count}] 📦 缓存命中 | {seg.Label}");
                continue;
            }

            // 合成到临时文件
            var synthFile = Path.Combine(_tempDir, $"synth_{i:D4}.mp3");
            try
            {
                await _engine.SynthesizeAsync(text, seg.Voice, synthFile, _rate, _volume, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                var msg = ex.Message.Length > 80 ? ex.Message[..80] : ex.Message;
                progress?.Report($"  [{i + 1}/{segments.Count}] ⚠️ 失败（{ex.GetType().Name}: {msg}）");
                continue;
            }

            if (!File.Exists(synthFile) || new FileInfo(synthFile).Length == 0)
            {
                progress?.Report($"  [{i + 1}/{segments.Count}] ⚠️ 跳过（空文件）");
                continue;
            }

            // 响度均衡（如有 normalizer）
            if (_normalizer != null)
            {
                try
                {
                    await _normalizer.NormalizeAsync(synthFile, cachePath, ct);
                }
                catch (Exception ex)
                {
                    progress?.Report($"  [{i + 1}/{segments.Count}] ⚠️ 归一化失败，使用原始音频（{ex.GetType().Name}: {ex.Message[..Math.Min(60, ex.Message.Length)]}）");
                    // 归一化失败，直接使用原始合成文件
                    File.Move(synthFile, cachePath, overwrite: true);
                }
            }
            else
            {
                // 无 normalizer，直接移动到缓存位置
                File.Move(synthFile, cachePath, overwrite: true);
            }

            // 清理临时文件
            try { if (File.Exists(synthFile)) File.Delete(synthFile); } catch { }

            outputFiles.Add(cachePath);
            fileProgress?.Report((i, cachePath));
            progress?.Report($"  [{i + 1}/{segments.Count}] 🎤 {seg.Voice.Replace("Neural", "")} | {seg.Label}");
        }

        return outputFiles;
    }

    /// <summary>
    /// 根据角色名和性别解析音色。
    /// </summary>
    public string ResolveVoice(string? characterName, string? gender, bool isDialog)
    {
        if (!isDialog) return _voices.GetNarratorVoice();
        if (!string.IsNullOrEmpty(characterName)) return _voices.GetVoiceForCharacter(characterName, gender);
        if (!string.IsNullOrEmpty(gender)) return _voices.GetVoiceForGender(gender);
        return _voices.GetFallbackVoice();
    }

    #region 输入解析

    private async Task<List<TtsSegment>> LoadNovelChapterSegments(TtsRequest request, IProgress<string>? progress)
    {
        var aligner = new NovelAligner();
        var (entries, stats) = await aligner.AlignByFileNameAsync(request.InputPath);
        progress?.Report($"对齐完成: {stats.TotalNovelChapters} 章, {stats.AlignedDialogs}/{stats.TotalDialogs} 对话");

        return entries
            .Where(e => !string.IsNullOrWhiteSpace(e.NovelText))
            .Select(e => new TtsSegment(
                e.NovelText,
                ResolveVoice(e),
                FormatLabel(e),
                e.ChapterTitle))
            .ToList();
    }

    private List<TtsSegment> LoadAlignedJsonSegments(TtsRequest request, IProgress<string>? progress)
    {
        var json = File.ReadAllText(request.InputPath);
        var entries = JsonSerializer.Deserialize<List<AlignmentEntryJson>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        progress?.Report($"加载 {entries.Count} 个片段");

        return entries
            .Where(e => !string.IsNullOrWhiteSpace(e.NovelText))
            .Select(e => new TtsSegment(
                e.NovelText,
                ResolveVoiceForJson(e),
                FormatLabelFromJson(e),
                e.ChapterTitle))
            .ToList();
    }

    private List<TtsSegment> LoadRawEntrySegments(TtsRequest request, IProgress<string>? progress)
    {
        // RawEntries 模式：InputPath 指向一个 JSON 文件（FormattedTextEntry 序列化）
        var json = File.ReadAllText(request.InputPath);
        var entries = JsonSerializer.Deserialize<List<RawEntryJson>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        return entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Dialog))
            .Select(e => new TtsSegment(
                e.Dialog!,
                _voices.GetVoiceForCharacter(e.CharacterName ?? ""),
                e.CharacterName ?? "旁白"))
            .ToList();
    }

    #endregion

    #region 音色解析辅助

    private string ResolveVoice(AlignmentEntry e)
    {
        if (!e.IsDialog) return _voices.GetNarratorVoice();
        if (!string.IsNullOrEmpty(e.CharacterName)) return _voices.GetVoiceForCharacter(e.CharacterName, e.Gender);
        if (!string.IsNullOrEmpty(e.Gender)) return _voices.GetVoiceForGender(e.Gender);
        return _voices.GetFallbackVoice();
    }

    private string ResolveVoiceForJson(AlignmentEntryJson e)
    {
        if (!e.IsDialog) return _voices.GetNarratorVoice();
        if (!string.IsNullOrEmpty(e.CharacterName)) return _voices.GetVoiceForCharacter(e.CharacterName, e.Gender);
        if (!string.IsNullOrEmpty(e.Gender)) return _voices.GetVoiceForGender(e.Gender);
        return _voices.GetFallbackVoice();
    }

    private static string FormatLabel(AlignmentEntry e) =>
        e.IsDialog ? $"{e.CharacterName ?? "?"}({e.Gender ?? "?"})" : "旁白";

    private static string FormatLabelFromJson(AlignmentEntryJson e) =>
        e.IsDialog ? $"{e.CharacterName ?? "?"}({e.Gender ?? "?"})" : "旁白";

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Where(c => !invalid.Contains(c)));
    }

    private static bool IsSkipped(TtsSegment s) => string.IsNullOrWhiteSpace(s.Text);

    #endregion

    #region JSON 反序列化辅助类型

    private class AlignmentEntryJson
    {
        public string NovelText { get; set; } = "";
        public bool IsDialog { get; set; }
        public string? CharacterName { get; set; }
        public string? CharacterCode { get; set; }
        public int EntryIndex { get; set; }
        public string ChapterTitle { get; set; } = "";
        public string? Gender { get; set; }
    }

    private class RawEntryJson
    {
        public string? CharacterName { get; set; }
        public string? Dialog { get; set; }
    }

    #endregion

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        GC.SuppressFinalize(this);
    }
}
