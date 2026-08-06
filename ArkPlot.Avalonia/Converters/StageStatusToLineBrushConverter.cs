using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ArkPlot.Avalonia.Models;

namespace ArkPlot.Avalonia.Converters;

/// <summary>
/// 将阶段状态映射为节点之间轨道连线的颜色：已完成段高亮实线，其余为暗色。
/// </summary>
public sealed class StageStatusToLineBrushConverter : IValueConverter
{
    public static readonly StageStatusToLineBrushConverter Instance = new();

    private static readonly IBrush DoneBrush = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.Parse("#D1D5DB"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is StageStatus status && status == StageStatus.Done ? DoneBrush : IdleBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}