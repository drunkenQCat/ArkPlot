using SqlSugar;

namespace ArkPlot.Core.Model;

/// <summary>
/// 表示格式化文本条目，包含原始文本及其转换后的多种格式
/// </summary>
[SugarTable("FormattedTextEntry")]
public class FormattedTextEntry
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnDataType = "INTEGER")]
    public long Id { get; set; }

    /// <summary>
    /// 关联的章节 ID（Plot 表主键）
    /// </summary>
    [SugarColumn(ColumnDataType = "INTEGER", IsNullable = false)]
    public long PlotId { get; set; }

    /// <summary>
    /// 导航到所属的 Plot（多对一）
    /// </summary>
    [Navigate(NavigateType.ManyToOne, nameof(PlotId))]
    public Plot? Plot { get; set; }

    /// <summary>
    /// 文本行的索引号
    /// </summary>
    [SugarColumn(ColumnDataType = "INTEGER")]
    public int Index { get; set; }
    /// <summary>
    /// 原始文本内容
    /// </summary>
    [SugarColumn(Length = 1000)]
    public string OriginalText { get; set; } = "";

    [SugarColumn(Length = 1000)]
    public string MdText { get; set; } = "";

    public int MdDuplicateCounter { get; set; }

    [SugarColumn(Length = 1000)]
    public string TypText { get; set; } = "";

    [SugarColumn(Length = 50)]
    public string Type { get; set; } = "";

    /// <summary>
    /// 解析后的命令参数表，SqlSugar 自动序列化为 TEXT JSON
    /// </summary>
    [SugarColumn(IsJson = true, ColumnDataType = "TEXT")]
    public StringDict CommandSet { get; set; } = new();

    public bool IsTagOnly { get; set; }

    [SugarColumn(Length = 100)]
    public string CharacterName { get; set; } = "";

    /// <summary>
    /// 归一化角色代码，如 "char_220_grani"。
    /// 同一角色的不同表情变体共享同一个 CharacterCode。
    /// </summary>
    [SugarColumn(Length = 100, IsNullable = true)]
    public string? CharacterCode { get; set; }

    [SugarColumn(Length = 1000)]
    public string Dialog { get; set; } = "";

    public int PngIndex { get; set; }

    /// <summary>
    /// 资源 URL 列表，SqlSugar 自动序列化为 TEXT JSON
    /// </summary>
    [SugarColumn(IsJson = true, ColumnDataType = "TEXT")]
    public List<string> ResourceUrls { get; set; } = new();

    /// <summary>
    /// 立绘图片 URL 列表，SqlSugar 自动序列化为 TEXT JSON
    /// </summary>
    [SugarColumn(IsJson = true, ColumnDataType = "TEXT")]
    public List<string> Portraits { get; set; } = new();

    /// <summary>
    /// 立绘焦点 / 布局模式：
    /// -1 = 无，0 = 单人居中，1 = 双人左，2 = 三人右
    /// </summary>
    public int PortraitFocus { get; set; }

    [SugarColumn(Length = 500)]
    public string Bg { get; set; } = "";

    /// <summary>
    /// 图片描述（PicDesc）。
    /// </summary>
    [SugarColumn(ColumnDataType = "TEXT")]
    public string PicDesc { get; set; } = "";

    /// <summary>
    /// 结构化视觉事实（YAML-like），供 Prompt 模式使用。
    /// 运行时从 PicDescription.PicFacts 填充，不持久化到 FormattedTextEntry 表。
    /// </summary>
    [SugarColumn(IsIgnore = true)]
    public string PicFacts { get; set; } = "";

    /// <summary>
    /// 瞬态标记：此条目不应生成立绘 MdText（如 charslot focus="none"）。
    /// 由 PrtsPreloader 设置，AkpParser 读取。不持久化。
    /// </summary>
    [SugarColumn(IsIgnore = true)]
    public bool SkipPortraitOutput { get; set; }

    /// <summary>
    /// 复制构造函数
    /// </summary>
    public FormattedTextEntry(FormattedTextEntry entry)
    {
        Index = entry.Index;
        OriginalText = entry.OriginalText;
        MdText = entry.MdText;
        MdDuplicateCounter = entry.MdDuplicateCounter;
        TypText = entry.TypText;
        Type = entry.Type;
        CommandSet = new(entry.CommandSet);
        IsTagOnly = entry.IsTagOnly;
        ResourceUrls = new(entry.ResourceUrls);
        CharacterName = entry.CharacterName;
        CharacterCode = entry.CharacterCode;
        Dialog = entry.Dialog;
        Bg = entry.Bg;
        Portraits = new(entry.Portraits);
        PortraitFocus = entry.PortraitFocus;
        PicDesc = entry.PicDesc;
        PicFacts = entry.PicFacts;
        SkipPortraitOutput = entry.SkipPortraitOutput;
    }

    /// <summary>
    /// 默认构造函数
    /// </summary>
    public FormattedTextEntry()
    {
    }

    /// <summary>
    /// 验证数据完整性
    /// </summary>
    public bool Validate()
    {
        if (string.IsNullOrEmpty(OriginalText) && string.IsNullOrEmpty(MdText) && string.IsNullOrEmpty(TypText))
            return false;
        if (Index < 0) return false;
        if (MdDuplicateCounter < 0) return false;
        if (PngIndex < 0) return false;
        return true;
    }
}
