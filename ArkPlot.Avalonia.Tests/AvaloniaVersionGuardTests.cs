using System;
using Avalonia;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

/// <summary>
/// 锁住 Avalonia 版本下限：11.3.5 之前的 ClassBindingManager.GetClassProperty 查表用带前缀的 key
/// （"__AvaloniaReserved::Classes::error"）、写表用不带前缀的 key（"error"），缓存永不命中，
/// 于是 XAML 里每求值一次 Classes.xxx="{Binding}" 就注册一个全新的全局 StyledProperty&lt;bool&gt;
/// 及其订阅，且永不释放。实测本应用 1491 条日志放大出 207 万个代理属性 + 订阅，占存活堆 86%（进程 5GB）。
/// 该守卫只能在版本被误降时报警；修复本身的有效性由 dump 侧的 StyledProperty&lt;Boolean&gt; 实例数验证。
/// </summary>
public class AvaloniaVersionGuardTests
{
    [Fact]
    public void Avalonia_IsAtLeast_ClassBindingManagerCacheFix()
    {
        var version = typeof(AvaloniaObject).Assembly.GetName().Version ?? new Version(0, 0);

        Assert.True(
            version >= new Version(11, 3, 5),
            $"Avalonia.Base 版本 {version} 低于 11.3.5：该版本的 ClassBindingManager 缓存 key 拼错，" +
            "每个控件实例的类绑定都会注册一个永不释放的全局属性（曾导致进程 5GB）。请勿降级。");
    }
}
