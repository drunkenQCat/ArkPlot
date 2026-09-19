using ArkPlot.Arknights;
using ArkPlot.Core.Model;

namespace ArkPlot.Novelizer.Tests;

/// <summary>
/// 孤星 CW-ST-1 受控样例：代码构造、完全自包含的代表性输入集。
/// 复现真实章节的关键形态（见 ArkPlot.Avalonia bin\Debug\arkplot.db 的 CW-ST-1 原始行，
/// 此处已按 AkpProcessor 处理后的状态构造：CharacterCode 已传播、PicFacts 已填充、
/// 「？？？」已归一化为「神秘人士（实际人物：X）」）。
///
/// 之所以不用「DB 行序列全量固化」：826 行 × 多字段会生成数百 KB 手维护基线，
/// 换来的只是 reintroduce 一个同样会漂移的文件；而结构断言的关键是输入形态准确，
/// 代码构造能精确控制每个机制（立绘内联 / 跨名揭示 / aside 分类 / 分组）的触发点。
/// </summary>
internal static class LoneTrailControlledInput
{
    public const string ChapterTitle = "CW-ST-1 阴云密布 幕间";

    // 角色 code（AkpProcessor 传播后的形态；真实数据里来自 charslot 的 CommandSet name 参数）
    public const string PrisonChiefCode = "avg_npc_134";   // 监狱负责人
    public const string StrangerCode = "avg_npc_892_1";    // 精英打扮的男性 → 小贾斯汀（跨名揭示）
    public const string MlyssCode = "avg_249_mlyss_1";     // 缪尔赛思
    public const string LorenzCode = "avg_npc_890_1";      // 神秘人士（实际人物：洛伦茨）

    // 立绘 URL（真实 prts wiki 形态）
    public const string PrisonChiefUrl = "https://media.prts.wiki/1/15/Avg_avg_npc_134.png";
    public const string StrangerUrl = "https://media.prts.wiki/8/82/Avg_avg_npc_892_1-2$1.png";
    public const string MlyssUrl = "https://media.prts.wiki/b/b6/Avg_avg_249_mlyss_1-11$1.png";
    public const string LorenzUrl = "https://media.prts.wiki/c/c4/Avg_avg_npc_890_1-4$1.png";

    // PicFacts：真实 Vision 输出形态（YAML-like 结构）
    public const string PrisonChiefFacts =
        "lighting: [阴影, 斜射, 昏暗]\n" +
        "materials: [金属, 皮革, 铜]\n" +
        "objects: [铁栅栏, 头盔, 护颈]";

    public const string StrangerFacts =
        "clothing: [米色风衣, 白衬衫, 芥末黄马甲]\n" +
        "equipment: [工牌, 纸币]\n" +
        "features: [眼镜, 微垂眼]";

    public const string MlyssFacts =
        "hair: [银灰, 长发, 垂落]\n" +
        "clothing: [风衣, 长裙, 深褐]\n" +
        "equipment: [机械弩, 工具包, 绿绳]";

    public const string LorenzFacts =
        "lighting: [均匀, 明亮, 中性色]\n" +
        "objects: [猫耳, 通讯器]\n" +
        "colors: [白色, 米白, 棕褐]";

    public const string PrisonCorridorSceneFacts =
        "lighting: [昏暗, 荧光灯, 冷色]\n" +
        "space: [走廊, 封闭, 狭长]\n" +
        "objects: [铁门, 监视器]";

    public const string DesertSceneFacts =
        "lighting: [烈日, 强光, 暖色]\n" +
        "space: [荒漠, 开阔, 无垠]\n" +
        "objects: [沙丘, 残骸]";

    public const string ItemFacts =
        "type: [图片, 过场CG]\n" +
        "content: [夜空, 流星, 人群]";

    // PicDesc：真实 Vision 输出的散文描述形态（Readable 模式与立绘表格描述列使用）
    public const string PrisonCorridorSceneDesc =
        "铁栅栏投下斜影，冷白的荧光灯沿着狭窄走廊延伸，尽头是厚重的铁门。";

