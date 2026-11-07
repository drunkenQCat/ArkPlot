namespace ArkPlot.Novelizer;

/// <summary>Artifacts and failures from this invocation, excluding unrelated historical output.</summary>
public sealed class NovelBatchResult
{
    public List<string> NovelFiles { get; } = new();
    public List<string> EpubFiles { get; } = new();
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public int CachedCount { get; set; }
}
