using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ArkPlot.Avalonia.Models;

namespace ArkPlot.Avalonia.Converters;

/// <summary>将阶段状态映射为阶段标题文本的颜色。</summary>
public sealed class StageStatusToTextBrushConverter : IValueConverter
{
    public static readonly StageStatusToTextBrushConverter Instance = new();

    private static readonly IBrush PendingBrush = new SolidColorBrush(Color.Parse("#6B7280"));
    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#2563EB"));
    private static readonly IBrush DoneBrush = new SolidColorBrush(Color.Parse("#16A34A"));
    private static readonly IBrush FailedBrush = new SolidColorBrush(Color.Parse("#DC2626"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is StageStatus status
            ? status switch
            {
                StageStatus.Active => ActiveBrush,
                StageStatus.Done => DoneBrush,
                StageStatus.Failed => FailedBrush,
                _ => PendingBrush,
            }
            : PendingBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}