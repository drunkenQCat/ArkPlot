# ArkPlot MdReconstructor 重构计划

> 生成日期：2026-06-23
> 源文件：`ArkPlot.Core/Utilities/WorkFlow/MdReconstructor.cs`
> 状态：God Object — 待渐进拆分

---

## 现状诊断

`MdReconstructor` 已演化为大型 God Object，同时承担：

| # | 职责 | 说明 |
|---|------|------|
| 1 | 数据预处理 | `RemoveEmptyLines()` |
| 2 | 图片描述生成 | `ProcessPicDescsAsync()` — 调 AI / 网络 |
| 3 | 分段算法 | `GroupLinesBySegment()` |
| 4 | 立绘识别 | `ProcessPortraits()` / `ExtractCharacterInfo()` |
| 5 | 角色去重 | `_describedCharacters` (HashSet) |
| 6 | 场景去重 | `_describedImages` (HashSet) |
| 7 | HTML 生成 | `MakePortraitChart()` / `BuildDescribedChart()` |
| 8 | Prompt 模式 | `GetPromptModeLines()` |
| 9 | Readable 模式 | `GetReadableModeLines()` |
| 10 | Markdown 拼接 | `Result` / `AppendResultToBuilder()` |
| 11 | 状态管理 | 6+ 个成员变量四处写入 |
| 12 | PicDesc 缓存 | `GetOrGenerateDescription()` / `GetPortraitFactsForCharacter()` |

名称已不匹配：它实际是一个 **MarkdownDocumentPipeline** / **StoryDocumentBuilder**，而非单纯的 "Reconstructor"。

---

## 核心问题：阶段与策略交织

两个维度混在一起：

```
Pipeline 维度：                    输出模式维度：
原始Entry → 补充图片信息            Readable
         → 清洗                    PromptOptimized
         → 分组
         → 角色分析
         → 描述分析
         → 输出
```

大量 `if (_outputMode == PromptOptimized)` 散落各处，导致分组/描述/立绘逻辑全都知道输出模式，复杂度爆炸。

---

## 重构核心原则

1. **行为一致 > 结构改善 > 代码风格**
2. **禁止推倒重写** — 渐进迁移，每步可验证
3. **先建回归测试，再动代码**
4. **禁止修改 `GroupLinesBySegment` 算法行为**

---

## 数据来源与 Golden Sample

- **SQLite 数据库**：`ArkPlot.Avalonia/bin/Debug/net9.0/arkplot.db`
- **测试数据**：活动「孤星」第一章
- **Golden 文件**：`Tests/Golden/LoneTrail_Chapter1.md`

---

## `GroupLinesBySegment` 算法理解

### 实际行为（非直觉）

算法**不是**"碰到 `----` 就切段"，而是三层分段策略：

```
1. 显式分隔符：`---`（Trim 后完全匹配）→ 直接分段，丢弃自身
2. 场景转换信号：`playmusic` → 强制分段，但保留在原位
3. 隐式分段：当前组 >= 16 行 且 当前行以 `-` 开头 → 切组
```

等价于：

```csharp
// 优先：显式分隔符
if (item.MdText.Trim() == "---") { Flush(); continue; }
// 其次：场景转换
if (item.Type == "playmusic") { Flush(); temp.Add(item); continue; }
// 兜底：长度 + 分隔线
if (current.Count < 16 || !IsSeparator(item)) { temp.Add(item); continue; }
Flush();
```

### 设计目的

避免剧情中大量 `----` 导致过度分段，同时利用 `---`（显式分隔）和 `playmusic`（音乐切换）作为更可靠的分段信号。

### 已知问题（2026-06-23 状态）

