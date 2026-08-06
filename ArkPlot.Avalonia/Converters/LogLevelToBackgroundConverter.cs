using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ArkPlot.Avalonia.Models;

namespace ArkPlot.Avalonia.Converters;

/// <summary>将日志级别映射为日志条目的背景色（错误带红色底纹，其余透明）。</summary>
public sealed class LogLevelToBackgroundConverter : IValueConverter
{
    public static readonly LogLevelToBackgroundConverter Instance = new();

    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#33EF4444"));
    private static readonly IBrush Transparent = Brushes.Transparent;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is LogLevel level && level == LogLevel.Error ? ErrorBrush : Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}