using ArkPlot.Avalonia.ViewModels;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

/// <summary>
/// 验证导航按钮的 CanExecute 通知机制
/// </summary>
public class NavigationButtonCanExecuteTests
{
    [Fact]
    public void PlayPrevCommand_ShouldNotifyCanExecute_WhenIsPlayingChanges()
    {
        var vm = new TtsViewModel("/tmp/test");
        
        // 初始状态：IsPlaying = false，命令应该不可执行
        Assert.False(vm.IsPlaying);
        Assert.False(vm.PlayPrevCommand.CanExecute(null));
        
        // 改变 IsPlaying 为 true
        vm.IsPlaying = true;
        
        // 关键测试：CanExecute 应该自动更新为 true
        // 如果没有 [NotifyCanExecuteChangedFor]，这里会失败
        Assert.True(vm.PlayPrevCommand.CanExecute(null), 
            "IsPlaying=true 后 PlayPrevCommand 应该可执行，但实际不可执行。" +
            "说明缺少 [NotifyCanExecuteChangedFor(nameof(PlayPrevCommand))] 特性");
        
        // 再次改变 IsPlaying 为 false
        vm.IsPlaying = false;
        
        // CanExecute 应该自动更新为 false
        Assert.False(vm.PlayPrevCommand.CanExecute(null),
            "IsPlaying=false 后 PlayPrevCommand 应该不可执行");
    }
    
    [Fact]
    public void PlayNextCommand_ShouldNotifyCanExecute_WhenIsPlayingChanges()
    {
        var vm = new TtsViewModel("/tmp/test");
        
        // 初始状态：IsPlaying = false，命令应该不可执行
        Assert.False(vm.IsPlaying);
        Assert.False(vm.PlayNextCommand.CanExecute(null));
        
        // 改变 IsPlaying 为 true
        vm.IsPlaying = true;
        
        // 关键测试：CanExecute 应该自动更新为 true
        Assert.True(vm.PlayNextCommand.CanExecute(null),
            "IsPlaying=true 后 PlayNextCommand 应该可执行，但实际不可执行。" +
            "说明缺少 [NotifyCanExecuteChangedFor(nameof(PlayNextCommand))] 特性");
        
        // 再次改变 IsPlaying 为 false
        vm.IsPlaying = false;
        
        // CanExecute 应该自动更新为 false
        Assert.False(vm.PlayNextCommand.CanExecute(null),
            "IsPlaying=false 后 PlayNextCommand 应该不可执行");
    }
}
