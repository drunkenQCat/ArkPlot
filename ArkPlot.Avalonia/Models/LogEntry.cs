using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArkPlot.Avalonia.Models;

/// <summary>日志级别，对应不同着色。</summary>
public enum LogLevel
{
    Info,
    Progress,
    Success,
    Warn,
    Error,
}

/// <summary>阶段内的一条日志记录。</summary>
public partial class LogEntry : ObservableObject
{
    /// <summary>记录时间（HH:mm:ss）。</summary>
    public string Time { get; }

    /// <summary>日志级别。</summary>
    public LogLevel Level { get; }

    /// <summary>日志正文。</summary>
    public string Message { get; }

    /// <summary>可选的行内进度（0-100），为 null 时不显示进度条。</summary>
    public int? Progress { get; }

    /// <summary>是否带行内进度条。</summary>
    public bool HasProgress => Progress.HasValue;

    /// <summary>是否为分段横幅（用于在阶段内标注子流程，如「图片描述进行中」）。</summary>
    public bool IsSection { get; }

    /// <summary>是否非分段横幅（普通日志）。</summary>
    public bool IsNotSection => !IsSection;

    /// <summary>关联图片 URL（图片描述日志：行内缩略图 + 悬停大图）。</summary>
    public string? ImageUrl { get; }

    /// <summary>是否关联图片。</summary>
    public bool HasImage => !string.IsNullOrEmpty(ImageUrl);

    /// <summary>悬停预览文本（如模型思考过程概览）。</summary>
    public string? Tooltip { get; }

    /// <summary>详细内容（点击展开：思考过程 + 返回值等长文本）。</summary>
    public string? Detail { get; }

    /// <summary>是否有可展开的详情。</summary>
    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    /// <summary>是否有悬停预览（图片大图 或 思考概览）——只有这类日志才飘窗。</summary>
    public bool HasPreview => HasImage || !string.IsNullOrEmpty(Tooltip);

    /// <summary>是否为普通日志行（非横幅、无预览）——不飘窗。</summary>
    public bool IsPlain => IsNotSection && !HasPreview;

    /// <summary>是否已展开详情（点击切换）。</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>关联的复盘轮次键（小说化 LLM 调用在 trace 中的标识，用于点击日志定位复盘面板）。</summary>
    public string? TurnKey { get; }

    /// <summary>级别显示文本。</summary>
    public string LevelText => Level switch
    {
        LogLevel.Info => "Info",
        LogLevel.Progress => "Progress",
        LogLevel.Success => "Success",
        LogLevel.Warn => "Warn",
        _ => "Error",
    };

    public LogEntry(
        LogLevel level,
        string message,
        int? progress = null,
        bool isSection = false,
        string? imageUrl = null,
        string? tooltip = null,
        string? detail = null,
        string? turnKey = null)
    {
        Level = level;
        Message = message;
        Progress = progress;
        IsSection = isSection;
        ImageUrl = imageUrl;
        Tooltip = tooltip;
        Detail = detail;
        TurnKey = turnKey;
        Time = DateTime.Now.ToString("HH:mm:ss");
    }
}