using System.Net;
using System.Text;
using System.Text.Json;
using ArkPlot.Vision;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

public class OmniVisionTests
{
    [Theory]
    [InlineData("qwen3.8-omni-flash", false)]
    [InlineData("qwen3.5-omni-flash", true)]
    public async Task Omni_RequestsTextOnlyAndCombinesStreamChunks(string model, bool useBase64)
    {
        using var http = new HttpClient(new FakeHandler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
            Assert.Equal("text", body.RootElement.GetProperty("modalities")[0].GetString());
            Assert.Equal(1, body.RootElement.GetProperty("modalities").GetArrayLength());
            if (model == "qwen3.8-omni-flash")
                Assert.Equal("none", body.RootElement.GetProperty("reasoning_effort").GetString());
            return """
                data: {"choices":[{"delta":{"role":"assistant","content":null}}]}

                data: {"choices":[{"delta":{"reasoning_content":"hidden thinking"}}]}

                data: {"choices":[{"delta":{"content":"白色"}}]}

                data: {"choices":[{"delta":{"content":"高塔。"}}]}

                data: {"choices":[],"usage":{"total_tokens":10}}

                data: [DONE]

                """;
        })) { BaseAddress = new Uri("https://example.invalid/v1/") };
        using var client = new BailianVisionClient(http, new BailianVisionConfig { Model = model, ApiKey = "test" });
        var result = useBase64 ? await client.DescribeImageBase64Async("image-data")
            : await client.DescribeImageUrlAsync("https://example.invalid/image.png");
        Assert.Equal("白色高塔。", result);
    }

    [Fact]
    public async Task Omni_RejectsTruncatedStreamWithoutCompletion()
    {
        await AssertStreamFails("""data: {"choices":[{"delta":{"content":"partial"}}]}""");
    }

    [Theory]
    [InlineData("length")]
    [InlineData("content_filter")]
    [InlineData("tool_calls")]
    public async Task Omni_RejectsAbnormalFinishEvenWithDoneMarker(string reason)
    {
        await AssertStreamFails("data: " + JsonSerializer.Serialize(new { choices = new[]
            { new { delta = new { content = "partial" }, finish_reason = reason } } }) + "\n\ndata: [DONE]\n");
    }

    [Theory]
    [InlineData("{\"error\":{\"message\":\"failed\"}}")]
    [InlineData("{\"code\":\"DataInspectionFailed\",\"message\":\"blocked\"}")]
    public async Task Omni_RejectsErrorAfterPartialOutput(string error)
    {
        await AssertStreamFails("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\ndata: " + error + "\n\ndata: [DONE]\n");
    }

    [Fact]
    public async Task Omni_AcceptsExplicitNormalFinishWithoutDoneMarker()
    {
        using var http = new HttpClient(new FakeHandler(_ => Task.FromResult(
            """data: {"choices":[{"delta":{"content":"complete"},"finish_reason":"stop"}]}""")))
            { BaseAddress = new Uri("https://example.invalid/v1/") };
        using var client = new BailianVisionClient(http, new BailianVisionConfig { Model = "qwen3.8-omni-flash", ApiKey = "test" });
        Assert.Equal("complete", await client.DescribeImageUrlAsync("https://example.invalid/image.png"));
    }

    private static async Task AssertStreamFails(string stream)
    {
        using var http = new HttpClient(new FakeHandler(_ => Task.FromResult(stream)))
            { BaseAddress = new Uri("https://example.invalid/v1/") };
        using var client = new BailianVisionClient(http, new BailianVisionConfig { Model = "qwen3.8-omni-flash", ApiKey = "test" });
        await Assert.ThrowsAsync<VisionException>(() => client.DescribeImageUrlAsync("https://example.invalid/image.png"));
    }

    [Fact]
    public async Task StandardVision_KeepsNonStreamingRequestAndResponse()
    {
        using var http = new HttpClient(new FakeHandler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.False(body.RootElement.TryGetProperty("stream", out _));
            Assert.False(body.RootElement.TryGetProperty("modalities", out _));
            return """{"choices":[{"message":{"content":"普通图片描述"}}]}""";
        })) { BaseAddress = new Uri("https://example.invalid/v1/") };
        using var client = new BailianVisionClient(http, new BailianVisionConfig { Model = "qwen3-vl-plus", ApiKey = "test" });
        Assert.Equal("普通图片描述", await client.DescribeImageUrlAsync("https://example.invalid/image.png"));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<string>> response) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            new(HttpStatusCode.OK) { Content = new StringContent(await response(request), Encoding.UTF8, "text/event-stream") };
    }
}
