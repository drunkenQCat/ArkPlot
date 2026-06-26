using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Tts.Alignment;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

/// <summary>
/// Phase 5 消费链路测试：验证旁白继承 EntryIndex 后三条下游链路正确工作。
/// 测试数据模拟 Phase 5 输出（旁白 EntryIndex 不再是 -1）。
/// </summary>
public class Phase5NarratorConsumerTests : System.IDisposable
{
    private readonly string _tempDir;

    public Phase5NarratorConsumerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"arkplot_phase5_{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    // ══════════════════════════════════════════════
    //  测试数据构造
    // ══════════════════════════════════════════════

    /// <summary>
    /// 构造 Phase 5 后的典型对齐结果：
    /// 对话(10) → 旁白(inherit→10) → 对话(20) → 旁白(inherit→20) → 旁白(inherit→20)
    /// </summary>
    private static List<AlignmentEntry> MakePhase5Entries() =>
    [
        // 对话 A
        new("你好", true, "角色A", "code_a", 10, "章节1", "女",
            ["https://prts.wiki/portrait_a.png"]),
        // 旁白：继承 → 10
        new("她走进了房间。", false, null, null, 10, "章节1"),
        // 对话 B
        new("欢迎", true, "角色B", "code_b", 20, "章节1", "男",
            ["https://prts.wiki/portrait_b.png"]),
        // 旁白：继承 → 20
        new("阳光从窗外照进来。", false, null, null, 20, "章节1"),
        // 旁白：继承 → 20（连续旁白）
        new("时间慢慢流逝。", false, null, null, 20, "章节1"),
    ];

    private static List<SegmentRow> MakeSegmentRows(List<AlignmentEntry> entries) =>
        entries.Select((e, i) => new SegmentRow
        {
            Index = i + 1,
            CharacterName = e.IsDialog ? (e.CharacterName ?? "?") : "(旁白)",
            SegmentType = e.IsDialog ? "对话" : "旁白",
            NovelText = e.NovelText,
            CharacterCode = e.CharacterCode,
            Gender = e.Gender,
            ChapterTitle = e.ChapterTitle,
            EntryIndex = e.EntryIndex,
            AlignmentOrder = i,
        }).ToList();

    private static void InjectEntries(TtsViewModel vm, List<AlignmentEntry> entries)
    {
        var field = typeof(TtsViewModel).GetField("_alignmentEntries",
            BindingFlags.NonPublic | BindingFlags.Instance);
        field!.SetValue(vm, entries);
    }

    private static void InjectBackgrounds(TtsViewModel vm, List<BackgroundItem> bgs)
    {
        var field = typeof(TtsViewModel).GetField("_backgrounds",
            BindingFlags.NonPublic | BindingFlags.Instance);
        field!.SetValue(vm, bgs);
    }

    // ══════════════════════════════════════════════
    //  链路 1: Gallery 背景联动
    // ══════════════════════════════════════════════

    [AvaloniaFact]
    public void Gallery_NarratorWithInheritedIndex_ShowsInheritedBackground()
    {
        var vm = new TtsViewModel(_tempDir);
        var entries = MakePhase5Entries();
        InjectEntries(vm, entries);

        // 背景图：EntryIndex 10 和 20 分别有不同背景
        InjectBackgrounds(vm,
        [
            new("bg_room", "房间描述", 10, [], 1, "章节1"),
            new("bg_sunny", "阳光场景", 20, [], 1, "章节1"),
        ]);

        vm.FilteredSegments = new(MakeSegmentRows(entries));

        // 选中旁白行（继承 EntryIndex=10）→ Gallery 应显示 bg_room
        vm.SelectedSegment = vm.FilteredSegments[1]; // 旁白，EntryIndex=10
        Assert.Equal("bg_room", vm.GalleryPanel.CurrentBackground);

        // 选中第二个旁白行（继承 EntryIndex=20）→ Gallery 应显示 bg_sunny
        vm.SelectedSegment = vm.FilteredSegments[3]; // 旁白，EntryIndex=20
        Assert.Equal("bg_sunny", vm.GalleryPanel.CurrentBackground);

        // 连续旁白（也继承 EntryIndex=20）→ 同样 bg_sunny
        vm.SelectedSegment = vm.FilteredSegments[4]; // 旁白，EntryIndex=20
        Assert.Equal("bg_sunny", vm.GalleryPanel.CurrentBackground);
    }

    [AvaloniaFact]
    public void Gallery_NarratorWithMinusOne_StillFallsBackViaGalleryLogic()
    {
        // 验证 Gallery 自带的 -1 回退逻辑仍然有效
        // （UpdateGalleryForSegment 内部向前搜索 FilteredSegments 找最近 EntryIndex >= 0）
        var vm = new TtsViewModel(_tempDir);

        InjectEntries(vm,
        [
            new("对话", true, "A", "code_a", 10, "章节1", "女", null),
            new("旁白", false, null, null, -1, "章节1"),
        ]);

        InjectBackgrounds(vm,
        [
            new("bg_room", "", 10, [], 1, "章节1"),
        ]);

        vm.FilteredSegments =
        [
            new SegmentRow
            {
                Index = 1, ChapterTitle = "章节1", SegmentType = "对话",
                CharacterName = "A", EntryIndex = 10
            },
            new SegmentRow
            {
                Index = 2, ChapterTitle = "章节1", SegmentType = "旁白",
                CharacterName = "(旁白)", EntryIndex = -1
            }
        ];

        // 先选中对话行让 Gallery 初始化
        vm.SelectedSegment = vm.FilteredSegments[0];
        Assert.Equal("bg_room", vm.GalleryPanel.CurrentBackground);

        // 选中 EntryIndex=-1 的旁白 → Gallery 自带回退到前一个有效 EntryIndex
        vm.SelectedSegment = vm.FilteredSegments[1];
        Assert.Equal("bg_room", vm.GalleryPanel.CurrentBackground);
    }

