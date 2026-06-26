namespace ArkPlot.Video;

/// <summary>
/// 视频片段 — 对应一段对话或旁白。
/// hash 与 TTS 缓存 key 一致（SHA256(文本|音色|速率|音量)），
/// 用于定位 TTS 音频文件和中间产物。
/// </summary>
public class VideoSegment
{
    /// <summary>FormattedTextEntry 的 DB 主键（唯一）。</summary>
    public long EntryId { get; init; }

    /// <summary>FormattedTextEntry.Index（Plot 内局部序号，非唯一）。</summary>
    public int EntryIndex { get; init; } = -1;

    /// <summary>与 TTS 缓存一致的 SHA256 hash。</summary>
    public string SegmentHash { get; init; } = "";

    /// <summary>台词/旁白文本。</summary>
    public string NovelText { get; init; } = "";

    /// <summary>是否为对话（true=对话，false=旁白）。</summary>
    public bool IsDialog { get; init; }

    /// <summary>角色名（旁白时为空）。</summary>
    public string CharacterName { get; init; } = "";

    /// <summary>拼好的 Typst 代码，直接传入 TypstCompiler。</summary>
    public string TypstCode { get; set; } = "";

    /// <summary>对应的 TTS 音频文件路径（由外部查找后填入）。</summary>
    public string? AudioFilePath { get; set; }

    /// <summary>音频时长（秒），用于 FFmpeg 对齐。</summary>
    public double AudioDurationSeconds { get; set; }

    // 中间产物路径（渲染后由 VideoRenderer 填入）
    public string? PngOutputPath { get; set; }
    public string? ClipOutputPath { get; set; }
}
