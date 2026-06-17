using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.ViewModels;
using Avalonia.Headless.XUnit;
using Xunit;
using Xunit.Abstractions;

namespace ArkPlot.Avalonia.Tests;

/// <summary>
/// 测试连播过程中上一句/下一句导航功能。
/// 使用真实 MP3 文件确保 PlayAudioFile 不会瞬间返回。
/// </summary>
public class TtsPlaybackNavigationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ITestOutputHelper _output;
    private readonly string _mp3Path;

    public TtsPlaybackNavigationTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"arkplot_playback_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // 从真实数据目录复制一个小 mp3 文件用于测试
        var cacheDir = @"C:\TechProjects\About_MyRepos\ArkPlot\ArkPlot.Avalonia\bin\Debug\net9.0\output\孤星\tts\_tts_cache";
        var sourceFile = Directory.Exists(cacheDir)
            ? Directory.GetFiles(cacheDir, "*.mp3").OrderBy(f => new FileInfo(f).Length).FirstOrDefault()
            : null;

        if (sourceFile != null)
        {
            _mp3Path = Path.Combine(_tempDir, "test.mp3");
            File.Copy(sourceFile, _mp3Path);
            _output.WriteLine($"已复制测试 MP3: {new FileInfo(_mp3Path).Length} bytes");
        }
        else
        {
            _mp3Path = null!;
            _output.WriteLine("⚠️ 未找到测试 MP3，部分测试将跳过");
        }
    }

    private T GetPrivate<T>(object obj, string name)
    {
        var field = obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        return (T)field!.GetValue(obj)!;
    }

    private void SetPrivate(object obj, string name, object? value)
    {
        obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(obj, value);
    }

    private bool HasRealMp3 => _mp3Path != null && File.Exists(_mp3Path);

    [AvaloniaFact]
    public void PlayPrev_Next_BeforePlayback_ShouldNotBeEnabled()
    {
        var vm = new TtsViewModel(_tempDir);

        Assert.False(vm.PlayPrevCommand.CanExecute(null), "未播放时 PlayPrev 应该禁用");
        Assert.False(vm.PlayNextCommand.CanExecute(null), "未播放时 PlayNext 应该禁用");

        vm.Dispose();
    }

    [AvaloniaFact]
    public void PlayPrev_AtFirstSegment_ShouldNotChangeIndex()
    {
        var vm = new TtsViewModel(_tempDir);
        vm.IsPlaying = true;
        SetPrivate(vm, "_playbackSegments", new[] { new SegmentRow { Index = 1 } }.ToList());
        SetPrivate(vm, "_playbackIndex", 0);

        vm.PlayPrevCommand.Execute(null);

        Assert.Equal(0, GetPrivate<int>(vm, "_playbackIndex"));
        vm.Dispose();
    }

    [AvaloniaFact]
    public void PlayNext_AtLastSegment_ShouldNotChangeIndex()
    {
        var vm = new TtsViewModel(_tempDir);
        var segments = Enumerable.Range(1, 3).Select(i => new SegmentRow { Index = i }).ToList();

        vm.IsPlaying = true;
        SetPrivate(vm, "_playbackSegments", segments);
        SetPrivate(vm, "_playbackIndex", 2);

        vm.PlayNextCommand.Execute(null);

        Assert.Equal(2, GetPrivate<int>(vm, "_playbackIndex"));
        vm.Dispose();
    }

    /// <summary>
    /// 核心测试：PlayPrev 更新索引并切换活跃段落
    /// </summary>
    [AvaloniaFact]
    public void PlayPrev_UpdatesIndex_BeforePlayback()
    {
        if (!HasRealMp3) return; // 跳过

        var vm = new TtsViewModel(_tempDir);
        var segments = Enumerable.Range(1, 5).Select(i => new SegmentRow
        {
            Index = i,
            CharacterName = $"角色{i}",
            NovelText = $"第{i}段",
            HasAudio = true,
            AudioFilePath = _mp3Path,
        }).ToList();

        foreach (var seg in segments)
            vm.FilteredSegments.Add(seg);

        vm.IsPlaying = true;
        SetPrivate(vm, "_playbackSegments", segments);
        SetPrivate(vm, "_playbackIndex", 3);

        var genBefore = GetPrivate<int>(vm, "_playbackGeneration");

        vm.PlayPrevCommand.Execute(null);

        // 验证：索引已递减
        var indexAfter = GetPrivate<int>(vm, "_playbackIndex");
        var genAfter = GetPrivate<int>(vm, "_playbackGeneration");

        Assert.Equal(2, indexAfter);
        Assert.True(genAfter > genBefore, $"代际应该递增: {genAfter} > {genBefore}");

        _output.WriteLine($"✅ PlayPrev: Index 3→{indexAfter}, Generation {genBefore}→{genAfter}");
        vm.Dispose();
    }

    /// <summary>
    /// 核心测试：PlayNext 更新索引并切换活跃段落
    /// </summary>
    [AvaloniaFact]
    public void PlayNext_UpdatesIndex_BeforePlayback()
    {
        if (!HasRealMp3) return;

        var vm = new TtsViewModel(_tempDir);
        var segments = Enumerable.Range(1, 5).Select(i => new SegmentRow
        {
            Index = i,
            CharacterName = $"角色{i}",
            NovelText = $"第{i}段",
            HasAudio = true,
            AudioFilePath = _mp3Path,
        }).ToList();

        foreach (var seg in segments)
            vm.FilteredSegments.Add(seg);

        vm.IsPlaying = true;
        SetPrivate(vm, "_playbackSegments", segments);
        SetPrivate(vm, "_playbackIndex", 1);

        var genBefore = GetPrivate<int>(vm, "_playbackGeneration");

        vm.PlayNextCommand.Execute(null);

        var indexAfter = GetPrivate<int>(vm, "_playbackIndex");
        var genAfter = GetPrivate<int>(vm, "_playbackGeneration");

        Assert.Equal(2, indexAfter);
        Assert.True(genAfter > genBefore, $"代际应该递增: {genAfter} > {genBefore}");

        _output.WriteLine($"✅ PlayNext: Index 1→{indexAfter}, Generation {genBefore}→{genAfter}");
        vm.Dispose();
    }

    /// <summary>
    /// 核心测试：快速连续点击不会导致状态错乱
    /// </summary>
    [AvaloniaFact]
    public void RapidNavigation_IndexUpdatesCorrectly()
    {
        if (!HasRealMp3) return;

        var vm = new TtsViewModel(_tempDir);
        var segments = Enumerable.Range(1, 10).Select(i => new SegmentRow
        {
            Index = i,
            CharacterName = $"角色{i}",
            NovelText = $"第{i}段",
            HasAudio = true,
            AudioFilePath = _mp3Path,
        }).ToList();

        foreach (var seg in segments)
            vm.FilteredSegments.Add(seg);

        vm.IsPlaying = true;
        SetPrivate(vm, "_playbackSegments", segments);
        SetPrivate(vm, "_playbackIndex", 5);

        // 快速连续点击：5→6→7→6→7
        vm.PlayNextCommand.Execute(null);
        vm.PlayNextCommand.Execute(null);
        vm.PlayPrevCommand.Execute(null);
        vm.PlayNextCommand.Execute(null);

        var finalIndex = GetPrivate<int>(vm, "_playbackIndex");
        Assert.Equal(7, finalIndex);

        _output.WriteLine($"✅ 快速导航: 5→6→7→6→7, Final={finalIndex}");
        vm.Dispose();
    }

    /// <summary>
    /// 测试：代际计数器防止旧 PlayLoopAsync 的 finally 覆盖新状态
    /// </summary>
    [AvaloniaFact]
    public async Task GenerationCounter_OldLoopFinally_DoesNotOverwrite()
    {
        if (!HasRealMp3) return;

        var vm = new TtsViewModel(_tempDir);
        var segments = Enumerable.Range(1, 5).Select(i => new SegmentRow
        {
            Index = i,
            CharacterName = $"角色{i}",
            NovelText = $"第{i}段",
            HasAudio = true,
            AudioFilePath = _mp3Path,
        }).ToList();

        foreach (var seg in segments)
            vm.FilteredSegments.Add(seg);

        // 模拟连播中
        vm.IsPlaying = true;
        SetPrivate(vm, "_playbackSegments", segments);
        SetPrivate(vm, "_playbackIndex", 2);

        // 调用 PlayPrev
        vm.PlayPrevCommand.Execute(null);

        // 等待一小段时间让 async 链启动
        await Task.Delay(200);

        // 验证：IsPlaying 仍然为 true（旧 finally 没有覆盖）
        Assert.True(vm.IsPlaying, "IsPlaying 不应该被旧循环的 finally 覆盖");

        var index = GetPrivate<int>(vm, "_playbackIndex");
        _output.WriteLine($"✅ 200ms 后: IsPlaying={vm.IsPlaying}, Index={index}");

        vm.Dispose();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
