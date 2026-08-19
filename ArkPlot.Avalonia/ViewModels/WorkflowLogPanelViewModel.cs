using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
using ArkPlot.Core.Infrastructure;
using ArkPlot.Core.Model;
using ArkPlot.Core.Services;
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

    /// <summary>点击带 turnKey 的日志（小说化思考/压缩）时触发，交由宿主打开复盘面板并定位。参数为 turnKey。
/// 使用委托属性而非 event，便于在 UserControl code-behind 中直接调用。</summary>
    public Action<string>? OpenTraceRequested;

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

    /// <summary>当前选中阶段是否存在未处理错误（控制「定位到问题」按钮显隐）。</summary>
    [ObservableProperty]
    private bool _selectedStageShowErrorHint;

    /// <summary>当前活动阶段（新日志追加到它下面）。</summary>
    private WorkflowStage? _activeStage;

    /// <summary>当前活动阶段的计时器（进入时启动，关闭/失败/完成时停止并写入 Duration）。</summary>
    private Stopwatch? _stageStopwatch;

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

    /// <summary>进入指定阶段（按索引），关闭上一个活动阶段并将其标为进行中、设为选中。</summary>
    public void EnterStage(int index)
    {
        if (index < 0 || index >= Stages.Count) return;
        var stage = Stages[index];

        // 幂等：重复进入当前活动阶段时不做任何事（避免关闭再重启、计时被重置）。
        if (ReferenceEquals(_activeStage, stage)) return;

        // 关闭上一个活动阶段（失败阶段已由 FailStage 收尾，保持不变）。
        if (_activeStage is { } previous && previous.Status != StageStatus.Failed)
        {
            previous.Status = StageStatus.Done;
            StopStageTiming(previous);
        }

        _activeStage = stage;
        stage.Status = StageStatus.Active;
        StartStageTiming(stage);
        SelectedStage = stage;
        RefreshVisibleLogs();
        UpdateProgress();
    }

    /// <summary>向当前阶段追加一条日志（线程安全）。</summary>
    public void Append(
        LogLevel level,
        string message,
        int? progress = null,
        bool isSection = false,
        string? imageUrl = null,
        string? tooltip = null,
        string? detail = null,
        string? turnKey = null)
    {
        // 在调用点同步捕获当前阶段：Post 是异步的，若延迟后在 Lambda 里才读 _activeStage，
        // 会读到 UI 线程已推进到的后续阶段，导致日志归属错乱。捕获后即使 Post 延迟也进它该进的阶段。
        var stage = _activeStage ?? SystemStage;
        // 在调用点构造 LogEntry：Time 取事件发生时刻，而非 UI 线程真正处理（队列排空）时刻。
        var entry = new LogEntry(level, message, progress, isSection, imageUrl, tooltip, detail, turnKey);
        Dispatcher.UIThread.Post(() =>
        {
            stage.Logs.Add(entry);
            if (level == LogLevel.Error)
            {
                stage.ErrorCount++;
                stage.ShowErrorHint = true;
                if (stage == SelectedStage) SelectedStageShowErrorHint = true;
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

    /// <summary>记录一条带完整上下文的日志（小说化）：悬停显示思考概览，点击展开「输入 Prompt / 思考 / 输出」。
/// turnKey 关联复盘面板中对应轮次，点击日志可定位。</summary>
    public void AddThought(string summary, string prompt, string thinking, string answer, string? imageUrl = null, string? turnKey = null)
    {
        var brief = thinking.Length <= 200 ? thinking : thinking[..200] + "...";
        var detail = $"—— 输入 Prompt ——\n\n{prompt}\n\n—— 思考过程 ——\n\n{thinking}\n\n—— 返回结果 ——\n\n{answer}";
        Append(LogLevel.Info, summary, imageUrl: imageUrl, tooltip: brief, detail: detail, turnKey: turnKey);
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
            // 停止当前活动阶段计时并写入 Duration（pending 阶段保持空字符串）。
            if (_activeStage is { } active)
            {
                StopStageTiming(active);
            }
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

    /// <summary>将当前阶段标记为失败（用于管线异常/网络错误等收尾路径），可附带一条错误日志。</summary>
    public void FailStage(string? message = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var stage = _activeStage;
            if (stage == null)
            {
                if (message != null)
                    SystemStage.Logs.Add(new LogEntry(LogLevel.Error, message));
                return;
            }
            stage.Status = StageStatus.Failed;
            if (message != null)
            {
                stage.Logs.Add(new LogEntry(LogLevel.Error, message));
                stage.ErrorCount++;
            }
            stage.ShowErrorHint = true;
            StopStageTiming(stage);
            _activeStage = null;
            UpdateProgress();
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

    // ---------- 清除缓存（开发开关控制显隐） ----------

    /// <summary>是否显示「清除缓存」按钮（由 DevOptionsPanel 开关控制，持久化于 settings.json）。</summary>
    [ObservableProperty]
    private bool isClearCacheVisible;

    /// <summary>当前故事输出目录（由宿主 VM 注入，指向主界面 OutputPath/{当前活动}）。</summary>
    public string? StoryOutputDir { get; set; }

    /// <summary>当前勾选的章节名（由宿主 VM 在 LoadMd 时注入，清除缓存按这些章节粒度执行）。</summary>
    public List<string> CurrentChapterNames { get; set; } = new();

    /// <summary>当前勾选章节的解析条目（由宿主 VM 注入，用于还原该章节的图片描述缓存 key）。</summary>
    public List<ScriptLine> CurrentChapterEntries { get; set; } = new();

    /// <summary>「清除缓存」：按当前勾选章节清除图片描述缓存（DB）与小说化缓存（json + 输出文件）。</summary>
    [RelayCommand]
    private void ClearCache()
    {
        try
        {
            var picDescCount = CacheCleanupDao.DeletePicDescriptions(CurrentChapterEntries);
            var (novelEntries, novelFiles) = CacheCleanupDao.DeleteNovelizerCache(
                StoryOutputDir ?? string.Empty, CurrentChapterNames);

            var parts = new List<string>();
            if (picDescCount > 0) parts.Add($"图片描述缓存 {picDescCount} 条");
            if (novelEntries > 0 || novelFiles > 0)
                parts.Add($"小说化缓存 {novelEntries} 条 / {novelFiles} 个文件");

            StatusMessage = parts.Count > 0
                ? $"已清除：{string.Join("、", parts)}（重新生成后将重新调用 API）"
                : "未发现可清除的缓存（请先选择并生成章节）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"清除缓存失败：{ex.Message}";
        }
    }

    /// <summary>「定位到问题」：将当前阶段错误日志置顶展示。</summary>
    [RelayCommand]
    private void LocateError()
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
        SelectedStageShowErrorHint = value?.ShowErrorHint ?? false;
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

    /// <summary>启动当前阶段的计时。</summary>
    private void StartStageTiming(WorkflowStage stage)
    {
        _stageStopwatch?.Stop();
        _stageStopwatch = Stopwatch.StartNew();
    }

    /// <summary>停止当前阶段的计时并写入其 Duration。</summary>
    private void StopStageTiming(WorkflowStage stage)
    {
        if (_stageStopwatch == null) return;
        _stageStopwatch.Stop();
        stage.Duration = FormatDuration(_stageStopwatch.Elapsed);
        _stageStopwatch = null;
    }

    /// <summary>格式化耗时：小于 1 秒显示毫秒（如 203ms），否则显示秒（如 1.24s）。</summary>
    private static string FormatDuration(TimeSpan elapsed)
    {
        return elapsed.TotalSeconds < 1
            ? $"{(int)elapsed.TotalMilliseconds}ms"
            : $"{elapsed.TotalSeconds:0.00}s";
    }

    /// <summary>根据已完成阶段与当前活动阶段进度推算总进度（由 _activeStage 驱动，而非遍历首个 Active）。</summary>
    private void UpdateProgress()
    {
        var total = Stages.Count;
        if (total == 0) return;
        var done = Stages.Count(s => s.Status == StageStatus.Done);
        var active = _activeStage is { Status: StageStatus.Active } ? _activeStage : null;
        var fraction = active?.Progress / 100.0 ?? 0;
        TotalProgress = (done + fraction) / total * 100.0;
        ProgressText = active != null
            ? $"阶段 {Math.Min(done + 1, total)}/{total}"
            : $"阶段 {done}/{total}";
    }
}