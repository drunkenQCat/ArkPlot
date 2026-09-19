using ArkPlot.Novelizer;
using Xunit;

namespace ArkPlot.Novelizer.Tests;

/// <summary>
/// BatchProcessAsync 的 onlyFile 语义：GUI 传本次导出的唯一输入文件。
/// 输出目录里可能残留历史全量快照（如 *​_original.md）——扫描模式会把没选的章节
/// 一并小说化（「选了一个章节、全部章节被导出」的根因），onlyFile 模式必须只碰指定文件。
/// </summary>
public class BatchProcessOnlyFileTests : IDisposable
{
    private readonly string _dir;

    public BatchProcessOnlyFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"only-file-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            Path.Combine(_dir, "当前活动.md"),
            "## 第一章 幕间\n\n正文内容。\n\n## 第二章\n\n更多内容。\n");
        // 历史遗留的全量快照：24 章规模的旧文件，混在同一输出目录
        File.WriteAllText(
            Path.Combine(_dir, "当前活动_original.md"),
            string.Join("\n", Enumerable.Range(1, 24).Select(i => $"## 旧章 {i}\n\n旧内容 {i}。\n")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 临时目录删不掉就算了 */ }
    }

    private NovelizerPipeline NewPipeline()
    {
        var client = new MockBailianClient(
            new HttpClient(),
            new ApiConfig { Provider = ApiProvider.Custom, ApiKey = "mock", BaseUrl = "http://localhost" });
        return new NovelizerPipeline(client, new ApiConfig(), useMock: true);
    }

    [Fact]
    public async Task OnlyFile_ProcessesJustThatFile_IgnoresOtherMdInDir()
    {
        var onlyFile = Path.Combine(_dir, "当前活动.md");

        await NewPipeline().BatchProcessAsync(_dir, ["mock-model"], force: true, onlyFile: onlyFile);

        Assert.True(File.Exists(Path.Combine(_dir, "当前活动_novel_mock-model.md")),
            "指定的输入文件应被小说化");
        Assert.False(File.Exists(Path.Combine(_dir, "当前活动_original_novel_mock-model.md")),
            "目录里的历史快照不应被顺带小说化");
    }

    [Fact]
    public async Task OnlyFile_MissingFile_LogsAndDoesNothing()
    {
        await NewPipeline().BatchProcessAsync(
            _dir, ["mock-model"], force: true, onlyFile: Path.Combine(_dir, "不存在.md"));

        Assert.Empty(Directory.GetFiles(_dir, "*_novel_*.md"));
    }

    [Fact]
    public async Task WithoutOnlyFile_KeepsLegacyScanBehavior()
    {
        // CLI 等扫描用法不受影响：仍处理目录下全部（非 _novel_）md
        await NewPipeline().BatchProcessAsync(_dir, ["mock-model"], force: true);

        Assert.True(File.Exists(Path.Combine(_dir, "当前活动_novel_mock-model.md")));
        Assert.True(File.Exists(Path.Combine(_dir, "当前活动_original_novel_mock-model.md")));
    }
}