    public const string DesertSceneDesc =
        "烈日灼烤着无垠荒漠，沙丘起伏延伸到天边，远处立着半截残骸。";

    public const string ItemDesc =
        "夜空下流星划落，人群仰头凝望，画面定格在这一瞬。";

    public const string PrisonChiefDesc =
        "铁栅栏的阴影斜斜压在地面，他静立如一座沉默的堡垒。";

    public const string StrangerDesc =
        "他站在灰白的背景里，尖耳微翘，发丝被风轻轻撩起。";

    public const string MlyssDesc =
        "银灰长发垂落肩头，几缕金黄穗饰在光下闪烁。";

    public const string LorenzDesc =
        "棕褐色虎斑纹的猫耳伏在额前，米白衬衫袖口微卷。";

    /// <summary>
    /// 构造受控条目序列（索引连续，模拟 DB OrderBy Index 后的形态）。
    /// 分组设计：
    ///   Scene 1（监狱探访）：background + 2 个 charslot 立绘 + 监狱负责人对话 +
    ///     精英打扮的男性/小贾斯汀（跨名揭示）+ subtitle + image + playmusic（组切分）
    ///   Scene 2（回忆/荒漠）：background + charslot 缪尔赛思 + 对话 +
    ///     神秘人士（实际人物：洛伦茨）+ 只有立绘不出声的过场立绘
    /// 覆盖：charslot 立绘传播、跨名揭示、aside 三分类、立绘内联、多段对话、组切分。
    /// </summary>
    public static List<FormattedTextEntry> Build()
    {
        var entries = new List<FormattedTextEntry>();
        int idx = 0;

        // ── Scene 1：监狱探访 ──
        entries.Add(Bg("bg_prison_corridor", PrisonCorridorSceneFacts, PrisonCorridorSceneDesc, idx++));
        entries.Add(Charslot(PrisonChiefCode, "监狱负责人", "Avg_avg_npc_134.png",
            new() { ["slot"] = "l", ["name"] = PrisonChiefCode, ["focus"] = "l", ["type"] = "charslot" },
            PrisonChiefUrl, PrisonChiefFacts, PrisonChiefDesc, idx++));
        entries.Add(Charslot(StrangerCode, "神秘人士", "Avg_avg_npc_892_1-2$1.png",
            new() { ["slot"] = "r", ["name"] = StrangerCode, ["focus"] = "r", ["type"] = "charslot" },
            StrangerUrl, StrangerFacts, StrangerDesc, idx++));
        entries.Add(Dialog("监狱负责人", PrisonChiefCode, "谁这么会挑时间，唔，银行的邮件，我不是还清了吗...", PrisonChiefFacts, idx++));
        entries.Add(Dialog("精英打扮的男性", StrangerCode, "先生，您生病了。", StrangerFacts, idx++));
        entries.Add(Dialog("监狱负责人", PrisonChiefCode, "什么？我好得很。", null, idx++));
        entries.Add(Dialog("小贾斯汀", StrangerCode, "您可以喊我小贾斯汀。", null, idx++));
        entries.Add(Subtitle("父母的葬礼当天，她站在最中央，周围人来人往。", idx++));
        entries.Add(Image("Avg_29_i10.png", ItemFacts, ItemDesc, idx++));
        entries.Add(PlayMusic("m_dia_street_loop", idx++));

        // ── Scene 2：荒漠回忆 ──
        entries.Add(Bg("bg_desert_1", DesertSceneFacts, DesertSceneDesc, idx++));
        entries.Add(Charslot(MlyssCode, "缪尔赛思", "Avg_avg_249_mlyss_1-11$1.png",
            new() { ["slot"] = "l", ["name"] = MlyssCode, ["focus"] = "l", ["type"] = "charslot" },
            MlyssUrl, MlyssFacts, MlyssDesc, idx++));
        entries.Add(Dialog("缪尔赛思", MlyssCode, "又想见你了。", MlyssFacts, idx++));
        entries.Add(Dialog("神秘人士（实际人物：洛伦茨）", LorenzCode, "博士，我们又见面了。", LorenzFacts, idx++));
        // 同一角色立绘重复出现（focus 切换）→ 验证 InjectPortraitFacts 的 code 去重，立绘描述只注入一次
        entries.Add(Charslot(LorenzCode, "神秘人士（实际人物：洛伦茨）", "Avg_avg_npc_890_1-4$1.png",
            new() { ["slot"] = "r", ["name"] = LorenzCode, ["focus"] = "r", ["type"] = "charslot" },
            LorenzUrl, LorenzFacts, LorenzDesc, idx++));

        return entries;
    }

