using ArkPlot.Core.Infrastructure;

namespace ArkPlot.Video;

/// <summary>
/// 视频合成器：协调 TypstComposer → NetworkImageCache → VideoRenderer → 拼接 的完整管线。
/// 所有路径通过 ArkPlot.Core.Infrastructure 路径类计算。
/// </summary>
public class VideoComposer
{
    private readonly VideoRenderer _renderer;
    private readonly string _storyName;
    private readonly Action<string>? _log;
    private readonly Action<int, int>? _progress;

    public VideoComposer(
        VideoRenderer renderer,
        string storyName,
        Action<string>? onLog = null,
        Action<int, int>? onProgress = null
    )
    {
        _renderer = renderer;
        _storyName = storyName;
        _log = onLog;
        _progress = onProgress;

        _renderer.TypstRoot = AppContext.BaseDirectory;
    }

    public async Task<string> ComposeChapterAsync(
        List<VideoSegment> segments,
        string chapterTitle,
        bool dryRun = false,
        CancellationToken cancellationToken = default
    )
    {
        var pageDir = VideoCachePaths.Pages(_storyName);
        var clipDir = VideoCachePaths.Clips(_storyName);

        Directory.CreateDirectory(pageDir);
        Directory.CreateDirectory(clipDir);

        // 1. TypstComposer: 查询 DB，为每个 segment 生成完整 Typst 代码
        var dbEntries = TypstComposer.Compose(segments);
        _log?.Invoke($"📝 TypstComposer: {segments.Count(s => !string.IsNullOrEmpty(s.TypstCode))}/{segments.Count} 段已生成 Typst 代码");

        // 2. NetworkImageCache: 下载网络图片，重写 TypstCode 中的 URL
        var imageCache = new NetworkImageCache(_storyName, _log);
        var urlToPath = await imageCache.EnsureAllCachedAsync(dbEntries.Values, cancellationToken);
        foreach (var seg in segments)
        {
            if (!string.IsNullOrEmpty(seg.TypstCode))
                seg.TypstCode = imageCache.RewriteUrls(seg.TypstCode, urlToPath);
        }

        // 3. 过滤可渲染的段：有 TypstCode + 有音频
        var renderable = segments
            .Where(s => !string.IsNullOrEmpty(s.TypstCode) && !string.IsNullOrEmpty(s.AudioFilePath))
            .ToList();

        _log?.Invoke($"🎬 可渲染: {renderable.Count}/{segments.Count} 段 (TypstCode+Audio)");

        if (renderable.Count == 0)
        {
            _log?.Invoke("⚠️ 没有可渲染的视频段");
            return "";
        }

        if (dryRun)
            return "";

        // 4. 渲染每段: Typst→PNG, PNG+Audio→MP4
        _log?.Invoke($"🎬 渲染 {renderable.Count} 个视频段...");
        for (int i = 0; i < renderable.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seg = renderable[i];
            var label = seg.IsDialog ? seg.CharacterName : "旁白";
            _log?.Invoke($"  [{i + 1}/{renderable.Count}] {label}: {Truncate(seg.NovelText, 30)}");

            await _renderer.RenderPageAsync(seg, pageDir);
            await _renderer.ComposeClipAsync(seg, clipDir);
            _progress?.Invoke(i + 1, renderable.Count);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 5. 拼接所有 MP4
        var finalPath = VideoCachePaths.VideoFile(_storyName, chapterTitle);
        await ConcatMp4Async(renderable, finalPath);

        _log?.Invoke($"✅ 视频已生成: {finalPath}");
        return finalPath;
    }

    // ── 辅助 ─────────────────────────────────────

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    private async Task ConcatMp4Async(IReadOnlyList<VideoSegment> segments, string outputPath)
    {
        var mp4Files = segments
            .Select(s => s.ClipOutputPath)
            .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
            .Cast<string>()
            .ToList();

        if (mp4Files.Count == 0)
            throw new InvalidOperationException("没有可拼接的视频段");

        var dir = System.IO.Path.GetDirectoryName(outputPath);
        if (dir != null)
            Directory.CreateDirectory(dir);

        var listPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"arkplot_concat_{Guid.NewGuid():N}.txt"
        );
        await File.WriteAllLinesAsync(
            listPath,
            mp4Files.Select(f => $"file '{f.Replace("'", "'\\''")}'")
        );

        try
        {
            var args = $"-y -f concat -safe 0 -i \"{listPath}\" -c copy \"{outputPath}\"";
            using var concatCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var engine = new FFmpeg.NET.Engine(_renderer.FfmpegPath);
            await engine.ExecuteAsync(args, concatCts.Token);
        }
        finally
        {
            if (File.Exists(listPath))
                File.Delete(listPath);
        }
    }
}
