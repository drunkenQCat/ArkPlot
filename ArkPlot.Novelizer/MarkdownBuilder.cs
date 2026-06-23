using System.Text.RegularExpressions;
using ArkPlot.Core.Model;

namespace ArkPlot.Novelizer;

/// <summary>
/// 从 FormattedTextEntry 数组或原始 .md 文本构建适合 LLM 消费的纯文本
/// </summary>
public static partial class MarkdownBuilder
{
    /// <summary>
    /// 预处理原始 .md 文件内容。
    /// 1. 将 Readable 模式的视觉容器统一归一化为 &lt;aside&gt; 格式
    /// 2. 去除其余 HTML 标签、表格分隔线
    /// 3. 清理指令前缀和多余空行
    /// </summary>
    public static string PreprocessMdContent(string rawMd)
    {
        var text = rawMd;

        // 1. 将 Readable 模式的 scene-desc 转换为 aside scene-facts
        text = SceneDescToAsideRegex().Replace(text, m =>
        {
            var bgName = m.Groups[1].Value;
            var descContent = m.Groups[2].Value;
            // 去掉指令前缀
            descContent = SceneDescPrefixRegex().Replace(descContent, "");
            return $"<aside class=\"scene-facts\" data-bg=\"{bgName}\">\n{descContent.Trim()}\n</aside>";
        });

        // 2. 将 Readable 模式的 portrait-table 转换为多个 aside portrait-facts
        text = PortraitTableRegex().Replace(text, ConvertPortraitTable);

        // 3. 去掉 image/audio 的 src URL（保留其余文本）
        text = HttpsSrcQuotedUrlRegex().Replace(text, "$1$2");
        text = HttpsSrcUnquotedUrlRegex().Replace(text, "$1");

        // 4. 去除剩余 HTML 标签，但保留 <aside> 和 </aside>
        text = NonAsideHtmlTagRegex().Replace(text, "");

        // 5. 清理 portrait-facts 中残留的指令前缀
        text = PortraitDescPrefixRegex().Replace(text, "");

        // 6. 去掉表格分隔线（|---|---|之类）
        text = TableSeparatorRegex().Replace(text, "");

        // 7. 清理多余空行
        text = MultiNewlineRegex().Replace(text, "\n");

        return text.Trim();
    }

    /// <summary>
    /// 将 portrait-table HTML 表格转换为多个 aside portrait-facts 块
    /// </summary>
    private static string ConvertPortraitTable(Match match)
    {
        var tableContent = match.Value;
        var results = new List<string>();

        // 提取每个 <td> 中的描述内容
        foreach (Match tdMatch in TdContentRegex().Matches(tableContent))
        {
            var content = tdMatch.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(content)) continue;

            // 跳过纯角色名（不含描述前缀的短文本）
            if (!PortraitDescPrefixRegex().IsMatch(content) && content.Length < 20)
                continue;

            // 提取角色名
            var charName = "";
            var prefixMatch = PortraitDescPrefixRegex().Match(content);
            if (prefixMatch.Success)
            {
                charName = prefixMatch.Groups[1].Value;
                content = PortraitDescPrefixRegex().Replace(content, "");
            }

            // 清理 <br> 标签
            content = content.Replace("<br>", "\n");

            if (!string.IsNullOrEmpty(charName))
                results.Add($"<aside class=\"portrait-facts\" data-character=\"{charName}\">\n{content.Trim()}\n</aside>");
        }

        return string.Join("\n\n", results);
    }

    /// <summary>
    /// 拼接所有条目的 MdText，去除 HTML 标签和纯标记行，保留对话和舞台指示
    /// </summary>
    public static string BuildNovelInput(IEnumerable<FormattedTextEntry> entries)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var entry in entries)
        {
            var text = entry.MdText;
            if (string.IsNullOrWhiteSpace(text)) continue;

            // 跳过分隔线
            if (text.Trim() == "---") continue;

            text = HttpsSrcQuotedUrlRegex().Replace(text, "$1$2");
            text = HttpsSrcUnquotedUrlRegex().Replace(text, "$1");
            text = HtmlTagRegex().Replace(text, "");

            // 清理多余空行
            text = MultiNewlineRegex().Replace(text, "\n");

            if (string.IsNullOrWhiteSpace(text)) continue;

            sb.AppendLine(text.Trim());
        }

        var result = sb.ToString().Trim();

        // 如果没有任何有效内容，使用 dialog 字段兜底
        if (string.IsNullOrWhiteSpace(result))
        {
            foreach (var entry in entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.CharacterName) && !string.IsNullOrWhiteSpace(entry.Dialog))
                {
                    sb.AppendLine($"**{entry.CharacterName}**：{entry.Dialog}");
                }
                else if (!string.IsNullOrWhiteSpace(entry.Dialog))
                {
                    sb.AppendLine(entry.Dialog);
                }
            }
            result = sb.ToString().Trim();
        }

        return result;
    }

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("(?i)(src\\s*=\\s*[\"'])https://[^\"']+([\"'])")]
    private static partial Regex HttpsSrcQuotedUrlRegex();

    [GeneratedRegex("(?i)(src\\s*=\\s*)https://[^\\s>]+")]
    private static partial Regex HttpsSrcUnquotedUrlRegex();

    [GeneratedRegex(@"^\|?[-:|\s]+\|?$", RegexOptions.Multiline)]
    private static partial Regex TableSeparatorRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultiNewlineRegex();

    /// <summary>
    /// 匹配 &lt;p class="scene-desc"&gt;...内容...&lt;/p&gt;，捕获背景名和描述内容
    /// </summary>
    [GeneratedRegex(@"<p\s+class=""scene-desc"">【此处为对场景图片([^】]*)的描述[^】]*】([\s\S]*?)</p>")]
    private static partial Regex SceneDescToAsideRegex();

    /// <summary>
    /// 匹配场景描述的指令前缀
    /// </summary>
    [GeneratedRegex(@"【此处为对[^】]*的描述，请结合上下文将其融入文中】[：:]\s*")]
    private static partial Regex SceneDescPrefixRegex();

    /// <summary>
    /// 匹配 portrait-table 整个表格块
    /// </summary>
    [GeneratedRegex(@"<table\s+class=""portrait-table"">[\s\S]*?</table>")]
    private static partial Regex PortraitTableRegex();

    /// <summary>
    /// 匹配 &lt;td&gt; 中的内容
    /// </summary>
    [GeneratedRegex(@"<td>([\s\S]*?)</td>")]
    private static partial Regex TdContentRegex();

    /// <summary>
    /// 匹配立绘描述的指令前缀，捕获角色名
    /// </summary>
    [GeneratedRegex(@"【此处为对([^】]*)的形象描述，请结合上下文将其融入文中，不要生搬硬套】[：:]\s*")]
    private static partial Regex PortraitDescPrefixRegex();

    /// <summary>
    /// 匹配非 aside 的 HTML 标签（保留 aside 和 /aside）
    /// </summary>
    [GeneratedRegex(@"<(?!/?aside\b)[^>]+>")]
    private static partial Regex NonAsideHtmlTagRegex();
}
