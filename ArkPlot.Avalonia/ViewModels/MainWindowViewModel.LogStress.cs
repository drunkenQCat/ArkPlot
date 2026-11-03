using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.Services;
using ArkPlot.Avalonia.Views.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArkPlot.Avalonia.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool isLogStressRunning;

    private async Task RunLogStressAsync(LogStressOptions options)
    {
        IsLogStressRunning = true;
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var window = desktop?.MainWindow;
        using var cancellation = new CancellationTokenSource();
        _loadMdCts = cancellation;
        void OnClosed(object? sender, EventArgs e) => cancellation.Cancel();
        if (window != null) window.Closed += OnClosed;
        var watch = Stopwatch.StartNew();
        var emitted = 0;
        var exitCode = 0;
        try
        {
            var path = Path.GetFullPath(options.ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var report = new StreamWriter(path, append: false) { AutoFlush = true };
            await using var log = new StreamWriter(path + ".log", append: false);
            using var process = Process.GetCurrentProcess();
            void Sample(string state)
            {
                process.Refresh();
                var visuals = window?.GetVisualDescendants().ToArray() ?? [];
                var scroll = visuals.OfType<WorkflowLogStream>().FirstOrDefault()?.FindControl<ScrollViewer>("LogScroll");
                var line = JsonSerializer.Serialize(new
                {
                    state, emitted, elapsedMs = watch.ElapsedMilliseconds,
                    retained = WorkflowLog.Stages.Sum(s => s.Logs.Count),
                    visible = WorkflowLog.VisibleLogs.Count,
                    realizedRows = visuals.OfType<LogRow>().Count(),
                    lastVisible = WorkflowLog.VisibleLogs.LastOrDefault()?.Message,
                    scrollOffset = scroll?.Offset.Y,
                    scrollMaximum = scroll?.ScrollBarMaximum.Y,
                    managedBytes = GC.GetTotalMemory(false),
                    workingSetBytes = process.WorkingSet64,
                    privateBytes = process.PrivateMemorySize64,
                    avalonia = typeof(AvaloniaObject).Assembly.GetName().Version?.ToString(),
                });
                report.WriteLine(line);
                Console.WriteLine(line);
            }

            Status = $"离线日志压测：{options.Count:N0} 条；报告：{path}";
            WorkflowLog.BeginPipeline(["日志压力测试（离线）"]);
            WorkflowLog.EnterStage(0);
            // Allow the normal window layout to run before measuring containers.
            await Task.Delay(100, cancellation.Token);
            Sample("started");
            var nextSample = 1000;
            try
            {
                while (emitted < options.Count)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var end = (int)Math.Min((long)options.Count, (long)emitted + options.BatchSize);
                    while (emitted < end)
                    {
                        var n = ++emitted;
                        var level = n % 100 == 0 ? LogLevel.Error : n % 10 == 0 ? LogLevel.Warn : n % 3 == 0 ? LogLevel.Success : LogLevel.Info;
                        var message = $"[stress {n:D8}] 日志压力测试：下载章节 / 解析文档 / 小说化完成。";
                        log.WriteLine($"{DateTimeOffset.Now:O} [{level}] {message}");
                        WorkflowLog.Append(level, message,
                            detail: n % 25 == 0 ? $"离线测试详情 #{n}\n输入、思考和输出示例。\n{new string('测', 512)}" : null,
                            isSection: n % 500 == 1);
                    }
                    // Append posts onto the actual dispatcher; drain it before sampling, without blocking layout.
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    await Task.Delay(options.IntervalMs, cancellation.Token);
                    await log.FlushAsync(cancellation.Token);
                    if (emitted >= nextSample)
                    {
                        Sample("running");
                        nextSample = emitted > int.MaxValue - 1000 ? int.MaxValue : emitted + 1000;
                    }
                }
                WorkflowLog.CompletePipeline();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                await Task.Delay(250, cancellation.Token);
                Sample("completed");
                Status = $"日志压测完成：{emitted:N0} 条；报告：{path}";
            }
            catch (OperationCanceledException)
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Sample("cancelled");
                Status = $"日志压测已停止：{emitted:N0} 条；报告：{path}";
            }
        }
        catch (OperationCanceledException)
        {
            Status = $"日志压测已停止：{emitted:N0} 条";
        }
        catch (Exception ex)
        {
            exitCode = 1;
            Status = $"日志压测失败：{ex.Message}";
            Console.Error.WriteLine(ex);
        }
        finally
        {
            if (window != null) window.Closed -= OnClosed;
            _loadMdCts = null;
            IsLogStressRunning = false;
            // Leave normal generation disabled: resource initialization was intentionally skipped.
            if (options.ExitWhenDone) desktop?.Shutdown(exitCode);
        }
    }
}
