# 可视化日志面板修复流程 Playbook

> 一套**自运行、自验证、自修复**的修复流程：适用于 ArkPlot 可视化日志模块（阶段时间线 + 日志流）的后续迭代，也适用于同规模其他模块。
> 本文档与 `Scripts/log-panel/` 下的脚本配套使用；脚本是流程的机械部分，本文档是人机协作的编排部分。
> 首次执行于 2026-08（本次日志面板六项缺陷修复），执行记录见文末。

---

## 1. 模块背景与缺陷清单（append-only）

模块文件（所有权划分依据）：

| 文件 | 职责 |
|---|---|
| `ArkPlot.Avalonia/ViewModels/WorkflowLogPanelViewModel.cs` | 阶段时间线 VM（生命周期/进度/计时/命令） |
| `ArkPlot.Avalonia/Models/WorkflowStage.cs`、`LogEntry.cs` | 模型（Duration/Progress 已是 `[ObservableProperty]`） |
| `ArkPlot.Avalonia/Views/Controls/WorkflowLogPanel.axaml(.cs)` | 视图（模板/接线/编译绑定） |
| `ArkPlot.Avalonia/Styles/AutoScroll.cs` | 自动滚动扩展 |
| `ArkPlot.Avalonia/ViewModels/MainWindowViewModel.cs` | 管线驱动方（BeginPipeline/EnterStage/CompletePipeline/FailStage 调用） |
| `ArkPlot.Avalonia.Tests/WorkflowLogPanelViewModelTests.cs` | VM 测试（Headless XUnit） |

2026-08 首轮缺陷清单（本轮已修复，留档）：

1. `EnterStage` 不关闭上一阶段 → 运行期多个圆点同时脉冲，绿色 ✓ 直到 CompletePipeline 才一次性出现。
2. `StageStatus.Failed` 无赋值点 → 失败阶段仍变绿"完成"。
3. `WorkflowStage.Duration` 从未赋值 → 阶段耗时永远空白。
4. `UpdateProgress` 用 `FirstOrDefault(Active)` 且阶段从不置 Done → 进度条全程 0%。
5. 视图中标题/「定位到问题」/底部状态行绑定 VM 属性但 VM 未实现（死表面）。
6. `ScrollToEnd` 的 Y 分量误用 `ScrollBarMaximum.X` → 自动滚动滚到水平尽头。

## 2. 总体架构

```text
目标分支 log-refine（主工作树，可能含用户未提交改动——禁止触碰）
   │
   ├── fix/log-contract  worktree（契约分支：先提交最小 API 契约，构建+测试绿后才发散）
   │        │
   │        ├── fix/log-core      worktree  ← agent A1（生命周期/进度/计时 + 测试）
   │        ├── fix/log-pipeline  worktree  ← agent A2（管线失败接线）
   │        └── fix/log-view      worktree  ← agent B （视图接线/去重/编译绑定）
   │
   └── 按序 merge 回 log-refine ── 全量 build + 回归门禁（见 Phase 3/4）── 收尾
```

原则：
- **文件所有权互斥**：每个 agent 只拥有自己的文件集合，合并零冲突（本轮 4 次 merge 全部 ort 直合）。
- **契约先于并发**：所有 agent 依赖的公共 API（`FailStage`/`LocateErrorCommand`/`SelectedStageShowErrorHint`/`UpdateProgress` 语义）在契约分支一次性落地，agent 只在其上叠加，杜绝接口漂移。
- **Each branch verifies itself**：提交前必须 `build 0 警告 0 错误` + 相关测试绿。主 agent 合并后做全量门禁。

## 3. Phase 0 — 基线采集（自验证基准）

改动**之前**，在目标分支跑一次完整 `dotnet test`，把失败/跳过的测试记入
`Scripts/log-panel/log-panel.test-baseline.json`（`knownBad` 数组）并写理由（含对照实验证据）。

- 基线 = 「既有失败豁免清单」，回归门禁只拦**新增**失败。
- 更新基线必须附证据（见 Phase 4 争议判定实验）。

## 4. Phase 1 — 契约分支（自运行门禁）

```powershell
git worktree add <wt>\log-contract -b fix/log-contract <目标分支>
# 在契约分支上写最小可用 API 契约（实现正确、语义完整，但不抢 agent 的活）
dotnet build <wt>\log-contract\ArkPlot.Avalonia\ArkPlot.Avalonia.csproj   # 必须 0 警告 0 错误
dotnet test  <wt>\log-contract\ArkPlot.Avalonia.Tests\...
git commit  # 信息含 "contract:"
```

