using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ArkPlot.Avalonia.ViewModels;

/// <summary>
/// 把 ChapterProcessor 拼接的完整 prompt（以「—— system ——」「—— user ——」等分节标记分割）
/// 拆分为 system 段与其余段（user / assistant 历史），供复盘面板「输入」页分开展示。
/// 每个分节内容单独收集并 Trim（去掉标记行前后的空行），节间以空行连接。
/// </summary>
public static class TracePromptSplitter
{
    /// <summary>分节标记：行首尾为「——」，中间为角色名。</summary>
    private const string Marker = "——";

    /// <returns>system 节（多条合并）与其余节（多条合并）。</returns>
    public static (string System, string User) Split(string prompt)
    {
        var sections = new List<(string Role, string Text)>();
        var role = "user";
        var buffer = new StringBuilder();

        foreach (var rawLine in prompt.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith(Marker, StringComparison.Ordinal) &&
                line.EndsWith(Marker, StringComparison.Ordinal))
            {
                FlushSection(sections, role, buffer);
                role = line.Trim('—').Trim();
                buffer = new StringBuilder();
                continue; // 分节标记行本身不进入内容
            }
            AppendLine(buffer, rawLine);
        }
        FlushSection(sections, role, buffer);

        var systems = Join(sections.Where(s => s.Role.Equals("system", StringComparison.OrdinalIgnoreCase)));
        var others = Join(sections.Where(s => !s.Role.Equals("system", StringComparison.OrdinalIgnoreCase)));
        return (systems, others);
    }

    /// <summary>把当前缓冲收集为一个小节（Trim 掉首尾空行）。</summary>
    private static void FlushSection(List<(string Role, string Text)> sections, string role, StringBuilder buffer)
    {
        var text = buffer.ToString().Trim();
        if (text.Length > 0) sections.Add((role, text));
        buffer.Clear();
    }

    /// <summary>字符串列表以空行（\n\n）连接。</summary>
    private static string Join(IEnumerable<(string Role, string Text)> sections)
        => string.Join("\n\n", sections.Select(s => s.Text));

    private static void AppendLine(StringBuilder sb, string line)
    {
        if (sb.Length > 0) sb.Append('\n');
        sb.Append(line);
    }
}