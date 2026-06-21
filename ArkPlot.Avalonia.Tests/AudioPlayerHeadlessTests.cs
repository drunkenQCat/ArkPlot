using System;
using System.Collections.Generic;
using System.IO;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Avalonia.ViewModels.Test;
using ArkPlot.Avalonia.Views;
using ArkPlot.Avalonia.Views.Controls;
using ArkPlot.Avalonia.Views.Test;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

public class MockAudioPlayer : IAudioPlayer
{
    public double FakeLength { get; set; } = 120.0;
    public double FakeCursor { get; set; }
    public bool FakeIsPlaying { get; set; }
    public int PlayCallCount { get; set; }
    public int PauseCallCount { get; set; }
    public int StopCallCount { get; set; }
    public int SeekCallCount { get; set; }
    public double LastSeekPosition { get; set; }
    public string? LastLoadedFile { get; set; }

    public event EventHandler? PlaybackEnded;

    public void LoadFile(string path)
    {
        LastLoadedFile = path;
        FakeCursor = 0;
        FakeIsPlaying = false;
    }

    public void Play()
    {
        PlayCallCount++;
        FakeIsPlaying = true;
    }

    public void Pause()
    {
        PauseCallCount++;
        FakeIsPlaying = false;
    }

    public void Stop()
    {
        StopCallCount++;
        FakeIsPlaying = false;
        FakeCursor = 0;
    }

    public void SeekToSeconds(double seconds)
    {
        SeekCallCount++;
        LastSeekPosition = seconds;
        FakeCursor = seconds;
    }

    public double GetLengthInSeconds() => FakeLength;
    public double GetCursorInSeconds() => FakeCursor;
    public bool IsPlaying() => FakeIsPlaying;

    public void RaisePlaybackEnded() =>
        PlaybackEnded?.Invoke(this, EventArgs.Empty);

    public void Dispose() { }
}

public class AudioPlayerHeadlessTests
{
    [AvaloniaFact]
    public void ViewModel_InitialState_IsNotPlaying()
    {
        var mock = new MockAudioPlayer();
        var vm = new AudioPlayerViewModel(mock);

        Assert.False(vm.IsPlaying);
        Assert.Equal("▶", vm.PlayButtonText);
        Assert.Equal("00:00", vm.CurrentTimeText);
        Assert.Equal("00:00", vm.TotalTimeText);
        Assert.Equal(0, vm.Position);
        Assert.Equal(0, vm.Duration);
    }

    [AvaloniaFact]
    public void ViewModel_LoadFile_SetsDuration()
    {
        var mock = new MockAudioPlayer { FakeLength = 180.5 };
        var vm = new AudioPlayerViewModel(mock);

        vm.LoadFile("test.mp3");

        Assert.Equal(180.5, vm.Duration);
        Assert.Equal("03:00", vm.TotalTimeText);
        Assert.Equal("test.mp3", mock.LastLoadedFile);
        Assert.Equal(0, vm.Position);
    }

    [AvaloniaFact]
    public void ViewModel_TogglePlay_SwitchesState()
    {
        var mock = new MockAudioPlayer();
        var vm = new AudioPlayerViewModel(mock);

        // Start playing
        vm.TogglePlayCommand.Execute(null);
        Assert.True(vm.IsPlaying);
        Assert.Equal("⏸", vm.PlayButtonText);
        Assert.Equal(1, mock.PlayCallCount);

        // Pause
        vm.TogglePlayCommand.Execute(null);
        Assert.False(vm.IsPlaying);
        Assert.Equal("▶", vm.PlayButtonText);
        Assert.Equal(1, mock.PauseCallCount);
    }

    [AvaloniaFact]
    public void ViewModel_Stop_ResetsPosition()
    {
        var mock = new MockAudioPlayer { FakeLength = 60, FakeCursor = 30 };
        var vm = new AudioPlayerViewModel(mock);
        vm.LoadFile("test.mp3");

        // Simulate playing then stop
        vm.TogglePlayCommand.Execute(null);
        Assert.True(vm.IsPlaying);

        vm.StopCommand.Execute(null);
        Assert.False(vm.IsPlaying);
        Assert.Equal(0, vm.Position);
        Assert.Equal("00:00", vm.CurrentTimeText);
        Assert.True(mock.StopCallCount >= 1);
    }

