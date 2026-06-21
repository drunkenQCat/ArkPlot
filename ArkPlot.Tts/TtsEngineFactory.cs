using ArkPlot.Tts.Engines;
using SqlSugar;

namespace ArkPlot.Tts;

/// <summary>
/// TTS 引擎工厂：根据配置创建引擎实例和音色管理器。
/// </summary>
public static class TtsEngineFactory
{
    /// <summary>
    /// 创建 TTS 引擎实例。
    /// MiniMax 引擎自动包裹 FallbackTtsEngine，失败时降级到 EdgeTTS。
    /// </summary>
    /// <param name="engineType">引擎类型。</param>
    /// <param name="apiKey">API Key（MiniMax 需要，EdgeTTS 忽略）。</param>
    /// <param name="model">模型标识（MiniMax 需要，默认 speech-2.8-hd）。</param>
    /// <param name="baseUrl">API 地址（MiniMax 需要，null 用 SDK 默认值）。</param>
    /// <param name="onFallback">降级回调（用于日志/告警）。</param>
    public static ITtsEngine CreateEngine(
        TtsEngineType engineType,
        string? apiKey = null,
        string model = "speech-2.8-hd",
        string? baseUrl = null,
        Action<string>? onFallback = null)
    {
        return engineType switch
        {
            TtsEngineType.EdgeTts => new EdgeTtsEngine(),
            TtsEngineType.MiniMax => CreateMiniMaxWithFallback(apiKey, model, baseUrl, onFallback),
            _ => throw new ArgumentOutOfRangeException(nameof(engineType), engineType, null)
        };
    }

    /// <summary>
    /// 创建音色管理器。
    /// </summary>
    /// <param name="engineType">引擎类型。</param>
    /// <param name="db">可选的 SqlSugarClient，用于音色分配持久化。</param>
    public static VoiceManager CreateVoiceManager(
        TtsEngineType engineType,
        SqlSugarClient? db = null)
    {
        return new VoiceManager(db, engineType);
    }

    /// <summary>
    /// 尝试根据引擎类型获取 API Key（环境变量 → 返回 null）。
    /// EdgeTTS 不需要 API Key，直接返回 null。
    /// </summary>
    public static string? ResolveApiKey(TtsEngineType engineType)
    {
        return engineType switch
        {
            TtsEngineType.EdgeTts => null,
            TtsEngineType.MiniMax => Environment.GetEnvironmentVariable("MINIMAX_API_KEY"),
            _ => null
        };
    }

    /// <summary>
    /// 检查指定的引擎类型是否可用（API Key 已配置或无需 API Key）。
    /// </summary>
    public static bool IsEngineAvailable(TtsEngineType engineType)
    {
        return engineType switch
        {
            TtsEngineType.EdgeTts => true,
            TtsEngineType.MiniMax => !string.IsNullOrEmpty(ResolveApiKey(engineType)),
            _ => false
        };
    }

    /// <summary>
    /// 获取可用引擎列表。
    /// </summary>
    public static TtsEngineType[] GetAvailableEngines()
    {
        var engines = new List<TtsEngineType> { TtsEngineType.EdgeTts };
        if (IsEngineAvailable(TtsEngineType.MiniMax))
            engines.Add(TtsEngineType.MiniMax);
        return engines.ToArray();
    }

    private static ITtsEngine CreateMiniMaxWithFallback(
        string? apiKey, string model, string? baseUrl, Action<string>? onFallback)
    {
        var key = apiKey ?? ResolveApiKey(TtsEngineType.MiniMax);
        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException(
                "MiniMax API Key 未配置。请设置环境变量 MINIMAX_API_KEY。");

        var miniMax = new MiniMaxTtsEngine(key, model, baseUrl);
        var edgeTts = new EdgeTtsEngine();
        return new FallbackTtsEngine(miniMax, edgeTts, onFallback);
    }
}