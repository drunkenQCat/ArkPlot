using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ArkPlot.Avalonia.Models;

namespace ArkPlot.Avalonia.Converters;

/// <summary>将日志级别映射为日志条目的文本颜色。</summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    public static readonly LogLevelToBrushConverter Instance = new();

    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#6B7280"));
    private static readonly IBrush ProgressBrush = new SolidColorBrush(Color.Parse("#2563EB"));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#16A34A"));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.Parse("#D97706"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#DC2626"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is LogLevel level
            ? level switch
            {
                LogLevel.Info => InfoBrush,
                LogLevel.Progress => ProgressBrush,
                LogLevel.Success => SuccessBrush,
                LogLevel.Warn => WarnBrush,
                _ => ErrorBrush,
            }
            : Brushes.White;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}