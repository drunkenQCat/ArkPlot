using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArkPlot.Tts.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ArkPlot.Tts.Tests.Mocks;

/// <summary>
/// Mock TTS 服务器，用于 HttpTtsEngine 集成测试。
/// 随机端口避免 CI 并发冲突，支持自定义 Handler 和请求历史捕获。
/// </summary>
internal sealed class MockTtsServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly object _lock = new();

    /// <summary>动态分配的本地基地址（如 http://127.0.0.1:51234）。</summary>
    public string BaseUrl => _app.Urls.First();

    /// <summary>允许测试用例注入自定义合成逻辑。</summary>
    public Func<TtsSynthesisRequest, HttpContext, TtsSynthesisResponse>? SynthesisHandler { get; set; }

    /// <summary>GET /v1/voices 返回的音色列表。</summary>
    public List<VoiceEntry> AvailableVoices { get; set; } = DefaultVoices();

    /// <summary>请求历史记录（线程安全）。</summary>
    public List<(TtsSynthesisRequest Request, string? AuthHeader)> RequestHistory { get; } = [];

    private MockTtsServer(WebApplication app) => _app = app;

    /// <summary>启动 Mock 服务器。</summary>
    public static async Task<MockTtsServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders(); // 减少测试噪音

        var app = builder.Build();
        var server = new MockTtsServer(app);

        app.MapPost("/v1/tts", async (HttpContext context, [FromBody] TtsSynthesisRequest req) =>
        {
            string? authHeader = context.Request.Headers.Authorization;
            lock (server._lock)
                server.RequestHistory.Add((req, authHeader));

            if (server.SynthesisHandler is not null)
            {
                try
                {
                    return Results.Ok(server.SynthesisHandler(req, context));
                }
                catch (MockTtsException ex)
                {
                    return Results.Json(
                        new { error = ex.Message, code = ex.Code },
                        statusCode: ex.StatusCode);
                }
            }

            return Results.Ok(new TtsSynthesisResponse
            {
                AudioHex = SilentMp3Hex,
                AudioLengthMs = 200,
                UsageCharacters = req.Text.Length
            });
        });

        app.MapGet("/v1/voices", () => Results.Ok(new { voices = server.AvailableVoices }));

        await app.StartAsync();
        return server;
    }

    private static List<VoiceEntry> DefaultVoices() =>
    [
        new("female-01", "少女", "Female", "zh-CN"),
        new("male-01", "青年", "Male", "zh-CN"),
    ];

    // 最小有效 MP3 静音帧（ID3v2 标签 + MPEG frame header）
    private const string SilentMp3Hex =
        "4944330300000000002354454E430000000B0000034C616D65332E39382E3400" +
        "FFFB90000000000000000000000000000000000000000000000000000000000000";

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>Mock TTS 异常。</summary>
internal sealed class MockTtsException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    public MockTtsException(string message, int statusCode, string code)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}

/// <summary>TTS 合成请求。</summary>
internal record TtsSynthesisRequest(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("voice")] string Voice,
    [property: JsonPropertyName("speed")] double Speed,
    [property: JsonPropertyName("volume")] double Volume,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("output_format")] string OutputFormat
);

/// <summary>TTS 合成响应。</summary>
internal class TtsSynthesisResponse
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
