using ArkPlot.Core.Model;
using ArkPlot.Core.Services;
using ArkPlot.Core.Utilities;
using ArkPlot.Core.Utilities.WorkFlow;

namespace ArkPlot.Cli.Pipeline;

/// <summary>
/// Step 8: 导出 Markdown 文件。
/// </summary>
public static class MarkdownExporter
{
    public static async Task<(string OutputDir, Plot Markdown)> ExportAsync(
        AkpStoryLoader storyLoader, PicDescService? picDescService, string actName)
    {
        Console.WriteLine("[8/8] 正在导出 Markdown...");
        var mdContent = await AkpProcessor.ExportPlotsAsync(storyLoader.ContentTable, picDescService, CliOptions.EnablePicDesc);
        var mdWithTitle = $"# {actName}\n\n{mdContent}";
        var markdown = new Plot(actName, new System.Text.StringBuilder(mdWithTitle));

        var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cli_output");
        Directory.CreateDirectory(outputDir);
        AkpProcessor.WriteMd(outputDir, markdown);

        return (outputDir, markdown);
    }
}
