using ArkPlot.Core.Infrastructure;
using ArkPlot.Core.Model;
using Xunit;

namespace ArkPlot.Video.Tests;

[Collection("SequentialDb")]
public class VideoComposerStateTests : IDisposable
{
    private readonly string _tempDbPath;

    public VideoComposerStateTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"arkplot_test_{Guid.NewGuid():N}.db");
        DbFactory.ConfigureForTesting($"Data Source={_tempDbPath}");
    }

    public void Dispose()
    {
        try { DbFactory.Reset(); } catch { }
        try { File.Delete(_tempDbPath); } catch { }
    }

    private static FormattedTextEntry MakeEntry(
        int index, string charName, string dialog, string bg,
        List<string>? portraits = null, int focus = 0)
    {
        return new FormattedTextEntry
        {
            Index = index,
            CharacterName = charName,
            Dialog = dialog,
            Bg = bg,
            Portraits = portraits ?? [],
            PortraitFocus = focus,
        };
    }

    private static List<FormattedTextEntry> InsertEntries(params FormattedTextEntry[] entries)
    {
        var db = DbFactory.GetClient();
        db.Insertable(entries.ToList()).ExecuteCommand();
        var indices = entries.Select(e => e.Index).ToList();
        return db.Queryable<FormattedTextEntry>()
            .Where(e => indices.Contains(e.Index))
            .ToList();
    }

    // ══════════════════════════════════════════════
    //  JudgeAvgType 分类
    // ══════════════════════════════════════════════

    [Fact]
    public void Compose_NarratorSegment_GeneratesNarratorTypst()
    {
        var inserted = InsertEntries(MakeEntry(0, "", "旁白文本", "bg_room.png", ["portrait.png"]));

        var segments = new List<VideoSegment>
        {
            new() { EntryId = inserted[0].Id, NovelText = "旁白文本", IsDialog = false, CharacterName = "" },
        };

        TypstComposer.Compose(segments);

        Assert.Contains("#arknights_narrator(", segments[0].TypstCode);
    }

    [Fact]
    public void Compose_SingleCharacterDialog_GeneratesArkSim()
    {
        var inserted = InsertEntries(MakeEntry(0, "医生", "你好", "bg_room.png", ["portrait_01.png"]));

        var segments = new List<VideoSegment>
        {
            new() { EntryId = inserted[0].Id, NovelText = "你好", IsDialog = true, CharacterName = "医生" },
        };

        TypstComposer.Compose(segments);

        Assert.Contains("#arknights_sim(", segments[0].TypstCode);
        Assert.DoesNotContain("#arknights_sim_2p(", segments[0].TypstCode);
    }

    [Fact]
    public void Compose_DoubleCharacterDialog_GeneratesArkSim2p()
    {
        var inserted = InsertEntries(MakeEntry(0, "阿米娅", "对话", "bg_room.png",
            ["portrait_a.png", "portrait_b.png"], focus: 1));

        var segments = new List<VideoSegment>
        {
            new() { EntryId = inserted[0].Id, NovelText = "对话", IsDialog = true, CharacterName = "阿米娅" },
        };

        TypstComposer.Compose(segments);

        Assert.Contains("#arknights_sim_2p(", segments[0].TypstCode);
        Assert.Contains("focus: 1", segments[0].TypstCode);
    }

    // ══════════════════════════════════════════════
    //  旁白段视觉上下文
    // ══════════════════════════════════════════════

    [Fact]
    public void Compose_NarratorWithNoPortraits_UsesTransparentImage()
    {
        var inserted = InsertEntries(MakeEntry(0, "", "旁白", "bg_office.png", []));

        var segments = new List<VideoSegment>
        {
            new() { EntryId = inserted[0].Id, NovelText = "旁白", IsDialog = false, CharacterName = "" },
        };

        TypstComposer.Compose(segments);

        Assert.Contains("transparent.png", segments[0].TypstCode);
    }

    [Fact]
    public void Compose_NarratorWithTwoPortraits_UsesNarrator2p()
    {
        var inserted = InsertEntries(MakeEntry(0, "", "旁白", "bg.png", ["p1.png", "p2.png"]));

        var segments = new List<VideoSegment>
        {
            new() { EntryId = inserted[0].Id, NovelText = "旁白", IsDialog = false, CharacterName = "" },
        };

        TypstComposer.Compose(segments);

        Assert.Contains("#arknights_narrator_2p(", segments[0].TypstCode);
    }

    // ══════════════════════════════════════════════
    //  边界情况
    // ══════════════════════════════════════════════

    [Fact]
    public void Compose_NoEntryId_Skipped()
    {
        var segments = new List<VideoSegment>
        {
            new() { EntryId = 0, NovelText = "未对齐的旁白", IsDialog = false, CharacterName = "" },
        };

        TypstComposer.Compose(segments);

        Assert.Equal("", segments[0].TypstCode);
    }

    [Fact]
    public void Compose_EmptySegments_ReturnsEmptyDict()
    {
        var result = TypstComposer.Compose([]);
        Assert.Empty(result);
    }

    [Fact]
    public void Compose_MissingDbEntry_SkipsSegment()
    {
        DbFactory.GetClient();

        var segments = new List<VideoSegment>
        {
            new() { EntryId = 99999, NovelText = "对话", IsDialog = true, CharacterName = "角色" },
        };

        TypstComposer.Compose(segments);

        Assert.Equal("", segments[0].TypstCode);
    }

    // ══════════════════════════════════════════════
    //  NarratorSize 文本长度判断
    // ══════════════════════════════════════════════

    [Theory]
    [InlineData("短文本", "normal")]
    [InlineData("这是一段中等长度的文本，大约三十到六十个字符之间的样子应该算作compact级别", "compact")]
    [InlineData("这是一段非常非常长的文本内容，超过了六十个字符的限制，应该被归类为small尺寸，这样在渲染的时候字体就会比较小以便能够完整显示所有的内容", "small")]
    public void Compose_NarratorSize_BasedOnTextLength(string text, string expectedSize)
    {
        var inserted = InsertEntries(new FormattedTextEntry
        {
            Index = 0,
            Bg = "bg.png",
            Portraits = [],
            PortraitFocus = 0,
        });

        var segments = new List<VideoSegment>
        {
            new() { EntryId = inserted[0].Id, NovelText = text, IsDialog = false, CharacterName = "" },
        };

        TypstComposer.Compose(segments);

        Assert.Contains($"size: \"{expectedSize}\"", segments[0].TypstCode);
    }
}
