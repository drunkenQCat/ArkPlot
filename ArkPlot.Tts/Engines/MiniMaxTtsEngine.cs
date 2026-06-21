using MiniMax;

namespace ArkPlot.Tts.Engines;

/// <summary>
/// 基于 MiniMax 平台的 TTS 引擎实现。
/// 通过 ITtsEngine 接口与现有 EdgeTTS 并存。
/// </summary>
public class MiniMaxTtsEngine : ITtsEngine, IDisposable
{
    private readonly MiniMaxClient _client;
    private readonly string _model;
    private readonly HttpClient _downloadClient;

    /// <summary>
    /// 创建 MiniMax TTS 引擎。
    /// </summary>
    /// <param name="apiKey">MiniMax API Key。</param>
    /// <param name="model">模型标识，默认 "speech-2.8-hd"。</param>
    /// <param name="baseUrl">API 地址，默认 null（使用 SDK 默认值 https://api.minimaxi.com）。minimax.io 用户需传入 "https://api.minimax.io/"。</param>
    public MiniMaxTtsEngine(string apiKey, string model = "speech-2.8-hd", string? baseUrl = null)
    {
        _model = model;
        _downloadClient = new HttpClient();

        _client = new MiniMaxClient(
            baseUri: baseUrl != null ? new Uri(baseUrl) : null,
            authorizations: [new EndPointAuthorization
            {
                Type = "Http",
                SchemeId = "HttpBearer",
                Location = "Header",
                Name = "Bearer",
                Value = apiKey,
            }]);
    }

    /// <inheritdoc/>
    public async Task SynthesizeAsync(
        string text,
        string voice,
        string outputPath,
        string rate = "+0%",
        string volume = "+0%",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(text);

        var response = await _client.Speech.CreateTextToSpeechAsync(
            model: _model,
            text: text,
            voiceSetting: new TtsVoiceSetting
            {
                VoiceId = voice,
                Speed = ConvertRateToSpeed(rate),
                Vol = ConvertRateToSpeed(volume),
            },
            outputFormat: TextToSpeechRequestOutputFormat.Url,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (response.Data?.Audio == null)
            throw new InvalidOperationException("MiniMax TTS 合成失败：未返回音频数据。");

        await DownloadAudioAsync(response.Data.Audio, outputPath).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TtsVoiceInfo>> ListVoicesAsync()
    {
        // MiniMax 没有 ListVoices API，返回预定义音色列表
        var voices = MiniMaxVoicePool.AllVoices
            .Select(v => new TtsVoiceInfo(v.VoiceId, "zh-CN", v.Gender))
            .ToList()
            .AsReadOnly();
        return Task.FromResult<IReadOnlyList<TtsVoiceInfo>>(voices);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _client.Dispose();
        _downloadClient.Dispose();
    }

    // ========== 参数转换 ==========

    /// <summary>
    /// 将 EdgeTTS 风格的速率字符串转换为 MiniMax 浮点数。
    /// "+10%" → 1.1f, "-5%" → 0.95f, "+0%" → 1.0f
    /// </summary>
    internal static float ConvertRateToSpeed(string rate)
    {
        if (string.IsNullOrEmpty(rate) || rate.Length < 2) return 1.0f;
        if ((rate[0] == '+' || rate[0] == '-') && rate[^1] == '%')
        {
            if (float.TryParse(rate[1..^1], out var percent))
                return rate[0] == '+' ? 1 + percent / 100 : 1 - percent / 100;
        }
        return 1.0f;
    }

    private async Task DownloadAudioAsync(string audioUrl, string outputPath)
    {
        var bytes = await _downloadClient.GetByteArrayAsync(audioUrl).ConfigureAwait(false);
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(outputPath, bytes).ConfigureAwait(false);
    }
}