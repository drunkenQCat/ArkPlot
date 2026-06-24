using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using ArkPlot.Core.Services;

namespace ArkPlot.Avalonia.ViewModels.Test;

/// <summary>
/// 模拟网络中断场景的测试 ViewModel。
/// 触发弹窗引导用户跳转到设置页配置代理。
/// </summary>
public partial class NetworkFailureTestViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _log = "点击下方按钮模拟网络中断场景。";

    [RelayCommand]
    private async Task SimulateNetworkFailureAsync()
    {
        Log += "\n[模拟] 正在生成内容...";

        // 模拟短暂的"生成中"延迟
        await Task.Delay(800);

        Log += "\n[模拟] 网络请求失败：连接超时";

        await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                var box = MessageBoxManager
                    .GetMessageBoxStandard("网络连接失败",
                        "生成过程中网络连接中断，无法下载所需资源。\n\n是否打开设置页面配置代理加速？",
                        ButtonEnum.YesNo, Icon.Warning);

                var result = await box.ShowAsync();
                if (result == ButtonResult.Yes)
                {
                    Log += "\n[用户] 选择了跳转到设置页";
                    var messenger = WeakReferenceMessenger.Default;
                    messenger.Send(new OpenWindowMessage("SettingsWindow", "", selectedTabIndex: 4));
                }
                else
                {
                    Log += "\n[用户] 取消了跳转";
                }
            }
            catch
            {
                Log += "\n[错误] 弹窗显示失败";
            }
        });
    }

    [RelayCommand]
    private void ClearLog()
    {
        Log = "";
    }
}