using System.IO;
using ArkPlot.Core.Model;
using ArkPlot.Core.Utilities.TagProcessingComponents;
using ArkPlot.Core.Utilities.WorkFlow;
using ArkPlot.Core.Utilities.WorkFlow.StoryDocument;
using Markdig;

namespace ArkPlot.Core.Utilities;

public abstract class AkpProcessor
{
    /// <summary>
    /// 将一组剧情导出为 Markdown 文本。
    /// </summary>
    public static async Task<string> ExportPlotsAsync(
        List<PlotManager> plotList,
        Services.PicDescService? picDescService = null,
        bool enableDescriptions = true,
        OutputMode outputMode = OutputMode.Readable)
    {
        var md = new StringBuilder();
        foreach (var chapter in plotList)
        {
            var textList = chapter.CurrentPlot.TextVariants;
            if (picDescService != null)
            {
                await PicDescEnricher.EnrichAsync(textList, picDescService);
                // 传播 CharacterCode：charslot 条目的 CharacterCode 传播到后续同名对话条目
                // 再将 charslot 的 PicFacts 复制给对话条目，确保 Prompt 模式输出 portrait-facts
                PropagateCharacterCodeAndFacts(textList);
            }
            var builder = new StoryDocumentBuilder(textList, enableDescriptions, outputMode);
            md.Append($"## {chapter.CurrentPlot.Title}\r\n\r\n");
            builder.AppendResultToBuilder(md);
        }

        return md.ToString();
    }

    /// <summary>
    /// 将指定的 Plot 对象以 Markdown 文件的形式写入到指定的路径。
    /// </summary>
    /// <param name="path">要写入 Markdown 文件的路径。</param>
    /// <param name="markdown">要写入为 Markdown 的 Plot 对象。</param>
    public static void WriteMd(string path, Plot markdown)
    {
        var mdOutPath = Path.Combine(path, markdown.Title + ".md");
        File.WriteAllText(mdOutPath, markdown.Content.ToString());
    }

    /// <summary>
    /// 将 Plot 对象的 HTML 内容写入文件。
    /// </summary>
    /// <param name="path">保存 HTML 文件的路径。</param>
    /// <param name="markdown">包含 markdown 内容的 Plot 对象。</param>
    public static void WriteHtml(string path, Plot markdown)
    {
        var htmlPath = Path.Combine(path, markdown.Title + ".html");
        var htmlContent = GetHtmlContent(markdown);
        var result = FormatHtmlBody(htmlContent, markdown.Title);
        File.WriteAllText(htmlPath, result);
    }

    /// <summary>
    /// 将 html 文本中的链接替换为本地相对地址。
    /// </summary>
    /// <param name="path">html 文件路径。</param>
    /// <param name="markdown">要转换为HTML的Plot对象。</param>
    public static void WriteHtmlWithLocalRes(string path, Plot markdown)
    {
        var htmlPath = Path.Combine(path, markdown.Title + ".html");
        var htmlContent = GetHtmlContent(markdown);
        var htmlWithLocalRes = htmlContent.Replace("https://", "");
        var result = FormatHtmlBody(htmlWithLocalRes, markdown.Title);
        File.WriteAllText(htmlPath, result);
    }

    /// <summary>
    /// 将 Plot 对象的内容转换为使用 Markdown 语法的 HTML。
    /// </summary>
    /// <param name="markdown">包含 Markdown 内容的 Plot 对象。</param>
    /// <returns>Markdown 内容的 HTML 表示。</returns>
    private static string GetHtmlContent(Plot markdown)
    {
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        new MarkdownPipelineBuilder()
            .UseAdvancedExtensions() // Add most of all advanced extensions
            .Build();
        return Markdown.ToHtml(markdown.Content.ToString(), pipeline);
    }

    /// <summary>
    /// 格式化HTML正文，添加必要的HTML标签、头部和尾部。
    /// </summary>
    /// <param name="body">要格式化的正文内容。</param>
    /// <param name="title">HTML文档的标题。</param>
    /// <returns>格式化后的HTML内容。</returns>
    private static string FormatHtmlBody(string body, string title)
    {
        body = $"<body>{body}</body>";
        title = $"<title>{title}</title>";
        // 读取头部和尾部
        var head = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets/head.html"));
        head = $"<head>{head}{title}</head>";
        var html = $"<html>{head}{body}</html>";
        html = "<!doctype html>" + html;
        var tail = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets/tail.html"));
        html += tail;
        return html;
    }

    public static void WriteTyp(string outputPath, AkpStoryLoader contentLoader)
    {
        List<PlotManager> plotList = contentLoader.ContentTable;
        var typFolder = outputPath;
        // 把模板复制过来
        string templateFolder = Path.Join(Directory.GetCurrentDirectory(), "typst-template");
        CopyDirectory(templateFolder, typFolder);

        int fileIndex = 1;
        foreach (var plot in plotList)
        {
            var result = "#import \"./template.typ\": arknights_sim, arknights_sim_2p\n";
            var content = string.Join(
                "\n",
                plot.CurrentPlot.TextVariants.Select(x => x.TypText).ToList()
            );
            result += content;
            var currentTyp = Path.Join(typFolder, $"{fileIndex}_{plot.CurrentPlot.Title}.typ");
            File.WriteAllText(currentTyp, result);
            fileIndex++;
        }
    }

    /// <summary>
    /// 传播 CharacterCode：从 charslot/character 条目传播到后续同名对话条目，
    /// 并将 charslot 条目的 PicFacts 复制给对话条目，确保 PromptRenderer 能输出 portrait-facts。
    /// </summary>
    private static void PropagateCharacterCodeAndFacts(IList<FormattedTextEntry> entries)
    {
        var nameToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var codeToFacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? pendingCode = null;
        string? pendingFacts = null;

        foreach (var entry in entries)
        {
            // charslot/character 条目：记录 CharacterCode 和 PicFacts
            if (entry.Type is "character" or "charactercutin" or "charslot"
                && !string.IsNullOrEmpty(entry.CharacterCode))
            {
                pendingCode = entry.CharacterCode;
                if (!string.IsNullOrEmpty(entry.PicFacts))
                    pendingFacts = entry.PicFacts;
            }
            // 对话条目：首次出现的 CharacterName 分配 CharacterCode 和 PicFacts
            else if (!string.IsNullOrEmpty(entry.CharacterName) && string.IsNullOrEmpty(entry.CharacterCode))
            {
                if (nameToCode.TryGetValue(entry.CharacterName, out var knownCode))
                {
                    entry.CharacterCode = knownCode;
                    if (string.IsNullOrEmpty(entry.PicFacts)
                        && codeToFacts.TryGetValue(knownCode, out var knownFacts))
                    {
                        entry.PicFacts = knownFacts;
                    }
                }
                else if (pendingCode != null)
                {
                    nameToCode[entry.CharacterName] = pendingCode;
                    entry.CharacterCode = pendingCode;
                    if (string.IsNullOrEmpty(entry.PicFacts) && pendingFacts != null)
                    {
                        codeToFacts[pendingCode] = pendingFacts;
                        entry.PicFacts = pendingFacts;
                    }
                    pendingCode = null;
                    pendingFacts = null;
                }
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        // Ensure the source directory exists
        if (!Directory.Exists(sourceDir))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
        }

        // Create all directories in the destination
        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(sourceDir, destinationDir));
        }

        // Copy all files to the destination
        foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
        {
            string destFile = file.Replace(sourceDir, destinationDir);
            File.Copy(file, destFile, true);
        }
    }
}
