# ArkPlot / SukiUI 布局原型

直接打开 `index.html` 即可使用，所有资源均在本目录，无运行时网络依赖。也可在本目录运行 `python -m http.server 4173 --bind 127.0.0.1` 后访问 `http://127.0.0.1:4173/`。

这是布局与交互评审稿。章节、服务配置、生成状态和输出文件都是示例；不会调用模型、发送或持久化 API Key、创建真实生成结果。顶部状态选择器与底部组件说明仅用于评审，不进入 Avalonia 成品。

## 主题依据

以项目实际使用的 SukiUI **6.0.3 / Blue** 为依据，源版本为 `501562183e4d907c896eb10c4a8a318ab26eb022`，不是独立创建的网页主题。

- [主题定义](https://github.com/kikipoulet/SukiUI/blob/501562183e4d907c896eb10c4a8a318ab26eb022/SukiUI/Theme/Index.axaml.cs)：主色 `#0A59F7`，强调色 `#F7A80A`。
- [Light](https://github.com/kikipoulet/SukiUI/blob/501562183e4d907c896eb10c4a8a318ab26eb022/SukiUI/ColorTheme/Light.axaml) / [Dark](https://github.com/kikipoulet/SukiUI/blob/501562183e4d907c896eb10c4a8a318ab26eb022/SukiUI/ColorTheme/Dark.axaml)：沿用文本、背景、边框与状态颜色；HTML 的玻璃合成作近似呈现，Avalonia 应直接使用主题资源。
- GlassCard 默认圆角 20；按钮基础圆角 8；Chips 与 Rounded 按钮为胶囊形；交互包含选中、悬停、焦点、禁用状态。
- Banner 原样取自 `ArkPlot.Avalonia/Assets/slogan.jpg`，保留顶部构图，未替换插画或追加宣传文案。
- 图标取自 Material Design Icons 7.4.47，授权文件见 `assets/MDI-LICENSE.txt`。`icons.js` 内嵌原始 SVG 以兼容直接打开 HTML 时的 CSS mask。

## Avalonia 复刻对应

| 原型区域 | Avalonia / SukiUI 对应 | 行为 |
| --- | --- | --- |
| 窗口与主题切换 | SukiWindow、SukiTheme | 使用现有主题资源，不硬编码整套新配色 |
| Banner | 现有 ImageBrush / Border | 保留原图与原有入口行为 |
| 剧情类别 | RadioButton.Chips | 当前分类明确选中 |
| 左右内容分组 | GlassCard | 左侧选择剧情，右侧配置与运行结果 |
| 活动 / 语言 | SukiUI ComboBox | 活动切换后刷新章节 |
| 章节 | ListBox + CheckBox | 范围、全选与清空直接可见 |
| 产物选择 | RadioButton / GlassCard.IsInteractive | 剧本、小说、有声书按目标选择 |
| 主操作 | Button.Rounded + ButtonExtensions | 唯一主操作，显示章节数量 |
| 输出选项 | Expander + ToggleSwitch | 缓存、画面描述按需展开 |
| 运行反馈 | Stepper + InfoBar | 明确真实阶段、部分失败与保留结果；不伪造时间百分比 |
| 设置 | SukiDialog + ComboBox + TextBox | 先连接模型，高级参数后置 |
| 短反馈 | SukiToast | 同步完成与配置更新反馈 |

## 已检查

使用本机无头 Chromium 实际截图并检查浅色、深色、运行中、部分失败，以及 1080 / 800 / 390 宽度；无横向溢出和页面脚本错误。

通过交互检查：空选择禁止生成、章节全选、活动切换、产物切换、缺少模型时打开设置、保存演示配置、生成与停止、失败重试、小说到配音交接、模拟音频导出。截图在本目录 `preview-*.png`。

主界面、设置与日志布局已复刻到 Avalonia。`settings.html` 和 `logs.html` 是新增原型，`native/` 保存实际原生渲染截图，`qa-compare.html` 提供对照；详细检查记录见 `design-qa.md`。
