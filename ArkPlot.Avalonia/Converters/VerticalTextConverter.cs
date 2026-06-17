using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ArkPlot.Avalonia.Converters;

/// <summary>
/// 将字符串转为竖排格式（逐字换行），用于角色名右下角装饰。
/// </summary>
public sealed class VerticalTextConverter : IValueConverter
{
    public static VerticalTextConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // 逐字用换行符分隔，形成竖排
        var chars = text.ToCharArray();
        return string.Join(Environment.NewLine, chars);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
