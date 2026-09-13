using Xunit;

namespace ArkPlot.Novelizer.Tests;

public class ChapterProcessorMultiTurnTests
{
    [Fact]
    public async Task MultiTurn_WithoutDashSeparators_SplitsAndSendsPriorAssistantAnswer()
    {
        var client = new RecordingClient();
        var processor = new ChapterProcessor(
            client, "system", _ => { }, _ => { },
            maxConcurrency: 1, enableMultiTurn: true, chunkSize: 50);

        var result = await processor.ProcessAllAsync(
            [new Chapter(0, "无分隔符长章", new string('文', 180))], "test-model");

        Assert.True(result[0].IsSuccess);
        Assert.True(client.HistoryCalls.Count > 1, "没有 --- 分隔符的长章也必须进入多轮请求。");
        Assert.Contains(client.HistoryCalls[1], m => m.Role == "assistant" && m.Content == "回复-1");
    }

    [Fact]
    public async Task MultiTurn_TokenThreshold_TriggersCompression_WhenIntervalIsDisabled()
    {
        var client = new RecordingClient();
        var processor = new ChapterProcessor(
            client, "system", _ => { }, _ => { },
            maxConcurrency: 1, enableMultiTurn: true, chunkSize: 50,
            compressInterval: 0, compressThresholdTokens: 100);

        await processor.ProcessAllAsync(
            [new Chapter(0, "阈值压缩", new string('文', 180))], "test-model");

        Assert.Contains(client.HistoryCalls,
            call => call.Last().Role == "user" && call.Last().Content.Contains("紧凑的情节摘要"));
    }

    [Fact]
    public async Task SingleTurn_TracePrompt_MatchesTheActualUserRequest()
    {
        var client = new RecordingClient();
        string? tracePrompt = null;
        var processor = new ChapterProcessor(
            client, "system", _ => { }, _ => { },
            onThought: (_, prompt, _, _, _) => tracePrompt = prompt);

        await processor.ProcessAllAsync([new Chapter(0, "短章", "原始剧情")], "test-model");

        Assert.Single(client.SingleCalls);
        Assert.NotNull(tracePrompt);
        Assert.Contains(client.SingleCalls[0].UserContent, tracePrompt!);
        Assert.Equal("请小说化以下内容：\n\n---\n\n原始剧情", client.SingleCalls[0].UserContent);
    }

    private sealed class RecordingClient : BailianClient
    {
        public RecordingClient()
            : base(new HttpClient(), new ApiConfig { Provider = ApiProvider.Bailian, ApiKey = "fake" }) { }

        public List<(string Model, string UserContent)> SingleCalls { get; } = new();
        public List<IReadOnlyList<ChatMessage>> HistoryCalls { get; } = new();

        public override Task<ChatResult> ChatAsync(string model, string systemPrompt, string userContent)
        {
            SingleCalls.Add((model, userContent));
            return Task.FromResult(new ChatResult("", "单轮回复", new TokenUsage(100, 10, 110)));
        }

        public override Task<ChatResult> ChatWithHistoryAsync(string model, IReadOnlyList<ChatMessage> messages)
        {
            var copy = messages.ToList();
            HistoryCalls.Add(copy);
            var isCompress = copy.Last().Content.Contains("紧凑的情节摘要");
            var answer = isCompress ? "压缩摘要" : $"回复-{HistoryCalls.Count}";
            return Task.FromResult(new ChatResult("", answer, new TokenUsage(100, 10, 110)));
        }
    }
}
