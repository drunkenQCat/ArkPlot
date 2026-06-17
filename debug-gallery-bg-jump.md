[OPEN] gallery-bg-jump

# 调试会话：gallery-bg-jump

## 问题摘要

- 症状：TTS 的 Gallery 组件背景图在切换行/切换章节时出现“乱窜”，与当前对白上下文无明显对应关系。
- 目标：证明背景图选择链路中哪一层使用了错误的索引键或错误的数据源，并给出最小修复。

## 可证伪假设（3-5 个）

1. **跨 Plot/章节索引冲突**：背景查找仅使用 `EntryIndex(Index)`，未带 `PlotId/Chapter` 约束，导致命中其他章节的 charslot/bg。
2. **背景数据源选错**：Gallery 读取的是 `charslot` 全表或第一章数据，而非当前章节（Plot）范围数据。
3. **对齐结果缺字段**：`AlignmentEntry` 没有 PlotId，导致 UI 层无法精确关联到 DB 的背景条目，只能做错误的近似匹配。
4. **排序/过滤逻辑错误**：背景条目排序仅按 `Index`，且未过滤 `Bg` 为空或 `focus="none"` 的场景，导致“最近背景”计算错。
5. **缓存污染**：GalleryPanelViewModel 或上游 `_backgrounds` 列表复用/叠加旧章节数据，未在切章节/换文件时清理，导致引用旧数据。

## 计划证据点

- 输出当前选中 seg 的 `ChapterTitle/EntryIndex/Character/CharacterCode` 与 Gallery 计算得到的 `current/prev/next` 背景条目来源（含 PlotId、Index）。
- 校验 `_backgrounds` 的构建范围：是否跨 plot 混合、是否只按 `Index` 排序。
- 从 DB 抽取同一 `Index` 在不同 `PlotId` 下的 `charslot.Bg` 分布，验证索引冲突是否真实存在。

## 已获得证据

- DB 中 `charslot.Bg` 的 `Index` 在多个 `PlotId` 下大量重复（例如 `Index=58` 同时出现在 `20` 个 plot 中），证明“仅按 Index 查背景”在数据层必然不可靠。
- `TtsViewModel.LoadBackgroundsAsync()` 原逻辑从 DB 读取了 **全库** `charslot`（无 plot 过滤），再仅按 `Index` 排序写入 `_backgrounds`，等价于把不同章节/不同活动的背景混成一条时间线。
- `UpdateGalleryForSegment()` 原逻辑在 `_backgrounds` 上按 `EntryIndex<=seg.EntryIndex` 取“最近背景”，当 `_backgrounds` 混合多 plot 时会出现背景跳转。
- 对于 `EntryIndex=-1` 的段（旁白/无对应 textentry），原逻辑等价于选不到背景（只会兜底到第一张），与“继承上一条背景”的期望相反。

## 已做修复

- 背景加载阶段按 `AlignmentEntry` 的章节标题反查 `PlotId`，只加载本次对齐结果涉及的 plot。
- 背景锚点不再仅依赖 `charslot`：改为在 plot 内按 `Index` 顺序扫描所有带 `Bg` 的条目，提取“Bg 变化点”作为背景锚点（否则像 `杰克逊 idx=333` 这类对白行的 `Bg` 会被漏掉）。
- `BackgroundItem` 增加 `PlotId/ChapterTitle`，使 UI 层能按章节过滤背景序列。
- Gallery 选图按 `SegmentRow.ChapterTitle` 过滤背景序列，避免跨章节乱窜。
- 对 `EntryIndex<0` 的段：回溯到同章节上一条 `EntryIndex>=0` 的段，用其 `EntryIndex` 作为背景定位点，实现“继承上一条背景”。
- 上下文台词也按章节过滤，避免上下文跨章节串联。
- Gallery 的 `Prev/Next` 预览过滤黑场背景（`Avg_bg_bg_black.png`）。

## 当前状态

- 已新增 `GalleryBackgroundSelectionTests` 两个回归测试覆盖：
  - 背景必须来自同章节
  - 旁白继承上一条背景
- 诊断日志暂时保留，待用户确认后再决定是否清理。
