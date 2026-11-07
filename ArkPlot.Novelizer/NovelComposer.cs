namespace ArkPlot.Novelizer;

/// <summary>
/// 小说组装器：负责将章节结果合并并写入文件
/// </summary>
public static class NovelComposer
{
    /// <summary>
    /// 根据源 MD 路径和模型名，生成小说输出文件路径
    /// </summary>
    public static string GetNovelPath(string sourceMdPath, string model)
    {
        return ChapterCache.GetNovelPath(sourceMdPath, model);
    }

    /// <summary>
    /// 将章节结果按顺序组装并写入小说文件
    /// </summary>
    public static string ComposeAndWrite(
        IReadOnlyList<ChapterResult> results,
        string sourceMdPath,
        string model,
        Action<string> log,
        string? documentTitle = null)
    {
        var novelPath = GetNovelPath(sourceMdPath, model);
        var allParts = results.Select(r => r.Content).ToList();

        var content = string.Join("\n\n", allParts);
        if (!string.IsNullOrWhiteSpace(documentTitle)) content = $"# {documentTitle}\n\n{content}";
        File.WriteAllText(novelPath, content);
        log($"[DIAG] 写入完成: {novelPath}");

        return novelPath;
    }
}
