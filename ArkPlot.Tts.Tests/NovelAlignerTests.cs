using ArkPlot.Core.Model;
using ArkPlot.Tts.Alignment;

namespace ArkPlot.Tts.Tests;

public class NovelAlignerTests
{
    // ── NormalizeStrict ──

    [Fact]
    public void NormalizeStrict_EllipsisVariants()
        => Assert.Equal("她…他", NovelAligner.NormalizeStrict("她......他"));

    [Fact]
    public void NormalizeStrict_ChinesePunctuation()
        => Assert.Equal("你好,世界.", NovelAligner.NormalizeStrict("你好，世界。"));

    [Fact]
    public void NormalizeStrict_Quotes()
        => Assert.Equal("\"hello\"", NovelAligner.NormalizeStrict("\u201Chello\u201D"));

    [Fact]
    public void NormalizeStrict_Whitespace()
        => Assert.Equal("a b c", NovelAligner.NormalizeStrict("a  b\tc"));

    // ── NormalizeLoose ──

    [Fact]
    public void NormalizeLoose_StripsPunctuation()
        => Assert.Equal("你好世界", NovelAligner.NormalizeLoose("你好，世界！"));

    [Fact]
    public void NormalizeLoose_StripsQuotes()
        => Assert.Equal("他说你好", NovelAligner.NormalizeLoose("\u201C他说\u201D\u201C你好\u201D"));

    [Fact]
    public void NormalizeLoose_KeepsAlphanumeric()
        => Assert.Equal("E7NS", NovelAligner.NormalizeLoose("E-7 NS"));

    // ── IsAnchorMatch ──

    [Fact]
    public void IsAnchorMatch_ExactMatch()
        => Assert.True(NovelAligner.IsAnchorMatch("先生，您生病了。", "先生，您生病了。"));

    [Fact]
    public void IsAnchorMatch_PunctuationDiff()
        => Assert.True(NovelAligner.IsAnchorMatch(
            NovelAligner.NormalizeStrict("你好，世界。"),
            NovelAligner.NormalizeStrict("你好,世界.")));

    [Fact]
    public void IsAnchorMatch_Substring_ShortContained()
    {
        var novel = NovelAligner.NormalizeStrict("你好");
        var db = NovelAligner.NormalizeStrict("你好，世界。今天天气不错。");
        // minLen=2 < 4, should be false
        Assert.False(NovelAligner.IsAnchorMatch(novel, db));
    }

    [Fact]
    public void IsAnchorMatch_Substring_LongEnough()
    {
        // 短文本 vs 长文本且不是子串 → 拒绝
        var novel = NovelAligner.NormalizeStrict("先生您生病了");
        var db = NovelAligner.NormalizeStrict("先生，您生病了。今天感觉如何？");
        Assert.False(NovelAligner.IsAnchorMatch(novel, db));

        // novel: 20 chars, db: 28 chars → ratio = 20/28 = 0.714 >= 0.7
        var novel2 = NovelAligner.NormalizeStrict("这是足够长的对话文本内容用于验证子串匹配");
        var db2 = NovelAligner.NormalizeStrict("这是足够长的对话文本内容用于验证子串匹配，后面追加了内容");
        Assert.True(NovelAligner.IsAnchorMatch(novel2, db2));
    }

    [Fact]
    public void IsAnchorMatch_CompletelyDifferent()
        => Assert.False(NovelAligner.IsAnchorMatch("你好世界", "今天天气不错"));

    // ── ComputeSimilarity ──

    [Fact]
    public void ComputeSimilarity_IdenticalStrings()
        => Assert.Equal(1.0, NovelAligner.ComputeSimilarity("abc", "abc"));

    [Fact]
    public void ComputeSimilarity_Contains()
    {
        // "abc" contains "ab" → 2/3
        Assert.Equal(2.0 / 3.0, NovelAligner.ComputeSimilarity("abc", "ab"), 2);
    }

    [Fact]
    public void ComputeSimilarity_CommonPrefix()
    {
        // "abcdef" and "abcxyz" → common prefix "abc" = 3, max = 6 → 0.5
        Assert.Equal(0.5, NovelAligner.ComputeSimilarity("abcdef", "abcxyz"), 2);
    }

    [Fact]
    public void ComputeSimilarity_EmptyString()
        => Assert.Equal(0, NovelAligner.ComputeSimilarity("", "abc"));

    [Fact]
    public void ComputeSimilarity_NoCommonPrefix()
        => Assert.Equal(0, NovelAligner.ComputeSimilarity("abc", "xyz"), 2);

