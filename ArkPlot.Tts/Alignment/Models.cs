namespace ArkPlot.Tts.Alignment;

/// <summary>
/// 小说文本中的一个片段（旁白或对话）。
/// </summary>
/// <param name="Text">文本内容（不含引号）</param>
/// <param name="Paragraph">所属自然段</param>
/// <param name="IsDialog">true=引号内对话，false=旁白/叙述</param>
public record NovelSegment(string Text, int Paragraph, bool IsDialog)
{
    /// <summary>对齐后填入：对应 FormattedTextEntry 的角色名（旁白为 null）</summary>
    public string? CharacterName { get; set; }

    /// <summary>对齐后填入：对应 FormattedTextEntry 的角色 code（旁白为 null）</summary>
    public string? CharacterCode { get; set; }

    /// <summary>对齐后填入：对应 FormattedTextEntry.Index（旁白为 -1）</summary>
    public int EntryIndex { get; set; } = -1;
}

/// <summary>
/// 小说文本中的一个章节（对应原始 Plot）。
/// </summary>
/// <param name="Title">章节标题（## 后面的文本）</param>
/// <param name="Segments">该章节内的旁白/对话片段列表</param>
public record NovelChapter(string Title, List<NovelSegment> Segments)
{
    /// <summary>该章节内所有 IsDialog=true 的片段（方便对齐使用）</summary>
    public IEnumerable<NovelSegment> Dialogs => Segments.Where(s => s.IsDialog);
}

/// <summary>
/// 对齐结果：一个小说片段与对应 FormattedTextEntry 的映射。
/// </summary>
public record AlignmentEntry(
    string NovelText,
    bool IsDialog,
    string? CharacterName,
    string? CharacterCode,
    int EntryIndex,
    string ChapterTitle,
    string? Gender = null,
    List<string>? Portraits = null
);

/// <summary>
/// 对齐统计信息。
/// </summary>
public record AlignmentStats(
    int TotalNovelChapters,
    int MatchedChapters,
    int TotalDialogs,
    int AlignedDialogs,
    int UnalignedDialogs,
    int AnchorMatches = 0,
    int WindowMatches = 0
);

/// <summary>
/// 同段落对话合并后的搜索单元。Phase 1-3 基于此进行匹配，
/// 匹配成功后展开回原始 dialogs 索引。
/// </summary>
public record SearchUnit(
    string MergedText,
    int Paragraph,
    List<int> DialogIndices
);
