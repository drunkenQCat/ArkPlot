using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using FFmpeg.NET;
using FFmpeg.NET.Events;

namespace ArkPlot.AudioNormalizer.Tests;

/// <summary>
/// 大批量集成测试 — 用真实 TTS 缓存 mp3 验证响度均衡。
/// 运行方式: dotnet test --filter "BatchNormalizeTest"
/// 或单独运行: dotnet test --filter "RunBatchNormalize"
/// </summary>
public class BatchNormalizeTest
{
    private const string CacheDir = @"C:\TechProjects\About_MyRepos\ArkPlot\ArkPlot.Avalonia\bin\Debug\net9.0\output\孤星\tts\_tts_cache";

    [Fact(Skip = "手动运行: dotnet test --filter RunBatchNormalize")]
    public async Task RunBatchNormalize()
    {
        var ffmpegPath = FindFfmpeg();
        if (ffmpegPath == null)
        {
            Console.WriteLine("SKIP: ffmpeg not in PATH");
            return;
        }

        var inputFiles = Directory.GetFiles(CacheDir, "*.mp3");
        if (inputFiles.Length == 0)
        {
            Console.WriteLine("SKIP: no mp3 files in cache");
            return;
        }

        var outputDir = Path.Combine(Path.GetTempPath(), $"loudnorm_batch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        var normalizer = new LoudnessNormalizer(ffmpegPath);
        var results = new List<(string Name, double InputLufs, double OutputLufs, bool Success, string? Error)>();

        Console.WriteLine($"=== 批量响度均衡测试 ===");
        Console.WriteLine($"输入: {inputFiles.Length} 个 MP3");
        Console.WriteLine($"输出: {outputDir}");
        Console.WriteLine($"目标: {normalizer.TargetLufs} LUFS");
        Console.WriteLine(new string('=', 60));

        var sw = Stopwatch.StartNew();
        int processed = 0;
        int succeeded = 0;
        int failed = 0;

        foreach (var inputFile in inputFiles)
        {
            processed++;
            var name = Path.GetFileNameWithoutExtension(inputFile);
            var shortName = name.Length > 12 ? name[..12] + "…" : name;
            var outputFile = Path.Combine(outputDir, $"{name}_norm.mp3");

            Console.Write($"  [{processed,3}/{inputFiles.Length}] {shortName,-16} ");

            try
            {
                // 测量输入 LUFS
                var inputMeasurement = await normalizer.MeasureAsync(inputFile);

                // 归一化
                await normalizer.NormalizeAsync(inputFile, outputFile);

                // 测量输出 LUFS
                var outputMeasurement = await normalizer.MeasureAsync(outputFile);

                var delta = Math.Abs(outputMeasurement.InputI - normalizer.TargetLufs);
                var ok = delta < 2.0; // 允许 ±2 LUFS 容差

                results.Add((shortName, inputMeasurement.InputI, outputMeasurement.InputI, ok, null));
                if (ok) succeeded++; else failed++;

                var status = ok ? "✅" : "⚠️";
                Console.WriteLine(
                    $"{status} input={inputMeasurement.InputI,6:F1} → output={outputMeasurement.InputI,6:F1} LUFS " +
                    $"(Δ={delta:F1}, tp={outputMeasurement.InputTp:F1}dBTP)");
            }
            catch (Exception ex)
            {
                failed++;
                results.Add((shortName, double.NaN, double.NaN, false, ex.Message));
                Console.WriteLine($"❌ {ex.GetType().Name}: {Truncate(ex.Message, 60)}");
            }
        }

        sw.Stop();
        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"\n=== 结果汇总 ===");
        Console.WriteLine($"总计: {processed} | 成功: {succeeded} | 失败: {failed}");
        Console.WriteLine($"耗时: {sw.Elapsed.TotalSeconds:F1}s ({sw.Elapsed.TotalSeconds / processed:F2}s/file)");

        if (results.Any(r => r.Success))
        {
            var successResults = results.Where(r => r.Success).ToList();
            var avgOutputLufs = successResults.Average(r => r.OutputLufs);
            var maxDelta = successResults.Max(r => Math.Abs(r.OutputLufs - normalizer.TargetLufs));
            var minOutput = successResults.Min(r => r.OutputLufs);
            var maxOutput = successResults.Max(r => r.OutputLufs);

            Console.WriteLine($"\n成功文件 LUFS 统计:");
            Console.WriteLine($"  平均输出 LUFS: {avgOutputLufs:F2}");
            Console.WriteLine($"  最小输出 LUFS: {minOutput:F2}");
            Console.WriteLine($"  最大输出 LUFS: {maxOutput:F2}");
            Console.WriteLine($"  最大偏差:      {maxDelta:F2} LUFS");
        }

        if (failed > 0)
        {
            Console.WriteLine($"\n=== 失败详情 ===");
            foreach (var r in results.Where(r => !r.Success))
                Console.WriteLine($"  {r.Name}: {r.Error}");
        }

        // 清理
        try { Directory.Delete(outputDir, true); } catch { }

        Assert.True(succeeded >= processed * 0.95, $"成功率 {succeeded}/{processed} < 95%");
    }

