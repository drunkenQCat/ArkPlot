namespace ArkPlot.Tts.Models;

/// <summary>
/// TTS 统一配置，支持多引擎并存（EdgeTTS + MiniMax + 自定义引擎）
/// </summary>
public record TtsSettings
{
    /// <summary>
    /// 是否启用 EdgeTTS
    /// </summary>
    public bool EdgeTtsEnabled { get; init; } = true;

    /// <summary>
    /// 是否启用 MiniMax
    /// </summary>
    public bool MiniMaxEnabled { get; init; }

    /// <summary>
    /// MiniMax API Key
    /// </summary>
    public string? MiniMaxApiKey { get; init; }

    /// <summary>
    /// MiniMax API 地址，如 "https://api.minimax.io/"
    /// </summary>
    public string? MiniMaxBaseUrl { get; init; }

    /// <summary>
    /// MiniMax 默认模型，如 "speech-2.8-hd"
    /// </summary>
    public string? MiniMaxModel { get; init; }

    /// <summary>
    /// MiniMax 音色列表（用户可增删）
    /// </summary>
    public VoiceEntry[] MiniMaxVoices { get; init; } = [];

    /// <summary>
    /// 自定义引擎列表
    /// </summary>
    public CustomTtsProvider[] CustomEngines { get; init; } = [];

    /// <summary>
    /// 默认旁白音色 VoiceId
    /// </summary>
    public string DefaultNarratorVoice { get; init; } = "zh-CN-XiaoxiaoNeural";

    public TtsSettings() { }

    /// <summary>
    /// 创建默认配置（EdgeTTS 启用，MiniMax 未配置，无自定义引擎）。
    /// </summary>
    public static TtsSettings CreateDefaults()
    {
        return new TtsSettings
        {
            EdgeTtsEnabled = true,
            MiniMaxEnabled = false,
            MiniMaxApiKey = null,
            MiniMaxBaseUrl = "https://api.minimax.io/",
            MiniMaxModel = "speech-2.8-hd",
            MiniMaxVoices = [],
            CustomEngines = [],
            DefaultNarratorVoice = "zh-CN-XiaoxiaoNeural"
        };
    }

    public TtsSettings(
        bool edgeTtsEnabled,
        bool miniMaxEnabled,
        string? miniMaxApiKey,
        string? miniMaxBaseUrl,
        string? miniMaxModel,
        VoiceEntry[] miniMaxVoices,
        CustomTtsProvider[] customEngines,
        string defaultNarratorVoice)
    {
        EdgeTtsEnabled = edgeTtsEnabled;
        MiniMaxEnabled = miniMaxEnabled;
        MiniMaxApiKey = miniMaxApiKey;
        MiniMaxBaseUrl = miniMaxBaseUrl;
        MiniMaxModel = miniMaxModel;
        MiniMaxVoices = miniMaxVoices;
        CustomEngines = customEngines;
        DefaultNarratorVoice = defaultNarratorVoice;
    }
}
