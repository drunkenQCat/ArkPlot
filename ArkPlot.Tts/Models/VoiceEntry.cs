namespace ArkPlot.Tts.Models;

/// <summary>
/// 统一音色条目，所有引擎（EdgeTTS、MiniMax、自定义）的音色都使用此结构
/// </summary>
public record VoiceEntry
{
    /// <summary>
    /// 引擎内部音色标识，如 "female-shaonv"、"zh-CN-XiaoxiaoNeural"
    /// </summary>
    public string VoiceId { get; init; } = string.Empty;

    /// <summary>
    /// 显示名称，如 "少女"、"晓晓"
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// 性别：Male / Female / Unknown
    /// </summary>
    public string Gender { get; init; } = "Unknown";

    /// <summary>
    /// 语言/地区，如 "zh-CN"、"en-US"
    /// </summary>
    public string? Locale { get; init; }

    public VoiceEntry() { }

    public VoiceEntry(string voiceId, string displayName, string gender = "Unknown", string? locale = null)
    {
        VoiceId = voiceId;
        DisplayName = displayName;
        Gender = gender;
        Locale = locale;
    }
}
