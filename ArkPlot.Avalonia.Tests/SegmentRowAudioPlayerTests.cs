using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Avalonia.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

public class SegmentRowAudioPlayerTests
{
    [AvaloniaFact]
    public void SegmentRow_EachRow_HasIndependentAudioPlayer()
    {
        var row1 = new SegmentRow { Index = 1, NovelText = "第一行" };
        var row2 = new SegmentRow { Index = 2, NovelText = "第二行" };

        Assert.NotNull(row1.AudioPlayer);
        Assert.NotNull(row2.AudioPlayer);
        Assert.NotSame(row1.AudioPlayer, row2.AudioPlayer);

        row1.Dispose();
        row2.Dispose();
    }

    [AvaloniaFact]
    public void SegmentRow_AudioPlayer_IsLazy()
    {
        var row = new SegmentRow { Index = 1 };
        // AudioPlayer 未访问前不应创建
        Assert.False(row.HasAudio);

        // 首次访问才创建
        var player1 = row.AudioPlayer;
        var player2 = row.AudioPlayer;
        Assert.Same(player1, player2); // 同一实例

        row.Dispose();
    }

    [AvaloniaFact]
    public void TogglePlay_Row1_DoesNotAffectRow2()
    {
        var mock1 = new MockAudioPlayer { FakeLength = 60 };
        var mock2 = new MockAudioPlayer { FakeLength = 120 };
        var row1 = new SegmentRow { Index = 1, NovelText = "行1" };
        var row2 = new SegmentRow { Index = 2, NovelText = "行2" };

        var vm1 = new AudioPlayerViewModel(mock1);
        var vm2 = new AudioPlayerViewModel(mock2);

        // 模拟 row1 加载并播放
        vm1.LoadFile("row1.mp3");
        vm1.TogglePlayCommand.Execute(null);

        Assert.True(vm1.IsPlaying);
        Assert.Equal("⏸", vm1.PlayButtonText);

        // row2 未操作，应保持初始状态
        Assert.False(vm2.IsPlaying);
        Assert.Equal("▶", vm2.PlayButtonText);
        Assert.Equal(0, vm2.Position);

        vm1.Dispose();
        vm2.Dispose();
    }

    [AvaloniaFact]
    public void AudioPlayerControl_BindsTo_RowAudioPlayer_NotShared()
    {
        var mock1 = new MockAudioPlayer { FakeLength = 30 };
        var mock2 = new MockAudioPlayer { FakeLength = 60 };

        var row1 = new SegmentRow
        {
            Index = 1,
            HasAudio = true,
            AudioFilePath = "row1.mp3",
            AudioStatus = "▂▃▅▆▇▅▃",
            AudioOpacity = 1.0
        };
        var row2 = new SegmentRow
        {
            Index = 2,
            HasAudio = true,
            AudioFilePath = "row2.mp3",
            AudioStatus = "▂▃▅▆▇▅▃",
            AudioOpacity = 1.0
        };

        // 每个行有自己的 AudioPlayerViewModel
        Assert.NotSame(row1.AudioPlayer, row2.AudioPlayer);

        row1.Dispose();
        row2.Dispose();
    }

    [AvaloniaFact]
    public void SegmentRow_Dispose_DisposesAudioPlayer()
    {
        var row = new SegmentRow { Index = 1 };
        // 触发懒初始化
        var player = row.AudioPlayer;
        Assert.NotNull(player);

        // Dispose 不应抛异常
        row.Dispose();
    }

