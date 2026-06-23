# 反照抄：从信息表示层根治图片描述照抄问题

## 根因分析

### 照抄不是 bug，而是系统最优解

当前管线的信息流：

```
图片 → Vision(散文) → Markdown(散文) → Novelizer(散文改写散文) → 小说
```

Novelizer 看到的不是图片，而是另一篇**文学文本**。它面临的任务本质上是：

```
小说A → 小说B     ← 当前系统
视觉事实 → 小说    ← 应该做的事
```

对于 Transformer，改写/同义替换永远比理解视觉+重建场景+重新叙事**概率更高**。
embedding 空间里"夜色如墨"和"夜幕低垂"距离最近，所以模型必然走向同义替换。

**结论：照抄是信息表示问题，不是模型行为问题。**

### 核心思路：信息降维

把文学表达从 Novelizer 的输入中彻底移除。如果 Novelizer 看到的是 YAML 结构化事实（`lighting: 冷, 暗`）而非散文（"夜色如墨，星子稀疏地缀在天幕上"），就没有可抄的句子结构、修辞、节奏——LLM 必须原创。

---

## 架构设计

### 改动后的完整管线

```
图片
  ↓ Vision(散文 prompt)
散文描述 → 存 PicDescription.PicDesc
  ↓ 同模型(DeepSeek V4 flash) 提取
YAML 事实 → 存 PicDescription.PicFacts（新列）
  ↓
MdReconstructor(OutputMode 枚举切换)
  ├─ Readable 模式: <p class="scene-desc">散文</p> + <table>立绘表格</table>（维持现状）
  └─ Prompt 模式: <aside class="scene-facts">YAML</aside> + <aside class="portrait-facts">YAML</aside>
  ↓
MarkdownBuilder.PreprocessMdContent（统一归一化所有视觉容器为 <aside> 格式）
  ↓
Novelizer（通用 prompt，只识别 <aside> = 素材）
  ↓
小说输出
```

### UI 切换逻辑

```
if (Avalonia 主窗口「生成小说」CheckBox 勾选 && 图片描述已开启)
    OutputMode = PromptOptimized
else
    OutputMode = Readable
```

只有两者同时开启时才走 Prompt 模式。CLI 端不受影响。

---

## 设计决策清单

| # | 决策点 | 结论 |
|---|--------|------|
| 1 | 两种 Markdown 模式的关系 | 同一个 `MdReconstructor`，`OutputMode` 枚举切换 |
| 2 | Prompt 模式下立绘描述位置 | 内联到角色第一次说话/出场的位置（而非堆在段落开头） |
| 3 | 立绘/场景描述的容器 | HTML 标记：`<aside class="portrait-facts" data-character="XXX">` 和 `<aside class="scene-facts" data-bg="XXX">` |
| 4 | 场景描述放置位置 | 紧跟 `background`/`largebg` 条目之后（真正的场景切换信号），不是 `---`（`theater` 段落分隔） |
| 5 | `sticker`/`subtitle` 处理 | 去掉 `> ` 和 `居中字幕：` 前缀，纯文本输出，让 LLM 照抄 |
| 6 | `showitem`/`cgitem`/`interlude` 处理 | 也变成 YAML 格式（`<aside class="item-facts">`） |
| 7 | 音乐/音效处理 | 删除所有音乐相关标签（`playmusic`/`stopmusic`/`musicvolume`/`musicstop`），只保留音效标签（`` `音效`：xxx ``），去掉音乐名 |
| 8 | Vision YAML 步骤 | 追加：散文→存 PicDesc → 同模型提取 YAML → 存 PicFacts |
| 9 | YAML 提取时机 | PicDescService 内部，紧跟 Vision 之后完成 |
| 10 | YAML 提取模型 | 复用 Novelizer 同一模型（DeepSeek V4 flash/pro） |
| 11 | PicFacts 存储 | `PicDescription` 表新增 `PicFacts` 列 |
| 12 | Novelizer prompt 适配 | 通用 prompt + `PreprocessMdContent` 统一归一化所有视觉容器为 `<aside>` 格式 |
| 13 | 测试数据准备 | 写一次性脚本/测试用例，批量将孤星活动所有 PicDesc 散文提取为 YAML 写入 PicFacts |
| 14 | 测试流程 | 孤星第一章 → 从 DB 取数据 → MdReconstructor(Prompt 模式) → 输出 prompt 专用 MD → Novelizer 跑小说化 → 对比评估 |