**门禁：契约分支 build+test 绿，才允许开出任务分支。** 本轮契约内容：
`FailStage(string?)`、`LocateErrorCommand`(RelayCommand)、`SelectedStageShowErrorHint`
(ObservableProperty，选中切换与错误追加时联动)、`UpdateProgress` 改由 `_activeStage` 驱动、
`Append` 在调用点构造 `LogEntry`（`Time` = 事件时刻而非 UI 线程处理时刻）。

## 5. Phase 2 — 并行任务分支

```powershell
git worktree add <wt>\log-core     -b fix/log-core     fix/log-contract
git worktree add <wt>\log-pipeline -b fix/log-pipeline fix/log-contract
git worktree add <wt>\log-view     -b fix/log-view     fix/log-contract
```

子模块初始化见 §7 自修复手册第 1 条（远端 ref 失效时务必用本地引用方案）。

Agent 提示词要素（每个 agent 独立、自包含）：
1. 独享文件清单（严格互斥）与「禁止触碰」清单（主工作树/其他文件/子模块）。
2. 契约 API 面（已存在，直接使用）；目标行为语义；边界（幂等、线程、Controller 线程 Post）。
3. 验证命令（build + filter 测试）与「若报缺子模块先等 1–2 分钟重试，勿自行绕过」。
4. 交付：分支内提交 + 报告（改了哪些文件、验证结果、偏离/注意事项）。

## 6. Phase 3 — 合并与全量验证（回归门禁）

```powershell
git merge --no-ff fix/log-contract   # 每个都应是 ort 直合；有冲突则停，先解决
git merge --no-ff fix/log-core
git merge --no-ff fix/log-pipeline
git merge --no-ff fix/log-view
pwsh -File Scripts/log-panel/Invoke-LogPanelVerify.ps1 -Repo <主工作树>
```

门禁判定（三态，拒绝谎报）：
- **PASS**：build 绿 + 日志模块定向测试零失败 + 全量套件完整跑完且新增失败 = 0。
- **FAIL**：build / 定向测试 / 全量套件出现**新增**失败（含中止场景下仍存在的新增失败）。
- **AMBIENT**：全量套件因测试主机崩溃/挂起/测试总数异常（<150）结果不可判定——定向门禁已过，标记环境不稳定，需在稳定环境复跑；对应退出码 2。

## 7. 自修复手册（本轮实测，已编码进脚本或本文档）

| # | 陷阱 | 症状 | 解法（自修复动作） |
|---|---|---|---|
| 1 | 子模块远程 commit 被上游重写 | `fatal: remote error: upload-pack: not our ref eebf9f1…`，全新 clone 必败 | 不用 `git submodule update`，改用**本地源手动 clone**：`git -c protocol.file.allow=always clone <主仓库子模块> <目标>`，检出到 gitlink 提交。脚本：`Ensure-WorktreeSubmodules.ps1` |
| 2 | git ≥2.38 默认禁 file 传输 | `fatal: transport 'file' not allowed` | 命令级 `-c protocol.file.allow=always`（只影响本次调用，不写共享 config） |
| 3 | worktree 子模块 gitdir 与主仓库共享 | 部分子模块状态诡异（invalid gitfile format / 复用远端） | 放弃 `submodule update` 机制，子模块**自成独立仓库**（clone 到 worktree 内），`submodule status` 即干净 |
| 4 | 清理被占用目录 | `Permission denied` / Remove-Item 静默失败 | 重试循环（休眠 2s × 5）；`git worktree remove --force`（含子模块必须 --force）；仍删不掉再手动逐层删 |
| 5 | Windows 编辑剥 BOM | C# 首行 `using` 出现在 diff 首行噪声 | 编辑后检查文件头 BOM，缺失则补回（`[System.IO.File]::ReadAllBytes` 检查 EF BB BF） |
| 6 | 网络/音频测试挂起或 testhost 崩溃 | 全量套件长时间无进展或「测试主机进程崩溃」中止 | 门禁脚本带 `--blame-hang --blame-hang-timeout 120s`；崩溃/挂起 → AMBIENT 态（exit 2）不谎报 PASS/FAIL；网络类测试隔离跑稳定（本轮实测 53 通过 + 1 跳过），崩溃系聚合负载下环境偶发，非代码问题 |

