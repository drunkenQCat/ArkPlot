using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArkPlot.Tts.Models;

namespace ArkPlot.Tts.Engines;

/// <summary>
/// 自定义 HTTP TTS 引擎，通过标准 HTTP API 调用外部 TTS 服务
/// </summary>
public class HttpTtsEngine : ITtsEngine
{
    private readonly CustomTtsProvider _provider;
    private readonly HttpClient _httpClient;

    public HttpTtsEngine(CustomTtsProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _httpClient = new HttpClient();

        // 配置 Authorization header（如果提供了 API Key）
        if (!string.IsNullOrEmpty(_provider.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _provider.ApiKey);
        }
    }

    /// <summary>
    /// 合成文本到音频文件
    /// </summary>
    public async Task SynthesizeAsync(
        string text,
        string voice,
        string outputPath,
        string rate = "+0%",
        string volume = "+0%",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(text);
        ArgumentNullException.ThrowIfNullOrEmpty(voice);
        ArgumentNullException.ThrowIfNullOrEmpty(outputPath);

        // 转换 rate/volume 格式（+10% → 1.1）
        var speed = ConvertPercentageToMultiplier(rate);
        var volumeMultiplier = ConvertPercentageToMultiplier(volume);

        var request = new
        {
            text,
            voice,
            speed,
            volume = volumeMultiplier,
            model = _provider.Model,
            output_format = "url"  // 优先使用 url 模式，便于下载
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{_provider.BaseUrl.TrimEnd('/')}/v1/tts",
                request,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SynthesisResponse>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("TTS 服务返回空响应");

            // 下载音频文件
            byte[] audioBytes;
            if (!string.IsNullOrEmpty(result.AudioUrl))
            {
                // url 模式：从 URL 下载
                audioBytes = await _httpClient.GetByteArrayAsync(result.AudioUrl, cancellationToken);
            }
            else if (!string.IsNullOrEmpty(result.AudioHex))
            {
                // hex 模式：解码十六进制字符串
                audioBytes = Convert.FromHexString(result.AudioHex);
            }
            else
            {
                throw new InvalidOperationException("TTS 服务未返回音频数据（audio_url 和 audio_hex 均为空）");
            }

            // 写入文件
            await File.WriteAllBytesAsync(outputPath, audioBytes, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"TTS 服务请求失败：{ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("TTS 服务请求超时", ex);
        }
    }

    /// <summary>
    /// 列出引擎配置中的音色（从 CustomTtsProvider.Voices 读取）
    /// </summary>
    public Task<IReadOnlyList<TtsVoiceInfo>> ListVoicesAsync()
    {
        var voices = _provider.Voices
            .Select(v => new TtsVoiceInfo(v.VoiceId, v.Locale ?? "zh-CN", v.Gender))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<TtsVoiceInfo>>(voices);
    }

    /// <summary>
    /// 从服务器动态获取音色列表（调用 GET /v1/voices）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务器返回的音色列表</returns>
    public async Task<IReadOnlyList<VoiceEntry>> FetchVoicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_provider.BaseUrl.TrimEnd('/')}/v1/voices",
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<VoicesResponse>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("音色列表服务返回空响应");

            return result.Voices ?? [];
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"获取音色列表失败：{ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("获取音色列表超时", ex);
        }
    }

    /// <summary>
    /// 将 "+10%" 格式的字符串转换为 1.1 这样的乘数
    /// </summary>
    private static double ConvertPercentageToMultiplier(string percentage)
    {
        if (string.IsNullOrWhiteSpace(percentage))
            return 1.0;

        var cleaned = percentage.Replace("%", "").Trim();
        if (cleaned.StartsWith("+"))
        {
            if (double.TryParse(cleaned[1..], out var value))
                return 1.0 + value / 100.0;
        }
        else if (cleaned.StartsWith("-"))
        {
            if (double.TryParse(cleaned[1..], out var value))
                return 1.0 - value / 100.0;
        }
        else if (double.TryParse(cleaned, out var value))
        {
            return value / 100.0;
        }

        return 1.0;
    }

    /// <summary>
    /// TTS 合成响应
    /// </summary>
    private class SynthesisResponse
    {
        [JsonPropertyName("audio_url")]
        public string? AudioUrl { get; set; }

        [JsonPropertyName("audio_hex")]
        public string? AudioHex { get; set; }

        [JsonPropertyName("audio_length_ms")]
        public int AudioLengthMs { get; set; }

        [JsonPropertyName("usage_characters")]
        public int UsageCharacters { get; set; }
    }

    /// <summary>
    /// 音色列表响应
    /// </summary>
    private class VoicesResponse
    {
        [JsonPropertyName("voices")]
        public VoiceEntry[]? Voices { get; set; }
    }
}