    /// <summary>
    /// 快速验证：只处理 5 个文件，检查基本功能。
    /// </summary>
    [Fact]
    public async Task QuickSmokeTest_5Files()
    {
        var ffmpegPath = FindFfmpeg();
        if (ffmpegPath == null)
            return;

        var inputFiles = Directory.GetFiles(CacheDir, "*.mp3")?.Take(5).ToArray();
        if (inputFiles == null || inputFiles.Length == 0)
            return;

        var outputDir = Path.Combine(Path.GetTempPath(), $"loudnorm_smoke_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        try
        {
            var normalizer = new LoudnessNormalizer(ffmpegPath);
            int success = 0;

            foreach (var inputFile in inputFiles)
            {
                var outputFile = Path.Combine(outputDir, Path.GetFileName(inputFile));
                await normalizer.NormalizeAsync(inputFile, outputFile);

                if (File.Exists(outputFile) && new FileInfo(outputFile).Length > 0)
                {
                    success++;
                    // 验证输出 LUFS
                    var m = await normalizer.MeasureAsync(outputFile);
                    var delta = Math.Abs(m.InputI - normalizer.TargetLufs);
                    Console.WriteLine($"  {Path.GetFileNameWithoutExtension(inputFile)}: output={m.InputI:F1} LUFS (Δ={delta:F1})");
                    Assert.True(delta < 3.0, $"LUFS 偏差过大: {m.InputI:F1} (target={normalizer.TargetLufs}, delta={delta:F1})");
                }
            }

            Assert.True(success >= 4, $"至少 4/5 文件应成功，实际 {success}");
        }
        finally
        {
            try { Directory.Delete(outputDir, true); } catch { }
        }
    }

    /// <summary>
    /// 性能基准：测量 10 个文件的平均处理时间。
    /// </summary>
    [Fact]
    public async Task PerformanceBenchmark_10Files()
    {
        var ffmpegPath = FindFfmpeg();
        if (ffmpegPath == null)
            return;

        var inputFiles = Directory.GetFiles(CacheDir, "*.mp3")?.Take(10).ToArray();
        if (inputFiles == null || inputFiles.Length == 0)
            return;

        var outputDir = Path.Combine(Path.GetTempPath(), $"loudnorm_perf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        try
        {
            var normalizer = new LoudnessNormalizer(ffmpegPath);
            var times = new List<double>();

            foreach (var inputFile in inputFiles)
            {
                var outputFile = Path.Combine(outputDir, Path.GetFileName(inputFile));
                var sw = Stopwatch.StartNew();
                await normalizer.NormalizeAsync(inputFile, outputFile);
                sw.Stop();
                times.Add(sw.ElapsedMilliseconds);
                Console.WriteLine($"  {Path.GetFileNameWithoutExtension(inputFile)}: {sw.ElapsedMilliseconds}ms");
            }

            Console.WriteLine($"\n性能: avg={times.Average():F0}ms, min={times.Min()}ms, max={times.Max()}ms");
            Assert.True(times.Average() < 30_000, $"平均处理时间过长: {times.Average():F0}ms");
        }
        finally
        {
            try { Directory.Delete(outputDir, true); } catch { }
        }
    }

    private static string? FindFfmpeg()
    {
        var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var sep = OperatingSystem.IsWindows() ? ';' : ':';
        foreach (var dir in pathVar.Split(sep, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, exeName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
