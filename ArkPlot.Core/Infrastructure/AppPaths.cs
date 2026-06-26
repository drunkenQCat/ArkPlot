using System.IO;

namespace ArkPlot.Core.Infrastructure;

/// <summary>
/// 应用级路径：定位 typst-template 目录和应用根目录。
/// </summary>
public static class AppPaths
{
    /// <summary>typst-template 目录的绝对路径。</summary>
    public static string TypstTemplateDir() =>
        Path.Combine(AppContext.BaseDirectory, "typst-template");

    /// <summary>typst-template/pics 目录的绝对路径。</summary>
    public static string TypstPicsDir() => Path.Combine(TypstTemplateDir(), "pics");

    /// <summary>Typst 代码中引用 pics/ 的相对路径（相对于 AppRoot）。</summary>
    public static string TypstPicsRelative => "typst-template/pics";
}
