namespace ArkPlot.Novelizer;

/// <summary>
/// 不调用真实 API 的 Mock 客户端，用于开发调试时跑通小说化管线。
/// 覆盖 ChatAsync / ChatWithHistoryAsync 返回预置占位文本，不产生任何网络请求。
/// </summary>
public class MockBailianClient : BailianClient
{
    private readonly Action<string>? _onLog;

    public MockBailianClient(HttpClient http, ApiConfig config, Action<string>? onLog = null)
        : base(http, config, onLog)
    {
        _onLog = onLog;
    }

    public override Task<ChatResult> ChatAsync(string model, string systemPrompt, string userContent)
    {
        _onLog?.Invoke(
            $"[Mock] 跳过真实 API。model={model}, 输入长度={userContent.Length}"
        );
        return Task.FromResult(
            new ChatResult(
                BuildMockReasoning(userContent),
                BuildMockContent(userContent),
                new TokenUsage(0, 0, 0)));
    }

    public override Task<ChatResult> ChatWithHistoryAsync(
        string model,
        IReadOnlyList<ChatMessage> messages)
    {
        var user = messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";
        _onLog?.Invoke(
            $"[Mock] 跳过真实 API。model={model}, 消息数={messages.Count}"
        );
        return Task.FromResult(
            new ChatResult(
                BuildMockReasoning(user),
                BuildMockContent(user),
                new TokenUsage(0, 0, 0)));
    }

    /// <summary>生成一段模拟的模型思考过程，用于在日志中展示「仍在思考」的完整链路。</summary>
    private static string BuildMockReasoning(string input)
    {
        var head = input.Length <= 200 ? input : input[..200] + "...";
        return $"""
（Mock 思考过程，未调用真实 API）

1. 先通读输入，识别剧情所处的场景与登场角色；
2. 判断当前叙事单元是否包含场景切换、角色对话或心理描写；
3. 决定采用第三人称有限视角，聚焦当前核心角色的感官与判断；
4. 检查是否出现 <aside> 视觉素材，需转化为原创叙事语言而非照抄；
5. 组织长段落结构，避免连续单句成段。

输入预览（前 200 字符）：
{head}
""";
    }

    private static string BuildMockContent(string input)
    {
        var head = input.Length <= 300 ? input : input[..300] + "...";
        return $"""
（Mock 输出，未调用真实 API）

这是小说化管线的占位结果，用于验证流程是否跑通。

输入预览：
{head}
""";
    }
}