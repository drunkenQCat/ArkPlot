using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkPlot.Avalonia.ViewModels;

public partial class AudioPlayerViewModel : ViewModelBase, IDisposable
{
    private readonly IAudioPlayer _player;
    private readonly DispatcherTimer _timer;
    private readonly Func<string?>? _filePathProvider;
    private bool _updatingFromTimer;
    private bool _fileLoaded;
    private TaskCompletionSource? _completionTcs;

    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _position;
    [ObservableProperty] private double _duration;
    [ObservableProperty] private string _currentTimeText = "00:00";
    [ObservableProperty] private string _totalTimeText = "00:00";

    public string PlayButtonText => IsPlaying ? "⏸" : "▶";

    public AudioPlayerViewModel() : this(null, null) { }

    public AudioPlayerViewModel(IAudioPlayer? player = null, Func<string?>? filePathProvider = null)
    {
        _player = player ?? CreateDefaultPlayer();
        _filePathProvider = filePathProvider;
        _player.PlaybackEnded += OnPlaybackEnded;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _timer.Tick += TimerTick;
    }

    /// <summary>等待当前播放完成（自然结束或手动停止）。</summary>
    public Task WaitForCompletionAsync()
    {
        return _completionTcs?.Task ?? Task.CompletedTask;
    }

    private static IAudioPlayer CreateDefaultPlayer()
    {
        return new Services.NAudioPlayer();
    }

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayButtonText));

    partial void OnPositionChanged(double value)
    {
        if (_updatingFromTimer) return;
        _player.SeekToSeconds(value);
        CurrentTimeText = FormatTime(value);
    }

    public void LoadFile(string path)
    {
        // 取消上一次等待，防止连播时 stale 回调提前完成新 TCS
        _completionTcs?.TrySetCanceled();
        Stop();
        _player.LoadFile(path);
        _fileLoaded = true;
        Duration = _player.GetLengthInSeconds();
        TotalTimeText = FormatTime(Duration);
        Position = 0;
        CurrentTimeText = "00:00";
    }

    private void EnsureFileLoaded()
    {
        if (_fileLoaded) return;
        var path = _filePathProvider?.Invoke();
        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            LoadFile(path);
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (IsPlaying)
        {
            _player.Pause();
            _timer.Stop();
            IsPlaying = false;
            // 暂停时不完成 _completionTcs — 让连播循环继续等待恢复
        }
        else
        {
            EnsureFileLoaded();
            if (Position >= Duration && Duration > 0)
                _player.SeekToSeconds(0);
            if (_completionTcs == null || _completionTcs.Task.IsCompleted)
                _completionTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _player.Play();
            _timer.Start();
            IsPlaying = true;
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _player.Stop();
        _timer.Stop();
        IsPlaying = false;
        Position = 0;
        CurrentTimeText = "00:00";
        _completionTcs?.TrySetResult();
    }

    private void TimerTick(object? sender, EventArgs e)
    {
        if (!_player.IsPlaying())
        {
            _timer.Stop();
            IsPlaying = false;
            _updatingFromTimer = true;
            Position = Duration;
            _updatingFromTimer = false;
            CurrentTimeText = FormatTime(Duration);
            return;
        }

        _updatingFromTimer = true;
        Position = _player.GetCursorInSeconds();
        _updatingFromTimer = false;
        CurrentTimeText = FormatTime(Position);
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        // 先完成 TCS（NAudio 线程调用，TrySetResult 线程安全）
        _completionTcs?.TrySetResult();

        // UI 更新派发到 UI 线程
        Dispatcher.UIThread.Post(() =>
        {
            _timer.Stop();
            IsPlaying = false;
            _updatingFromTimer = true;
            Position = Duration;
            _updatingFromTimer = false;
            CurrentTimeText = FormatTime(Duration);
        });
    }

    public void SeekToPosition(double seconds)
    {
        _player.SeekToSeconds(seconds);
        CurrentTimeText = FormatTime(seconds);
    }

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            return "00:00";
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= TimerTick;
        _player.PlaybackEnded -= OnPlaybackEnded;
        _player.Dispose();
    }
}
