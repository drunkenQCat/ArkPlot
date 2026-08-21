using ArkPlot.Arknights;
using ArkPlot.Core.Model;
using ArkPlot.Core.Utilities.WorkFlow.StoryDocument;
using Xunit;

namespace ArkPlot.Novelizer.Tests;

/// <summary>
/// MdReconstructor 结构化回归测试 — 孤星 CW-ST-1 受控样例。
///
/// 取代易碎的逐字节 Golden 比对（MdReconstructorGoldenTests）：
///  1. 输入完全自包含（LoneTrailControlledInput 代码构造，模拟 AkpProcessor 传播后的形态），
///     不读 ArkPlot.Avalonia\bin\Debug 的 arkplot.db，不随程序/DB 状态漂移。
///  2. 断言面向"结构不变量"：aside 分类、立绘内联位置、跨名标注、媒体清理、分组，
///     只对语义契约敏感，容忍空白/换行/排版类的合理重构。
///  3. 无基线文件、无"刷新 Golden"机制需求 — 结构断言锚定的是渲染契约而非某次运行快照。
///
/// 立绘内联/跨名机制均由 ArkPlot.Adapters\ArkPlot.Core.Tests 的单测另行精测，
/// 本类验证它们在"受控整章输入 + 两种渲染模式"下的集成行为。
/// </summary>
public class MdReconstructorStructureTests
{
    // ─── 字段与入口 ───

    private static readonly string ChapterTitle = LoneTrailControlledInput.ChapterTitle;

    /// <summary>生成受控输入的 Readable / Prompt 两种输出。</summary>
    private static (string Readable, string Prompt) GenerateOutputs()
    {
        // 深拷贝：StoryDocumentBuilder 会原地改写 Index 并清空立绘行 MdText
        // （RemoveEmptyLines / RemovePortraitLines），两种模式必须各自独立的输入列表。
        var source = LoneTrailControlledInput.Build();

        var readableEntries = LoneTrailControlledInput.CloneForRender(source);
        var promptEntries = LoneTrailControlledInput.CloneForRender(source);
        return (Render(OutputMode.Readable, readableEntries), Render(OutputMode.PromptOptimized, promptEntries));
    }

    private static string Render(OutputMode mode, List<ArkPlot.Core.Model.ScriptLine> entries)
    {
        var builder = new StoryDocumentBuilder(
            entries,
            enableDescriptions: true,
            outputMode: mode);
        var sb = new System.Text.StringBuilder();
        sb.Append($"## {ChapterTitle}\r\n\r\n");
        builder.AppendResultToBuilder(sb);
        return sb.ToString();
    }

    // ─── 断言辅助 ───

