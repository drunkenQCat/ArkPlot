namespace ArkPlot.Tts;

/// <summary>
/// MiniMax 中文音色池（从 332 个系统音色中精选 30 个）。
/// 按性别分组，旁白音色独占不参与角色分配。
/// </summary>
public static class MiniMaxVoicePool
{
    /// <summary>
    /// 音色元数据。
    /// </summary>
    public record VoiceEntry(string VoiceId, string Gender, string Label);

    // ========== 男声池（13 个） ==========

    /// <summary>男声音色池。</summary>
    public static readonly string[] Male =
    [
        // ---- 青年/少年 ----
        "Chinese (Mandarin)_Gentleman",             // 磁性绅士：魅力、磁性嗓音
        "Chinese (Mandarin)_Unrestrained_Young_Man", // 不羁青年：自由奔放
        "Chinese (Mandarin)_Gentle_Youth",          // 温柔青年：温和内敛
        "Chinese (Mandarin)_Straightforward_Boy",   // 直率少年：深思直率
        "Chinese (Mandarin)_Pure-hearted_Boy",      // 纯真少年：坚定，心地单纯
        "Chinese (Mandarin)_Stubborn_Friend",       // 倔强朋友：不羁、固执

        // ---- 成年/中年 ----
        "Chinese (Mandarin)_Reliable_Executive",    // 可靠高管：沉稳可信
        "Chinese (Mandarin)_Sincere_Adult",         // 真诚成人：真诚鼓励
        "Chinese (Mandarin)_Male_Announcer",        // 男播音员：磁性，清晰权威
        "Chinese (Mandarin)_Radio_Host",            // 电台主持人：诗意，流畅

        // ---- 老年/特殊 ----
        "Chinese (Mandarin)_Humorous_Elder",        // 幽默老者：北方口音
        "Chinese (Mandarin)_Lyrical_Voice",         // 醇厚男声：流畅，表现力丰富
        "Chinese (Mandarin)_Southern_Young_Man",    // 南方青年：诚恳，南方口音
    ];

    // ========== 女声池（15 个） ==========

    /// <summary>女声音色池。</summary>
    public static readonly string[] Female =
    [
        // ---- 成熟/御姐 ----
        "Chinese (Mandarin)_Mature_Woman",          // 熟女：迷人，成熟
        "Arrogant_Miss",                             // 傲慢小姐：自信，优越
        "Chinese (Mandarin)_Wise_Women",            // 睿智女性：抒情，智慧
        "Chinese (Mandarin)_IntellectualGirl",       // 知性女孩：清晰，知识渊博

        // ---- 温柔/暖系 ----
        "Chinese (Mandarin)_Warm_Bestie",           // 温暖闺蜜：友好，清晰
        "Chinese (Mandarin)_Sweet_Lady",            // 甜美女士：温柔，甜美
        "Chinese (Mandarin)_Warm_Girl",             // 温暖女孩：柔和，温暖
        "Chinese (Mandarin)_Gentle_Senior",         // 温柔学姐：温柔，舒适
        "Chinese (Mandarin)_Soft_Girl",             // 温柔女孩：南方口音，柔和

        // ---- 活泼/可爱 ----
        "Chinese (Mandarin)_Cute_Spirit",           // 可爱精灵：甜美，年轻
        "Chinese (Mandarin)_Crisp_Girl",            // 清脆女孩：温暖，清脆
        "Chinese (Mandarin)_ExplorativeGirl",        // 探索女孩：好奇心，活力

        // ---- 年长/长辈 ----
        "Chinese (Mandarin)_Kind-hearted_Antie",    // 善良阿姨：温柔，关怀
        "Chinese (Mandarin)_Kind-hearted_Elder",     // 善良长者：温柔，年长

        // ---- 特殊风格 ----
        "Chinese (Mandarin)_Laid_BackGirl",          // 悠闲女孩：放松，慵懒
        "Chinese (Mandarin)_BashfulGirl",            // 害羞女孩：腼腆，羞涩
    ];

    // ========== 特殊音色（不参与性别池自动分配） ==========

    /// <summary>特殊音色（机甲/南方口音等）。</summary>
    public static readonly string[] Special =
    [
        "Robot_Armor",                               // 机甲电子声：科幻/机器人/非人角色
        "Chinese (Mandarin)_HK_Flight_Attendant",    // 香港空姐：南方口音，礼貌清晰
    ];

    /// <summary>旁白专用音色（不参与角色分配）。</summary>
    public const string Narrator = "Chinese (Mandarin)_News_Anchor"; // 专业播音腔

    /// <summary>所有女声音色名。</summary>
    public static readonly HashSet<string> FemaleVoiceNames = new(Female);

    /// <summary>所有男声音色名。</summary>
    public static readonly HashSet<string> MaleVoiceNames = new(Male);

    /// <summary>所有音色（含特殊）的完整列表。</summary>
    public static readonly VoiceEntry[] AllVoices =
    [
        ..Male.Select(v => new VoiceEntry(v, "Male", ExtractLabel(v))),
        ..Female.Select(v => new VoiceEntry(v, "Female", ExtractLabel(v))),
        ..Special.Select(v => new VoiceEntry(v, "Special", ExtractLabel(v))),
        new VoiceEntry(Narrator, "Female", "新闻主播（旁白）"),
    ];

    /// <summary>判断一个音色是否为女声。</summary>
    public static bool IsFemaleVoice(string voice) =>
        FemaleVoiceNames.Contains(voice) || voice == Narrator;

    /// <summary>判断一个音色是否为男声。</summary>
    public static bool IsMaleVoice(string voice) =>
        MaleVoiceNames.Contains(voice);

    private static string ExtractLabel(string voiceId)
    {
        // 从 "Chinese (Mandarin)_Gentleman" 提取 "Gentleman"
        var idx = voiceId.LastIndexOf('_');
        return idx >= 0 ? voiceId[(idx + 1)..] : voiceId;
    }
}