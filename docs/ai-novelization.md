# 🤖 AI 小说化配置教程

ArkPlot 可以调用大语言模型，将方舟的 AVG 剧本转化为连贯的小说文本。本文档从零开始，一步步完成小说化功能的配置和使用。

## 第一步：选择平台

ArkPlot 支持三个小说化平台：

| 平台 | 推荐场景 | 成本 |
|------|----------|------|
| **DeepSeek 官方** | 追求高质量、文学性强 | V4 Pro 约 ¥2/百万token，Flash 约 ¥0.1/百万token |
| **阿里云百炼** | 想用多种模型（GLM、Kimi、MiniMax） | 各模型定价不同，通常比 DeepSeek 官方略贵 |
| **自定义 Provider** | 有其他 OpenAI 兼容接口（OpenRouter、Groq 等） | 取决于你的平台 |

**推荐**：首次使用建议选 **DeepSeek 官方 + V4 Flash**，成本低、速度快，效果足够日常使用。

## 第二步：获取 API Key

### DeepSeek 官方

1. 打开 [DeepSeek 开放平台](https://platform.deepseek.com/)
2. 注册/登录后，进入「API Keys」页面
3. 点击「创建 API Key」，复制生成的密钥

### 阿里云百炼

1. 打开 [百炼控制台](https://bailian.console.aliyun.com/)
2. 进入「API-KEY 管理」页面
3. 点击「创建 API Key」，复制生成的密钥

## 第三步：配置 ArkPlot

有两种配置方式：

### 方式 A：环境变量（推荐）

将 API Key 设为环境变量，ArkPlot 会自动读取：

```bash
# DeepSeek
export DEEPSEEK_API_KEY="sk-xxxxxxxx"

# 百炼
export DASHSCOPE_API_KEY="sk-xxxxxxxx"
```

Windows 用户可在「系统属性 → 高级 → 环境变量」中设置，或临时设置：

```powershell
$env:DEEPSEEK_API_KEY = "sk-xxxxxxxx"
```

### 方式 B：设置页直接填写

1. 打开 ArkPlot 主窗口
2. 点击右上角「设置」按钮
3. 进入「小说化设置」标签页
4. 在「API Key」输入框中直接粘贴密钥

> **注意**：设置页填写的 Key 会保存到本地配置文件，适合不想折腾环境变量的用户。

## 第四步：选择模型

| 平台 | 可选模型 | 特点 |
|------|----------|------|
| DeepSeek 官方 | `deepseek-v4-pro` | 高质量，文学性强，细节丰富 |
| DeepSeek 官方 | `deepseek-v4-flash` | 快速，成本低，日常够用 |
| 百炼 | `deepseek-v4-pro` / `flash` | 同 DeepSeek 官方 |
| 百炼 | `glm-5` | 智谱 GLM |
| 百炼 | `MiniMax-M2.5` | MiniMax |
| 百炼 | `kimi-k2.5` | 月之暗面 Kimi |

在设置页的「模型选择」下拉框中选择。

## 第五步：启用小说化

1. 回到 ArkPlot 主窗口
2. 勾选左下角的「启用小说化生成」
3. 选择活动名，点击「开始」

 ArkPlot 会：
- 先解析原始剧情文本，生成 Markdown / HTML
- 然后逐章调用 LLM，将剧本转化为小说
- 多章并行处理（最多 3 章），不阻塞 UI

## 第六步：查看输出

生成完成后，输出目录会有：

```
无忧梦呓.md                           # 原始剧本 Markdown
无忧梦呓.html                         # 原始剧本 HTML
无忧梦呓_novel_deepseek-v4-flash.md   # 小说 Markdown
无忧梦呓_novel_deepseek-v4-flash.html # 小说 HTML（含角色名染色）
无忧梦呓_novel_deepseek-v4-flash.epub # epub 电子书（需安装 Pandoc）
```

文件名后缀会随实际使用的模型名变化。

## EPUB 输出（可选）

如需生成 epub 电子书：

1. 安装 [Pandoc](https://pandoc.org/installing.html)
2. 确保 `pandoc` 命令可用（终端运行 `pandoc --version` 验证）
3. 小说化生成时会自动输出 epub

## 自定义 Provider（高级）

如果你想用 OpenRouter、Groq、Together AI 等其他平台：

1. 进入「设置页 → 小说化设置 → 自定义平台管理」
2. 点击「添加平台」
3. 填写：
   - **平台名称**：如 `OpenRouter`
   - **API Base URL**：如 `https://openrouter.ai/api/v1`
   - **API Key**：对应平台的密钥
   - **模型列表**：逗号分隔，如 `gpt-4o,claude-3.5-sonnet`
4. 保存后，平台选择下拉框中会出现新平台

## 常见问题

**Q: 小说化很慢怎么办？**

A: 每章独立调用 API，多章并行最多 3 章。如果活动有 20+ 章，总耗时可能在 5-10 分钟。建议先用 V4 Flash 模型，速度快、成本低。

**Q: 重复运行会重复调用 API 吗？**

A: 不会。生成结果缓存基于源文件 MD5 hash，只有源文件变化才会重新调用。

**Q: 小说质量不满意怎么办？**

A: 模型选择影响最大。V4 Pro 比 Flash 文学性更强，细节更丰富。你也可以在 `ArkPlot.Novelizer` 项目中修改系统提示词（`DefaultSystemPrompt`），调整生成风格。

**Q: 百炼平台怎么选模型？**

A: 百炼支持 DeepSeek V4 系列，也支持 GLM、Kimi、MiniMax 等。价格和效果各有差异，建议先试用 `deepseek-v4-flash`，再根据需求切换。