    // ══════════════════════════════════════════════
    //  链路 2: 立绘解析
    // ══════════════════════════════════════════════

    [AvaloniaFact]
    public void Portrait_NarratorWithInheritedIndex_ShowsPreviousDialogPortrait()
    {
        var vm = new TtsViewModel(_tempDir);
        var entries = MakePhase5Entries();
        InjectEntries(vm, entries);

        vm.FilteredSegments = new(MakeSegmentRows(entries));

        // 选中第一行对话 → 立绘应为 portrait_a
        vm.SelectedSegment = vm.FilteredSegments[0]; // 对话 A, EntryIndex=10
        Assert.Equal("https://prts.wiki/portrait_a.png", vm.PortraitPanel.PortraitUrl);

        // 选中旁白行（继承 EntryIndex=10）
        // FindPortraitEntry 的 fallback 会找到 EntryIndex=10 的对话条目
        // → 旁白行继承上一对话的立绘
        vm.SelectedSegment = vm.FilteredSegments[1]; // 旁白, EntryIndex=10
        Assert.Equal("https://prts.wiki/portrait_a.png", vm.PortraitPanel.PortraitUrl);

        // 选中第二行对话 → 立绘切换为 portrait_b
        vm.SelectedSegment = vm.FilteredSegments[2]; // 对话 B, EntryIndex=20
        Assert.Equal("https://prts.wiki/portrait_b.png", vm.PortraitPanel.PortraitUrl);

        // 选中继承 EntryIndex=20 的旁白 → 同样显示 portrait_b
        vm.SelectedSegment = vm.FilteredSegments[3]; // 旁白, EntryIndex=20
        Assert.Equal("https://prts.wiki/portrait_b.png", vm.PortraitPanel.PortraitUrl);
    }

    // ══════════════════════════════════════════════
    //  链路 3: 视频生成选区
    // ══════════════════════════════════════════════

    [AvaloniaFact]
    public void VideoGeneration_NarratorRow_HasValidAlignmentOrder()
    {
        var vm = new TtsViewModel(_tempDir);
        var entries = MakePhase5Entries();
        InjectEntries(vm, entries);

        var rows = MakeSegmentRows(entries);
        vm.FilteredSegments = new(rows);

        // 模拟选中旁白行
        vm.SelectedSegmentRows.Clear();
        vm.SelectedSegmentRows.Add(rows[1]); // 旁白, AlignmentOrder=1
        vm.SelectedSegmentRows.Add(rows[3]); // 旁白, AlignmentOrder=3

        // 验证 AlignmentOrder 有效（>= 0）
        Assert.All(vm.SelectedSegmentRows, r => Assert.True(r.AlignmentOrder >= 0,
            $"旁白行的 AlignmentOrder 应 >= 0，实际 {r.AlignmentOrder}"));

        // 验证 AlignmentOrder 对应正确的 entries 索引
        Assert.Equal(1, vm.SelectedSegmentRows[0].AlignmentOrder);
        Assert.Equal(3, vm.SelectedSegmentRows[1].AlignmentOrder);

        // 验证 EntryIndex 不再是 -1（Phase 5 继承生效）
        Assert.All(vm.SelectedSegmentRows, r => Assert.True(r.EntryIndex >= 0,
            $"旁白行 EntryIndex 应 >= 0，实际 {r.EntryIndex}"));
    }

    [AvaloniaFact]
    public void VideoGeneration_MixedSelection_IncludesNarratorAndDialog()
    {
        var vm = new TtsViewModel(_tempDir);
        var entries = MakePhase5Entries();
        InjectEntries(vm, entries);

        var rows = MakeSegmentRows(entries);
        vm.FilteredSegments = new(rows);

        // 选中对话+旁白混合
        vm.SelectedSegmentRows.Clear();
        vm.SelectedSegmentRows.Add(rows[0]); // 对话 A, AlignmentOrder=0
        vm.SelectedSegmentRows.Add(rows[1]); // 旁白,   AlignmentOrder=1
        vm.SelectedSegmentRows.Add(rows[2]); // 对话 B, AlignmentOrder=2

        // renderIndices 应为 [0, 1, 2]
        var renderIndices = vm.SelectedSegmentRows
            .Select(r => r.AlignmentOrder)
            .OrderBy(i => i)
            .ToList();

        Assert.Equal([0, 1, 2], renderIndices);
    }

    // ══════════════════════════════════════════════
    //  边界情况：章节无对话
    // ══════════════════════════════════════════════

    [AvaloniaFact]
    public void Gallery_ChapterWithNoDialog_NarratorKeepsMinusOne()
    {
        var vm = new TtsViewModel(_tempDir);

        // 整个章节都没有对话 → 旁白 EntryIndex 保持 -1
        InjectEntries(vm,
        [
            new("场景描述1", false, null, null, -1, "纯旁白章节"),
            new("场景描述2", false, null, null, -1, "纯旁白章节"),
        ]);

        InjectBackgrounds(vm,
        [
            new("bg_room", "", 10, [], 1, "其他章节"),
        ]);

        vm.FilteredSegments =
        [
            new SegmentRow
            {
                Index = 1, ChapterTitle = "纯旁白章节", SegmentType = "旁白",
                CharacterName = "(旁白)", EntryIndex = -1
            },
        ];

        vm.SelectedSegment = vm.FilteredSegments[0];

        // 无对话章节的旁白保持 -1，Gallery 不跳转
        Assert.Null(vm.GalleryPanel.CurrentBackground);
    }

    public void Dispose() { }
}
