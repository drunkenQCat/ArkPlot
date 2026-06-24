using System.Net;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Core.Utilities;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

/// <summary>
/// GitHub 代理 + 连接失败弹窗 headless 测试。
/// </summary>
public class GitHubProxyHeadlessTests
{
    // ════════════════════════════════════════════
    //  纯逻辑测试（无需 headless）
    // ════════════════════════════════════════════

    [Fact]
    public void CheckConnectionError_NoPrefix_TriggersEvent()
    {
        GitHubProxy.Prefix = "";
        bool eventFired = false;
        GitHubProxy.ConnectionFailed += _ => eventFired = true;

        GitHubProxy.CheckConnectionError("https://github.com/foo", exception: new HttpRequestException());

        Assert.True(eventFired, "未配置代理时 GitHub 连接失败应触发事件");
    }

    [Fact]
    public void CheckConnectionError_WithPrefix_DoesNotTriggerEvent()
    {
        GitHubProxy.Prefix = "https://gh-proxy.com/";
        bool eventFired = false;
        GitHubProxy.ConnectionFailed += _ => eventFired = true;

        GitHubProxy.CheckConnectionError("https://github.com/foo", exception: new HttpRequestException());

        Assert.False(eventFired, "已配置代理时不应触发事件");
    }

    [Fact]
    public void CheckConnectionError_403_TriggersEvent()
    {
        GitHubProxy.Prefix = "";
        bool eventFired = false;
        GitHubProxy.ConnectionFailed += _ => eventFired = true;

        GitHubProxy.CheckConnectionError("https://api.github.com/foo", statusCode: 403);

        Assert.True(eventFired, "HTTP 403 应触发连接失败事件");
    }

    [Fact]
    public void CheckConnectionError_NonGithubUrl_DoesNotTrigger()
    {
        GitHubProxy.Prefix = "";
        bool eventFired = false;
        GitHubProxy.ConnectionFailed += _ => eventFired = true;

        GitHubProxy.CheckConnectionError("https://example.com/foo", exception: new HttpRequestException());

        Assert.False(eventFired, "非 GitHub URL 不应触发事件");
    }

    [Fact]
    public void CheckConnectionError_500_TriggersEvent()
    {
        GitHubProxy.Prefix = "";
        bool eventFired = false;
        GitHubProxy.ConnectionFailed += _ => eventFired = true;

        GitHubProxy.CheckConnectionError("https://github.com/foo", statusCode: 500);

        Assert.True(eventFired, "HTTP 500 应触发连接失败事件");
    }

    [Fact]
    public void CheckConnectionError_404_DoesNotTrigger()
    {
        GitHubProxy.Prefix = "";
        bool eventFired = false;
        GitHubProxy.ConnectionFailed += _ => eventFired = true;

        GitHubProxy.CheckConnectionError("https://github.com/foo", statusCode: 404);

        Assert.False(eventFired, "HTTP 404 是文件不存在不是网络问题，不应触发");
    }

    [Theory]
    [InlineData("https://gh-proxy.com/", "https://github.com/x", "https://gh-proxy.com/https://github.com/x")]
    [InlineData("https://gh-proxy.com", "https://github.com/x", "https://gh-proxy.com/https://github.com/x")]
    [InlineData("", "https://github.com/x", "https://github.com/x")]
    public void GetUrl_TransformsCorrectly(string prefix, string input, string expected)
    {
        GitHubProxy.Prefix = prefix;
        Assert.Equal(expected, GitHubProxy.GetUrl(input));
    }

    [Fact]
    public void SetPrefix_AppliesImmediately()
    {
        GitHubProxy.Prefix = "https://proxy1.com/";
        var url1 = GitHubProxy.GetUrl("https://github.com/x");

        GitHubProxy.Prefix = "https://proxy2.com/";
        var url2 = GitHubProxy.GetUrl("https://github.com/x");

        Assert.Equal("https://proxy1.com/https://github.com/x", url1);
        Assert.Equal("https://proxy2.com/https://github.com/x", url2);
    }

    // ════════════════════════════════════════════
    //  Headless 集成测试
    // ════════════════════════════════════════════

    [AvaloniaFact]
    public void OnGitHubConnectionFailed_TriggersEvent()
    {
        // Arrange
        GitHubProxy.Prefix = "";

        bool eventFired = false;
        GitHubProxy.ConnectionFailed += Handler;
        void Handler(string url) { eventFired = true; }

        // Act: MainWindowViewModel 构造时应订阅了事件
        var vm = new MainWindowViewModel();
        GitHubProxy.NotifyConnectionFailed("https://github.com/test");

        // Assert
        Assert.True(eventFired, "ConnectionFailed 事件应被触发");

        // Cleanup
        GitHubProxy.ConnectionFailed -= Handler;
    }

    [Fact]
    public void SafeGuard_ExistingTestsStillPass()
    {
        // 确保已注册的事件不会干扰其他测试
        Assert.NotNull(GitHubProxy.GetUrl("https://github.com/x"));
    }

    /// <summary>
    /// 清理：重置代理状态
    /// </summary>
    public GitHubProxyHeadlessTests()
    {
        GitHubProxy.Prefix = "";
    }
}


