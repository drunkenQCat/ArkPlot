namespace ArkPlot.Tts;

/// <summary>
/// TTS 引擎类型。
/// </summary>
public enum EngineType
{
    /// <summary>Microsoft Edge TTS（免费，WebSocket 协议）。</summary>
    EdgeTts,

    /// <summary>MiniMax TTS（商业 API，HTTP 协议）。</summary>
    MiniMax,

    /// <summary>自定义 TTS 引擎（用户配置，HTTP 协议）。</summary>
    Custom,
}