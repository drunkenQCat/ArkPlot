using System.Collections.Generic;
using System.IO;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Tts.Alignment;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

public class GalleryBackgroundSelectionTests : System.IDisposable
{
    private readonly string _tempDir;

    public GalleryBackgroundSelectionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"arkplot_gallery_bg_test_{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [AvaloniaFact]
    public void Gallery_PicksBackgroundFromSameChapter()
    {
        var vm = new TtsViewModel(_tempDir);

        var backgroundsField = typeof(TtsViewModel).GetField("_backgrounds",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(backgroundsField);

        backgroundsField!.SetValue(vm, new List<BackgroundItem>
        {
            new("bg_a", "", 10, [], 1, "章节A"),
            new("bg_b", "", 10, [], 2, "章节B"),
        });

        vm.FilteredSegments =
        [
            new SegmentRow
            {
                Index = 1,
                ChapterTitle = "章节A",
                SegmentType = "对话",
                CharacterName = "A",
                EntryIndex = 20
            }
        ];

        vm.SelectedSegment = vm.FilteredSegments[0];

        Assert.Equal("bg_a", vm.GalleryPanel.CurrentBackground);
    }

    [AvaloniaFact]
    public void Gallery_Narration_InheritsPreviousBackground()
    {
        var vm = new TtsViewModel(_tempDir);

        var backgroundsField = typeof(TtsViewModel).GetField("_backgrounds",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(backgroundsField);

        var entriesField = typeof(TtsViewModel).GetField("_alignmentEntries",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(entriesField);

        entriesField!.SetValue(vm, new List<AlignmentEntry>
        {
            new("对话", true, "A", "code_a", 20, "章节A", "男", null),
            new("旁白", false, null, null, -1, "章节A", null, null),
        });

        backgroundsField!.SetValue(vm, new List<BackgroundItem>
        {
            new("bg_a", "", 10, [], 1, "章节A"),
        });

        vm.FilteredSegments =
        [
            new SegmentRow
            {
                Index = 1,
                ChapterTitle = "章节A",
                SegmentType = "对话",
                CharacterName = "A",
                EntryIndex = 20
            },
            new SegmentRow
            {
                Index = 2,
                ChapterTitle = "章节A",
                SegmentType = "旁白",
                CharacterName = "(旁白)",
                EntryIndex = -1
            }
        ];

        vm.SelectedSegment = vm.FilteredSegments[1];

        Assert.Equal("bg_a", vm.GalleryPanel.CurrentBackground);
    }

    [AvaloniaFact]
    public void Gallery_FiltersBlackBackground()
    {
        var vm = new TtsViewModel(_tempDir);

        var backgroundsField = typeof(TtsViewModel).GetField("_backgrounds",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(backgroundsField);

        const string black = "https://media.prts.wiki/8/8a/Avg_bg_bg_black.png";

        backgroundsField!.SetValue(vm, new List<BackgroundItem>
        {
            new(black, "", 1, [], 1, "章节A"),
            new("bg_a", "", 4, [], 1, "章节A"),
            new(black, "", 5, [], 1, "章节A"),
            new("bg_b", "", 10, [], 1, "章节A"),
        });

        vm.FilteredSegments =
        [
            new SegmentRow
            {
                Index = 1,
                ChapterTitle = "章节A",
                SegmentType = "旁白",
                CharacterName = "(旁白)",
                EntryIndex = -1
            },
            new SegmentRow
            {
                Index = 2,
                ChapterTitle = "章节A",
                SegmentType = "对话",
                CharacterName = "A",
                EntryIndex = 6
            }
        ];

        vm.SelectedSegment = vm.FilteredSegments[1];

        Assert.Equal("bg_a", vm.GalleryPanel.CurrentBackground);
        Assert.Null(vm.GalleryPanel.PrevBackground);
        Assert.Equal("bg_b", vm.GalleryPanel.NextBackground);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [AvaloniaTheory]
    [InlineData(0, "bg_a", null, "bg_b")]
    [InlineData(6, "bg_a", null, "bg_b")]
    [InlineData(12, "bg_b", "bg_a", null)]
    public void Gallery_TransitionsUseVisibleScenesWithinChapter(int entry, string expected, string? previous, string? next)
    {
        using var vm = new TtsViewModel(_tempDir);
        var field = typeof(TtsViewModel).GetField("_backgrounds",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        const string black = "https://media.prts.wiki/8/8a/Avg_bg_bg_black.png";
        field.SetValue(vm, new List<BackgroundItem>
        {
            new("other-chapter", "", 0, [], 2, "B"),
            new("bg_b", "", 10, [], 1, "A"),
            new(black, "", 5, [], 1, "A"),
            new("bg_a", "", 4, [], 1, "A"),
            new(black, "", 1, [], 1, "A")
        });
        vm.FilteredSegments = [new SegmentRow { ChapterTitle = "A", EntryIndex = entry }];
        vm.SelectedSegment = vm.FilteredSegments[0];
        Assert.Equal(expected, vm.GalleryPanel.CurrentBackground);
        Assert.Equal(previous, vm.GalleryPanel.PrevBackground);
        Assert.Equal(next, vm.GalleryPanel.NextBackground);
    }
}

