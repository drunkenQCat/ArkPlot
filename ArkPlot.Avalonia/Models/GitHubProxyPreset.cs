using System;
using System.IO;
using System.Text.Json;

namespace ArkPlot.Avalonia.Models;

/// <summary>
/// GitHub 代理预设列表，从 Assets/github_proxies.json 加载。
/// </summary>
public class GitHubProxyPresetList
{
    public int Version { get; set; }
    public string UpdatedAt { get; set; } = "";
    public string Description { get; set; } = "";
    public GitHubProxyPreset[] Presets { get; set; } = [];

    private static readonly string FilePath = Path.Combine(
        AppContext.BaseDirectory, "Assets", "github_proxies.json");

    /// <summary>加载预设列表。文件不存在时返回带默认直连的列表。</summary>
    public static GitHubProxyPresetList Load()
    {
        if (!File.Exists(FilePath))
        {
            return new GitHubProxyPresetList
            {
                Presets = [new GitHubProxyPreset { Name = "直连（不加速）", Url = "" }]
            };
        }

        var json = File.ReadAllText(FilePath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<GitHubProxyPresetList>(json, options) ?? new GitHubProxyPresetList();
    }
}

public class GitHubProxyPreset
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}
