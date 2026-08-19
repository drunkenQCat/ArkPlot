using System.IO;
using ArkPlot.Avalonia.Services;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Avalonia.Views;
using ArkPlot.Core.Infrastructure;
using ArkPlot.Core.Services;
using ArkPlot.Novelizer;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Messaging;

namespace ArkPlot.Avalonia;

public partial class App : Application
{
    /// <summary>小说化 LLM 调用复盘收集器（进程级共享：主窗口写入，复盘面板读取）。</summary>
    public static NovelizerTraceCollector TraceCollector { get; } = new();
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        var messenger = WeakReferenceMessenger.Default;
        messenger.Register<OpenWindowMessage>(
            this,
            (recipient, message) =>
            {
                // 根据消息中的WindowName打开相应的窗口
                if (message.WindowName == "SettingsWindow")
                {
                    var settingsView = new SettingsWindow();
                    var settingsViewModel = new SettingsViewModel(message.JsonPath!);
                    if (message.SelectedTabIndex.HasValue)
                        settingsViewModel.SelectedTabIndex = message.SelectedTabIndex.Value;
                    settingsView.DataContext = settingsViewModel;
                    settingsView.Show();
                }
                else if (message.WindowName == "TtsWindow")
                {
                    var ttsView = new TtsWindow();
                    var ttsViewModel = new TtsViewModel(message.ActName!);
                    ttsView.DataContext = ttsViewModel;
                    ttsView.Show();
                }
                else if (message.WindowName == "TraceReviewWindow")
                {
                    var view = new TraceReviewWindow();
                    var viewModel = new TraceReviewViewModel
                    {
                        TraceRoot = message.ActName is { } act
                            ? Path.Combine(OutputPaths.ActRootAbsolute(act), "novelizer-traces")
                            : null,
                        LiveCollector = TraceCollector,
                    };
                    view.DataContext = viewModel;
                    viewModel.RefreshRuns();
                    if (!string.IsNullOrEmpty(message.TurnKey))
                        viewModel.SelectTurnByKey(message.TurnKey);
                    view.Show();
                }
            }
        );
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel() };

            var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
            GlobalStorageProvider.StorageProvider = topLevel!.StorageProvider;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
