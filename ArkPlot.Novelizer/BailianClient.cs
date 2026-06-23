using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArkPlot.Novelizer;

public class BailianClient
{
    private readonly HttpClient _http;
    private readonly ApiConfig _config;
    private readonly Action<string>? _onLog;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public BailianClient(HttpClient http, ApiConfig config, Action<string>? onLog = null)
    {
        _http = http;
        _config = config;
        _onLog = onLog;
        _http.BaseAddress = new Uri(config.BaseUrl.EndsWith('/') ? config.BaseUrl : config.BaseUrl + "/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", config.ApiKey);
        _http.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
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
    /// 调用百炼 Chat Completions API（单轮，system + user）。
    /// </summary>
    public async Task<ChatResult> ChatAsync(string model, string systemPrompt, string userContent)
    {
        Log($"[DIAG] ChatAsync 开始。model={model}, userContent长度={userContent.Length}, max_tokens={_config.MaxTokens}");

        var messages = new List<ChatMessage>
        {
            new("system", systemPrompt),
            new("user", userContent)
        };

        var requestBody = BuildRequestBody(model, messages);
        return await SendChatRequestAsync(model, requestBody);
    }

    /// <summary>
    /// 多轮对话调用：接受预构建的 messages 列表（含 system / user / assistant 多轮）。
    /// 其余逻辑（平台字段、重试、token 统计）与 ChatAsync 一致。
    /// </summary>
    public async Task<ChatResult> ChatWithHistoryAsync(
        string model,
        IReadOnlyList<ChatMessage> messages)
    {
        Log($"[DIAG] ChatWithHistoryAsync 开始。model={model}, messages={messages.Count} 条");

        var requestBody = BuildRequestBody(model, messages);
        return await SendChatRequestAsync(model, requestBody);
    }

    /// <summary>
    /// 构建 messages 数组（原有 ChatAsync 的 messages 内联构造移入此方法）
    /// </summary>
    private Dictionary<string, object> BuildRequestBody(string model, IReadOnlyList<ChatMessage> messages)
    {
        var requestBody = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            ["max_tokens"] = _config.MaxTokens
        };

        if (_config.Provider == ApiProvider.DeepSeek)
        {
            requestBody["reasoning_effort"] = "high";
            requestBody["extra_body"] = new { thinking = new { type = "enabled" } };
        }
        else if (_config.Provider == ApiProvider.Bailian)
        {
            requestBody["extra_body"] = new { enable_thinking = _config.EnableThinking };
        }

        return requestBody;
    }

    /// <summary>
    /// 发送 POST 请求 + 重试 + 解析响应（从 ChatAsync 提取的共享逻辑）
    /// </summary>
    private async Task<ChatResult> SendChatRequestAsync(string model, Dictionary<string, object> requestBody)
    {
        var json = JsonSerializer.Serialize(requestBody, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        Log($"[DIAG] 请求体 {json.Length} 字节，开始发送 POST");

        HttpResponseMessage response;
        int attempt = 0;

        while (true)
        {
            attempt++;
            Log($"[DIAG] 第 {attempt} 次 POST 尝试...");

            var reqSw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                response = await _http.PostAsync("chat/completions", content);
                reqSw.Stop();
                Log($"[DIAG] 收到响应 status={response.StatusCode}，耗时 {reqSw.Elapsed.TotalSeconds:F1}s");
            }
            catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
            {
                reqSw.Stop();
                LogError($"[DIAG] POST 网络异常({ex.GetType().Name}): {ex.Message}，耗时 {reqSw.Elapsed.TotalSeconds:F1}s");

                if (attempt >= _config.MaxRetries)
                {
                    LogError($"[DIAG] 重试耗尽，网络异常");
                    throw new BailianException($"API 网络异常（已重试 {attempt} 次）: {ex.Message}", ex);
                }

                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                LogError($"  [{model}] 网络异常，{delay.TotalSeconds}s 后重试 ({attempt}/{_config.MaxRetries})...");
                Log($"[DIAG] 可重试错误，等待 {delay.TotalSeconds}s...");
                await Task.Delay(delay);
                continue;
            }

            if (response.IsSuccessStatusCode)
                break;

            if (attempt >= _config.MaxRetries)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                LogError($"[DIAG] 重试耗尽。status={response.StatusCode}, body={errorBody.Truncate(200)}");
                throw new BailianException(
                    $"API 调用失败（{response.StatusCode}），已重试 {attempt} 次: {errorBody.Truncate(500)}");
            }

            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                LogError($"  [{model}] {response.StatusCode}，{delay.TotalSeconds}s 后重试 ({attempt}/{_config.MaxRetries})...");
                Log($"[DIAG] 可重试错误，等待 {delay.TotalSeconds}s...");
                await Task.Delay(delay);
                continue;
            }

            var errBody = await response.Content.ReadAsStringAsync();
            LogError($"[DIAG] 不可重试错误。status={response.StatusCode}, body={errBody.Truncate(200)}");
            throw new BailianException($"API 调用失败（{response.StatusCode}）: {errBody.Truncate(500)}");
        }

        Log($"[DIAG] 读取响应体...");
        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var choice = doc.RootElement.GetProperty("choices")[0];
        var message = choice.GetProperty("message");

        var reasoning = message.TryGetProperty("reasoning_content", out var r) ? r.GetString() ?? "" : "";
        var answer = message.TryGetProperty("content", out var a) ? a.GetString() ?? "" : "";

        var usage = doc.RootElement.TryGetProperty("usage", out var u)
            ? new TokenUsage(
                u.GetProperty("prompt_tokens").GetInt32(),
                u.GetProperty("completion_tokens").GetInt32(),
                u.GetProperty("total_tokens").GetInt32())
            : null;

        Log($"[DIAG] SendChatRequestAsync 返回成功。answer={answer.Length}字符, reason={reasoning.Length}字符, usage={usage?.TotalTokens}");
        return new ChatResult(reasoning, answer, usage);
    }
}

/// <summary>
/// 多轮对话中的一条消息
/// </summary>
public record ChatMessage(string Role, string Content);

public record ChatResult(string ReasoningContent, string AnswerContent, TokenUsage? Usage);

public record TokenUsage(int PromptTokens, int CompletionTokens, int TotalTokens);

public class BailianException : Exception
{
    public BailianException(string message) : base(message) { }
    public BailianException(string message, Exception inner) : base(message, inner) { }
}

internal static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
}