    [AvaloniaFact]
    public void ViewModel_SeekFromSlider_CallsSeek()
    {
        var mock = new MockAudioPlayer { FakeLength = 120 };
        var vm = new AudioPlayerViewModel(mock);
        vm.LoadFile("test.mp3");

        // Simulate slider drag
        vm.Position = 60.0;

        Assert.Equal(1, mock.SeekCallCount);
        Assert.Equal(60.0, mock.LastSeekPosition);
        Assert.Equal("01:00", vm.CurrentTimeText);
    }

    [AvaloniaFact]
    public void AudioPlayerControl_CanBeCreated()
    {
        var mock = new MockAudioPlayer();
        var vm = new AudioPlayerViewModel(mock);
        var control = new AudioPlayerControl { DataContext = vm };

        Assert.NotNull(control);
    }

    [AvaloniaFact]
    public void AudioPlayerControl_ContainsSliderAndButton()
    {
        var mock = new MockAudioPlayer { FakeLength = 60 };
        var vm = new AudioPlayerViewModel(mock);
        var control = new AudioPlayerControl { DataContext = vm };

        var window = new Window { Content = control };
        window.Show();

        var slider = control.GetVisualDescendants().OfType<Slider>().FirstOrDefault();
        var button = control.GetVisualDescendants().OfType<Button>().FirstOrDefault();

        Assert.NotNull(slider);
        Assert.NotNull(button);
    }

    [AvaloniaFact]
    public void AudioTestPanel_CanBeCreatedAndShown()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"arkplot_audio_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllBytes(Path.Combine(tempDir, "test.mp3"), new byte[100]);

            var vm = new AudioTestViewModel(tempDir);
            var panel = new AudioTestPanel { DataContext = vm };

            var window = new Window { Content = panel };
            window.Show();

            Assert.True(window.IsVisible);
            Assert.Single(vm.Mp3Files);
            Assert.Equal("找到 1 个 MP3 文件", vm.StatusText);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [AvaloniaFact]
    public void AudioTestPanel_NoMp3Files_ShowsMessage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"arkplot_audio_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var vm = new AudioTestViewModel(tempDir);
            Assert.Empty(vm.Mp3Files);
            Assert.Equal("未找到 MP3 文件", vm.StatusText);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [AvaloniaFact]
    public void AudioTestPanel_DirNotExists_ShowsMessage()
    {
        var vm = new AudioTestViewModel("/nonexistent/path");
        Assert.Empty(vm.Mp3Files);
        Assert.Contains("目录不存在", vm.StatusText);
    }

    [AvaloniaFact]
    public void AudioTestViewModel_SelectFile_LoadsPlayer()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"arkplot_audio_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var mp3Path = Path.Combine(tempDir, "song.mp3");
            File.WriteAllBytes(mp3Path, new byte[100]);

            var mock = new MockAudioPlayer { FakeLength = 60 };
            var vm = new AudioTestViewModel(tempDir, mock);
            vm.SelectedFile = mp3Path;

            Assert.Contains("已加载", vm.StatusText);
            Assert.Contains("song.mp3", vm.StatusText);
            Assert.Equal(mp3Path, mock.LastLoadedFile);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [AvaloniaFact]
    public void TestWindow_ContainsAudioTab()
    {
        var window = new TestWindow();
        window.Show();

        var tabControl = window.GetVisualDescendants()
            .OfType<TabControl>()
            .FirstOrDefault();

        Assert.NotNull(tabControl);

        var tabs = tabControl.Items.OfType<TabItem>().ToList();
        Assert.Contains(tabs, t => t.Header?.ToString() == "音频播放");
    }

    [AvaloniaFact]
    public void AudioTestPanel_ContainsAudioPlayerControl()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"arkplot_audio_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var vm = new AudioTestViewModel(tempDir);
            var panel = new AudioTestPanel { DataContext = vm };

            var window = new Window { Content = panel };
            window.Show();

            var audioPlayer = panel.GetVisualDescendants()
                .OfType<AudioPlayerControl>()
                .FirstOrDefault();

            Assert.NotNull(audioPlayer);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
