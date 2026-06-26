using System.IO;

namespace ArkPlot.Core.Infrastructure;

/// <summary>
/// 用户输出目录体系：统一管理 TTS/视频/缓存等输出路径。
/// 所有路径基于 outputRoot（MainWindow 的 OutputPath）和 storyName（活动名）。
/// </summary>
public static class VideoCachePaths
{
    public static string Pages(string storyName) =>
        Path.Combine(OutputPaths.VideoDir(storyName), "_page_cache");

    /// <summary>{StoryRoot}/tts/_align_cache — 对齐缓存。</summary>
    public static string Clips(string storyName) =>
        Path.Combine(OutputPaths.VideoDir(storyName), "_clip_cache");

    /// <summary>{VideoDir}/{chapterTitle}.mp4 — 最终视频路径。</summary>
    public static string VideoFile(string storyName, string chapterTitle) =>
        Path.Combine(OutputPaths.VideoDir(storyName), $"{chapterTitle}.mp4");
}
