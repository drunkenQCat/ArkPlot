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

    // ---------- 详情区：阶段条翻页 / 折叠 / prompt 拆分 ----------

    private static TraceReviewViewModel VmWithTurn(string prompt, string thinking = "t", string answer = "a")
    {
        var traceDir = NewTraceDir();
        Directory.CreateDirectory(traceDir);
        var collector = new NovelizerTraceCollector();
        collector.BeginRun("model");
        collector.Add("🧠 Turn0", prompt, thinking, answer, "孤星", false);
        var vm = new TraceReviewViewModel { TraceRoot = traceDir, LiveCollector = collector };
        vm.RefreshRuns();
        vm.SelectedRun = vm.Runs[0];
        vm.SelectedTurn = vm.Turns[0];
        return vm;
    }

    [Fact]
    public void SelectPage_Think_OnlyThinkPageVisible()
    {
        var vm = VmWithTurn("p");
        try
        {
            Assert.True(vm.IsInputPageVisible);
            Assert.False(vm.IsThinkPageVisible);

            vm.SelectPageCommand.Execute("Think");

            Assert.True(vm.IsThinkActive);
            Assert.True(vm.IsThinkPageVisible);
            Assert.True(vm.IsThinkShellVisible);
            Assert.False(vm.IsInputPageVisible);
            Assert.False(vm.IsInputShellVisible);

            vm.SelectPageCommand.Execute("Output");
            Assert.True(vm.IsOutputPageVisible);
            Assert.False(vm.IsThinkPageVisible);
        }
        finally { Cleanup(vm); }
    }

    [Fact]
    public void ToggleFold_InputCollapses_HeaderKeepsVisibleAndShowsHint()
    {
        var vm = VmWithTurn("p");
        try
        {
            vm.ToggleFoldCommand.Execute("Input");

            Assert.True(vm.IsInputCollapsed);
            Assert.True(vm.IsInputHintVisible);
            Assert.False(vm.IsInputPageVisible);
            // 折叠头本身仍可见，保证可再展开
            Assert.True(vm.IsInputShellVisible);
            Assert.Contains("已折叠", vm.InputFoldHeader);

            vm.ToggleFoldCommand.Execute("Input");
            Assert.False(vm.IsInputCollapsed);
            Assert.True(vm.IsInputPageVisible);
            Assert.False(vm.IsInputHintVisible);
        }
        finally { Cleanup(vm); }
    }

    [Fact]
    public void ToggleSubFold_SystemCollapse_TogglesContentOnly()
    {
        var vm = VmWithTurn("—— system ——\n\n系统指令\n\n—— user ——\n\n正文");
        try
        {
            Assert.True(vm.IsSystemHeadVisible);
            Assert.True(vm.IsSystemContentVisible);

            vm.ToggleSubFoldCommand.Execute("System");

            Assert.False(vm.IsSystemContentVisible);
            // 子部头仍在（可再展开）
            Assert.True(vm.IsSystemHeadVisible);
            Assert.Contains("已折叠", vm.SystemSubHeader);

            vm.ToggleSubFoldCommand.Execute("System");
            Assert.True(vm.IsSystemContentVisible);
        }
        finally { Cleanup(vm); }
    }

    [Fact]
    public void PromptSplitter_SplitsSystemAndUser()
    {
        var (sys, usr) = TracePromptSplitter.Split("—— system ——\n\n系统提示词\n\n—— user ——\n\n请小说化下文");
        Assert.Equal("系统提示词", sys);
        Assert.Equal("请小说化下文", usr);
    }

    [Fact]
    public void PromptSplitter_MergesMultipleSystemSections()
    {
        var (sys, usr) = TracePromptSplitter.Split("—— system ——\n\nA\n\n—— system ——\n\nB\n\n—— user ——\n\nC");
        Assert.Equal("A\n\nB", sys);
        Assert.Equal("C", usr);
    }

    [Fact]
    public void PromptSplitter_NoMarkers_AllInUser()
    {
        var (sys, usr) = TracePromptSplitter.Split("随便的文本");
        Assert.Equal("", sys);
        Assert.Equal("随便的文本", usr);
    }

    [Fact]
    public void TurnRow_SystemUser_AreSplitFromPrompt()
    {
        var vm = VmWithTurn("—— system ——\n\nsys\n\n—— user ——\n\nusr");
        try
        {
            var turn = vm.SelectedTurn!;
            Assert.Equal("sys", turn.System);
            Assert.Equal("usr", turn.User);
            Assert.Contains("system 3 字", vm.InputFoldHeader);
            Assert.Contains("user 3 字", vm.InputFoldHeader);
        }
        finally { Cleanup(vm); }
    }

    /// <summary>清理 VmWithTurn 创建的临时 trace 目录。</summary>
    private static void Cleanup(TraceReviewViewModel vm)
    {
        try { Directory.Delete(vm.TraceRoot!, recursive: true); } catch { }
    }
}