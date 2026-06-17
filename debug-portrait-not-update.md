[OPEN] portrait-not-update

# 调试会话：portrait-not-update

## 问题摘要

- 症状：TTS 窗口首次点击带立绘角色时显示正常，后续切换到其他行时头像看起来不更新。
- 当前已知证据：不同角色在诊断日志中返回了同一个立绘 URL，怀疑问题位于数据链路而非 UI 绑定层。

## 可证伪假设

1. `NovelAligner` 对齐错位，把错误的 `FormattedTextEntry` 绑定到了目标对白，导致 `EntryIndex` 与实际说话角色不一致。
2. 数据库中的 `FormattedTextEntry.Portraits` 本身就被写成了相同 URL，UI 只是正确展示了错误数据。
3. `MakeAlignedDialogEntry()` 在构造 `AlignmentEntry` 时复用了错误的画像数据，导致 `_allEntries` 中的 `Portraits` 被污染。
4. `GetPortraitUrl(entryIndex)` 虽然按 `EntryIndex` 查询，但命中了重复/错误的条目，返回了非当前 segment 对应的数据。
5. 头像 URL 实际已经变化，但下游图片加载/缓存层把不同角色请求折叠成同一资源，造成视觉上不更新。

## 计划中的证据点

- 检查对齐产物中 `EntryIndex=86/88` 对应的角色名、角色码与画像列表。
- 检查数据库中 `FormattedTextEntry` 的原始 `Portraits` 值。
- 审查 `NovelAligner`、`TtsViewModel`、`PortraitPanelViewModel` 的数据流与索引逻辑。
- 必要时添加最小化诊断日志，比较修复前后的运行时输出。

## 已获得证据

- `CW-ST-1` 的 `EntryIndex=88` 在对齐结果中角色正确，`CharacterCode=avg_npc_892_1`。
- DB 中 `PlotId=31` 的 `Index=86/88` 都是双人同场快照，`Portraits` 都同时包含 `avg_npc_134` 与 `avg_npc_892_1`。
- 旧逻辑在 `TtsViewModel.GetPortraitUrl(...)` 中固定取第一张非透明图，因此 `Index=88` 也误取 `avg_npc_134`。
- 新增回归测试在修复前稳定失败，修复后已通过。

## 当前结论

- 假设 1（对齐错位）：已排除。
- 假设 2（DB 原始 Portraits 错误）：已排除。DB 数据是舞台快照，不是单角色专属图。
- 假设 3（MakeAlignedDialogEntry 污染 Portraits）：已排除。该层仅透传。
- 假设 4（GetPortraitUrl 命中错误数据）：部分成立。更准确地说，是“条目定位过宽 + 选图策略错误”。
- 假设 5（图片缓存层导致不刷新）：已排除。

## 当前状态

- 已完成最小修复与测试验证。
- 诊断日志暂时保留，待用户确认后再决定是否清理。
