using ArkPlot.Avalonia.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArkPlot.Avalonia.ViewModels.Test;

/// <summary>
/// 「开发选项」测试面板的 ViewModel。
/// 目前含两个开发开关：Mock小说化（Novelizer 不调真实 API）、Mock图片描述（Vision 不调真实 API）。
/// 开关状态持久化到 settings.json 的 Novelizer.UseMock / Vision.UseMockVision。
/// </summary>
public partial class DevOptionsViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool useMockNovelizer;

    [ObservableProperty]
    private bool useMockVision;

    /// <summary>日志面板是否显示「清除缓存」按钮（开发调试用）。持久化到 settings.json 的 Novelizer.ShowClearCacheButton。</summary>
    [ObservableProperty]
    private bool showClearCacheButton;

    public DevOptionsViewModel()
    {
        var settings = AppSettings.Load();
        UseMockNovelizer = settings.Novelizer.UseMock;
        UseMockVision = settings.Vision?.UseMockVision ?? false;
        ShowClearCacheButton = settings.Novelizer.ShowClearCacheButton;
    }

    partial void OnUseMockNovelizerChanged(bool value)
    {
        var settings = AppSettings.Load();
        var novelizer = settings.Novelizer with { UseMock = value };
        settings = settings with { Novelizer = novelizer };
        settings.Save();
    }

    partial void OnUseMockVisionChanged(bool value)
    {
        var settings = AppSettings.Load();
        var vision = (settings.Vision ?? VisionSettings.CreateDefaults()) with { UseMockVision = value };
        settings = settings with { Vision = vision };
        settings.Save();
    }

    partial void OnShowClearCacheButtonChanged(bool value)
    {
        var settings = AppSettings.Load();
        var novelizer = settings.Novelizer with { ShowClearCacheButton = value };
        settings = settings with { Novelizer = novelizer };
        settings.Save();
    }
}