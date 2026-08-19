using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ArkPlot.Novelizer;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkPlot.Avalonia.ViewModels;

/// <summary>小说化复盘面板：浏览历史运行的每次 LLM 调用，查看完整 prompt / 思考 / 输出 / 压缩。</summary>
public partial class TraceReviewViewModel : ViewModelBase
{
    /// <summary>一行运行记录（左侧列表项）。</summary>
    public sealed class RunRow
    {
        public required string FileName { get; init; }
        public required string Display { get; init; }
        public required RunDocument Document { get; init; }
    }

    /// <summary>一行 LLM 调用记录（中间列表项，四段数据已展开为平铺字段便于绑定）。</summary>
    public sealed class TurnRow
    {
        public required string Label { get; init; }
        public required string Prompt { get; init; }
        public required string Thinking { get; init; }
        public required string Answer { get; init; }
        public required string ChapterTitle { get; init; }
        public required bool IsCompress { get; init; }
    }

    /// <summary>搜索的 trace 目录（输出根目录/当前故事的 novelizer-traces）。</summary>
    public string? TraceRoot { get; set; }

    /// <summary>当前运行的实时收集器（由宿主注入；面板打开时优先展示它，随生成实时累积）。</summary>
    public NovelizerTraceCollector? LiveCollector { get; set; }

    [ObservableProperty]
    private ObservableCollection<RunRow> _runs = new();

    [ObservableProperty]
    private RunRow? _selectedRun;

    [ObservableProperty]
    private ObservableCollection<TurnRow> _turns = new();

    [ObservableProperty]
    private TurnRow? _selectedTurn;

    /// <summary>是否有选中轮次（详情区显隐）。</summary>
    [ObservableProperty]
    private bool _hasSelectedTurn;

    partial void OnSelectedTurnChanged(TurnRow? value) => HasSelectedTurn = value != null;

    /// <summary>状态行（加载结果 / 空目录提示）。</summary>
    [ObservableProperty]
    private string _status = "选择故事输出目录后刷新";

    public bool HasRuns => Runs.Count > 0;

    partial void OnSelectedRunChanged(RunRow? value)
    {
        Turns.Clear();
        SelectedTurn = null;
        if (value is null) return;

        foreach (var t in value.Document.Turns)
        {
            Turns.Add(new TurnRow
            {
                Label = t.Label,
                Prompt = t.Prompt,
                Thinking = t.Thinking,
                Answer = t.Answer,
                ChapterTitle = t.ChapterTitle,
                IsCompress = t.IsCompress,
            });
        }
        Status = Turns.Count > 0
            ? $"共 {Turns.Count} 次 LLM 调用"
            : "该运行无 LLM 调用记录";
    }

    /// <summary>加载目录下的全部运行：实时收集器（当前运行，置顶）→ 历史落盘文件（最新在前）。</summary>
    [RelayCommand]
    public void RefreshRuns()
    {
        Runs.Clear();
        SelectedRun = null;

        if (string.IsNullOrWhiteSpace(TraceRoot) || !Directory.Exists(TraceRoot))
        {
            Status = "trace 目录不存在，请先运行一次小说化";
            return;
        }

        // 实时运行（内存收集器），置顶并标注「进行中/已结束」
        if (LiveCollector is { Turns.Count: > 0 })
        {
            Runs.Add(new RunRow
            {
                FileName = "live",
                Display = $"{LiveCollector.RunId}（当前运行）｜ {LiveCollector.Turns.Count} 次调用",
                Document = new RunDocument
                {
                    Model = LiveCollector.RunId,
                    StartedAt = "实时",
                    TurnCount = LiveCollector.Turns.Count,
                    Turns = LiveCollector.Turns.ToList(),
                },
            });
        }

        foreach (var doc in NovelizerTraceCollector.LoadAll(TraceRoot))
        {
            Runs.Add(new RunRow
            {
                FileName = "history",
                Display = $"{doc.StartedAt} ｜ {doc.Model} ｜ {doc.TurnCount} 次调用",
                Document = doc,
            });
        }
        Status = Runs.Count > 0
            ? $"已加载 {Runs.Count} 次运行"
            : "未找到 trace 文件（novelizer-traces/*.json），请先运行一次小说化";
    }

    /// <summary>按 turnKey（"runId:序号"）定位到对应轮次并选中。</summary>
    public void SelectTurnByKey(string turnKey)
    {
        var parts = turnKey.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var index))
            return;

        // 优先实时运行：RunId 匹配时直接定位；否则在历史行中匹配 StartedAt 前缀
        if (LiveCollector?.RunId == parts[0] || parts[0] == LiveCollector?.RunId)
        {
            if (Runs.FirstOrDefault(r => r.FileName == "live") is { } liveRun)
            {
                SelectedRun = liveRun;
                if (index >= 0 && index < Turns.Count) SelectedTurn = Turns[index];
            }
            return;
        }

        var history = Runs.FirstOrDefault(r => r.Document.StartedAt.Replace(":", "").Replace("/", "").Replace("-", "") == parts[0]);
        if (history is null) return;
        SelectedRun = history;
        if (index >= 0 && index < Turns.Count) SelectedTurn = Turns[index];
    }
}