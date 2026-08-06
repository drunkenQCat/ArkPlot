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

    /// <summary>级别显示文本。</summary>
    public string LevelText => Level switch
    {
        LogLevel.Info => "Info",
        LogLevel.Progress => "Progress",
        LogLevel.Success => "Success",
        LogLevel.Warn => "Warn",
        _ => "Error",
    };

    public LogEntry(LogLevel level, string message, int? progress = null, bool isSection = false)
    {
        Level = level;
        Message = message;
        Progress = progress;
        IsSection = isSection;
        Time = DateTime.Now.ToString("HH:mm:ss");
    }
}