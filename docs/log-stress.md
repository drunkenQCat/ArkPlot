# 日志面板压力测试

无需点击界面，启动后直接向实际工作流日志面板灌入日志。跳过资源同步、剧情下载和模型调用；不需要 API Key。

```powershell
dotnet run --project ArkPlot.Avalonia -- --log-stress
```

默认生成 50,000 条混合级别日志，每批 100 条，批间隔 10 毫秒。包含分段标题和可展开详情。结束后保留窗口，运行中可用主窗口的停止按钮取消。

自动复测并退出：

```powershell
dotnet run --project ArkPlot.Avalonia -- --log-stress --log-stress-count 50000 --log-stress-batch 100 --log-stress-interval 10 --log-stress-report ./tmp_artifact/log-stress.jsonl --log-stress-exit
```

已有构建可直接运行 `dotnet 路径/ArkPlot.Avalonia.dll --log-stress ...`。已打包程序可直接传相同参数给可执行文件。

参数：

| 参数 | 默认值 | 含义 |
| --- | --- | --- |
| `--log-stress` | 关闭 | 开启离线日志压测 |
| `--log-stress-count` | 50000 | 日志总数，正整数 |
| `--log-stress-batch` | 100 | 每批条数，1–1000 |
| `--log-stress-interval` | 10 | 批间隔，正整数毫秒；实际速度受 UI 布局影响 |
| `--log-stress-report` | `./log-stress.jsonl` | 指标路径，相对当前工作目录；覆盖已有同名文件 |
| `--log-stress-exit` | 关闭 | 结束后自动退出；异常退出码为 1，参数错误为 2 |

报告在开始、每约 1000 条及结束时记录指标，并同步写标准输出。`state=completed` 表示完成，`cancelled` 表示中止。另在报告路径后追加 `.log` 保存全部生成的日志。

重点观察 `retained`（完整历史）、`visible`（展示集合，最多 1000）、`realizedRows`（实际视觉树中的行控件）、`scrollOffset/scrollMaximum`（是否跟随到底）和内存指标。完整历史有意保留，内存会随数据增加；可见集合上限不等于控件虚拟化有效。比较不同总数下的实际行控件数和内存增速。

压测结束后保持离线状态，正常生成需重启且去掉压测参数。

## 2026-11-03 实测

Windows、Avalonia 11.3.22，50,000 条日志、每批 1,000 条、间隔 1 毫秒：约 151 秒完成。完整历史及落盘日志均为 50,000 条，可见集合为 1,000 条，结束时实际行控件为 12 个，滚动位置等于最大值。

GC 堆快照中，32,000 条及结束时的 `StyledProperty<bool>` 均为 97 个；结束时存活 `LogRow` 为 12 个。本次离线日志压测未复现属性对象暴涨；该结果不覆盖联网、图片或模型调用流程。