---

## 实施步骤

### Phase 0：基础设施

#### 0.1 PicDescription 表新增 PicFacts 列

文件：`ArkPlot.Core/Model/PicDescription.cs`

```csharp
[SugarColumn(ColumnDataType = "TEXT", IsNullable = true)]
public string? PicFacts { get; set; }
```

SqlSugar `CodeFirst.InitTables` 会自动 `ALTER TABLE ADD COLUMN`。

#### 0.2 OutputMode 枚举

文件：`ArkPlot.Core/Utilities/WorkFlow/MdReconstructor.cs`（或新建文件）

```csharp
public enum OutputMode
{
    Readable,       // 维持现状：HTML 表格 + 散文描述
    PromptOptimized // Prompt 特调：<aside> + YAML 事实
}
```

### Phase 1：PicDescService YAML 提取（决策 #8, #9, #10, #11）

#### 1.1 PicDescService 新增 YAML 提取逻辑

文件：`ArkPlot.Core/Services/PicDescService.cs`

新增参数和逻辑：

```csharp
public class PicDescService : IDisposable
{
    private readonly Func<string, Task<string>>? _describeByUrl;    // Vision 模型
    private readonly Func<string, Task<string>>? _extractFacts;     // YAML 提取（复用 Novelizer 模型）

    public PicDescService(
        Func<string, Task<string>>? describeByUrl = null,
        Func<string, Task<string>>? extractFacts = null,  // 新增
        bool debugMode = false)
    { ... }
}
```

在 `GenerateAndCacheAsync` 中，散文写入 PicDesc 后，调用 `_extractFacts` 提取 YAML 写入 PicFacts：

```csharp
private async Task<string> GenerateAndCacheAsync(string imageUrl, string dedupKey)
{
    // 现有逻辑：Vision → 散文
    string description = ...;
    
    // 新增：散文 → YAML 事实
    string? facts = null;
    if (_extractFacts != null && !string.IsNullOrWhiteSpace(description))
    {
        try { facts = await _extractFacts(description); }
        catch { /* YAML 提取失败不影响散文 */ }
    }

    UpsertPicDesc(dedupKey, imageUrl, description, facts);
    return description;
}
```

#### 1.2 UpsertPicDesc 增加 facts 参数

```csharp
private void UpsertPicDesc(string dedupKey, string imageUrl, string desc, string? facts = null)
{
    // ... 现有逻辑 ...
    // Insertable / Updateable 加上 PicFacts = facts
}
```

#### 1.3 YAML 提取 prompt

在 PicDescService 中定义一个 `const string` 或从外部注入的提取 prompt：

```
你是视觉要素提取器。将以下散文描述转化为结构化视觉事实。

规则：
1. 禁止句子。只输出关键词和短语。
2. 禁止修辞、比喻。只保留客观事实。
3. 严格按以下 YAML 格式输出。
4. 每个字段 2-6 个词，每个词不超过 4 个字。

场景图模板：
lighting: [光源类型, 明暗程度, 色温]
materials: [材质1, 材质2, 材质3]
objects: [显著物体1, 显著物体2, 显著物体3]
space: [空间类型, 布局特征, 规模]
colors: [主色, 辅色, 点缀色]
mood: [氛围词1, 氛围词2]

立绘/角色模板：
hair: [颜色, 长度, 发型特征]
clothing: [上装, 下装, 鞋/配饰]
equipment: [武器/道具1, 武器/道具2]
posture: [姿态描述]
features: [其他显著外貌特征1, 显著外貌特征2]
colors: [主色, 辅色]

根据输入内容自动判断是场景还是角色，选择对应模板输出。
```

#### 1.4 Avalonia/CLI 初始化时注入 extractFacts 委托

文件：`ArkPlot.Avalonia/ViewModels/MainWindowViewModel.cs`

在创建 `PicDescService` 时，用 `BailianClient`（Novelizer 同一模型）创建 `extractFacts` 委托：

