using System;
using System.Globalization;
using Avalonia.Data.Converters;
using MdEngine = Markdown.Avalonia.Markdown;

namespace ArkPlot.Avalonia.Views.Converters;

/// <summary>
/// 把 Markdown 文本渲染为 Avalonia 控件树（复用 Markdown.Avalonia 引擎与 Markdig 解析）。
/// 供复盘详情各内容块使用：绑定文本 → 生成可在外层 ScrollViewer 中随文档流滚动的内容。
/// </summary>
public sealed class MarkdownToControlConverter : IValueConverter
{
    /// <summary>共享引擎实例（渲染是纯文本转换，无状态，可跨绑定复用）。</summary>
    private static readonly MdEngine Engine = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string { Length: > 0 } text ? Engine.Transform(text) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}