namespace ArkPlot.Tts.Alignment;

/// <summary>
/// 对齐诊断结果：追踪一个小说片段在各 Phase 中的匹配过程。
/// </summary>
public class AlignmentDiagnostic
{
    public string TargetText { get; init; } = "";
    public string ChapterTitle { get; init; } = "";
    public string NovelFilePath { get; init; } = "";

    /// <summary>小说片段在 dialogs 列表中的索引（dialogs 里的序号）</summary>
    public int NovelDialogIdx { get; init; }

    /// <summary>最终对齐到的 DB EntryIndex（-1 = 未对齐）</summary>
    public int FinalEntryIndex { get; set; } = -1;

    /// <summary>匹配到的 DB 条目的原始 Index 字段（用于验证）</summary>
    public int MatchedDbEntryIndex { get; set; } = -1;

    /// <summary>是否由 Phase 3.1 全局回退匹配</summary>
    public bool Phase31Matched { get; set; }

    /// <summary>是否由 Phase 3.5 Check 4 (长文本拆分匹配) 修复</summary>
    public bool Phase35Check4Matched { get; set; }

    /// <summary>是否由 Phase 3.5 Check 5 (短文本专用匹配) 修复</summary>
    public bool Phase35Check5Matched { get; set; }

    // ── 各 Phase 的诊断信息 ──

    public Phase1Diag? Phase1 { get; set; }
    public Phase3Diag? Phase3 { get; set; }
    public Phase35Diag? Phase35 { get; set; }
}

public class Phase1Diag
{
    public bool Matched { get; set; }
    public int MatchedDbIdx { get; set; } = -1;
    public string? SkipReason { get; set; }
}

public class Phase3Diag
{
    /// <summary>该片段所在的锚点间隙</summary>
    public int GapIndex { get; set; }
    public int PrevAnchorNi { get; set; }
    public int PrevAnchorDi { get; set; }
    public int NextAnchorNi { get; set; }
    public int NextAnchorDi { get; set; }

    /// <summary>在间隙内的位置（i）</summary>
    public int GapPosition { get; set; }
    public int NGap { get; set; }
    public int DGap { get; set; }

    /// <summary>预期 DB 位置</summary>
    public int ExpectedPos { get; set; }

    /// <summary>搜索范围 [searchStart, searchEnd]</summary>
    public int SearchStart { get; set; }
    public int SearchEnd { get; set; }

    /// <summary>窗口内的所有候选及其分数</summary>
    public List<CandidateScore> Candidates { get; set; } = new();

    /// <summary>是否窗口外存在更好的匹配</summary>
    public List<CandidateScore> OutOfWindowCandidates { get; set; } = new();

    public bool Matched { get; set; }
    public int MatchedDbIdx { get; set; } = -1;
    public double MatchedScore { get; set; }

    /// <summary>跳过原因（如间隙为空）</summary>
    public string? SkipReason { get; set; }
}

public class CandidateScore
{
    public int DbIdx { get; set; }
    public string DbText { get; set; } = "";
    public double Score { get; set; }
    public bool IsSelected { get; set; }
}

public class Phase35Diag
{
    public bool WasAligned { get; set; }

    public Check1Diag? Check1 { get; set; }
    public Check2Diag? Check2 { get; set; }
    public Check3Diag? Check3 { get; set; }

    public bool Fixed { get; set; }
    public int FixedToDbIdx { get; set; } = -1;
}

public class Check1Diag
{
    public int PrevDbIdx { get; set; }
    public string PrevDbText { get; set; } = "";
    public bool IsSubstring { get; set; }
    public string? SkipReason { get; set; }
}

public class Check2Diag
{
    public int NextDbIdx { get; set; }
    public string NextDbText { get; set; } = "";
    public bool IsSubstring { get; set; }
    public string? SkipReason { get; set; }
}

public class Check3Diag
{
    public string MergedText { get; set; } = "";
    public bool Matched { get; set; }
    public int MatchedDbIdx { get; set; } = -1;
    public string? SkipReason { get; set; }
}