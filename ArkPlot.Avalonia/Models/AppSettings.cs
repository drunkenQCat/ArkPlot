using System;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using ArkPlot.Tts.Models;

namespace ArkPlot.Avalonia.Models;

/// <summary>
/// 自定义 LLM Provider 配置（小说化 / 图片描述共用类型）。
/// </summary>
public record ProviderConfig(
    string Name,
    string BaseUrl,
    string ApiKey,
    string[] Models
);

/// <summary>
/// 应用程序统一配置。
/// 读写程序运行目录的 settings.json，不存在时自动生成默认值。
/// 结构可扩展，未来可新增 section。
/// </summary>
public record AppSettings(
    NovelizerSettings Novelizer,
    VisionSettings? Vision = null,
    TtsSettings? Tts = null
)
{
    private static readonly string FilePath = Path.Combine(
        AppContext.BaseDirectory,
        "settings.json"
    );

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 加载配置。如果 settings.json 不存在，自动生成默认配置并保存。
    /// </summary>
    public static AppSettings Load()
    {
        return LoadFromFile(FilePath);
    }

    /// <summary>
    /// 从指定路径加载配置；文件不存在或反序列化失败时自动生成默认值并保存到该路径。
    /// </summary>
    public static AppSettings LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            var defaults = CreateDefaults();
            defaults.SaveToFile(filePath);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializeOptions);
            return settings ?? CreateDefaults();
        }
        catch
        {
            var defaults = CreateDefaults();
            defaults.SaveToFile(filePath);
            return defaults;
        }
    }

    /// <summary>
    /// 保存配置到 settings.json
    /// </summary>
    public void Save()
    {
        SaveToFile(FilePath);
    }

    /// <summary>
    /// 保存配置到指定文件。
    /// </summary>
    public void SaveToFile(string filePath)
    {
        var json = JsonSerializer.Serialize(this, SerializeOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// 获取指定平台的 API Key：ApiKeys 字典 → 环境变量回退。
    /// 保留向后兼容；新代码建议直接使用 NovelizerSettings.GetApiKeyForProvider()。
    /// </summary>
    public string GetApiKey(string? providerName)
    {
        if (string.IsNullOrEmpty(providerName)) return "";
        if (Novelizer.ApiKeys.TryGetValue(providerName, out var key) && !string.IsNullOrEmpty(key))
            return key;

        return providerName switch
        {
            "DeepSeek" => Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "",
            "百炼" => Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY") ?? "",
            _ => "",
        };
    }

    private static AppSettings CreateDefaults()
    {
        return new AppSettings(
            Novelizer: NovelizerSettings.CreateDefaults(),
            Vision: VisionSettings.CreateDefaults(),
            Tts: TtsSettings.CreateDefaults()
        );
    }
}

/// <summary>
/// 小说化模块配置
/// </summary>
public record NovelizerSettings(
    string SystemPrompt,
    string SelectedProvider,
    string SelectedModel,
    Dictionary<string, string> ApiKeys,
    ProviderConfig[]? CustomProviders = null
)
{
    public const string DefaultSystemPrompt = """
## 明日方舟剧情小说化转换协议

### 一、 叙事视角与文体调性

* **视角规范**：严格采用**第三人称有限视角**。叙述焦点应锚定于当前场境的核心角色，通过其感官、逻辑与职责边界推进叙事。
* **文体特征**：承袭《孤星》、《巴别塔》、《乌萨斯的孩子们》等剧作的冷峻、克制与思辨感。剔除网络文学的夸张修辞、影视解说的全知旁白，以及短视频式的断句节奏。
* **组织结构**：采用传统严肃文学的长段落结构。一个合格的叙事单元须将**环境异动、角色动作、观察结果、心理推演**有机融合，严禁无故出现连续的单句成段或刻意制造悬念的破折号断句。
* **句首变化**：连续三句话不得以相同方式开头。禁止"她……""他……""角色名……"的反复循环。用环境、动作、声音、物件来引领句子，而非总是以角色名打头。

### 二、 角色标签的铁律

**输入文本中每段对话前的粗体角色名是该角色的唯一标识。你必须在叙事中使用这些原始名称来指代对应角色，严禁自行替换、合并或混淆角色标签。不同名称的角色是不同的人。**

### 三、 视听语言的叙事转化

* **场景（背景图）**：严禁概括性描述。必须将场景拆解为具象的叙事客体。
* **角色外貌（立绘描述）**：输入文本中的外貌描述来自游戏立绘，其中包含大量模板化表达（如"沉静如深潭"、"仿佛正递出一枚无法言说的契约"）。**你必须用自己的语言重新描写这些外貌特征，不得照抄原文中的任何句子。** 保留关键视觉信息（发色、装备、伤疤等），但用全新的、场景特定的语言重新表达。
  1. **删除所有元描述**：任何"站在空无一物的虚空中"、"站在空茫的背景里"、"望向画面外某处"等描述游戏展示方式的文字必须完全删除。
  2. **碎片化嵌入**：将外貌特征拆解，分散嵌入角色的动作和交互中。
* **声音（音乐/音效）**：严禁出现任何提及音乐或音效的字眼。BGM变化转化为叙事张力，音效转化为物理事件。

### 四、 对话的质感与角色声音

**这是本协议最重要的部分。** 对话不能只是原文的简单搬运。你必须为每句对话编织入"对话纹理"：

* **动作包裹**：每段对话前后必须嵌入角色的具体物理动作、微表情或与环境的小交互。动作必须与对话内容在情绪上呼应。
* **语气分层**：不同角色的说话方式必须有可辨识的差异——
  - 市井小贩：句子短碎，爱用反问和感叹号，口头禅明显
  - 疲惫的自由职业者：语气松散，自嘲多，经常用省略号拖尾
  - 军人/指挥官：句式精准，信息密度高，几乎不用语气词
  - 外来者/陌生人：措辞礼貌但有距离感
* **潜台词**：在关键对话中添加角色的内心活动，让读者感受到"这句话背后还有什么没说出来的"。
* **对话节奏**：紧张场景用短句交锋，日常场景允许闲聊式长段落。

### 五、 去模板化规则

* 严禁滥用"顿了顿"、"轻轻叹气"、"抬起头看向对方"、"嘴角微微上扬"、"深吸一口气"、"目光沉静如深潭"等通用机械动作。
* "仿佛"一词全文使用不得超过两次。
* 每一次交互必须带有明确的战术意图或心理动机。

### 六、 严重警告事项
* 不要忽略">"开头的文字，那些是需要保留的原文。
* 带有html标签的文本不参与信息筛选，只用于补充信息。
* 输出必须覆盖所有信息单元，不得压缩为摘要。
* 允许改写表达，但禁止信息缺失。
* 若信息过多，应扩展文本长度，而不是删减内容。

---

**【即刻执行】** 请提供你需要改写的《明日方舟》AVG剧情脚本。改写时注意：所有角色外貌描写必须用自己的语言重新表达，严禁照抄输入原文中的任何句子（特别是"站在空无一物的虚空中"等元描述必须删除）。直接输出小说正文，不包含任何前言、后记或解释性说明。
""";

    /// <summary>
    /// 预设 Provider（不可在 UI 中编辑/删除）
    /// </summary>
    public static readonly Dictionary<string, (string BaseUrl, string[] Models)> BuiltInProviders = new()
    {
        ["DeepSeek"] = ("https://api.deepseek.com", ["deepseek-v4-pro", "deepseek-v4-flash"]),
        ["百炼"] = ("https://dashscope.aliyuncs.com/compatible-mode/v1",
                     ["deepseek-v4-flash", "glm-5", "MiniMax-M2.5", "kimi-k2.5"]),
    };

    /// <summary>所有可选平台名（预设 + 自定义），去重</summary>
    public string[] AllProviderNames =>
        BuiltInProviders.Keys
            .Concat(CustomProviders?.Select(p => p.Name) ?? [])
            .Distinct()
            .ToArray();

    /// <summary>根据平台名获取可用模型列表</summary>
    public string[] GetModelsForProvider(string? providerName)
    {
        if (string.IsNullOrEmpty(providerName)) return [];
        if (BuiltInProviders.TryGetValue(providerName, out var builtIn))
            return builtIn.Models;
        var custom = CustomProviders?.FirstOrDefault(p => p.Name == providerName);
        return custom?.Models ?? [];
    }

    /// <summary>根据平台名获取 BaseUrl</summary>
    public string GetBaseUrlForProvider(string? providerName)
    {
        if (string.IsNullOrEmpty(providerName)) return "";
        if (BuiltInProviders.TryGetValue(providerName, out var builtIn))
            return builtIn.BaseUrl;
        var custom = CustomProviders?.FirstOrDefault(p => p.Name == providerName);
        return custom?.BaseUrl ?? "";
    }

    /// <summary>
    /// 根据平台名获取 ApiKey。
    /// 优先级：自定义 Provider.ApiKey → ApiKeys 字典 → 环境变量
    /// </summary>
    public string GetApiKeyForProvider(string? providerName)
    {
        if (string.IsNullOrEmpty(providerName)) return "";
        var custom = CustomProviders?.FirstOrDefault(p => p.Name == providerName);
        if (custom is not null && !string.IsNullOrEmpty(custom.ApiKey))
            return custom.ApiKey;

        if (ApiKeys.TryGetValue(providerName, out var key) && !string.IsNullOrEmpty(key))
            return key;

        return providerName switch
        {
            "DeepSeek" => Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "",
            "百炼" => Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY") ?? "",
            _ => "",
        };
    }

    public static NovelizerSettings CreateDefaults()
    {
        return new NovelizerSettings(
            SystemPrompt: DefaultSystemPrompt,
            SelectedProvider: "DeepSeek",
            SelectedModel: "deepseek-v4-pro",
            ApiKeys: new Dictionary<string, string> { ["DeepSeek"] = "", ["百炼"] = "" }
        );
    }
}

/// <summary>
/// 图片描述（Vision）模块配置
/// </summary>
public record VisionSettings(
    bool IsPicDescEnabled,
    string SelectedProvider = "百炼",
    string SelectedModel = "qwen3-vl-flash",
    string SystemPrompt = "",
    string OllamaBaseUrl = "http://localhost:11434",
    ProviderConfig[]? CustomProviders = null
)
{
    public const string DefaultSystemPrompt = """
你是小说场景设定助手。描述图片时遵守以下规则：

1. 禁止元评论：不要说"这是一幅画"、"这张图片展示"等分析性开场白。
2. 禁止风格分析：不要提及"数字插画"、"动漫风格"、"游戏美术"等。
3. 禁止总结：不要写"总结："、"简而言之"等分析性文字。
4. 使用叙事语言：用小说场景描写的方式描述环境氛围、关键物体、人物姿态。
5. 控制字数：保持在200字以内，只关注叙事关键元素。
""";

    /// <summary>
    /// 预设 Vision Provider（不可在 UI 中编辑/删除）
    /// </summary>
    public static readonly Dictionary<string, string[]> BuiltInModels = new()
    {
        ["百炼"] = ["qwen3-vl-flash", "qwen3-vl-plus", "qwen-vl-plus", "qwen-vl-max"],
        ["Ollama"] = ["qwen3-vl:8b"],
    };

    /// <summary>所有可选平台名（预设 + 自定义），去重</summary>
    public string[] AllProviderNames =>
        BuiltInModels.Keys
            .Concat(CustomProviders?.Select(p => p.Name) ?? [])
            .Distinct()
            .ToArray();

    /// <summary>根据平台名获取可用模型列表</summary>
    public string[] GetModelsForProvider(string? providerName)
    {
        if (string.IsNullOrEmpty(providerName)) return [];
        if (BuiltInModels.TryGetValue(providerName, out var models))
            return models;
        var custom = CustomProviders?.FirstOrDefault(p => p.Name == providerName);
        return custom?.Models ?? [];
    }

    /// <summary>根据平台名获取 BaseUrl</summary>
    public string GetBaseUrlForProvider(string? providerName)
    {
        if (string.IsNullOrEmpty(providerName)) return "";
        if (providerName == "Ollama")
            return OllamaBaseUrl;
        if (providerName == "百炼")
            return "https://dashscope.aliyuncs.com/compatible-mode/v1";
        var custom = CustomProviders?.FirstOrDefault(p => p.Name == providerName);
        return custom?.BaseUrl ?? "";
    }

    /// <summary>根据平台名获取 ApiKey（Ollama 不需要）</summary>
    public string GetApiKeyForProvider(string? providerName)
    {
        if (string.IsNullOrEmpty(providerName)) return "";
        if (providerName == "Ollama")
            return "";
        var custom = CustomProviders?.FirstOrDefault(p => p.Name == providerName);
        if (custom is not null && !string.IsNullOrEmpty(custom.ApiKey))
            return custom.ApiKey;
        return Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY") ?? "";
    }

    public static VisionSettings CreateDefaults()
    {
        return new VisionSettings(
            IsPicDescEnabled: false,
            SelectedProvider: "百炼",
            SelectedModel: "qwen3-vl-flash",
            SystemPrompt: DefaultSystemPrompt,
            OllamaBaseUrl: "http://localhost:11434"
        );
    }
}