| 问题 | 严重度 | 状态 |
|------|--------|------|
| `IsItemOnlyDashes` 命名完全相反 | 高 | ⚠️ 仍存在 — 返回 `!item.MdText.StartsWith('-')`，语义是"不是分隔线" |
| 布尔表达式难读 | 中 | ⚠️ 仍存在 — `IsItemOnlyDashes(item) \|\| temp.Count < 16` |
| 魔数 16 | 中 | ⚠️ 仍存在 |
| 循环结束未 flush | 高 | ✅ **已修复** — 末尾 `if (temp.Count > 0) { ... }` |

### 新增分段逻辑（已实现）

1. **`---` 显式分段**：在循环体开头 `if (item.MdText.Trim() == "---")` 直接分段并丢弃分隔符自身，不再依赖 `IsItemOnlyDashes` 的隐式逻辑
2. **`playmusic` 场景转换**：`Type == "playmusic"` 强制分段（音乐切换 = 场景边界），但 playmusic 本身保留在原位

---

## 重构规划

### 第一阶段：低风险（不新增类）

**目标**：改善可读性，不改变任何行为。

1. ~~检查循环结束是否遗漏 flush~~ ✅ 已修复
2. 提取魔数 → `private const int MinSegmentSize = 16;`
3. 重命名 `IsItemOnlyDashes` → `IsNotSeparator` 或提取 `IsSeparator`
4. 提取 `ShouldSplit()` 方法

### 第二阶段：允许新增类

**目标**：抽出独立组件，旧代码可共存。

#### SegmentGrouper

```csharp
class SegmentGrouper
{
    List<EntryList> Group(List<FormattedTextEntry>);
}
```

输出必须与现有 `GroupLinesBySegment()` 完全一致。

#### Renderer（策略模式）

当前 `if (_outputMode)` 散落各处 → 策略模式：

```csharp
interface IMdRenderer
{
    List<string> Render(EntryList group);
}

class ReadableRenderer : IMdRenderer { }
class PromptRenderer : IMdRenderer { }
```

`_outputMode` 字段将直接消失，大量条件分支随之消除。

### 第三阶段：高风险（需先建测试）

以下部分暂冻结，优先建立测试后再动：

- **ProcessPicDescsAsync** — 涉及 AI、SQLite、缓存、CharacterCode 去重
- **ProcessPortraits** — 涉及 HTML、focus、ResourceUrls

---

## 目标架构

```
FormattedTextEntry[]
        │
        ▼
  PicDescEnricher        ← 图片描述前置，构造函数不再调网络
        │
        ▼
  SegmentGrouper         ← 分段算法独立
        │
        ▼
  PortraitAnalyzer       ← 立绘分析独立
        │
        ▼
  DocumentModel
        │
        ├── ReadableRenderer
        │
        └── PromptRenderer
```

对比现状：

```
MdReconstructor → （全部逻辑）
```

---

## 成功标准

全部满足才视为成功：

- [ ] 编译通过
- [ ] 孤星第一章 Markdown 无差异
- [ ] Prompt 模式输出无差异
- [ ] Readable 模式输出无差异
- [ ] Group 数量一致
- [ ] 立绘数量一致
- [ ] CharacterCode 去重结果一致

---

## 禁止事项

- [ ] 直接重写整个 `MdReconstructor`
- [ ] 先删旧代码再实现新代码
- [ ] 根据个人理解修改剧情输出
- [ ] 改变 `GroupLinesBySegment` 算法行为
- [ ] 在无测试保护下修改 `ProcessPicDescsAsync` / `ProcessPortraits`

---

## 推荐执行顺序

```
Step 1 ─ 建立 Golden Test（孤星第一章）
  │
Step 2 ─ 提取魔数 + 重命名 + ShouldSplit
  │
Step 3 ─ 提取 SegmentGrouper
  │
Step 4 ─ 提取 Renderer（Readable + Prompt）
  │
Step 5 ─ 提取 PicDescEnricher + PortraitAnalyzer
```

整个过程必须依赖 `SQLite + 孤星第一章 + Golden Test` 形成稳定的验证闭环。