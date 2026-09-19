using ArkPlot.Novelizer;
using Xunit;

namespace ArkPlot.Novelizer.Tests;

/// <summary>
/// 复盘文件的实时落盘：每轮 Add 后 trace 文件就要存在且内容最新，
/// 而不是等 BatchProcessAsync 结束才一次性写出（中途取消/崩溃会丢掉全部已完成轮次）。
/// </summary>
public class NovelizerTraceCollectorFlushTests : IDisposable
{
    private readonly string _dir;

    public NovelizerTraceCollectorFlushTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trace-flush-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 临时目录删不掉就算了 */ }
    }

    private string ExpectedPath => Path.Combine(
        _dir, "novelizer-traces", $"{_collector.RunId}_novelizer-trace.json");

    private readonly NovelizerTraceCollector _collector = new();

    [Fact]
    public void FirstAdd_WritesTraceFileImmediately()
    {
        _collector.BeginRun("mock-model", _dir);

        _collector.Add("label-1", "prompt", "thinking", "answer", "章节");

        Assert.True(File.Exists(ExpectedPath), "第一轮 Add 后复盘文件就应该已经落盘");
    }

    [Fact]
    public void EachAdd_RewritesFileWithLatestTurns()
    {
        _collector.BeginRun("mock-model", _dir);
        _collector.Add("label-1", "p", "t", "a", "章节");

        _collector.Add("label-2", "p", "t", "a", "章节", isCompress: true);

        var doc = RunDocumentLoad();
        Assert.Equal(2, doc!.TurnCount);
        Assert.Equal("label-2", doc.Turns[^1].Label);
        Assert.True(doc.Turns[^1].IsCompress);
    }

    [Fact]
    public void FinalSave_RewritesSameFileAndReturnsItsPath()
    {
        _collector.BeginRun("mock-model", _dir);
        _collector.Add("label-1", "p", "t", "a", "章节");
        _collector.Add("label-2", "p", "t", "a", "章节");

        var saved = _collector.Save(_dir);

        Assert.Equal(ExpectedPath, saved);
        Assert.Equal(2, RunDocumentLoad()!.TurnCount);
    }

    [Fact]
    public void BeginRunWithoutDir_DoesNotFlush_ButSaveStillWorks()
    {
        _collector.BeginRun("mock-model");
        _collector.Add("label-1", "p", "t", "a", "章节");

        Assert.False(File.Exists(ExpectedPath), "未指定落盘目录时不应写文件");

        var other = Path.Combine(_dir, "late");
        Directory.CreateDirectory(other);
        var saved = _collector.Save(other);
        Assert.NotNull(saved);
        Assert.True(File.Exists(saved!));
    }

    [Fact]
    public void CancelledRun_KeepsCompletedTurnsOnDisk()
    {
        // 模拟「生成到一半被取消」：没有走到 Save，磁盘上也要有已完成轮次可供复盘
        _collector.BeginRun("mock-model", _dir);
        _collector.Add("turn-1", "p", "t", "a", "章节");
        _collector.Add("turn-2", "p", "t", "a", "章节");
        // ……这里直接放弃 collector，不调 Save

        var doc = RunDocumentLoad();
        Assert.NotNull(doc);
        Assert.Equal(2, doc!.TurnCount);
    }

    [Fact]
    public void Save_UnwritableDir_ReturnsNullInsteadOfThrowing()
    {
        // novelizer-traces 位置被一个同名文件占住 → Directory.CreateDirectory 必失败
        var blocked = Path.Combine(_dir, "blocked-story");
        Directory.CreateDirectory(blocked);
        File.WriteAllText(Path.Combine(blocked, "novelizer-traces"), "不是目录");

        _collector.BeginRun("mock-model", blocked);
        _collector.Add("label-1", "p", "t", "a", "章节");

        // FlushCore（增量）已静默吞掉；收尾 Save 也不应把一次成功的生成报成失败
        var ex = Record.Exception(() => _collector.Save(blocked));
        Assert.Null(ex);
        Assert.Null(_collector.Save(blocked));
    }

    private RunDocument? RunDocumentLoad()
        => System.Text.Json.JsonSerializer.Deserialize<RunDocument>(File.ReadAllText(ExpectedPath));
}