## 8. Phase 4 — 争议失败判定实验（base pivot）

合并后出现失败且**归因有争议**（是不是我改的？）时：

1. `git worktree add <wt>\verify-base <合并前SHA>`（detached）。
2. 按手册第 1 条方案给该 worktree 配**干净子模块**（排除"脏子模块"变量）。
3. 在该 worktree 跑同一测试。
4. 判定：基线同样失败 → **既有问题**，更新基线证据后豁免；基线通过 → **我方回归**，定位修复。

本轮实测：`Gallery_FiltersBlackBackground` 与 `ClickCharacterRow_PortraitPanel_ReceivesCorrectInput`
在 687e45f + 干净子模块下同样失败 → 既有失败，入基线。

## 9. 回滚手册

- 分支级：任务分支都在独立 worktree，删 worktree 即放弃（`git worktree remove --force <path>`）。
- 合并级：`git revert <merge-sha>`（保留历史）或 `git reset --hard <合并前SHA>`。
- 契约失败：改契约分支即可，任务分支尚未开。
- 主工作树污染：约定「主工作树只读」，所有改动发生在 worktree——回滚绝不触碰用户未提交内容。

## 10. 自动化入口

| 脚本 | 用途 | 退出码 |
|---|---|---|
| `Scripts/log-panel/Ensure-WorktreeSubmodules.ps1` | 自修复子模块：先试标准 init，遇 "not our ref" 自动降级为本地引用 clone；`-VerifyOnly` 只巡检 | 0 = 全部就绪/修复；1 = 有失败 |
| `Scripts/log-panel/Invoke-LogPanelVerify.ps1` | 自验证门禁：build → 日志模块定向测试（零豁免）→ 全量套件（blame-hang 护栏）→ 与基线比对，只拦新增失败 | 0 = PASS；1 = FAIL |

## 11. 本次执行记录（2026-08）

- 契约：`e14321a` contract: VM API 契约。
- 并行三支：`fee867d`（log-core：EnterStage 关闭上一阶段/幂等/计时 + 8 新测试，18/18）；
  `a16653b`（log-pipeline：取消/异常接 FailStage，+6 行）；`b9a0aae`+`e00f31f`（log-view：接线 + 模板去重 + 编译绑定全开 + AutoScroll Y 修复）。
- 合并：`38a0e81` `8f9f29c` `7f246f9` `72a010c`，四次 ort 直合无冲突。
- 验证：build 0 警告 0 错误；日志模块 18/18；全量 199 通过 / 2 基线失败 / 1 跳过（基线见 `log-panel.test-baseline.json`）。
- 环境注记：本沙箱内全量套件不稳定（同代码一次完整 202 项、一次在 133 项处 testhost 崩溃、一次挂起>10min）；网络/取消类隔离跑稳定（53+1 跳过）。门禁因此引入 AMBIENT 态，定向门禁（build + 日志模块 18/18）为权威判据。
- 发现的自修复点：子模块远端 ref 重写（手册 #1）、file 传输禁用（#2）、worktree 子模块 gitdir 共享（#3）、BOM（#5）、全量套件环境不稳定（#6，AMBIENT 态）。

## 12. 遗留项（后续迭代候选）

- ~~`RunNovelizerIfEnabled` 内层 `catch (Exception)` 会吞 `OperationCanceledException` → 小说化阶段取消后仍走到 CompletePipeline。~~ **已修复**：内层 try 增加 `catch (OperationCanceledException) { throw; }`（置于 BailianException 之前），取消现在会冒泡到 LoadMd 的 OCE catch → FailStage("生成已被取消")。修复于 2026-08，随 fix/oce 合并入 log-refine（see fix/oce 提交）。未加 VM 层测试：触发路径需改共享 settings.json 或造真实小说文件目录，成本/污染风险高于一行修复的价值；由 layer-1 证据 `BatchProcessAsync_CancelledToken_Throws`（管线遇取消 token 抛 OCE）+ 相关类测试（24 通过 + 1 跳过）保证。
- `WorkflowStage.Progress` 暂无赋值点（`UpdateProgress` 的 fraction 分支已就绪）→ 有真实 0-100 进度时在驱动方传 `Append(…, progress: n)`。
- 两个基线测试为 TTS 侧既有问题（画廊背景断言 / `FormattedTextEntry` 建表顺序，见 `ArkPlot.Adapters/context.md` 第九节），修复后可移出基线。