using System.Net;
using ArkPlot.Novelizer;
using Xunit;

namespace ArkPlot.Novelizer.Tests;

/// <summary>
/// 验证 MockBailianClient：不发起任何网络请求，且 NovelizerPipeline 用 Mock
/// 能端到端跑通并产出小说文件（用于小说化开发调试）。
/// </summary>
public class MockBailianClientTests
{
    // 任何网络访问都被拒绝的 handler——若 Mock 尝试发请求会抛异常
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Mock 不应发起任何网络请求");
    }

    private static ApiConfig Config() => new()
    {
        Provider = ApiProvider.Bailian,
        ApiKey = "",
        BaseUrl = "https://api.example.com",
        MaxTokens = 1000
    };

    private static MockBailianClient CreateClient() =>
        new(new HttpClient(new ThrowingHandler()), Config());

    [Fact]
    public async Task ChatAsync_ReturnsPlaceholder_WithoutNetworkCall()
    {
        var client = CreateClient();
        var result = await client.ChatAsync("deepseek-v4-flash", "system", "这是用户输入");

        Assert.False(string.IsNullOrEmpty(result.AnswerContent));
        Assert.Contains("这是用户输入", result.AnswerContent);
        Assert.NotNull(result.Usage);
    }

    [Fact]
    public void ChatAsync_ReturnsMockReasoning_ForLogDisplay()
    {
        // Mock 应返回一段可用于日志展示的思考过程（ReasoningContent 非空）
        var client = CreateClient();
        var result = client.ChatAsync("deepseek-v4-flash", "system", "剧情输入")
            .GetAwaiter().GetResult();

        Assert.False(string.IsNullOrEmpty(result.ReasoningContent));
        Assert.Contains("思考", result.ReasoningContent);
    }

    [Fact]
    public async Task ChatWithHistoryAsync_ReturnsPlaceholder_WithoutNetworkCall()
    {
        var client = CreateClient();
        var messages = new List<ChatMessage>
        {
            new("system", "system"),
            new("user", "history user content"),
        };
        var result = await client.ChatWithHistoryAsync("deepseek-v4-flash", messages);

        Assert.False(string.IsNullOrEmpty(result.AnswerContent));
        Assert.Contains("history user content", result.AnswerContent);
    }

    [Fact]
    public async Task NovelizerPipeline_WithMock_ProducesNovelFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arkplot_mock_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var mdPath = Path.Combine(dir, "mock_chapter.md");
        File.WriteAllText(mdPath, """
## 第一章
**角色A**：你好。
角色A站在那里。
""");

        try
        {
            var client = CreateClient();
            var pipeline = new NovelizerPipeline(
                client,
                Config(),
                enableMultiTurn: false,
                useMock: true
            );

            var thrown = await Record.ExceptionAsync(async () =>
                await pipeline.ProcessMdFileAsync(mdPath, "deepseek-v4-flash", dir));

            Assert.Null(thrown);

            var novelPath = NovelComposer.GetNovelPath(mdPath, "deepseek-v4-flash");
            Assert.True(File.Exists(novelPath), $"应产出小说文件: {novelPath}");
            var content = File.ReadAllText(novelPath);
            Assert.False(string.IsNullOrEmpty(content));
            Assert.Contains("Mock 输出", content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}