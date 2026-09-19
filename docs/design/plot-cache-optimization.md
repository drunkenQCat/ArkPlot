# PlotCache 缓存优化设计

## 现状问题

1. **ID 爆炸**：`SaveAsync` upsert 路径用 DELETE+INSERT 而非 UPDATE，每次跑管线条目 ID 重新自增。
2. **冗余解析**：缓存命中（Status=2）后 PrtsPreloader 和 StartParseLines 仍无条件重跑，字段已完整却重新计算。
3. **无版本管理**：解析逻辑变更后需手动清库，没有自动失效机制。

## 优化方案

### 1. SaveAsync：DELETE+INSERT → UPDATE

**文件**：`ArkPlot.Core/Services/PlotCache.cs`

将 upsert 路径中的全量删+插改为按 `Id` 判断：

```csharp
// 现有条目（Id > 0）→ UPDATE，ID 不变
var existing = entries.Where(e => e.Id > 0).ToList();
if (existing.Count > 0)
    await db.Updateable(existing).ExecuteCommandAsync();

// 新条目（Id == 0）→ INSERT
var newOnes = entries.Where(e => e.Id == 0).ToList();
if (newOnes.Count > 0)
    await db.Insertable(newOnes).ExecuteCommandAsync();
```

缓存命中时条目都有 `Id > 0`，走 UPDATE，ID 稳定。

### 2. ParserVersion：解析版本管理

**控制范围**：是否重新解析（PrtsPreloader + StartParseLines）。

**定义**：

| 位置 | 说明 |
|---|---|
| `PlotCache.CurrentParserVersion` | 代码常量，如 `const int CurrentParserVersion = 1` |
| `Plot.ParserVersion` | 数据库字段，记录该章节最后一次解析时用的版本 |

**TryLoadAsync 命中条件变更**：

```csharp
// 版本不匹配时仍返回数据，但标记 IsCurrent=false
var isCurrent = plot.ParserVersion == CurrentParserVersion;
return (plot, entries, isCurrent);
```

**TryLoadAsync 返回值变更**：从 `(Plot, List<FormattedTextEntry>)?` 改为 `(Plot, List<FormattedTextEntry>, bool IsCurrent)?`。

**SaveAsync 写入**：保存时设置 `plot.ParserVersion = CurrentParserVersion`。

**管线行为**：

| 缓存状态 | IsCurrent | 行为 |
|---|---|---|
| 未命中 | — | 下载 + 解析 + 保存 |
| 命中 + 版本匹配 | true | 跳过所有处理，直接用 |
| 命中 + 版本不匹配 | false | **不重新下载**，重新跑 PrtsPreloader + StartParseLines + UPDATE |

**何时改 CurrentParserVersion**：PrtsPreloader 或 AkpParser 的解析逻辑有变更时，改 `PlotCache` 里的常量。

### 3. DownloadVersion：下载版本管理

**控制范围**：是否重新下载原始文本。

**定义**：

| 位置 | 说明 |
|---|---|
| `Act.DownloadVersion` | 数据库字段，默认 1 |
| 代码常量 | 不需要，这是纯数据库字段 |

**GetAllChapters 缓存判断**：加载 Plot 时额外检查 `Act.DownloadVersion`，若与本地记录不一致则视为未缓存，触发重新下载。

**何时改**：几乎不需要改。只有当 GitHub 原始数据格式发生变化时才去 DB 里手动调对应 Act 的 DownloadVersion。

### 4. 跳过已缓存章节的解析

管线中 `GetPreloadInfo` 和 `StartParseLines` 对 `IsCurrent=true` 的章节跳过：

```csharp
// ResourceLoader / CliPipeline 中
if (pm.IsCurrent)
{
    // 字段已完整，MdText/TypText 已在 DB 中，跳过
    continue;
}
```

## 改动清单

| 文件 | 改动 |
|---|---|
| `Plot.cs` | 新增 `int ParserVersion` 字段 |
| `Act.cs` | 新增 `int DownloadVersion` 字段（默认 1） |
| `PlotCache.cs` | 新增 `const int CurrentParserVersion`；`TryLoadAsync` 返回值加 `IsCurrent`；`SaveAsync` 中 DELETE+INSERT 改为 UPDATE+INSERT；写入时设 `ParserVersion` |
| `AkpStoryLoader.cs` | `GetAllChapters` 适配新返回值；缓存命中时标记 `PlotManager.IsCurrent` |
| `PlotManager.cs` | 新增 `bool IsCurrent` 属性 |
| `CliPipeline.cs` | `IsCurrent` 的章节跳过解析步骤 |
| `ResourceLoader.cs` | `IsCurrent` 的章节跳过 PrtsPreloader |

## 行为对照

| 场景 | 旧行为 | 新行为 |
|---|---|---|
| 缓存命中，版本匹配 | 重新解析，DELETE+INSERT | 跳过所有处理，零 DB 写入 |
| 缓存命中，版本不匹配 | 重新解析，DELETE+INSERT | 重新解析，UPDATE（ID 稳定） |
| 新下载 | INSERT | INSERT |
| 改 PrtsPreloader 逻辑 | 手动清 DB | 改 `CurrentParserVersion` 值 |