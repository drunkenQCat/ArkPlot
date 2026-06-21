using ArkPlot.Tts.Engines;
using ArkPlot.Tts.Models;

namespace ArkPlot.Tts;

/// <summary>
/// 基于统一音色池的路由引擎。
/// 按 voiceId 反查所属引擎，再把合成请求路由到正确的后端。
/// </summary>
public sealed class RoutedTtsEngine : ITtsEngine, IDisposable
{
    private readonly VoiceManagerUnified _voices;
    private readonly EdgeTtsEngine _edge = new();
    private readonly MiniMaxTtsEngine? _miniMax;
    private readonly Dictionary<Guid, HttpTtsEngine> _customEngines = new();

    public RoutedTtsEngine(TtsSettings settings, VoiceManagerUnified voices)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _voices = voices ?? throw new ArgumentNullException(nameof(voices));

        if (settings.MiniMaxEnabled && !string.IsNullOrWhiteSpace(settings.MiniMaxApiKey))
        {
            _miniMax = new MiniMaxTtsEngine(
                settings.MiniMaxApiKey!,
                settings.MiniMaxModel ?? "speech-2.8-hd",
                settings.MiniMaxBaseUrl);
        }

        foreach (var provider in settings.CustomEngines)
        {
            _customEngines[provider.Id] = new HttpTtsEngine(provider);
        }
    }

    public Task<IReadOnlyList<TtsVoiceInfo>> ListVoicesAsync()
    {
        var voices = _voices.Pool
            .Select(v => new TtsVoiceInfo(
                v.Entry.VoiceId,
                v.Entry.Locale ?? "zh-CN",
                v.Entry.Gender))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<TtsVoiceInfo>>(voices);
    }

    public Task SynthesizeAsync(
        string text,
        string voice,
        string outputPath,
        string rate = "+0%",
        string volume = "+0%",
        CancellationToken cancellationToken = default)
    {
        var assignment = _voices.FindAssignment(voice);
        var engine = ResolveEngine(assignment);
        return engine.SynthesizeAsync(text, assignment.VoiceId, outputPath, rate, volume, cancellationToken);
    }

    public void Dispose()
    {
        _miniMax?.Dispose();
    }

    private ITtsEngine ResolveEngine(VoiceAssignment assignment)
    {
        return assignment.Engine switch
        {
            EngineType.EdgeTts => _edge,
            EngineType.MiniMax when _miniMax != null => _miniMax,
            EngineType.Custom when assignment.CustomEngineId.HasValue
                && _customEngines.TryGetValue(assignment.CustomEngineId.Value, out var custom) => custom,
            EngineType.MiniMax => throw new InvalidOperationException(
                $"MiniMax 音色 `{assignment.VoiceId}` 已被选中，但当前未配置可用的 MiniMax 引擎。"),
            EngineType.Custom => throw new InvalidOperationException(
                $"自定义音色 `{assignment.VoiceId}` 对应的引擎未找到。"),
            _ => throw new InvalidOperationException(
                $"音色 `{assignment.VoiceId}` 的引擎类型 `{assignment.Engine}` 无法路由。")
        };
    }
}