```csharp
// 复用 Novelizer 的 BailianClient
var novelizerConfig = new ApiConfig { ... }; // DeepSeek V4 flash
var novelizerClient = new BailianClient(http, novelizerConfig);

Func<string, Task<string>> extractFacts = async prose =>
{
    var result = await novelizerClient.ChatAsync(
        model: "deepseek-v4-flash",
        systemPrompt: yamlExtractionPrompt,
        userContent: prose);
    return result.AnswerContent;
};

picDescService = new PicDescService(describeByUrl, extractFacts);
```

### Phase 2：MdReconstructor Prompt 模式（决策 #1-#7）

#### 2.1 MdReconstructor 构造函数新增 OutputMode 参数

文件：`ArkPlot.Core/Utilities/WorkFlow/MdReconstructor.cs`

```csharp
private readonly OutputMode _outputMode;

public MdReconstructor(
    EntryList entries,
    PicDescService? picDescService = null,
    bool enableDescriptions = true,
    OutputMode outputMode = OutputMode.Readable  // 默认维持现状
)
{
    _outputMode = outputMode;
    // ... 其余不变 ...
}
```

#### 2.2 Prompt 模式：场景描述紧跟 background 条目

文件：`ArkPlot.Core/Utilities/WorkFlow/MdReconstructor.cs` → `GetRawMdLines` 方法

当前逻辑（Readable 模式）：
```csharp
// 为非立绘的图片条目追加描述
mdList.Add($"<p class=\"scene-desc\">【此处为对场景图片...的描述...】{desc}</p>");
```

Prompt 模式改为：
```csharp
if (_outputMode == OutputMode.PromptOptimized)
{
    var facts = entry.PicFacts; // 从 FormattedTextEntry 新增的 PicFacts 属性读取
    if (!string.IsNullOrEmpty(facts))
    {
        var bgName = Path.GetFileNameWithoutExtension(entry.Bg);
        mdList.Add($"<aside class=\"scene-facts\" data-bg=\"{bgName}\">\n{facts}\n</aside>");
    }
}
else
{
    // 现有逻辑不变
}
```

**判断 background/largebg 条目**：`entry.Type == "background" || entry.Type == "largebg"` 时插入 scene-facts。其他非立绘图片（`showitem`/`cgitem`/`interlude`）用 `<aside class="item-facts">`。

#### 2.3 Prompt 模式：立绘描述内联到角色首次出场

当前逻辑（Readable 模式）：
- `MakePortraitChart` 把 `<table class="portrait-table">` 插在段落组最前面

Prompt 模式改为：
- 不在段落组前面插入表格
- 跟踪每个角色的首次出场位置（`_describedCharacters` 已有）
- 当某个角色第一次出现在对话中时（`entry.CharacterName` 匹配），在该对话行**之前**插入 `<aside class="portrait-facts">`

实现位置：`GetRawMdLines` 方法中，对每个对话条目检查：

```csharp
if (_outputMode == OutputMode.PromptOptimized 
    && !string.IsNullOrEmpty(entry.CharacterName))
{
    var code = entry.CharacterCode;
    if (!string.IsNullOrEmpty(code) && !_describedCharacters.Contains(code))
    {
        _describedCharacters.Add(code);
        var facts = GetOrGeneratePortraitFacts(entry);
        if (!string.IsNullOrEmpty(facts))
        {
            mdList.Add($"<aside class=\"portrait-facts\" data-character=\"{entry.CharacterName}\">\n{facts}\n</aside>");
        }
    }
}
// 然后正常添加对话行
mdList.Add(entry.MdText);
```

`GetOrGeneratePortraitFacts` 从 `entry.PicFacts` 读取 YAML，或调用 `_picDescService` 生成。

#### 2.4 Prompt 模式：sticker/subtitle 去标记

文件：需要追溯 `FormattedTextEntry.MdText` 的生成源头（`PrtsPreloader` 或 `AkpParser`）

Prompt 模式下，`sticker` 类型的 MdText 去掉 `> ` 前缀，`subtitle` 类型去掉 `> 居中字幕：` 前缀。

实现方式有两种：
- **A. 在 MdReconstructor 的 Result/GetRawMdLines 中后处理**：检查 entry.Type，做字符串替换
- **B. 在 OutputMode 传递到上游解析阶段**

推荐 **A**（改动最小）：