    // ── FindAnchors ──

    [Fact]
    public void FindAnchors_ExactMatchesInOrder()
    {
        var dialogs = new List<NovelSegment>
        {
            new("如若此后百年千年", true),
            new("先生，您生病了。", true),
            new("什么？我好得很。", true),
        };
        var dbEntries = new List<FormattedTextEntry>
        {
            MakeDialog(0, null, "如若此后百年千年"),
            MakeDialog(1, null, "她坐在树桩上"),
            MakeDialog(2, null, "即将进行的低空飞行"),
            MakeDialog(10, "精英打扮的男性", "先生，您生病了。"),
            MakeDialog(11, "监狱负责人", "什么？我好得很。"),
        };

        var anchors = NovelAligner.FindAnchors(dialogs, dbEntries);

        Assert.Equal(3, anchors.Count);
        Assert.Equal((0, 0), anchors[0]); // 如若此后百年千年 → entry 0
        Assert.Equal((1, 3), anchors[1]); // 先生您生病了 → entry 10
        Assert.Equal((2, 4), anchors[2]); // 什么我好得很 → entry 11
    }

    [Fact]
    public void FindAnchors_SkipsMergedDialogs()
    {
        // Novel has 2 dialogs, DB has 5 entries (3 were merged by LLM)
        var dialogs = new List<NovelSegment>
        {
            new("如若此后百年千年", true),
            new("先生，您生病了。", true),
        };
        var dbEntries = new List<FormattedTextEntry>
        {
            MakeDialog(0, null, "如若此后百年千年"),
            MakeDialog(1, null, "她坐在树桩上一边哼着歌"),
            MakeDialog(2, null, "即将进行的低空飞行对于父母来说"),
            MakeDialog(3, null, "戴好护目镜绑上安全带"),
            MakeDialog(10, "精英打扮的男性", "先生，您生病了。"),
        };

        var anchors = NovelAligner.FindAnchors(dialogs, dbEntries);

        Assert.Equal(2, anchors.Count);
        Assert.Equal(0, anchors[0].DbIdx);  // first dialog → entry 0
        Assert.Equal(4, anchors[1].DbIdx);  // second dialog → entry 10 (skipped 1-3)
    }

    [Fact]
    public void FindAnchors_MinLength3_IgnoresShort()
    {
        var dialogs = new List<NovelSegment>
        {
            new("嗯", true),   // too short after normalize
            new("你好世界今天天气不错", true),
        };
        var dbEntries = new List<FormattedTextEntry>
        {
            MakeDialog(0, null, "嗯"),
            MakeDialog(1, null, "你好世界今天天气不错"),
        };

        var anchors = NovelAligner.FindAnchors(dialogs, dbEntries);

        // "嗯" is 1 char after normalize → skipped
        // "你好世界今天天气不错" should match
        Assert.Single(anchors);
        Assert.Equal(1, anchors[0].NovelIdx);
    }

    [Fact]
    public void BuildCharCodeAtEntry_RecognizesCharacterTypeAsCharSlot()
    {
        var allEntriesByPlot = new Dictionary<long, List<FormattedTextEntry>>
        {
            [71] =
            [
                new FormattedTextEntry
                {
                    PlotId = 71,
                    Index = 412,
                    Type = "character",
                    CharacterCode = "avg_npc_535_1",
                    CommandSet = new StringDict
                    {
                        ["name"] = "avg_npc_535_1#1$1",
                        ["name2"] = "avg_npc_534_1#1$1",
                        ["focus"] = "1",
                        ["type"] = "character"
                    }
                },
                new FormattedTextEntry
                {
                    PlotId = 71,
                    Index = 413,
                    Type = "",
                    CharacterName = "桑尼",
                    Dialog = "嗯？"
                }
            ]
        };

        var nameToCode = NovelAligner.BuildNameToCodeMap(allEntriesByPlot);
        var charCodeAtEntry = NovelAligner.BuildCharCodeAtEntry(allEntriesByPlot, nameToCode);

        Assert.Equal("avg_npc_535_1", nameToCode["桑尼"]);
        Assert.Equal("avg_npc_535_1", charCodeAtEntry[(71, 413)]);
    }

    // ── helpers ──

    private static FormattedTextEntry MakeDialog(int index, string? characterName, string dialog)
        => new()
        {
            Index = index,
            Type = "dialog",
            CharacterName = characterName!,
            Dialog = dialog,
        };
}
