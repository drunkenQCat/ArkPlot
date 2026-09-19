using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArkPlot.Avalonia.Models;

/// <summary>阶段状态。</summary>
public enum StageStatus
{
    Pending, // 未开始
    Active,  // 进行中
    Done,    // 成功
    Failed,  // 失败
}

/// <summary>工作流中的一个阶段（里程碑节点）。</summary>
public partial class WorkflowStage : ObservableObject
{
    /// <summary>阶段名称。</summary>
    public string Name { get; }

    /// <summary>阶段序号（从 1 开始；0 表示系统日志阶段，不在时间线展示）。</summary>
    public int Number { get; }

    /// <summary>该阶段产出的日志流。</summary>
    public ObservableCollection<LogEntry> Logs { get; } = new();

    /// <summary>阶段状态。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(CircleGlyph))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    private StageStatus _status = StageStatus.Pending;

    /// <summary>阶段耗时文本（如 203ms）。</summary>
    [ObservableProperty]
    private string _duration = string.Empty;

    /// <summary>是否显示「定位到问题」提示。</summary>
    [ObservableProperty]
    private bool _showErrorHint;

    /// <summary>该阶段内的错误条数。</summary>
    [ObservableProperty]
    private int _errorCount;

    /// <summary>阶段内进度（0-100），用于阶段进度条与总进度推算。</summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>是否为第一个阶段。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotFirst))]
    private bool _isFirst;

    /// <summary>是否为最后一个阶段。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLast))]
    private bool _isLast;

    /// <summary>是否为系统日志阶段（接纳 pipeline 之外的消息，不显示在时间线）。</summary>
    [ObservableProperty]
    private bool _isSystem;

    public WorkflowStage(string name, int number)
    {
        Name = name;
        Number = number;
    }

    /// <summary>状态徽章文本。</summary>
    public string StatusText => Status switch
    {
        StageStatus.Done => "完成",
        StageStatus.Active => "进行中",
        StageStatus.Failed => "失败",
        _ => "等待",
    };

    /// <summary>节点圆形图标内容。</summary>
    public string CircleGlyph => Status switch
    {
        StageStatus.Done => "\u2713",   // ✓
        StageStatus.Failed => "\u2717", // ✗
        StageStatus.Active => "\u25CF", // ●
        _ => Number.ToString(),
    };

    public bool IsDone => Status == StageStatus.Done;
    public bool IsFailed => Status == StageStatus.Failed;
    public bool IsActive => Status == StageStatus.Active;
    public bool IsPending => Status == StageStatus.Pending;

    /// <summary>是否为非首个阶段。</summary>
    public bool IsNotFirst => !IsFirst;

    /// <summary>是否为非最后阶段。</summary>
    public bool IsNotLast => !IsLast;
}