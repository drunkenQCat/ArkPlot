using ArkPlot.Tts.Engines;
using ArkPlot.Tts.Models;
using SqlSugar;

namespace ArkPlot.Tts;

/// <summary>
/// 统一 TTS 引擎工厂：支持多引擎并存，根据音色分配自动路由到对应引擎。
/// </summary>
public class UnifiedTtsEngineFactory
{
    private readonly TtsSettings _settings;
    private readonly Dictionary<EngineType, ITtsEngine> _engines = new();
    private readonly Dictionary<Guid, ITtsEngine> _customEngines = new();

    /// <summary>
    /// 创建统一引擎工厂。
    /// </summary>
    /// <param name="settings">TTS 配置。</param>
    /// <param name="onFallback">MiniMax 降级到 EdgeTTS 时的回调（可选）。</param>
    public UnifiedTtsEngineFactory(TtsSettings settings, Action<string>? onFallback = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        InitializeEngines(onFallback);
    }

    /// <summary>
    /// 根据音色分配获取对应的合成引擎。
    /// </summary>
    public ITtsEngine GetEngine(VoiceAssignment assignment)
    {
        if (assignment.Engine == EngineType.Custom && assignment.CustomEngineId.HasValue)
        {
            if (_customEngines.TryGetValue(assignment.CustomEngineId.Value, out var engine))
                return engine;
            throw new InvalidOperationException($"自定义引擎 {assignment.CustomEngineId.Value} 未找到");
        }

        if (_engines.TryGetValue(assignment.Engine, out var builtinEngine))
            return builtinEngine;

        // 兜底：EdgeTTS
        return _engines[EngineType.EdgeTts];
    }

    /// <summary>
    /// 根据音色分配获取对应的合成引擎。
    /// </summary>
    public ITtsEngine GetEngine(EngineType engineType, Guid? customEngineId = null)
    {
        if (engineType == EngineType.Custom && customEngineId.HasValue)
        {
            if (_customEngines.TryGetValue(customEngineId.Value, out var engine))
                return engine;
            throw new InvalidOperationException($"自定义引擎 {customEngineId.Value} 未找到");
        }

        if (_engines.TryGetValue(engineType, out var builtinEngine))
            return builtinEngine;

        return _engines[EngineType.EdgeTts];
    }

    /// <summary>
    /// 创建统一音色池管理器。
    /// </summary>
    public VoiceManagerUnified CreateVoiceManager(SqlSugarClient? db = null)
    {
        var pool = VoicePoolBuilder.Build(_settings);
        return new VoiceManagerUnified(pool, _settings.DefaultNarratorVoice, db);
    }

    /// <summary>
    /// 合成文本到音频文件（自动路由到对应引擎）。
    /// </summary>
    public async Task SynthesizeAsync(
        VoiceAssignment assignment,
        string text,
        string outputPath,
        string rate = "+0%",
        string volume = "+0%",
        CancellationToken cancellationToken = default)
    {
        var engine = GetEngine(assignment);
        await engine.SynthesizeAsync(text, assignment.VoiceId, outputPath, rate, volume, cancellationToken);
    }

    /// <summary>
    /// 释放所有引擎资源。
    /// </summary>
    public void Dispose()
    {
        foreach (var engine in _engines.Values)
            (engine as IDisposable)?.Dispose();
        foreach (var engine in _customEngines.Values)
            (engine as IDisposable)?.Dispose();
    }

    private void InitializeEngines(Action<string>? onFallback)
    {
        // EdgeTTS（始终可用，作为兜底）
        if (_settings.EdgeTtsEnabled)
        {
            _engines[EngineType.EdgeTts] = new EdgeTtsEngine();
        }
        else
        {
            // 即使禁用，也保留一个 EdgeTTS 实例作为兜底
            _engines[EngineType.EdgeTts] = new EdgeTtsEngine();
        }

        // MiniMax（需要 API Key）
        if (_settings.MiniMaxEnabled && !string.IsNullOrEmpty(_settings.MiniMaxApiKey))
        {
            var miniMax = new MiniMaxTtsEngine(
                _settings.MiniMaxApiKey!,
                _settings.MiniMaxModel ?? "speech-2.8-hd",
                _settings.MiniMaxBaseUrl);

            // MiniMax 自动包裹 FallbackTtsEngine，失败时降级到 EdgeTTS
            _engines[EngineType.MiniMax] = new FallbackTtsEngine(
                miniMax,
                _engines[EngineType.EdgeTts],
                onFallback);
        }

        // 自定义引擎
        foreach (var provider in _settings.CustomEngines)
        {
            var httpEngine = new HttpTtsEngine(provider);
            _customEngines[provider.Id] = new FallbackTtsEngine(
                httpEngine,
                _engines[EngineType.EdgeTts],
                onFallback);
        }
    }
}