    /// <summary>
    /// 深拷贝供 StoryDocumentBuilder 渲染使用：Builder 会原地改写 Index、清空立绘行 MdText，
    /// Readable 与 Prompt 两种模式必须各自持有独立输入。
    /// </summary>
    public static List<ArkPlot.Core.Model.ScriptLine> CloneForRender(List<FormattedTextEntry> source)
        => source.Select(e => (ArkPlot.Core.Model.ScriptLine)new FormattedTextEntry(e)).ToList();

    // ─── 构造小函数 ───

    private static FormattedTextEntry Bg(string key, string sceneFacts, string sceneDesc, int index) => new()
    {
        Index = index,
        Type = "background",
        MdText = $"<img  src=\"https://media.prts.wiki/8/8a/Avg_bg_{key}.png\" alt=\"{key}\" loading=\"lazy\" style=\"max-height:350px\"/>\r\n\r\n`背景`：{key}",
        OriginalText = $"[Background(image=\"{key}\")]",
        ResourceUrls = { $"https://media.prts.wiki/8/8a/Avg_bg_{key}.png" },
        PicFacts = sceneFacts,
        PicDesc = sceneDesc,
    };

    private static FormattedTextEntry Charslot(
        string code, string displayName, string pngName,
        Dictionary<string, string> commandSet, string url, string facts, string desc, int index) => new()
    {
        Index = index,
        Type = "charslot",
        CharacterCode = code,
        MdText = $"<img class=\"portrait\" src=\"{url}\" alt=\"{pngName}\" loading=\"lazy\" style=\"max-height:300px\"/>\r\n\r\n`立绘`{displayName}",
        OriginalText = $"[charslot(name=\"{code}\")]",
        CommandSet = new StringDict(commandSet),
        ResourceUrls = { url },
        PicFacts = facts,
        PicDesc = desc,
    };

    private static FormattedTextEntry Dialog(string name, string code, string dialog, string? picFacts, int index) => new()
    {
        Index = index,
        Type = "multiline",
        CharacterName = name,
        CharacterCode = code,
        MdText = $"**{name}**`讲道：`{dialog}",
        OriginalText = $"[multiline(name=\"{name}\")]{dialog}",
        Dialog = dialog,
        PicFacts = picFacts ?? "",
    };

    private static FormattedTextEntry Subtitle(string text, int index) => new()
    {
        Index = index,
        Type = "subtitle",
        MdText = $"> `居中字幕`：{text}",
        OriginalText = $"[Subtitle(text=\"{text}\")]",
        Dialog = text,
    };

    private static FormattedTextEntry Image(string pngPath, string facts, string desc, int index) => new()
    {
        Index = index,
        Type = "image",
        MdText = $"<img  src=\"https://media.prts.wiki/0/00/{pngPath}\" alt=\"{pngPath}\" loading=\"lazy\" style=\"max-height:350px\"/>\r\n\r\n`图像`{pngPath}",
        OriginalText = $"[Image(image=\"{pngPath}\")]",
        ResourceUrls = { $"https://media.prts.wiki/0/00/{pngPath}" },
        PicFacts = facts,
        PicDesc = desc,
    };

    private static FormattedTextEntry PlayMusic(string key, int index) => new()
    {
        Index = index,
        Type = "playmusic",
        MdText = $"<audio class=\"music\" controls class=\"lazy-audio\" width=\"300\" alt=\"$m_{key}\"><source src=\"https://torappu.prts.wiki/assets/audio/music/beta3_181101/m_{key}.mp3\" type=\"audio/mpeg\"></audio>\r\n\r\n`音乐`：{key}",
        OriginalText = $"[PlayMusic(key=\"$m_{key}\")]",
    };
}