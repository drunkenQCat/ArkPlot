using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Novelizer;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

/// <summary>
/// 小说化复盘面板（TraceReviewViewModel）回归测试。
/// 全部使用内存数据（NovelizerTraceCollector + 临时目录落盘 JSON），不触发任何真实 LLM API。
/// </summary>
public class TraceReviewViewModelTests
{
    private static string NewTraceDir()
        => Path.Combine(Path.GetTempPath(), $"trace_test_{Guid.NewGuid():N}");

    private static string WriteHistoryFile(string dir, string startedAt, string model, int turnCount)
    {
        var traceSubDir = Path.Combine(dir, "novelizer-traces");
        Directory.CreateDirectory(traceSubDir);
        var doc = new RunDocument
        {
            Model = model,
            StartedAt = startedAt,
            TurnCount = turnCount,
            Turns = Enumerable.Range(0, turnCount)
                .Select(i => new NovelizerTraceCollector.TurnTrace(
                    $"🧠 历史上限{i}", "历史prompt", "历史思考", "历史输出", "孤星", false, DateTime.Now))
                .ToList(),
        };
        var path = Path.Combine(traceSubDir, $"{startedAt.Replace(":", "").Replace("/", "").Replace("-", "")}_novelizer-trace.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(doc));
        return path;
    }

    [Fact]
    public void RefreshRuns_LiveCollectorFirst_ThenHistory()
    {
        var traceDir = NewTraceDir();
        try
        {
            // 历史落盘：一条
            WriteHistoryFile(traceDir, "2026-08-18 14:51:02", "deepseek-v4-flash", 3);

            // 实时收集器：一条
            var collector = new NovelizerTraceCollector();
            collector.BeginRun("deepseek-v4-flash");
            collector.Add("🧠 实时", "实时prompt", "实时思考", "实时输出", "孤星", false);

            var vm = new TraceReviewViewModel { TraceRoot = traceDir, LiveCollector = collector };
            vm.RefreshRuns();

            // 实时运行置顶（第一条），历史在后
            Assert.Equal(2, vm.Runs.Count);
            Assert.Equal("live", vm.Runs[0].FileName);
            Assert.Contains("（当前运行）", vm.Runs[0].Display);
            Assert.Equal("history", vm.Runs[1].FileName);
        }
        finally
        {
            try { Directory.Delete(traceDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void RefreshRuns_NoTraceDir_ReportsStatus()
    {
        var vm = new TraceReviewViewModel { TraceRoot = NewTraceDir(), LiveCollector = new NovelizerTraceCollector() };
        vm.RefreshRuns();

        Assert.Empty(vm.Runs);
        Assert.Contains("trace 目录不存在", vm.Status);
    }

    [Fact]
    public void SelectTurnByKey_LiveTurn_SelectsTurn()
    {
        var traceDir = NewTraceDir();
        try
        {
            Directory.CreateDirectory(traceDir);
            var collector = new NovelizerTraceCollector();
            collector.BeginRun("deepseek-v4-flash");
            var idx0 = collector.Add("🧠 Turn0", "p0", "t0", "a0", "孤星", false);
            var idx1 = collector.Add("🔄 压缩", "pc", "tc", "ac", "孤星", true);

            var vm = new TraceReviewViewModel { TraceRoot = traceDir, LiveCollector = collector };
            vm.RefreshRuns();
            vm.SelectTurnByKey($"{collector.RunId}:{idx1}");

            Assert.NotNull(vm.SelectedTurn);
            Assert.Equal("🔄 压缩", vm.SelectedTurn.Label);
            Assert.Equal("ac", vm.SelectedTurn.Answer);
        }
        finally
        {
            try { Directory.Delete(traceDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SelectTurnByKey_HistoryTurn_SelectsTurn()
    {
        var traceDir = NewTraceDir();
        try
        {
            var file = WriteHistoryFile(traceDir, "2026-08-18 14:51:02", "deepseek-v4-flash", 3);
            var runId = Path.GetFileNameWithoutExtension(file).Split('_')[0]; // 20260818145102

            var vm = new TraceReviewViewModel { TraceRoot = traceDir, LiveCollector = null };
            vm.RefreshRuns();
            vm.SelectTurnByKey($"{runId}:2");

            Assert.NotNull(vm.SelectedTurn);
            Assert.Contains("历史上限2", vm.SelectedTurn.Label);
        }
        finally
        {
            try { Directory.Delete(traceDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SelectTurnByKey_InvalidKey_NoSelection()
    {
        var traceDir = NewTraceDir();
        try
        {
            Directory.CreateDirectory(traceDir);
            var vm = new TraceReviewViewModel { TraceRoot = traceDir, LiveCollector = null };
            vm.RefreshRuns();
            vm.SelectTurnByKey("bad:key:format");

            Assert.Null(vm.SelectedTurn);
        }
        finally
        {
            try { Directory.Delete(traceDir, recursive: true); } catch { }
        }
    }
}