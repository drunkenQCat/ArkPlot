using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArkPlot.Vision;

/// <summary>
/// OpenAI 兼容视觉模型客户端（百炼、DeepSeek、自定义平台），支持 URL 或 Base64 图片。
/// 参照 ArkPlot.Novelizer.BailianClient 的调用模式，使用 OpenAI 兼容接口。
/// </summary>
public class BailianVisionClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly BailianVisionConfig _config;
    private readonly Action<string>? _onLog;

    private bool IsOmni => _config.Model.Contains("omni", StringComparison.OrdinalIgnoreCase);
    private bool IsQwen38Omni => _config.Model.StartsWith("qwen3.8-omni", StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public BailianVisionClient(BailianVisionConfig? config = null, Action<string>? onLog = null)
    {
        _config = config ?? new BailianVisionConfig();
        _onLog = onLog;
        _http = new HttpClient
        {
            BaseAddress = new Uri(_config.BaseUrl.EndsWith('/') ? _config.BaseUrl : _config.BaseUrl + "/"),
            Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds)
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
    }

    /// <summary>
    /// 允许外部注入 HttpClient（用于依赖注入场景）
    /// </summary>
    public BailianVisionClient(HttpClient http, BailianVisionConfig config, Action<string>? onLog = null)
    {
        _http = http;
        _config = config;
        _onLog = onLog;
        _http.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
    }

    private void Log(string msg)
    {
        Console.WriteLine(msg);
        _onLog?.Invoke(msg);
    }

    private void LogError(string msg)
    {
        Console.Error.WriteLine(msg);
        _onLog?.Invoke(msg);
    }

    /// <summary>
    /// 通过图片 URL 描述图片（百炼支持直接传 HTTP URL，无需下载和 Base64 编码）
    /// </summary>
    /// <param name="imageUrl">图片的 HTTP URL</param>
    /// <param name="userPrompt">可选的用户提示词，覆盖默认系统提示</param>
    /// <returns>图片描述文本</returns>
    public async Task<string> DescribeImageUrlAsync(string imageUrl, string? userPrompt = null)
    {
        var prompt = userPrompt ?? _config.SystemPrompt;

        // 百炼 OpenAI 兼容格式：image_url 对象
        var requestBody = new
        {
            model = _config.Model,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = prompt
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image_url", image_url = new { url = imageUrl } },
                        new { type = "text", text = "用小说场景描写的语言描述这张图片。如同在小说中描写一个场景。" }
                    }
                }
            },
            max_tokens = _config.MaxTokens,
            modalities = IsOmni ? new[] { "text" } : null,
            stream = IsOmni ? true : (bool?)null,
            reasoning_effort = IsQwen38Omni ? "none" : null,
            thinking = _config.ProviderName == "DeepSeek" ? new { type = "disabled" } : null
        };

        return await SendRequestAsync(requestBody, $"imageUrl={imageUrl.Truncate(80)}");
    }

    /// <summary>
    /// 描述 Base64 编码的图片（作为 Data URL 发送）
    /// </summary>
    /// <param name="base64Image">Base64 编码的图片数据</param>
    /// <param name="userPrompt">可选的用户提示词</param>
    /// <returns>图片描述文本</returns>
    public async Task<string> DescribeImageBase64Async(string base64Image, string? userPrompt = null)
    {
        var prompt = userPrompt ?? _config.SystemPrompt;

        // 百炼支持 Data URL 格式
        var requestBody = new
        {
            model = _config.Model,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = prompt
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{base64Image}" } },
                        new { type = "text", text = "用小说场景描写的语言描述这张图片。如同在小说中描写一个场景。" }
                    }
                }
            },
            max_tokens = _config.MaxTokens,
            modalities = IsOmni ? new[] { "text" } : null,
            stream = IsOmni ? true : (bool?)null,
            reasoning_effort = IsQwen38Omni ? "none" : null,
            thinking = _config.ProviderName == "DeepSeek" ? new { type = "disabled" } : null
        };

        return await SendRequestAsync(requestBody, "base64 image");
    }

    /// <summary>
    /// 描述本地图片文件（内部转为 Base64 Data URL 发送）
    /// </summary>
    /// <param name="imagePath">本地图片路径</param>
    /// <param name="userPrompt">可选的用户提示词</param>
    /// <returns>图片描述文本</returns>
    public async Task<string> DescribeImageAsync(string imagePath, string? userPrompt = null)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"图片文件不存在: {imagePath}");

        var imageBytes = await File.ReadAllBytesAsync(imagePath);
        var base64Image = Convert.ToBase64String(imageBytes);
        return await DescribeImageBase64Async(base64Image, userPrompt);
    }

    private async Task<string> SendRequestAsync(object requestBody, string logLabel)
    {
        var json = JsonSerializer.Serialize(requestBody, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        Log($"[{_config.ProviderName}Vision] 开始调用，model={_config.Model}, {logLabel}");

        HttpResponseMessage response;
        int attempt = 0;

        while (true)
        {
            attempt++;
            Log($"[{_config.ProviderName}Vision] 第 {attempt} 次请求尝试...");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                response = await _http.PostAsync("chat/completions", content);
                sw.Stop();
                Log($"[{_config.ProviderName}Vision] 收到响应 status={response.StatusCode}，耗时 {sw.Elapsed.TotalSeconds:F1}s");
            }
            catch (TaskCanceledException)
            {
                sw.Stop();
                var msg = $"[{_config.ProviderName}Vision] 请求超时！耗时 {sw.Elapsed.TotalSeconds:F1}s（Timeout={_config.TimeoutSeconds}s）";
                LogError(msg);
                throw new VisionException($"{_config.ProviderName} API 请求超时（{sw.Elapsed.TotalSeconds:F0}s）", new TimeoutException());
            }
            catch (Exception ex)
            {
                sw.Stop();
                LogError($"[{_config.ProviderName}Vision] 网络异常({ex.GetType().Name}): {ex.Message}");
                throw new VisionException($"{_config.ProviderName} 网络异常: {ex.Message}", ex);
            }

            if (response.IsSuccessStatusCode)
                break;

            if (attempt >= _config.MaxRetries)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                LogError($"[{_config.ProviderName}Vision] 重试耗尽。status={response.StatusCode}, body={errorBody.Truncate(200)}");
                throw new VisionException($"{_config.ProviderName} 调用失败（{response.StatusCode}），已重试 {attempt} 次: {errorBody.Truncate(500)}");
            }

            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                LogError($"[{_config.ProviderName}Vision] {response.StatusCode}，{delay.TotalSeconds}s 后重试 ({attempt}/{_config.MaxRetries})...");
                await Task.Delay(delay);
                continue;
            }

            var errBody = await response.Content.ReadAsStringAsync();
            LogError($"[{_config.ProviderName}Vision] 不可重试错误。status={response.StatusCode}, body={errBody.Truncate(200)}");
            throw new VisionException($"{_config.ProviderName} 调用失败（{response.StatusCode}）: {errBody.Truncate(500)}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        Log($"[{_config.ProviderName}Vision] 响应 {responseJson.Length} 字节，开始解析");

        string description;
        if (IsOmni)
        {
            description = ReadStreamText(responseJson);
        }
        else
        {
            using var doc = JsonDocument.Parse(responseJson);
            var choice = doc.RootElement.GetProperty("choices")[0];
            if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String && finish.GetString() != "stop")
                throw new VisionException($"{_config.ProviderName} 图片描述未正常完成（finish_reason={finish.GetString()}），未保存截断内容。");
            var message = choice.GetProperty("message");
            description = message.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
        }

        if (string.IsNullOrWhiteSpace(description))
            throw new VisionException($"{_config.ProviderName} 返回了空响应");

        Log($"[{_config.ProviderName}Vision] 描述完成，{description.Length} 字符");
        return description.Trim();
    }

    private static string ReadStreamText(string response)
    {
        var text = new StringBuilder();
        var completed = false;
        using var reader = new StringReader(response);
        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") { completed = true; break; }
            if (data.Length == 0) continue;
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out _))
                throw new VisionException("图片描述模型在流式响应中返回错误。");
            if (root.TryGetProperty("code", out var code) &&
                code.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) &&
                code.ToString() is not ("" or "0" or "200"))
                throw new VisionException($"图片描述模型在流式响应中返回错误：{code}");
            if (!root.TryGetProperty("choices", out var choices)) continue;
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String)
                {
                    if (finish.GetString() != "stop")
                        throw new VisionException($"图片描述未正常完成（finish_reason={finish.GetString()}），未保存截断内容。");
                    completed = true;
                }
                if (choice.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    text.Append(content.GetString());
            }
        }
        if (!completed)
            throw new VisionException("图片描述流式响应提前结束，未保存不完整内容。");
        return text.ToString();
    }

    public void Dispose()
    {
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 百炼视觉模型配置
/// </summary>
public record BailianVisionConfig
{
    public string ProviderName { get; init; } = "百炼";

    /// <summary>
    /// 百炼 API 基地址，默认 OpenAI 兼容接口
    /// </summary>
    public string BaseUrl { get; init; } = "https://dashscope.aliyuncs.com/compatible-mode/v1";

    /// <summary>
    /// 百炼 API Key
    /// </summary>
    public string ApiKey { get; init; } = Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY") ?? "";

    /// <summary>
    /// 模型名称，默认 qwen-vl-plus（支持视觉理解）
    /// 可选：qwen-vl-plus, qwen-vl-max, qwen3-vl-plus, qwen3-vl-flash 等
    /// </summary>
    public string Model { get; init; } = "qwen3-vl-flash";

    /// <summary>
    /// 请求超时秒数，默认 120 秒
    /// </summary>
    public int TimeoutSeconds { get; init; } = 120;

    /// <summary>
    /// 最大重试次数，默认 3 次
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// 系统提示词（叙事版 - 小说风格，禁止元评论）
    /// </summary>
    public string SystemPrompt { get; init; } = """
你是小说场景设定助手。描述图片时遵守以下规则：

1. 禁止元评论：不要说"这是一幅画"、"这张图片展示"等分析性开场白。
2. 禁止风格分析：不要提及"数字插画"、"动漫风格"、"游戏美术"等。
3. 禁止总结：不要写"总结："、"简而言之"等分析性文字。
4. 使用叙事语言：用小说场景描写的方式描述环境氛围、关键物体、人物姿态。
5. 控制字数：保持在200字以内，只关注叙事关键元素。
""";

    /// <summary>
    /// 最大生成 token 数，默认 2048
    /// </summary>
    public int MaxTokens { get; init; } = 2048;
}
