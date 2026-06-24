using System.Text.Json;
using ArkPlot.Avalonia.Models;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

public class GitHubProxyPresetTests
{
    [Fact]
    public void Deserialize_FromJsonFile_ShouldPopulateAllFields()
    {
        var json = """
        {
          "version": 1,
          "updatedAt": "2026-06-24",
          "description": "test",
          "presets": [
            { "name": "直连（不加速）", "url": "" },
            { "name": "tvv.tw", "url": "https://tvv.tw/" },
            { "name": "gh.catmak.name", "url": "https://gh.catmak.name" }
          ]
        }
        """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = JsonSerializer.Deserialize<GitHubProxyPresetList>(json, options);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Version);
        Assert.Equal("2026-06-24", result.UpdatedAt);
        Assert.NotNull(result.Presets);
        Assert.Equal(3, result.Presets.Length);

        Assert.Equal("直连（不加速）", result.Presets[0].Name);
        Assert.Equal("", result.Presets[0].Url);

        Assert.Equal("tvv.tw", result.Presets[1].Name);
        Assert.Equal("https://tvv.tw/", result.Presets[1].Url);

        Assert.Equal("gh.catmak.name", result.Presets[2].Name);
        Assert.Equal("https://gh.catmak.name", result.Presets[2].Url);
    }

    [Fact]
    public void Deserialize_WithoutCaseInsensitive_MissesFields()
    {
        // 验证不配 PropertyNameCaseInsensitive 时小写 key 不会被映射
        var json = """{ "presets": [{ "name": "test", "url": "https://test" }] }""";

        var result = JsonSerializer.Deserialize<GitHubProxyPresetList>(json);

        Assert.NotNull(result);
        // 大小写不匹配时 Presets 数组保持默认空数组
        Assert.NotNull(result!.Presets);
        Assert.Empty(result.Presets);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaultWithDirect()
    {
        var result = GitHubProxyPresetList.Load();

        Assert.NotNull(result);
        Assert.NotNull(result.Presets);
        Assert.Single(result.Presets);
        Assert.Equal("直连（不加速）", result.Presets[0].Name);
        Assert.Equal("", result.Presets[0].Url);
    }
}
