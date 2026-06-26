using ArkPlot.Avalonia.Services;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Avalonia.Views;
using ArkPlot.Core.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Messaging;

namespace ArkPlot.Avalonia;

public partial class App : Application
{
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
