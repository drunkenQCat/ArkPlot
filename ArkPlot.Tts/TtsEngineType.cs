namespace ArkPlot.Tts;

/// <summary>
/// TTS 引擎类型。
/// </summary>
public enum TtsEngineType
{
    /// <summary>Microsoft Edge TTS（免费，WebSocket 协议）。</summary>
    EdgeTts,

    /// <summary>MiniMax TTS（商业 API，HTTP 协议）。</summary>
    MiniMax,
}