```csharp
if (_outputMode == OutputMode.PromptOptimized)
{
    if (entry.Type == "sticker" && mdText.StartsWith("> "))
        mdText = mdText[2..];
    else if (entry.Type == "subtitle")
        mdText = mdText.Replace("> `居中字幕`：", "").Replace("> `居中字幕`:", "");
}
```

#### 2.5 Prompt 模式：删除音乐标签，保留音效

在 `GetRawMdLines` 中，Prompt 模式下过滤掉音乐相关条目：

```csharp
if (_outputMode == OutputMode.PromptOptimized)
{
    var skipTypes = new HashSet<string> { "playmusic", "stopmusic", "musicvolume", "musicstop" };
    // 在遍历 grp 时跳过这些类型
}
```

音效标签（`palysound`/`playsound`/`stopsound`）保留原始格式 `` `音效`：xxx ``。

#### 2.6 FormattedTextEntry 新增 PicFacts 属性

文件：`ArkPlot.Core/Model/FormattedTextEntry.cs`

```csharp
[SugarColumn(ColumnDataType = "TEXT", IsIgnore = true)]  // 不入库，运行时填充
public string PicFacts { get; set; } = "";
```

在 `ProcessPicDescsAsync` 中同时填充 `PicDesc` 和 `PicFacts`：

```csharp
entry.PicDesc = string.Join("; ", descs);
entry.PicFacts = string.Join("; ", factss);  // 从 PicDescService 获取
```

需要 `PicDescService` 新增 `GetOrCreatePicFactsAsync` 方法，或在 `GetOrCreatePicDescAsync` 返回值中包含 facts。

### Phase 3：MarkdownBuilder 统一归一化（决策 #12）

#### 3.1 PreprocessMdContent 统一所有视觉容器为 <aside>

文件：`ArkPlot.Novelizer/MarkdownBuilder.cs`

Prompt 模式的 MD 文件已经直接用 `<aside>` 格式。Readable 模式的 MD 文件用的是 `<p class="scene-desc">` 和 `<table class="portrait-table">`。

为了让 Novelizer prompt 只有一套规则，`PreprocessMdContent` 需要把 Readable 模式的容器也转换为 `<aside>`：

```csharp
// 将 <p class="scene-desc">【此处为对场景图片XXX的描述...】散文描述</p>
// 转换为 <aside class="scene-facts" data-bg="XXX">散文描述</aside>
text = SceneDescRegex().Replace(text, m => 
    $"<aside class=\"scene-facts\" data-bg=\"{m.Groups[1].Value}\">{m.Groups[2].Value}</aside>");

// 将 <table class="portrait-table">...<td>【此处为对XXX的形象描述...】：散文</td>...</table>
// 转换为多个 <aside class="portrait-facts" data-character="XXX">散文</aside>
text = PortraitTableRegex().Replace(text, ConvertPortraitTable);
```

这样无论上游是哪种 OutputMode，Novelizer 看到的都是统一的 `<aside>` 格式。

#### 3.2 Novelizer DefaultSystemPrompt 适配 <aside> 标记

文件：`ArkPlot.Novelizer/NovelizerPipeline.cs`

在"三、视听语言的叙事转化"一节中，将输入描述的说明改为：

```
输入文本中的 <aside> HTML 标签包含视觉素材：
- <aside class="scene-facts">：场景视觉事实（YAML 格式）
- <aside class="portrait-facts" data-character="XXX">：角色外貌事实（YAML 格式）
- <aside class="item-facts">：物品/图像视觉事实

这些是原始素材，你必须从中提取视觉信息，用完全原创的叙事语言重构。
同义替换等同于照抄。输出的小说中不应出现任何 <aside> 标签。
```

### Phase 4：Novelizer CLI --prompt/--tag 参数

文件：`ArkPlot.Novelizer/Program.cs`、`ArkPlot.Novelizer/NovelizerPipeline.cs`

新增两个 CLI 参数（与之前对话中已实现的功能一致）：
- `--prompt <file>` — 加载自定义 system prompt 覆盖默认提示词
- `--tag <name>` — 输出文件标签，输出文件名为 `{input}_novel_{tag}.md`

### Phase 5：测试与验证

#### 5.1 一次性脚本：孤星活动 PicDesc 批量 YAML 化