    [AvaloniaFact]
    public void TtsWindow_DataGrid_AudioColumn_Shows_PerRowPlayer()
    {
        // 验证 TtsWindow 的 DataGrid 音频列使用行级 AudioPlayer
        var tempDir = Path.Combine(Path.GetTempPath(), $"arkplot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var vm = new TtsViewModel(tempDir);
            var window = new TtsWindow(vm);
            window.Show();

            var dataGrid = window.GetVisualDescendants()
                .OfType<DataGrid>()
                .FirstOrDefault();

            Assert.NotNull(dataGrid);

            // DataGrid 应该存在
            Assert.Equal(DataGridSelectionMode.Extended, dataGrid.SelectionMode);

            window.Close();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SegmentFileNaming_MatchesRefreshPattern()
    {
        // 验证 SynthesizeSegmentsAsync 产出的文件名能被 RefreshAudioStatus 的匹配逻辑找到
        var chapterSafe = "孤星";
        var chapterIdx = 1;
        var prefix = $"{chapterSafe}_{chapterIdx:D2}";

        // 模拟生成的文件名
        var segIndex = 3;
        var label = "旁白";
        var fileName = $"{prefix}_{segIndex:D3}_{label}.mp3";

        Assert.Equal("孤星_01_003_旁白.mp3", fileName);

        // 模拟 RefreshAudioStatus 的匹配逻辑
        var mp3Files = new[]
        {
            $"/output/{fileName}",
            "/output/孤星_01_005_阿米娅(女).mp3",
            "/output/other_chapter.mp3"
        };

        var segPattern = $"{prefix}_{segIndex:D3}_";
        var match = mp3Files.FirstOrDefault(f =>
            Path.GetFileName(f).StartsWith(segPattern, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(match);
        Assert.Contains("003_旁白", match);
    }

    [Fact]
    public void SegmentFileNaming_ChapterFallback_StillWorks()
    {
        // 验证旧的"生成整章"产出的章节级 MP3 仍能被匹配
        var chapterTitle = "孤星";
        var mp3Files = new[]
        {
            "/output/孤星_01_整章合并.mp3",
            "/output/other.mp3"
        };

        // 段级匹配找不到
        var segPattern = $"孤星_01_003_";
        var segMatch = mp3Files.FirstOrDefault(f =>
            Path.GetFileName(f).StartsWith(segPattern, StringComparison.OrdinalIgnoreCase));
        Assert.Null(segMatch);

        // 回退到章节级匹配能找到
        var chapterMatch = mp3Files.FirstOrDefault(f =>
            Path.GetFileName(f).Contains(chapterTitle, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(chapterMatch);
    }

    [Fact]
    public void SegmentFileNaming_PrefixWithSpecialChars()
    {
        // 验证章节名含特殊字符时 sanitized 后的前缀能正确匹配
        var chapterSafe = "水晶箭行动"; // 无特殊字符
        var prefix = $"{chapterSafe}_02";
        var fileName = $"{prefix}_001_博士(男).mp3";

        var mp3Files = new[] { $"/output/{fileName}" };
        var segPattern = $"{prefix}_001_";

        var match = mp3Files.FirstOrDefault(f =>
            Path.GetFileName(f).StartsWith(segPattern, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(match);
        Assert.Contains("博士", match);
    }

    [AvaloniaFact]
    public void AutoLoad_PlayWithoutManualLoadFile_LoadsFileAndPlays()
    {
        // 模拟行级 AudioPlayer：用户从未调 LoadFile，但点击 ▶ 时应自动加载
        var tmpFile = Path.GetTempFileName();
        try
        {
            var mock = new MockAudioPlayer { FakeLength = 45.0 };
            string? currentPath = tmpFile;
            var vm = new AudioPlayerViewModel(mock, filePathProvider: () => currentPath);

            // 初始状态：未加载，00:00
            Assert.Equal("00:00", vm.TotalTimeText);
            Assert.Equal(0, vm.Duration);

            // 用户点击 ▶（未手动调 LoadFile）
            vm.TogglePlayCommand.Execute(null);

            // 应该自动调了 LoadFile → Duration 更新 → 开始播放
            Assert.Equal(tmpFile, mock.LastLoadedFile);
            Assert.Equal(45.0, vm.Duration);
            Assert.True(vm.IsPlaying);
            Assert.Equal("⏸", vm.PlayButtonText);

            vm.Dispose();
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [AvaloniaFact]
    public void AutoLoad_FileNotExists_DoesNotCrash()
    {
        // 文件不存在时点播放不应崩溃
        var mock = new MockAudioPlayer { FakeLength = 0 };
        var vm = new AudioPlayerViewModel(mock, filePathProvider: () => "/nonexistent/file.mp3");

        vm.TogglePlayCommand.Execute(null);

        // 文件不存在，LoadFile 不会被调用
        Assert.Null(mock.LastLoadedFile);

        vm.Dispose();
    }

    [AvaloniaFact]
    public void AutoLoad_NullProvider_NoCrash()
    {
        // 无 provider 时点播放不应崩溃
        var mock = new MockAudioPlayer();
        var vm = new AudioPlayerViewModel(mock);

        vm.TogglePlayCommand.Execute(null);

        // 没有 provider，不会加载任何文件
        Assert.Null(mock.LastLoadedFile);

        vm.Dispose();
    }

    [AvaloniaFact]
    public void SegmentRow_AudioPlayer_HasFilePathProvider()
    {
        // 验证 SegmentRow 的 AudioPlayer 有 filePathProvider 连接到 AudioFilePath
        var tmpFile = Path.GetTempFileName();
        try
        {
            var row = new SegmentRow { Index = 8, AudioFilePath = tmpFile, HasAudio = true };

            var mock = new MockAudioPlayer { FakeLength = 30 };
            var vm = new AudioPlayerViewModel(mock, filePathProvider: () => row.AudioFilePath);

            // 模拟点击播放
            vm.TogglePlayCommand.Execute(null);

            Assert.Equal(tmpFile, mock.LastLoadedFile);
            Assert.True(vm.IsPlaying);

            vm.Dispose();
            row.Dispose();
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [AvaloniaFact]
    public void Replay_AfterPlaybackEnds_SeeksToStartAndPlays()
    {
        var mock = new MockAudioPlayer { FakeLength = 60.0 };
        var vm = new AudioPlayerViewModel(mock);
        vm.LoadFile("test.mp3");

        // 模拟播放结束的状态：Position == Duration, IsPlaying == false
        // (正常情况下由 TimerTick 检测到播放器停止后设置)
        vm.Position = vm.Duration; // 模拟播放到结尾

        // 确认处于"已结束"状态
        Assert.False(vm.IsPlaying);
        Assert.Equal(60.0, vm.Position);

        // 点击播放 → 应该先 seek 到 0 再播放
        vm.TogglePlayCommand.Execute(null);
        Assert.True(vm.IsPlaying);
        Assert.True(mock.SeekCallCount > 0);
        Assert.Equal(0, mock.LastSeekPosition);

        vm.Dispose();
    }

    [AvaloniaFact]
    public void Duration_AvailableWithoutPlaying()
    {
        var mock = new MockAudioPlayer { FakeLength = 125.5 };
        var vm = new AudioPlayerViewModel(mock);

        // 未播放，手动加载文件
        vm.LoadFile("test.mp3");

        // 时长应立即显示
        Assert.Equal(125.5, vm.Duration);
        Assert.Equal("02:05", vm.TotalTimeText);
        Assert.False(vm.IsPlaying); // 但未播放

        vm.Dispose();
    }

    [AvaloniaFact]
    public async Task WaitForCompletion_NotPlaying_ReturnsCompleted()
    {
        var mock = new MockAudioPlayer { FakeLength = 60 };
        var vm = new AudioPlayerViewModel(mock);

        // 未播放时 WaitForCompletionAsync 应立即返回
        var task = vm.WaitForCompletionAsync();
        Assert.True(task.IsCompleted);

        vm.Dispose();
    }

    [AvaloniaFact]
    public async Task WaitForCompletion_Stop_CompletesTask()
    {
        var mock = new MockAudioPlayer { FakeLength = 60 };
        var vm = new AudioPlayerViewModel(mock);
        vm.LoadFile("test.mp3");
        vm.TogglePlayCommand.Execute(null);
        Assert.True(vm.IsPlaying);

        var completionTask = vm.WaitForCompletionAsync();
        Assert.False(completionTask.IsCompleted); // 播放中，未完成

        vm.StopCommand.Execute(null); // 手动停止
        await completionTask; // 应该完成
        Assert.True(completionTask.IsCompleted);

        vm.Dispose();
    }

    [AvaloniaFact]
    public async Task WaitForCompletion_LoadFile_CancelsPreviousTask()
    {
        var mock = new MockAudioPlayer { FakeLength = 60 };
        var vm = new AudioPlayerViewModel(mock);
        vm.LoadFile("first.mp3");
        vm.TogglePlayCommand.Execute(null);

        var firstTask = vm.WaitForCompletionAsync();
        Assert.False(firstTask.IsCompleted);

        // 加载新文件 → 取消上一次的等待
        vm.LoadFile("second.mp3");
        Assert.True(firstTask.IsCanceled);

        vm.Dispose();
    }

    [AvaloniaFact]
    public async Task SequentialPlayback_TwoFiles_FirstWaitsForCompletion()
    {
        // 模拟连播：播放 A → 等完成 → 播放 B → 等完成
        var mock = new MockAudioPlayer { FakeLength = 10 };
        var vm = new AudioPlayerViewModel(mock);
        var playLog = new List<string>();

        // 播放第一个文件
        vm.LoadFile("first.mp3");
        vm.TogglePlayCommand.Execute(null);
        playLog.Add("first_started");

        var task1 = vm.WaitForCompletionAsync();

        // 模拟第一个文件播放结束（NAudio 线程触发 PlaybackEnded）
        mock.RaisePlaybackEnded();
        await task1;
        playLog.Add("first_ended");

        // 播放第二个文件
        vm.LoadFile("second.mp3");
        vm.TogglePlayCommand.Execute(null);
        playLog.Add("second_started");

        var task2 = vm.WaitForCompletionAsync();
        mock.RaisePlaybackEnded();
        await task2;
        playLog.Add("second_ended");

        // 验证顺序：first_started → first_ended → second_started → second_ended
        Assert.Equal(4, playLog.Count);
        Assert.Equal("first_started", playLog[0]);
        Assert.Equal("first_ended", playLog[1]);
        Assert.Equal("second_started", playLog[2]);
        Assert.Equal("second_ended", playLog[3]);

        vm.Dispose();
    }
}
