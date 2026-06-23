namespace ArkPlot.Core.Utilities.WorkFlow.StoryDocument;

/// <summary>
/// StoryDocument 的输出模式。
/// </summary>
public enum OutputMode
{
    /// <summary>可读模式：HTML 表格 + 散文描述，面向人类阅读。</summary>
    Readable,

    /// <summary>Prompt 特调模式：aside + YAML 事实，面向 LLM 输入。</summary>
    PromptOptimized
}