    private static int Count(string text, string needle)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        { count++; index += needle.Length; }
        return count;
    }

    private static int IndexOf(string text, string needle)
        => text.IndexOf(needle, StringComparison.Ordinal);

    private static void AssertTopicTitle(string prompt, string readable)
    {
        Assert.Contains($"## {ChapterTitle}", prompt);
        Assert.Contains($"## {ChapterTitle}", readable);
    }

    // ─── Prompt 模式：aside 分类计数 ───

    [Fact]
    public void Prompt_Asides_AreClassifiedByType()
    {
        var (_, prompt) = GenerateOutputs();

        // 受控输入有 2 个带 PicFacts 的 background → 恰好 2 个 scene-facts
        Assert.Equal(2, Count(prompt, "<aside class=\"scene-facts\""));
        // 受控输入有 1 个带 PicFacts 的 image → 恰好 1 个 item-facts
        Assert.Equal(1, Count(prompt, "<aside class=\"item-facts\""));
        // 立绘 aside：监狱负责人 / 精英打扮的男性 / 缪尔赛思 / 神秘人士(洛伦茨) 每个 ≥1。
        // 注意同一角色可能因"仅出场+插话"双路径出现多次（注入 + 对话首次触发），
        // 因此只断言"下界"，不断言精确数量——精确数量对渲染器合理重构过于敏感。
        Assert.True(Count(prompt, "<aside class=\"portrait-facts\"") >= 4,
            $"portrait-facts 应至少 4 个，实际 {Count(prompt, "<aside class=\"portrait-facts\"")}");
    }

    [Fact]
    public void Prompt_EachCharacter_HasAtLeastOnePortraitFacts()
    {
        var (_, prompt) = GenerateOutputs();

        string[] expectedCharacters =
        {
            "data-character=\"监狱负责人\"",
            "data-character=\"精英打扮的男性\"",
            "data-character=\"缪尔赛思\"",
            // 「？？？」隐藏角色只出场（只有 charslot，没有对话），其描述必须经 Inject 注入出现
            "data-character=\"神秘人士（实际人物：洛伦茨）\"",
        };
        foreach (var needle in expectedCharacters)
            Assert.Contains(needle, prompt);
    }

    // ─── Prompt 模式：立绘内联位置与顺序 ───

    [Fact]
    public void Prompt_PortraitFacts_InlinedInPortraitGroup()
    {
        var (_, prompt) = GenerateOutputs();

        // 内联契约：立绘描述必须在"该立绘组首个非立绘渲染行"之前出现，
        // 而不是被推迟到角色首次对话之后。用每个角色的首次 aside 位置 < 首次对话行位置 验证。
        AssertTrueExclusive(prompt, "<aside class=\"portrait-facts\" data-character=\"监狱负责人\">", "**监狱负责人**");
        AssertTrueExclusive(prompt, "<aside class=\"portrait-facts\" data-character=\"精英打扮的男性\">", "**精英打扮的男性");
        AssertTrueExclusive(prompt, "<aside class=\"portrait-facts\" data-character=\"缪尔赛思\">", "**缪尔赛思**");
        // 神秘人士只有立绘没有对话：其 aside 必须在（若有对话）之前，此处只断言 aside 存在
        Assert.Contains("<aside class=\"portrait-facts\" data-character=\"神秘人士（实际人物：洛伦茨）\">", prompt);
    }

    private static void AssertTrueExclusive(string text, string asideNeedle, string dialogNeedle)
    {
        int asideIdx = IndexOf(text, asideNeedle);
        int dialogIdx = IndexOf(text, dialogNeedle);
        Assert.True(asideIdx >= 0, $"未找到 aside: {asideNeedle}");
        Assert.True(dialogIdx >= 0, $"未找到对话行: {dialogNeedle}");
        Assert.True(asideIdx < dialogIdx,
            $"立绘描述应内联在对话之前，实际 aside@{asideIdx} 晚于 对话@{dialogIdx}");
    }

    // ─── Prompt 模式：跨名标注 ───

    [Fact]
    public void Prompt_CrossName_AnnotationsPresent()
    {
        var (_, prompt) = GenerateOutputs();

        // 精英打扮的男性 → 小贾斯汀：同 code（avg_npc_892_1）首名标注后文作、后名标注即前文
        Assert.Contains("**精英打扮的男性（后文作“小贾斯汀”）**", prompt);
        Assert.Contains("**小贾斯汀（即前文“精英打扮的男性”）**", prompt);
        // aside 的名字列表把跨名角色合并标注
        Assert.Contains("（即同一角色）", prompt);
    }

    // ─── Prompt 模式：无残留旧结构 / 媒体清理 / 音乐跳过 / 字幕规范化 ───

    [Fact]
    public void Prompt_NoLegacyStructures_AndMediaCleaned()
    {
        var (_, prompt) = GenerateOutputs();

        // 旧结构完全清除
        Assert.Equal(0, Count(prompt, "class=\"scene-desc\""));
        Assert.Equal(0, Count(prompt, "class=\"portrait-table\""));
        // 音乐行（playmusic）被跳过
        Assert.Equal(0, Count(prompt, "m_dia_street_loop"));
        // 媒体 URL 清空，只保留标签语义（噪声最小化）
        Assert.Equal(0, Count(prompt, "src=\"https"));
        // 字幕前缀被规范化移除，正文保留
        Assert.Equal(0, Count(prompt, "居中字幕"));
        Assert.Contains("父母的葬礼当天，她站在最中央，周围人来人往。", prompt);
    }

    // ─── Prompt 模式：分组结构 ───

    [Fact]
    public void Prompt_Groups_SeparatedByDash()
    {
        var (_, prompt) = GenerateOutputs();

        // 受控输入含 playmusic 切分（Scene1/Scene2），输出必须保留组间隔行
        Assert.True(Count(prompt, "\r\n\r\n---\r\n\r\n") >= 1,
            "两组内容之间应有分隔行");
    }

    // ─── Readable 模式：立绘表格 + 场景描述，保留媒体，无 aside ───

    [Fact]
    public void Readable_PortraitTable_AndSceneDesc_NoAside()
    {
        var (readable, _) = GenerateOutputs();

        // 2 个立绘组 → 2 个 portrait-table
        Assert.Equal(2, Count(readable, "class=\"portrait-table\""));
        // 场景描述以 scene-desc 段落插入（受控输入 2 个 background + 1 个 image 带 PicDesc）
        Assert.True(Count(readable, "class=\"scene-desc\"") >= 3,
            $"scene-desc 应至少 3 个，实际 {Count(readable, "class=\"scene-desc\"")}");
        // Readable 不产生 aside
        Assert.Equal(0, Count(readable, "<aside"));
        // 媒体 URL 原样保留（Readable 面向人类阅读，不清理资源）
        Assert.True(Count(readable, "src=\"https") >= 2);
        // 无跨名标注（Readable 不含面向 LLM 的跨名提示）
        Assert.Equal(0, Count(readable, "（后文作"));
        Assert.Equal(0, Count(readable, "（即前文"));
        // 对话保留
        Assert.Contains("**监狱负责人**`讲道：`谁这么会挑时间", readable);
        Assert.Contains("**小贾斯汀**`讲道：`您可以喊我小贾斯汀。", readable);
    }

    // ─── 两种模式共享的标题契约 ───

    [Fact]
    public void BothModes_ContainChapterTitle()
    {
        var (readable, prompt) = GenerateOutputs();
        AssertTopicTitle(prompt, readable);
    }
}