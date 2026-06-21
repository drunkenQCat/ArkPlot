namespace ArkPlot.Tts.Engines;

/// <summary>
/// 带降级容错的 TTS 引擎装饰器。
/// 主引擎失败时自动降级到备用引擎。
/// </summary>
public class FallbackTtsEngine : ITtsEngine
{
    private readonly ITtsEngine _primary;
    private readonly ITtsEngine _fallback;
    private readonly Action<string>? _onFallback;

    /// <summary>
    /// 创建降级引擎。
    /// </summary>
    /// <param name="primary">主引擎（如 MiniMax）。</param>
    /// <param name="fallback">备用引擎（如 EdgeTTS）。</param>
    /// <param name="onFallback">降级时触发的回调（用于日志/告警）。</param>
    public FallbackTtsEngine(
        ITtsEngine primary,
        ITtsEngine fallback,
        Action<string>? onFallback = null)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _onFallback = onFallback;
    }

    /// <summary>降级次数。</summary>
    public int FallbackCount { get; private set; }

    /// <inheritdoc/>
    public async Task SynthesizeAsync(
        string text,
        string voice,
        string outputPath,
        string rate = "+0%",
        string volume = "+0%",
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _primary.SynthesizeAsync(text, voice, outputPath, rate, volume, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FallbackCount++;
            var message = $"TTS 主引擎失败，降级到备用引擎: {ex.GetType().Name}: {ex.Message}";
            _onFallback?.Invoke(message);

            await _fallback.SynthesizeAsync(text, voice, outputPath, rate, volume, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TtsVoiceInfo>> ListVoicesAsync()
    {
        return _primary.ListVoicesAsync();
    }

    /// <summary>
    /// 释放资源（如两个引擎都实现了 IDisposable）。
    /// </summary>
    public void Dispose()
    {
        (_primary as IDisposable)?.Dispose();
        (_fallback as IDisposable)?.Dispose();
    }
}