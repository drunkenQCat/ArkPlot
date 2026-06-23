using SqlSugar;

namespace ArkPlot.Core.Model;

[SugarTable("PicDescriptions")]
[SugarIndex("uk_picdesc", nameof(DedupKey), OrderByType.Asc, isUnique: true)]
public class PicDescription
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnDataType = "INTEGER")]
    public long Id { get; set; }

    /// <summary>
    /// 去重键：立绘用 CharacterCode，场景/背景用 ImageUrl 自身
    /// </summary>
    [SugarColumn(ColumnDataType = "TEXT", IsNullable = false)]
    public string DedupKey { get; set; } = string.Empty;

    /// <summary>
    /// 实际调用视觉模型时用的图片 URL
    /// </summary>
    [SugarColumn(ColumnDataType = "TEXT", IsNullable = false)]
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>
    /// 图片描述文本
    /// </summary>
    [SugarColumn(ColumnDataType = "TEXT", IsNullable = false)]
    public string PicDesc { get; set; } = string.Empty;

    /// <summary>
    /// 结构化视觉事实（YAML-like），由散文描述提取而来，供 Novelizer Prompt 模式使用
    /// </summary>
    [SugarColumn(ColumnDataType = "TEXT", IsNullable = true)]
    public string? PicFacts { get; set; }

    /// <summary>
    /// 描述来源：Vision / Placeholder / Error
    /// </summary>
    [SugarColumn(Length = 20, IsNullable = false)]
    public string Source { get; set; } = "Vision";

    [SugarColumn(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [SugarColumn(IsNullable = false)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
