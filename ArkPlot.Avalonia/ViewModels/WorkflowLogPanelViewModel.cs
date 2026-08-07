using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.Services;
using SukiUI.Toasts;

namespace ArkPlot.Avalonia.ViewModels;

/// <summary>
/// 工作流日志面板的视图模型：以「阶段」为单位组织日志，供主程序在 pipeline
/// 各阶段推进时调用（BeginPipeline / EnterStage / Append / CompletePipeline）。
/// </summary>
public partial class WorkflowLogPanelViewModel : ObservableObject
{
    /// <summary>全部阶段（时间线展示，不含系统阶段）。</summary>
    public ObservableCollection<WorkflowStage> Stages { get; } = new();

    /// <summary>右侧日志区当前展示的日志（随选中阶段与「仅显示错误」过滤）。</summary>
    public ObservableCollection<LogEntry> VisibleLogs { get; } = new();

    /// <summary>系统日志阶段，接纳 pipeline 之外的消息（不显示在时间线）。</summary>
    public WorkflowStage SystemStage { get; }

    /// <summary>Toast 通知管理器（由宿主 ViewModel 注入，用于导出等操作反馈）。</summary>
    public ISukiToastManager? ToastManager { get; set; }

    /// <summary>当前选中的阶段（右侧展示其日志）。</summary>
    [ObservableProperty]
    private WorkflowStage? _selectedStage;

    /// <summary>总进度（0-100）。</summary>
    [ObservableProperty]
    private double _totalProgress;

    /// <summary>总进度文本，如「阶段 3/7」。</summary>
    [ObservableProperty]
    private string _progressText = string.Empty;

    /// <summary>全部阶段是否完成。</summary>
    [ObservableProperty]
    private bool _isComplete;

    /// <summary>仅显示错误日志。</summary>
    [ObservableProperty]
    private bool _showErrorsOnly;

    /// <summary>右侧日志区标题。</summary>
    [ObservableProperty]
    private string _headerText = "工作流日志";

    /// <summary>底部状态提示（导出 / 清空 / 定位等反馈）。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>当前活动阶段（新日志追加到它下面）。</summary>
    private WorkflowStage? _activeStage;

    public WorkflowLogPanelViewModel()
    {
        SystemStage = new WorkflowStage("系统", 0) { IsSystem = true };
        _activeStage = SystemStage;
        SelectedStage = SystemStage;
    }

    // ---------- 数据接入 API（供主程序 pipeline 调用） ----------

    /// <summary>开始一轮 pipeline，按名称列表重建阶段时间线。</summary>
    public void BeginPipeline(IReadOnlyList<string> stageNames)
    {
        Stages.Clear();
        for (var i = 0; i < stageNames.Count; i++)
        {
            Stages.Add(new WorkflowStage(stageNames[i], i + 1)
            {
                IsFirst = i == 0,
                IsLast = i == stageNames.Count - 1,
            });
        }
        _activeStage = null;
        SelectedStage = null;
        TotalProgress = 0;
        ProgressText = stageNames.Count > 0 ? $"阶段 0/{stageNames.Count}" : string.Empty;
        IsComplete = false;
        StatusMessage = string.Empty;
        RefreshVisibleLogs();
    }

    /// <summary>进入指定阶段（按索引），将其标为进行中并设为选中。</summary>
    public void EnterStage(int index)
    {
        if (index < 0 || index >= Stages.Count) return;
        _activeStage = Stages[index];
        _activeStage.Status = StageStatus.Active;
        SelectedStage = _activeStage;
        RefreshVisibleLogs();
    }

    /// <summary>向当前阶段追加一条日志（线程安全）。</summary>
    public void Append(
        LogLevel level,
        string message,
        int? progress = null,
        bool isSection = false,
        string? imageUrl = null,
        string? tooltip = null,
        string? detail = null)
    {
        // 在调用点同步捕获当前阶段：Post 是异步的，若延迟后在 Lambda 里才读 _activeStage，
        // 会读到 UI 线程已推进到的后续阶段，导致日志归属错乱。捕获后即使 Post 延迟也进它该进的阶段。
        var stage = _activeStage ?? SystemStage;
        Dispatcher.UIThread.Post(() =>
        {
            stage.Logs.Add(new LogEntry(level, message, progress, isSection, imageUrl, tooltip, detail));
            if (level == LogLevel.Error)
            {
                stage.ErrorCount++;
                stage.ShowErrorHint = true;
            }
            if (stage == SelectedStage) RefreshVisibleLogs();
            UpdateProgress();
        });
    }

    /// <summary>在阶段内插入一条醒目的分段横幅，用于标注子流程（如「图片描述进行中」）。</summary>
    public void AddSection(string text) => Append(LogLevel.Info, text, isSection: true);

    /// <summary>记录一条带图片的日志（图片描述）：行内缩略图 + 悬停大图 + 点击展开描述。</summary>
    public void AddImage(string imageUrl, string description)
        => Append(LogLevel.Success, description, imageUrl: imageUrl, detail: description);