文件：新建 `ArkPlot.Novelizer.Tests/ManualYamlExtraction.cs`（或集成到现有测试）

```csharp
[Fact]
public async Task ExtractYamlForGuxingActivity()
{
    // 1. 连接数据库
    // 2. 查询孤星活动所有 PicDescription 记录（Source="Vision"）
    // 3. 对每条 PicDesc 调 DeepSeek V4 flash 提取 YAML
    // 4. 写入 PicFacts 列
    // 5. 输出统计：成功/失败/跳过数量
}
```

#### 5.2 端到端测试：孤星第一章

步骤：
1. 从数据库读取孤星第一章的 `FormattedTextEntry` 列表
2. 用 `MdReconstructor(entries, picDescService, true, OutputMode.PromptOptimized)` 生成 prompt 专用 MD
3. 用 `NovelizerPipeline` 对该 MD 跑小说化
4. 输出文件：`孤星_ch1_novel_prompt_mode.md`

#### 5.3 对比评估

对比基线：`孤星_novel_deepseek-v4-flash.md`（现有可读模式输出）

**评估指标**：

**指标 1：n-gram overlap**
对每个图片描述片段，计算输出文本中 2-gram 和 3-gram 的重合率。
如果某段输出的 3-gram 重合率 > 30%，视为疑似照抄。

**指标 2：信息保真度**
检查每个视觉要素是否在输出中有对应表达。
反照抄不能以牺牲信息为代价。

**指标 3：LLM Judge**
让另一个 LLM 判断输出是否只是输入的同义改写：
- 1分：明显改写
- 2分：轻度改写
- 3分：中度重构
- 4分：高度重构
- 5分：完全原创

---

## 关键文件清单

| 文件 | 改动 |
|------|------|
| `ArkPlot.Core/Model/PicDescription.cs` | 新增 `PicFacts` 列 |
| `ArkPlot.Core/Model/FormattedTextEntry.cs` | 新增 `PicFacts` 瞬态属性 + 复制构造函数 |
| `ArkPlot.Core/Services/PicDescService.cs` | 新增 `_extractFacts` 委托 + YAML 提取逻辑 + `UpsertPicDesc` 增加 facts 参数 |
| `ArkPlot.Core/Utilities/WorkFlow/MdReconstructor.cs` | 新增 `OutputMode` 枚举 + Prompt 模式分支（scene-facts/portrait-facts 内联/sticker 去标记/音乐删除） |
| `ArkPlot.Novelizer/MarkdownBuilder.cs` | `PreprocessMdContent` 统一归一化所有视觉容器为 `<aside>` |
| `ArkPlot.Novelizer/NovelizerPipeline.cs` | DefaultSystemPrompt 适配 `<aside>` 标记 + `--prompt`/`--tag` CLI 参数 + `outputTag` 支持 |
| `ArkPlot.Novelizer/Program.cs` | 新增 `--prompt`/`--tag` 参数解析 |
| `ArkPlot.Avalonia/ViewModels/MainWindowViewModel.cs` | 创建 PicDescService 时注入 `extractFacts` 委托 + OutputMode 判断 |
| `ArkPlot.Novelizer.Tests/ManualYamlExtraction.cs` | 新建：孤星活动批量 YAML 化测试 |

## 不变的文件（维持现状）

| 文件 | 说明 |
|------|------|
| `ArkPlot.Vision/BailianVisionClient.cs` | Vision 散文 prompt 不变，YAML 提取在 PicDescService 层做 |
| `ArkPlot.Vision/VisionConfig.cs` | 不变 |
| `ArkPlot.Vision/OllamaVisionClient.cs` | 不变 |
| `ArkPlot.Avalonia/Models/AppSettings.cs` | VisionSettings.DefaultSystemPrompt 不变 |
| `ArkPlot.Novelizer.Tests/bundles/00_baseline/` | 不变（Vision 输出仍为散文，YAML 是后提取的） |

---

## 后续演进方向（本任务不做）

1. **NarrativeIntent 层**：在 VisualFact 和 Novelizer 之间插入叙事意图规划
2. **Embedding 相似度评估**：用 sentence-transformers 计算 cos similarity，> 0.9 判定为改写
3. **自动重跑**：Novelizer 输出后跑 Judge 模型，得分 ≤ 2 自动重跑
