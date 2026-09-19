using System;
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
    /// <summary>详情区当前查看的页（阶段条翻页）。</summary>
    public enum DetailPageKind { Input, Think, Output }

    /// <summary>一行运行记录（左侧列表项）。</summary>
    public sealed class RunRow
    {
        public required string FileName { get; init; }
        public required string Display { get; init; }
        public required RunDocument Document { get; init; }
    }

    /// <summary>一行 LLM 调用记录（中间列表项，五段数据已展开为平铺字段便于绑定）。</summary>
    public sealed class TurnRow
    {
        public required string Label { get; init; }
        public required string Prompt { get; init; }
        /// <summary>prompt 拆出的 system 段（多条合并）。</summary>
        public required string System { get; init; }
        /// <summary>prompt 拆出的 user/其余段（多条合并）。</summary>
        public required string User { get; init; }
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

    /// <summary>详情区当前页（阶段条翻页）。</summary>
    [ObservableProperty]
    private DetailPageKind _detailPage = DetailPageKind.Input;

    /// <summary>输入页是否折叠。</summary>
    [ObservableProperty]
    private bool _isInputCollapsed;

    /// <summary>思考页是否折叠。</summary>
    [ObservableProperty]
    private bool _isThinkCollapsed;

    /// <summary>输出页是否折叠。</summary>
    [ObservableProperty]
    private bool _isOutputCollapsed;

    /// <summary>输入页内 system 子部是否折叠。</summary>
    [ObservableProperty]
    private bool _isSystemCollapsed;

    /// <summary>输入页内 user 子部是否折叠。</summary>
    [ObservableProperty]
    private bool _isUserCollapsed;

    // ---------- 详情区可见性（阶段条高亮 / 翻页 / 折叠） ----------

    public bool IsInputActive => DetailPage == DetailPageKind.Input;
    public bool IsThinkActive => DetailPage == DetailPageKind.Think;
    public bool IsOutputActive => DetailPage == DetailPageKind.Output;

    /// <summary>输入页外壳（折叠头所在，选中轮次且当前页时恒显，保证折叠后仍可展开）。</summary>
    public bool IsInputShellVisible => HasSelectedTurn && DetailPage == DetailPageKind.Input;
    public bool IsThinkShellVisible => HasSelectedTurn && DetailPage == DetailPageKind.Think;
    public bool IsOutputShellVisible => HasSelectedTurn && DetailPage == DetailPageKind.Output;

    /// <summary>输入页内容区（未折叠时）。</summary>
    public bool IsInputPageVisible => IsInputShellVisible && !IsInputCollapsed;
    public bool IsThinkPageVisible => IsThinkShellVisible && !IsThinkCollapsed;
    public bool IsOutputPageVisible => IsOutputShellVisible && !IsOutputCollapsed;

    /// <summary>输入页折叠占位（折叠时提示可点击展开）。</summary>
    public bool IsInputHintVisible => IsInputShellVisible && IsInputCollapsed;
    public bool IsThinkHintVisible => IsThinkShellVisible && IsThinkCollapsed;
    public bool IsOutputHintVisible => IsOutputShellVisible && IsOutputCollapsed;

    /// <summary>system 子部外壳（有内容才显示）。</summary>
    public bool IsSystemHeadVisible => IsInputPageVisible && SelectedTurn is { System.Length: > 0 };
    public bool IsUserHeadVisible => IsInputPageVisible && SelectedTurn is { User.Length: > 0 };

    /// <summary>system/user 子部内容（未折叠时）。</summary>
    public bool IsSystemContentVisible => IsSystemHeadVisible && !IsSystemCollapsed;
    public bool IsUserContentVisible => IsUserHeadVisible && !IsUserCollapsed;

    // ---------- 折叠头文本（含字符数与折叠态提示） ----------

    public string InputFoldHeader => IsInputCollapsed
        ? "▸ ① 输入 Prompt（已折叠）"
        : $"▾ ① 输入 Prompt · system {SelectedTurn?.System.Length ?? 0} 字 / user {SelectedTurn?.User.Length ?? 0} 字";

    public string ThinkFoldHeader => IsThinkCollapsed
        ? "▸ ② 思考过程（已折叠）"
        : $"▾ ② 思考过程 · {SelectedTurn?.Thinking.Length ?? 0} 字";

    public string OutputFoldHeader => IsOutputCollapsed
        ? "▸ ③ 输出内容（已折叠）"
        : $"▾ ③ 输出内容 · {SelectedTurn?.Answer.Length ?? 0} 字";

    public string SystemSubHeader => IsSystemCollapsed
        ? "▸ system（已折叠）"
        : $"▾ system · {SelectedTurn?.System.Length ?? 0} 字";

    public string UserSubHeader => IsUserCollapsed
        ? "▸ user（已折叠）"
        : $"▾ user · {SelectedTurn?.User.Length ?? 0} 字";

    partial void OnSelectedTurnChanged(TurnRow? value)
    {
        HasSelectedTurn = value != null;
        NotifyDetailView();
    }

    partial void OnDetailPageChanged(DetailPageKind value) => NotifyDetailView();
    partial void OnIsInputCollapsedChanged(bool value) => NotifyDetailView();
    partial void OnIsThinkCollapsedChanged(bool value) => NotifyDetailView();
    partial void OnIsOutputCollapsedChanged(bool value) => NotifyDetailView();
    partial void OnIsSystemCollapsedChanged(bool value) => NotifyDetailView();
    partial void OnIsUserCollapsedChanged(bool value) => NotifyDetailView();

    /// <summary>详情区任一状态变化时，批量通知所有可见性 / 折叠头文本。</summary>
    private void NotifyDetailView()
    {
        OnPropertyChanged(nameof(IsInputActive));
        OnPropertyChanged(nameof(IsThinkActive));
        OnPropertyChanged(nameof(IsOutputActive));
        OnPropertyChanged(nameof(IsInputShellVisible));
        OnPropertyChanged(nameof(IsThinkShellVisible));
        OnPropertyChanged(nameof(IsOutputShellVisible));
        OnPropertyChanged(nameof(IsInputPageVisible));
        OnPropertyChanged(nameof(IsThinkPageVisible));
        OnPropertyChanged(nameof(IsOutputPageVisible));
        OnPropertyChanged(nameof(IsInputHintVisible));
        OnPropertyChanged(nameof(IsThinkHintVisible));
        OnPropertyChanged(nameof(IsOutputHintVisible));
        OnPropertyChanged(nameof(IsSystemHeadVisible));
        OnPropertyChanged(nameof(IsUserHeadVisible));
        OnPropertyChanged(nameof(IsSystemContentVisible));
        OnPropertyChanged(nameof(IsUserContentVisible));
        OnPropertyChanged(nameof(InputFoldHeader));
        OnPropertyChanged(nameof(ThinkFoldHeader));
        OnPropertyChanged(nameof(OutputFoldHeader));
        OnPropertyChanged(nameof(SystemSubHeader));
        OnPropertyChanged(nameof(UserSubHeader));
    }

    // ---------- 详情区交互命令 ----------

    [RelayCommand]
    private void SelectPage(string page)
        => DetailPage = Enum.TryParse<DetailPageKind>(page, out var p) ? p : DetailPageKind.Input;

    [RelayCommand]
    private void ToggleFold(string page)
    {
        if (page == nameof(DetailPageKind.Think)) IsThinkCollapsed = !IsThinkCollapsed;
        else if (page == nameof(DetailPageKind.Output)) IsOutputCollapsed = !IsOutputCollapsed;
        else IsInputCollapsed = !IsInputCollapsed;
    }

    [RelayCommand]
    private void ToggleSubFold(string sub)
    {
        if (sub == "User") IsUserCollapsed = !IsUserCollapsed;
        else IsSystemCollapsed = !IsSystemCollapsed;
    }

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
            var (system, user) = TracePromptSplitter.Split(t.Prompt);
            Turns.Add(new TurnRow
            {
                Label = t.Label,
                Prompt = t.Prompt,
                System = system,
                User = user,
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

        var liveRunId = LiveCollector is null ? null : CompactRunId(LiveCollector.RunId);
        foreach (var doc in NovelizerTraceCollector.LoadAll(TraceRoot))
        {
            // 当前运行的增量落盘文件与上面的实时行是同一份，跳过避免重复展示
            if (liveRunId is { Length: > 0 } && CompactRunId(doc.StartedAt) == liveRunId)
                continue;
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

        // 优先实时运行：RunId 匹配时直接定位；否则在历史行中匹配 StartedAt（数字归一化后）
        if (LiveCollector != null && CompactRunId(LiveCollector.RunId) == CompactRunId(parts[0]))
        {
            if (Runs.FirstOrDefault(r => r.FileName == "live") is { } liveRun)
            {
                SelectedRun = liveRun;
                if (index >= 0 && index < Turns.Count) SelectedTurn = Turns[index];
                else Status = $"该运行只有 {Turns.Count} 轮，无法定位到第 {index + 1} 轮";
            }
            return;
        }

        var history = Runs.FirstOrDefault(r => r.FileName == "history" && CompactRunId(r.Document.StartedAt) == CompactRunId(parts[0]));
        if (history is null)
        {
            Status = $"未找到运行记录 {parts[0]}（trace 目录可能不是生成时的输出目录）";
            return;
        }
        SelectedRun = history;
        if (index >= 0 && index < Turns.Count) SelectedTurn = Turns[index];
    }

    /// <summary>把 RunId（20260919_103000）与 StartedAt（2026-09-19 10:30:00）归一成同一段数字再比对。</summary>
    private static string CompactRunId(string value)
        => new string(value.Where(char.IsDigit).ToArray());
}