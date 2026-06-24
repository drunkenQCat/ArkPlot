using ArkPlot.Core.Utilities;
using Xunit;

namespace ArkPlot.Core.Tests;

public class GitHubProxyTests
{
    [Fact]
    public void EmptyPrefix_ReturnsOriginalUrl()
    {
        GitHubProxy.Prefix = "";
        Assert.Equal("https://api.github.com/repos/foo/bar", GitHubProxy.GetUrl("https://api.github.com/repos/foo/bar"));
    }

    [Fact]
    public void Prefix_IsPrepended()
    {
        GitHubProxy.Prefix = "https://gh-proxy.com/";
        Assert.Equal(
            "https://gh-proxy.com/https://api.github.com/repos/foo/bar",
            GitHubProxy.GetUrl("https://api.github.com/repos/foo/bar"));
    }

    [Fact]
    public void Prefix_IsPrepended_ToRawUrl()
    {
        GitHubProxy.Prefix = "https://gh-proxy.com/";
        Assert.Equal(
            "https://gh-proxy.com/https://raw.githubusercontent.com/Kengxxiao/ArknightsGameData/master/zh_CN/gamedata/excel/story_review_table.json",
            GitHubProxy.GetUrl("https://raw.githubusercontent.com/Kengxxiao/ArknightsGameData/master/zh_CN/gamedata/excel/story_review_table.json"));
    }

    [Fact]
    public void Prefix_WithoutTrailingSlash_AddsSlash()
    {
        GitHubProxy.Prefix = "https://gh-proxy.com";
        Assert.Equal("https://gh-proxy.com/https://github.com/x", GitHubProxy.GetUrl("https://github.com/x"));
    }

    [Fact]
    public void NullPrefix_SetsToEmpty()
    {
        GitHubProxy.Prefix = null!;
        Assert.Equal("https://github.com/x", GitHubProxy.GetUrl("https://github.com/x"));
    }

    [Fact]
    public void WhitespacePrefix_SetsToEmpty()
    {
        GitHubProxy.Prefix = "   ";
        Assert.Equal("https://github.com/x", GitHubProxy.GetUrl("https://github.com/x"));
    }

    [Fact]
    public void NullUrl_ReturnsNull()
    {
        GitHubProxy.Prefix = "https://gh-proxy.com/";
        Assert.Null(GitHubProxy.GetUrl(null!));
    }

    [Fact]
    public void EmptyUrl_ReturnsEmpty()
    {
        GitHubProxy.Prefix = "https://gh-proxy.com/";
        Assert.Equal("", GitHubProxy.GetUrl(""));
    }

    [Fact]
    public void SwitchingPrefix_UpdatesUrl()
    {
        GitHubProxy.Prefix = "https://proxy1.com/";
        var url1 = GitHubProxy.GetUrl("https://github.com/x");

        GitHubProxy.Prefix = "https://proxy2.com/";
        var url2 = GitHubProxy.GetUrl("https://github.com/x");

        Assert.Equal("https://proxy1.com/https://github.com/x", url1);
        Assert.Equal("https://proxy2.com/https://github.com/x", url2);
    }
}
