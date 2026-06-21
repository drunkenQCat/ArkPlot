using ArkPlot.Core.Infrastructure;
using ArkPlot.Tts.Alignment;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArkPlot.Tts.Tests;

public sealed class NovelAlignerRealDataTests : IDisposable
{
    [Fact]
    public async Task AlignByFileName_GreenFieldDream_ProducesKnownGenders()
    {
        var repoRoot = GetRepoRoot();
        var dbPath = Path.Combine(repoRoot, "ArkPlot.Avalonia", "bin", "Debug", "net9.0", "arkplot.db");
        var novelPath = Path.Combine(repoRoot, "ArkPlot.Avalonia", "bin", "Debug", "net9.0", "output", "绿野幻梦", "绿野幻梦_novel_MiniMax-M2.5.md");

        Assert.True(File.Exists(dbPath), $"数据库不存在: {dbPath}");
        Assert.True(File.Exists(novelPath), $"小说文件不存在: {novelPath}");

        var cacheDir = Path.Combine(Path.GetTempPath(), "arkplot-align-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDir);

        DbFactory.ConfigureForTesting($"Data Source={dbPath}");
        var aligner = new NovelAligner();
        var (entries, _) = await aligner.AlignByFileNameAsync(novelPath, cacheDir);

        var dialogEntries = entries.Where(e => e.IsDialog && !string.IsNullOrWhiteSpace(e.CharacterName)).ToList();
        var withGenderCount = dialogEntries.Count(e => !string.IsNullOrWhiteSpace(e.Gender));
        var characters = dialogEntries
            .GroupBy(e => NormalizeCharacterName(e.CharacterName))
            .Select(g => new
            {
                Name = g.Key,
                Gender = g.Select(e => e.Gender).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "?",
                Code = g.Select(e => e.CharacterCode).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
            })
            .OrderBy(x => x.Name)
            .ToList();

        var sunny = characters.FirstOrDefault(x => x.Name == "桑尼");
        var unknownSamples = string.Join(", ", characters
            .Where(x => x.Gender == "?")
            .Take(10)
            .Select(x => $"{x.Name}[{x.Code ?? "null"}]"));

        Assert.True(
            withGenderCount > 0,
            $"本次 headless 对齐结果中所有角色性别都为空。dialogs={dialogEntries.Count}, characters={characters.Count}, unknownSamples={unknownSamples}");

        Assert.True(
            sunny != null,
            $"未在 headless 对齐结果中找到“桑尼”。characters={string.Join(", ", characters.Take(20).Select(x => x.Name))}");

        Assert.True(
            sunny!.Gender != "?",
            $"“桑尼”在 headless 对齐结果中仍是 '?'. code={sunny.Code ?? "null"}, dialogs={dialogEntries.Count}, withGender={withGenderCount}, unknownSamples={unknownSamples}");
    }

    [Fact]
    public async Task AlignByFileName_GreenFieldDream_RebuildsStaleUiCache()
    {
        var repoRoot = GetRepoRoot();
        var dbPath = Path.Combine(repoRoot, "ArkPlot.Avalonia", "bin", "Debug", "net9.0", "arkplot.db");
        var novelPath = Path.Combine(repoRoot, "ArkPlot.Avalonia", "bin", "Debug", "net9.0", "output", "绿野幻梦", "绿野幻梦_novel_MiniMax-M2.5.md");
        var cacheDir = Path.Combine(repoRoot, "ArkPlot.Avalonia", "bin", "Debug", "net9.0", "output", "绿野幻梦", "tts", "_align_cache");

        Assert.True(File.Exists(dbPath), $"数据库不存在: {dbPath}");
        Assert.True(File.Exists(novelPath), $"小说文件不存在: {novelPath}");
        Directory.CreateDirectory(cacheDir);

        var novelText = await File.ReadAllTextAsync(novelPath);
        var contentHash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(novelText))).ToLowerInvariant();
        var cacheFile = Path.Combine(cacheDir, $"{contentHash}.json");

        DbFactory.ConfigureForTesting($"Data Source={dbPath}");
        var aligner = new NovelAligner();
        await aligner.AlignByFileNameAsync(novelPath, cacheDir);

        Assert.True(File.Exists(cacheFile), $"缓存文件不存在: {cacheFile}");

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(cacheFile));
        var root = doc.RootElement;
        var version = root.TryGetProperty("Version", out var versionNode) ? versionNode.GetInt32() : 0;
        var entries = root.GetProperty("Entries").EnumerateArray().ToList();
        var dialogWithGenderCount = entries.Count(e =>
            e.TryGetProperty("IsDialog", out var isDialogNode)
            && isDialogNode.GetBoolean()
            && e.TryGetProperty("Gender", out var genderNode)
            && genderNode.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(genderNode.GetString()));

        Assert.True(version >= 2, $"缓存版本未升级，version={version}, cacheFile={cacheFile}");
        Assert.True(dialogWithGenderCount > 0, $"缓存已重建，但 Gender 仍全空。cacheFile={cacheFile}");
    }

    public void Dispose()
    {
        DbFactory.Reset();
    }

    private static string GetRepoRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private static string NormalizeCharacterName(string? characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName))
            return string.Empty;

        return new string(characterName.Where(c => !char.IsWhiteSpace(c)).ToArray());
    }
}