    /// <summary>记录一条带思考过程的日志（小说化/图片描述）：悬停显示思考概览，点击展开完整思考 + 返回值。</summary>
    public void AddThought(string summary, string thinking, string answer, string? imageUrl = null)
    {
        var brief = thinking.Length <= 200 ? thinking : thinking[..200] + "...";
        var detail = $"—— 思考过程 ——\n\n{thinking}\n\n—— 返回结果 ——\n\n{answer}";
        Append(LogLevel.Info, summary, imageUrl: imageUrl, tooltip: brief, detail: detail);
    }

    /// <summary>切换某条日志的详情展开状态（点击 log 条目触发）。</summary>
    public void ToggleExpand(LogEntry entry)
    {
        if (entry.HasDetail) entry.IsExpanded = !entry.IsExpanded;
    }

    /// <summary>结束 pipeline：将未失败阶段全部置为完成，标记整体完成。</summary>
    public void CompletePipeline()
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var s in Stages)
            {
                if (s.Status != StageStatus.Failed) s.Status = StageStatus.Done;
            }
            _activeStage = null;
            TotalProgress = 100;
            ProgressText = Stages.Count > 0 ? $"阶段 {Stages.Count}/{Stages.Count}" : string.Empty;
            IsComplete = true;
            RefreshVisibleLogs();
        });
    }

    // ---------- 对外交互 ----------

    /// <summary>点击阶段节点，切换右侧展示其日志。</summary>
    public void SelectStage(WorkflowStage stage)
    {
        SelectedStage = stage;
        RefreshVisibleLogs();
    }

    /// <summary>「定位到问题」：将当前阶段错误日志置顶展示。</summary>
    public void LocateError()
    {
        if (SelectedStage == null) return;
        var errors = SelectedStage.Logs.Where(e => e.Level == LogLevel.Error).ToList();
        if (errors.Count == 0)
        {
            StatusMessage = "当前阶段没有错误日志";
            return;
        }
        VisibleLogs.Clear();
        foreach (var e in errors) VisibleLogs.Add(e);
        StatusMessage = $"已定位到 {errors.Count} 条错误日志";
    }

    partial void OnShowErrorsOnlyChanged(bool value) => RefreshVisibleLogs();

    partial void OnSelectedStageChanged(WorkflowStage? value)
    {
        HeaderText = value == null ? "工作流日志" : $"{value.Name} · 阶段日志";
        RefreshVisibleLogs();
    }

    /// <summary>根据选中阶段与过滤开关重建右侧可见日志。</summary>
    public void RefreshVisibleLogs()
    {
        VisibleLogs.Clear();
        if (SelectedStage == null) return;
        foreach (var e in SelectedStage.Logs)
        {
            if (ShowErrorsOnly && e.Level != LogLevel.Error) continue;
            VisibleLogs.Add(e);
        }
    }

    // ---------- 工具栏命令 ----------

    [RelayCommand]
    private void ClearLogs()
    {
        foreach (var s in Stages) s.Logs.Clear();
        SystemStage.Logs.Clear();
        RefreshVisibleLogs();
        StatusMessage = "日志已清空";
    }

    [RelayCommand]
    private async Task ExportLogs()
    {
        var storageProvider = GlobalStorageProvider.StorageProvider;
        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出工作流日志",
            SuggestedFileName = "workflow-log.txt",
            DefaultExtension = "txt",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("文本文件") { Patterns = new[] { "*.txt" } },
            },
        });
        if (file == null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(BuildExportText());
            StatusMessage = "日志已导出";
            ToastManager?.CreateToast()
                .WithTitle("导出成功")
                .WithContent($"已导出到 {file.Path.LocalPath}")
                .OfType(NotificationType.Success)
                .Dismiss()
                .After(TimeSpan.FromSeconds(3))
                .Queue();
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出失败：{ex.Message}";
        }
    }

    /// <summary>构建导出文本（阶段 + 日志）。</summary>
    private string BuildExportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("ArkPlot 工作流日志导出");
        sb.AppendLine("=".PadRight(48, '='));
        foreach (var s in Stages)
        {
            sb.AppendLine($"\n【{s.Number}. {s.Name}】 {s.StatusText}  {s.Duration}");
            foreach (var e in s.Logs)
            {
                sb.AppendLine($"  [{e.Time}] [{e.LevelText}] {e.Message}");
                if (e.HasDetail)
                    sb.AppendLine($"      └ {e.Detail!.Replace("\n", "\n        ")}");
            }
        }
        return sb.ToString();
    }

    // ---------- 内部 ----------

    /// <summary>根据已完成/进行中阶段推算总进度。</summary>
    private void UpdateProgress()
    {
        var total = Stages.Count;
        if (total == 0) return;
        var done = Stages.Count(s => s.Status == StageStatus.Done);
        var active = Stages.FirstOrDefault(s => s.Status == StageStatus.Active);
        var fraction = active?.Progress / 100.0 ?? 0;
        TotalProgress = (done + fraction) / total * 100.0;
        var shown = Math.Min(done + (active != null ? 1 : 0), total);
        ProgressText = active != null || done == total ? $"阶段 {shown}/{total}" : $"阶段 {done}/{total}";
    }
}