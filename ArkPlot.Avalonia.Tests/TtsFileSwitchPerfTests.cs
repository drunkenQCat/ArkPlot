using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Core.Infrastructure;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;
using Xunit.Abstractions;

namespace ArkPlot.Avalonia.Tests;

/// <summary>
/// 性能基准测试：测量 TTS 窗口切换小说文件时各阶段的耗时。
/// 使用孤星活动的真实数据。
/// </summary>
public class TtsFileSwitchPerfTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _outputDir;
    private readonly bool _hasTestData;

    private static readonly string[] TargetFiles =
    [
        "孤星_novel_kimi-k2.5.md",
        "孤星_novel_deepseek-v4-flash.md"
    ];

    public TtsFileSwitchPerfTests(ITestOutputHelper output)
    {
        _output = output;

        var possiblePaths = new[]
        {
            Path.Combine(Path.GetDirectoryName(typeof(TtsFileSwitchPerfTests).Assembly.Location)!,
                "..", "..", "..", "..", "ArkPlot.Avalonia", "bin", "Debug", "net9.0", "output", "孤星"),
            Path.Combine("C:\\TechProjects\\About_MyRepos\\ArkPlot\\ArkPlot.Avalonia\\bin\\Debug\\net9.0\\output\\孤星"),
        };

        _outputDir = possiblePaths.FirstOrDefault(p => Directory.Exists(p)) ?? "";
        _hasTestData = !string.IsNullOrEmpty(_outputDir)
                       && TargetFiles.All(f => File.Exists(Path.Combine(_outputDir, f)));

        // 指向 Avalonia 项目的真实数据库（含孤星活动数据）
        if (_hasTestData)
        {
            var dbDir = Path.GetDirectoryName(Path.GetDirectoryName(_outputDir))!;
            var dbPath = Path.Combine(dbDir, "arkplot.db");
            if (File.Exists(dbPath))
            {
                DbFactory.ConfigureForTesting($"Data Source={dbPath}");
                _output.WriteLine($"DB: {dbPath} ({new FileInfo(dbPath).Length / 1024 / 1024}MB)");
            }
        }
    }

    public void Dispose()
    {
        DbFactory.Reset();
    }

    /// <summary>
    /// 核心性能测试：冷启动 → 热启动，逐文件测量对齐耗时。
    /// </summary>
    [AvaloniaFact(Timeout = 120_000)]
    public async Task FileSwitch_PerfBreakdown_ColdVsHot()
    {
        if (!_hasTestData)
        {
            _output.WriteLine("SKIP: 孤星测试数据不存在");
            return;
        }

        // ── Phase 0: 清理对齐缓存（测冷启动） ──
        // 旧路径（novel 文件目录）和新路径（tts/ 目录下）都清理
        var oldCacheDir = Path.Combine(_outputDir, "_align_cache");
        var newCacheDir = Path.Combine(_outputDir, "tts", "_align_cache");
        foreach (var dir in new[] { oldCacheDir, newCacheDir })
        {
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir, "*.json"))
                    File.Delete(f);
                _output.WriteLine($"已清理对齐缓存: {dir}");
            }
        }

        _output.WriteLine("═══════════════════════════════════════════════════");
        _output.WriteLine("  冷启动测试（无对齐缓存）");
        _output.WriteLine("═══════════════════════════════════════════════════");

        var coldResults = await MeasureFileLoadAsync(clearCache: true);
        PrintResults("冷启动", coldResults);

        _output.WriteLine("");
        _output.WriteLine("═══════════════════════════════════════════════════");
        _output.WriteLine("  热启动测试（有对齐缓存）");
        _output.WriteLine("═══════════════════════════════════════════════════");

        var hotResults = await MeasureFileLoadAsync(clearCache: false);
        PrintResults("热启动", hotResults);

        // ── 对比分析 ──
        _output.WriteLine("");
        _output.WriteLine("═══════════════════════════════════════════════════");
        _output.WriteLine("  瓶颈分析");
        _output.WriteLine("═══════════════════════════════════════════════════");

        foreach (var file in TargetFiles)
        {
            var cold = coldResults.GetValueOrDefault(file);
            var hot = hotResults.GetValueOrDefault(file);
            if (cold == null || hot == null) continue;

            _output.WriteLine($"\n  [{file}]");
            _output.WriteLine($"  冷启动: {cold.TotalMs}ms");
            _output.WriteLine($"  热启动: {hot.TotalMs}ms");
            _output.WriteLine($"  加速比: {(cold.TotalMs > 0 ? (double)cold.TotalMs / Math.Max(hot.TotalMs, 1) : 0):F1}x");
        }

        // ── 文件切换模拟（热缓存下连续切换两个文件） ──
        _output.WriteLine("");
        _output.WriteLine("═══════════════════════════════════════════════════");
        _output.WriteLine("  连续切换测试（模拟用户点 radio 来回切换）");
        _output.WriteLine("═══════════════════════════════════════════════════");

        await MeasureContinuousSwitchAsync();
    }

    private async Task<Dictionary<string, FilePerfResult>> MeasureFileLoadAsync(bool clearCache)
    {
        var results = new Dictionary<string, FilePerfResult>();

        foreach (var fileName in TargetFiles)
        {
            if (clearCache)
            {
                foreach (var dir in new[]
                {
                    Path.Combine(_outputDir, "_align_cache"),
                    Path.Combine(_outputDir, "tts", "_align_cache")
                })
                {
                    if (Directory.Exists(dir))
                        foreach (var f in Directory.GetFiles(dir, "*.json"))
                            File.Delete(f);
                }
            }

            // 每次创建新的 ViewModel 以模拟首次打开
            var vm = new TtsViewModel(_outputDir);

            // 等待构造函数的初始加载完成
            await Task.Delay(200);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(500);
            Dispatcher.UIThread.RunJobs();

            // 清除日志
            var prevLog = vm.LogText;

            // 选中目标文件（取消其他文件的选中）
            var targetFile = vm.NovelFiles.FirstOrDefault(f => f.FileName == fileName);
            if (targetFile == null)
            {
                _output.WriteLine($"  SKIP: {fileName} 未在扫描结果中找到");
                vm.Dispose();
                continue;
            }

            // 取消所有选中，再选中目标
            foreach (var f in vm.NovelFiles) f.IsSelected = false;
            targetFile.IsSelected = true;

            // 等待 LoadAlignmentAsync 完成
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1000);
            Dispatcher.UIThread.RunJobs();

            // 解析日志中的 [perf] 行
            var newLog = vm.LogText.Substring(prevLog.Length);
            var perf = ParsePerfLogs(newLog, fileName);

            results[fileName] = perf;
            _output.WriteLine($"  {fileName}: Total={perf.TotalMs}ms, Entries={perf.EntryCount}");

            vm.Dispose();
        }

        return results;
    }

    private async Task MeasureContinuousSwitchAsync()
    {
        var vm = new TtsViewModel(_outputDir);

        // 等待初始加载
        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();

        for (int round = 0; round < 3; round++)
        {
            foreach (var fileName in TargetFiles)
            {
                var prevLog = vm.LogText;

                var target = vm.NovelFiles.FirstOrDefault(f => f.FileName == fileName);
                if (target == null) continue;

                foreach (var f in vm.NovelFiles) f.IsSelected = false;

                var sw = Stopwatch.StartNew();
                target.IsSelected = true;

                await Task.Delay(300);
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(1000);
                Dispatcher.UIThread.RunJobs();
                sw.Stop();

                var newLog = vm.LogText.Substring(prevLog.Length);
                var perf = ParsePerfLogs(newLog, fileName);

                _output.WriteLine($"  Round {round + 1} → {fileName}: " +
                    $"wall={sw.ElapsedMilliseconds}ms, " +
                    $"align={perf.AlignMs}ms, bg={perf.BgMs}ms, " +
                    $"seg={perf.SegmentMs}ms, voice={perf.VoiceMs}ms, " +
                    $"total={perf.TotalMs}ms");
            }
        }

        vm.Dispose();
    }

    private static FilePerfResult ParsePerfLogs(string logText, string fileName)
    {
        var result = new FilePerfResult { FileName = fileName };

        // [perf] AlignByFileNameAsync(孤星_novel_kimi-k2.5.md): 1234ms
        var alignMatch = Regex.Match(logText, @"\[perf\] AlignByFileNameAsync.*?:\s*(\d+)ms");
        if (alignMatch.Success) result.AlignMs = int.Parse(alignMatch.Groups[1].Value);

        // [perf] LoadBackgroundsAsync: 567ms
        var bgMatch = Regex.Match(logText, @"\[perf\] LoadBackgroundsAsync:\s*(\d+)ms");
        if (bgMatch.Success) result.BgMs = int.Parse(bgMatch.Groups[1].Value);

        // [perf] Chapters+Segments: 890ms
        var segMatch = Regex.Match(logText, @"\[perf\] Chapters\+Segments:\s*(\d+)ms");
        if (segMatch.Success) result.SegmentMs = int.Parse(segMatch.Groups[1].Value);

        // [perf] BuildVoiceConfigs: 123ms
        var voiceMatch = Regex.Match(logText, @"\[perf\] BuildVoiceConfigs:\s*(\d+)ms");
        if (voiceMatch.Success) result.VoiceMs = int.Parse(voiceMatch.Groups[1].Value);

        // [perf] TOTAL LoadAlignmentAsync: 2345ms | 5 章节, 100 片段
        var totalMatch = Regex.Match(logText, @"\[perf\] TOTAL LoadAlignmentAsync:\s*(\d+)ms\s*\|\s*(\d+)\s*章节,\s*(\d+)\s*片段");
        if (totalMatch.Success)
        {
            result.TotalMs = int.Parse(totalMatch.Groups[1].Value);
            result.ChapterCount = int.Parse(totalMatch.Groups[2].Value);
            result.EntryCount = int.Parse(totalMatch.Groups[3].Value);
        }

        // [perf] LoadSegments: 50 rows=10ms, RefreshAudioStatus=20ms
        var loadSegMatch = Regex.Match(logText, @"\[perf\] LoadSegments:\s*(\d+)\s*rows=(\d+)ms,\s*RefreshAudioStatus=(\d+)ms");
        if (loadSegMatch.Success)
        {
            result.RowCount = int.Parse(loadSegMatch.Groups[1].Value);
            result.RowCreateMs = int.Parse(loadSegMatch.Groups[2].Value);
            result.RefreshAudioMs = int.Parse(loadSegMatch.Groups[3].Value);
        }

        // [perf] RefreshAudioStatus: matched=10, AudioFileReader=5ms, total=20ms
        var refreshMatch = Regex.Match(logText, @"\[perf\] RefreshAudioStatus: matched=(\d+),\s*AudioFileReader=(\d+)ms,\s*total=(\d+)ms");
        if (refreshMatch.Success)
        {
            result.AudioMatchedCount = int.Parse(refreshMatch.Groups[1].Value);
            result.AudioReaderMs = int.Parse(refreshMatch.Groups[2].Value);
        }

        return result;
    }

    private void PrintResults(string label, Dictionary<string, FilePerfResult> results)
    {
        _output.WriteLine($"\n  {"Phase".PadRight(30)} {"Ms".PadLeft(8)}  {"Pct".PadLeft(6)}  Note");
        _output.WriteLine($"  {new string('-', 75)}");

        foreach (var (file, perf) in results)
        {
            _output.WriteLine($"\n  ▸ {file} ({perf.EntryCount} 片段, {perf.ChapterCount} 章节)");
            var total = Math.Max(perf.TotalMs, 1);

            PrintRow("AlignByFileNameAsync", perf.AlignMs, total, "对齐(含缓存检查+DB查询)");
            PrintRow("LoadBackgroundsAsync", perf.BgMs, total, "背景图(DB查询+构建)");
            PrintRow("Chapters+Segments", perf.SegmentMs, total, "章节列表+SegmentRow创建");
            PrintRow("  └ Row创建", perf.RowCreateMs, total, $"({perf.RowCount} 行)");
            PrintRow("  └ RefreshAudioStatus", perf.RefreshAudioMs, total, $"(匹配{perf.AudioMatchedCount}个音频)");
            PrintRow("    └ AudioFileReader", perf.AudioReaderMs, total, "NAudio 读取时长");
            PrintRow("BuildVoiceConfigs", perf.VoiceMs, total, "音色配置构建");
            _output.WriteLine($"  {"TOTAL".PadRight(30)} {perf.TotalMs,6}ms  {100.0,5:F1}%  total");
        }
    }

    private void PrintRow(string phase, long ms, long total, string note)
    {
        var pct = total > 0 ? ms * 100.0 / total : 0;
        _output.WriteLine($"  {phase.PadRight(30)} {ms,6}ms  {pct,5:F1}%  {note}");
    }

    private class FilePerfResult
    {
        public string FileName { get; init; } = "";
        public long AlignMs { get; set; }
        public long BgMs { get; set; }
        public long SegmentMs { get; set; }
        public long VoiceMs { get; set; }
        public long TotalMs { get; set; }
        public int ChapterCount { get; set; }
        public int EntryCount { get; set; }
        public int RowCount { get; set; }
        public long RowCreateMs { get; set; }
        public long RefreshAudioMs { get; set; }
        public int AudioMatchedCount { get; set; }
        public long AudioReaderMs { get; set; }
    }
}
