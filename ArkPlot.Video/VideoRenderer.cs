using FFmpeg.NET;
using Typst;

namespace ArkPlot.Video;

/// <summary>
/// 单段视频渲染器：Typst 编译 → PNG，FFmpeg 合成 → MP4。
/// PNG 和 MP4 分别输出到不同目录（Pages / Clips）。
/// </summary>
public class VideoRenderer
{
    private readonly Engine _engine;
    private readonly Action<string>? _log;

    public string FfmpegPath { get; }

    /// <summary>Typst 路径解析的根目录（AppContext.BaseDirectory）。</summary>
    public string? TypstRoot { get; set; }

    /// <summary>Typst 渲染 PPI（每英寸像素数）。96 = 1920×1080 (Full HD)，72 = 1440×810 (标清)。</summary>
    public float Ppi { get; set; } = 96.0f;

    public VideoRenderer(string ffmpegPath, Action<string>? onLog = null)
    {
        FfmpegPath = ffmpegPath;
        _engine = new Engine(ffmpegPath);
        _log = onLog;
    }

    /// <summary>渲染 Typst 代码为 PNG，写入 pageDir。</summary>
    public async Task RenderPageAsync(VideoSegment segment, string pageDir)
    {
        var pages = await Task.Run(() => CompileTypstToPng(segment.TypstCode));
        if (pages.Count == 0)
            throw new InvalidOperationException($"Typst 编译未输出页面: {segment.SegmentHash}");
        var pngPath = Path.Combine(pageDir, $"{segment.SegmentHash}.png");
        await File.WriteAllBytesAsync(pngPath, pages[0]);
        segment.PngOutputPath = pngPath;
    }

    /// <summary>将已有 PNG + 音频合成为 MP4，写入 clipDir。</summary>
    public async Task ComposeClipAsync(VideoSegment segment, string clipDir)
    {
        if (string.IsNullOrEmpty(segment.PngOutputPath) || !File.Exists(segment.PngOutputPath))
            return;
        if (string.IsNullOrEmpty(segment.AudioFilePath) || !File.Exists(segment.AudioFilePath))
            return;

        var mp4Path = Path.Combine(clipDir, $"{segment.SegmentHash}.mp4");
        await ComposeVideoAsync(segment.PngOutputPath, segment.AudioFilePath, mp4Path);
        segment.ClipOutputPath = mp4Path;
    }

    private List<byte[]> CompileTypstToPng(string typCode)
    {
        using var compiler = new TypstCompiler(typCode, root: TypstRoot);
        var (pages, warnings) = compiler.Compile(format: "png", ppi: Ppi);
        return pages;
    }

    private async Task ComposeVideoAsync(string pngPath, string audioPath, string outputPath)
    {
        var args =
            $"-y -loop 1 -framerate 24 -i \"{pngPath}\" -i \"{audioPath}\" -c:v libx264 -tune stillimage -vf \"pad=ceil(iw/2)*2:ceil(ih/2)*2\" -c:a aac -ar 32000 -ac 1 -b:a 192k -shortest -pix_fmt yuv420p -r 24 \"{outputPath}\"";

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await _engine.ExecuteAsync(args, cts.Token);
    }
}
