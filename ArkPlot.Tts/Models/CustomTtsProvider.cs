namespace ArkPlot.Tts.Models;

/// <summary>
/// 自定义 TTS 引擎配置
/// </summary>
public record CustomTtsProvider
{
    /// <summary>
    /// 内部稳定标识，不随改名失效
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// 显示名称，如 "我的本地 TTS"
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// API 地址，如 "http://192.168.1.100:7860"
    /// </summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// API Key，可选，留空则不发送 Authorization header
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// 默认模型标识，可选
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// 该引擎提供的音色列表，可手动维护或从服务器获取
    /// </summary>
    public VoiceEntry[] Voices { get; init; } = [];

    public CustomTtsProvider() { }

    public CustomTtsProvider(Guid id, string name, string baseUrl, string? apiKey, string? model, VoiceEntry[] voices)
    {
        Id = id;
        Name = name;
        BaseUrl = baseUrl;
        ApiKey = apiKey;
        Model = model;
        Voices = voices;
    }
}
