using System.IO;

namespace ArkPlot.Core.Infrastructure;

/// <summary>
/// 用户输出目录体系：统一管理 TTS/视频/缓存等输出路径。
/// 所有路径基于 outputRoot（MainWindow 的 OutputPath）和 storyName（活动名）。
/// </summary>
public static class TtsCachePaths
{
    public static string Tts(string storyName) =>
        Path.Combine(OutputPaths.TtsDir(storyName), "_tts_cache");

    /// <summary>{StoryRoot}/tts/_align_cache — 对齐缓存。</summary>
    public static string Align(string storyName) =>
        Path.Combine(OutputPaths.TtsDir(storyName), "_align_cache");
}
