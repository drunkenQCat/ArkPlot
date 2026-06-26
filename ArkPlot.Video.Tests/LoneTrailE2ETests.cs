using System.Text.Json;
using System.Text.Json.Serialization;
using ArkPlot.AudioNormalizer;
using ArkPlot.Core.Infrastructure;
using ArkPlot.Core.Model;
using ArkPlot.Tts;
using Xunit;

namespace ArkPlot.Video.Tests;

[Collection("SequentialDb")]
public class LoneTrailE2ETests : IDisposable
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static readonly string FixtureDir = Path.Combine(
        RepoRoot, "ArkPlot.Video.Tests", "fixtures", "LoneTrail_Chapter1");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Dictionary<string, double> CharacterFrequencies = new()
    {
        { "", 330.0 },
        { "小贾斯汀", 440.0 }, { "三十号", 480.0 }, { "缪尔赛思", 520.0 },
        { "霍尔海雅", 560.0 }, { "伊芙利特", 600.0 }, { "赫默", 490.0 },
        { "塞雷娅", 410.0 }, { "杰克逊", 450.0 }, { "斐尔迪南", 510.0 },
        { "布莱克", 380.0 }, { "迷迭香", 580.0 }, { "锡人", 370.0 },
        { "凯尔希", 430.0 }, { "克丽斯腾", 470.0 },
    };

    private const double DefaultFrequency = 500.0;
    private readonly string _tempDbPath;

    public LoneTrailE2ETests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"arkplot_test_{Guid.NewGuid():N}.db");
        DbFactory.ConfigureForTesting($"Data Source={_tempDbPath}");
    }

    public void Dispose()
    {
        try { DbFactory.Reset(); } catch { }
        try { File.Delete(_tempDbPath); } catch { }
    }

    [Fact]
    public void LoneTrail_CWST1_DryRun_Succeeds()
    {
        var (segments, _) = LoadChapterFixtures("CW-ST-1 阴云密布 幕间");

        var ex = Record.Exception(() => TypstComposer.Compose(segments));
        Assert.Null(ex);

        var withCode = segments.Count(s => !string.IsNullOrEmpty(s.TypstCode));
        Assert.True(withCode > 0, $"应该有段生成 TypstCode，实际 {withCode}/{segments.Count}");
    }

    [Fact]
    public async Task LoneTrail_CWST1_FiveSegments_Succeeds()
    {
        var ffmpeg = FfmpegResolver.FindFfmpeg();
        if (ffmpeg == null)
        {
            Console.WriteLine("⏭️ 无 FFmpeg");
            return;
        }

        const string storyName = "孤星";

        var (allSegments, _) = LoadChapterFixtures("CW-ST-1 阴云密布 幕间");
        var segments = allSegments.Take(5).ToList();
        Assert.Equal(5, segments.Count);

        var audioDir = TtsCachePaths.Tts(storyName);
        Directory.CreateDirectory(audioDir);
        GenerateMockAudio(segments, audioDir);

        var renderer = new VideoRenderer(ffmpeg, msg => Console.WriteLine(msg));
        var composer = new VideoComposer(renderer, storyName, msg => Console.WriteLine(msg));

        var outputPath = await composer.ComposeChapterAsync(segments, "CW-ST-1");

        Assert.True(File.Exists(outputPath), $"MP4 应存在: {outputPath}");
        var fi = new FileInfo(outputPath);
        Assert.True(fi.Length > 1000, $"MP4 应 > 1KB，实际 {fi.Length}");

        Console.WriteLine($"\n✅ 视频已生成: {outputPath} ({fi.Length} 字节)");
    }

    // ══════════════════════════════════════════════
    //  Helper
    // ══════════════════════════════════════════════

    private (List<VideoSegment> Segments, List<FormattedTextEntry> DbEntries) LoadChapterFixtures(
        string chapterTitle)
    {
        // 加载 FormattedTextEntries 并插入 DB
        var entriesPath = Path.Combine(FixtureDir, "FormattedTextEntries.json");
        var entriesJson = File.ReadAllText(entriesPath);
        var dbEntries = JsonSerializer.Deserialize<List<FormattedTextEntry>>(entriesJson, JsonOpts) ?? [];

        var db = DbFactory.GetClient();
        if (dbEntries.Count > 0)
            db.Insertable(dbEntries).ExecuteCommand();

        // 查询回 DB 获取 auto-generated Id，构建 Index→Id 映射
        var insertedEntries = db.Queryable<FormattedTextEntry>()
            .Select(e => new { e.Index, e.Id })
            .ToList();
        var indexToIdMap = insertedEntries.ToDictionary(e => e.Index, e => e.Id);

        // 加载对齐缓存，构建 VideoSegment
        var cachePath = Path.Combine(FixtureDir, "AlignmentCache.json");
        var cacheJson = File.ReadAllText(cachePath);
        var cacheDoc = JsonDocument.Parse(cacheJson);
        var rawEntries = cacheDoc.RootElement.GetProperty("Entries");

        var segments = new List<VideoSegment>();
        foreach (var e in rawEntries.EnumerateArray())
        {
            var novelText = e.GetProperty("NovelText").GetString() ?? "";
            var isDialog = e.GetProperty("IsDialog").GetBoolean();
            var entryIdx = e.TryGetProperty("EntryIndex", out var ei) ? ei.GetInt32() : -1;
            var chapter = e.TryGetProperty("ChapterTitle", out var ct) ? ct.GetString() : "";
            var charName = e.TryGetProperty("CharacterName", out var cn) ? cn.GetString() : "";

            if (chapter != chapterTitle)
                continue;

            var voiceLabel = isDialog ? "dialog" : "narrator";
            var hash = TtsCacheService.GetCacheKey(TextSanitizer.Sanitize(novelText), voiceLabel);

            segments.Add(new VideoSegment
            {
                EntryId = indexToIdMap.GetValueOrDefault(entryIdx),
                EntryIndex = entryIdx,
                SegmentHash = hash,
                NovelText = novelText,
                IsDialog = isDialog,
                CharacterName = isDialog ? (charName ?? "?") : "",
            });
        }

        return (segments, dbEntries);
    }

    private static void GenerateMockAudio(List<VideoSegment> segments, string audioDir)
    {
        foreach (var seg in segments)
        {
            var wavPath = Path.Combine(audioDir, $"{seg.SegmentHash}.mp3");
            if (File.Exists(wavPath))
            {
                seg.AudioFilePath = wavPath;
                continue;
            }

            var freq = seg.IsDialog
                ? (CharacterFrequencies.TryGetValue(seg.CharacterName, out var f) ? f : DefaultFrequency)
                : CharacterFrequencies[""];
            MockAudio.GenerateSineWav(wavPath, seg.NovelText, freq);
            seg.AudioFilePath = wavPath;
        }
    }
}
