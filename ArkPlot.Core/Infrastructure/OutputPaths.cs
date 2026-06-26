using System.IO;

namespace ArkPlot.Core.Infrastructure;

/// <summary>
/// 用户输出目录体系：统一管理 TTS/视频/缓存等输出路径。
/// 所有路径基于 outputRoot（MainWindow 的 OutputPath）和 storyName（活动名）。
/// </summary>
public static class OutputPaths
{
    /// <summary>output/{storyName} — 单个活动的输出根。</summary>
    public static string ActRootRelative(string storyName) => Path.Combine("output", storyName);

    /// <summary>output/{storyName} — 单个活动的输出根。</summary>
    public static string ActRootAbsolute(string storyName) =>
        Path.Combine(AppContext.BaseDirectory, ActRootRelative(storyName));

    /// <summary>{StoryRoot}/tts — TTS 音频输出目录。</summary>
    public static string TtsDir(string storyName) =>
        Path.Combine(ActRootAbsolute(storyName), "tts");

    /// <summary>{StoryRoot}/video — 视频段输出目录。</summary>
    public static string VideoDir(string storyName) =>
        Path.Combine(ActRootAbsolute(storyName), "video");
}
