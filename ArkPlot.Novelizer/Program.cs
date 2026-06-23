using System.Text.Json;
using ArkPlot.Core.Infrastructure;
using ArkPlot.Core.Model;
using ArkPlot.Core.Services;
using ArkPlot.Core.Utilities.WorkFlow;
using ArkPlot.Core.Utilities.WorkFlow.StoryDocument;

namespace ArkPlot.Novelizer;

class Program
{
    static async Task<int> Main(string[] args)
    {
        LoadEnvFile();

        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].ToLowerInvariant();

        return command switch
        {
            "run" => await RunAsync(args[1..]),
            "test" => await TestAsync(args[1..]),
            "verify" => VerifyAsync(args[1..]),
            _ => PrintUsageWithError($"未知命令: {command}")
        };
    }

    static async Task<int> RunAsync(string[] args)
    {
        var (input, compare, force, model, provider, promptFile, tag, multiTurn, chunkSize, compressInterval) = ParseRunArgs(args);
        var config = LoadConfig(provider);

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            Console.Error.WriteLine("❌ 未配置 API Key。请设置 DEEPSEEK_API_KEY 或 DASHSCOPE_API_KEY 环境变量");
            return 1;
        }

        // 将 "flash"/"pro" 映射到 config.Models 中的实际模型名
        string[] models;
        if (model is not null)
        {
            var resolved = model.Contains("flash")
                ? config.Models.Last(m => m.Contains("flash"))
                : config.Models.First(m => !m.Contains("flash"));
            models = [resolved];
        }
        else
        {
            models = compare ? config.Models : [config.Models[0]];
        }

        // 加载自定义 system prompt
        string? customPrompt = null;
        if (promptFile is not null)
        {
            if (!File.Exists(promptFile))
            {
                Console.Error.WriteLine($"❌ Prompt 文件不存在: {promptFile}");
                return 1;
            }
            customPrompt = File.ReadAllText(promptFile);
            Console.WriteLine($"📝 自定义 Prompt: {Path.GetFileName(promptFile)} ({customPrompt.Length} 字符)");
        }

        var outputTag = tag ?? models[0];
        Console.WriteLine($"🔌 平台: {config.Provider}, 模型: {string.Join(", ", models)}, 标签: {outputTag}");

        using var http = new HttpClient();
        var client = new BailianClient(http, config);

        if (multiTurn)
        {
            var compressInfo = compressInterval > 0 ? $", 每 {compressInterval} 轮压缩" : "";
            Console.WriteLine($"🔄 多轮对话模式: chunkSize={chunkSize}{compressInfo}");
        }

        var pipeline = new NovelizerPipeline(
            client, config,
            systemPrompt: customPrompt,
            enableMultiTurn: multiTurn,
            chunkSize: chunkSize,
            compressInterval: compressInterval);

        if (Directory.Exists(input))
        {
            await pipeline.BatchProcessAsync(input, models, force);
        }
        else if (File.Exists(input))
        {
            if (input.EndsWith(".json"))
            {
                var json = File.ReadAllText(input);
                var entries = JsonSerializer.Deserialize<List<FormattedTextEntry>>(json) ?? [];

                foreach (var m in models)
                {
                    var outputPath = Path.ChangeExtension(input, null) + $"_novel_{m}.md";
                    await pipeline.ProcessEntriesAsync(entries, m, outputPath, Path.GetFileName(input));
                }
            }
            else if (input.EndsWith(".md"))
            {
                var dir = Path.GetDirectoryName(input) ?? ".";
                var cache = new ChapterCache(dir);

                foreach (var m in models)
                {
                    var cacheKey = tag is not null ? $"{m}::{tag}" : m;
                    var cached = cache.Check(input, cacheKey, force);
                    if (cached is not null)
                    {
                        Console.WriteLine($"⏭️  跳过（缓存命中）: {Path.GetFileName(cached)}");
                        continue;
                    }

                    try
                    {
                        await pipeline.ProcessMdFileAsync(input, m, dir, tag);
                        cache.Update(input, cacheKey);
                    }
                    catch (BailianException ex)
                    {
                        Console.Error.WriteLine($"❌ [{m}] 失败: {ex.Message}");
                    }
                }
            }
            else
            {
                Console.Error.WriteLine($"❌ 不支持的文件类型: {input}");
                return 1;
            }
        }
        else
        {
            Console.Error.WriteLine($"❌ 路径不存在: {input}");
            return 1;
        }

        return 0;
    }

    static async Task<int> TestAsync(string[] args)
    {
        var (input, _, _, model, provider, _, _, _, _, _) = ParseRunArgs(args);
        if (string.IsNullOrEmpty(input))
        {
            Console.Error.WriteLine("用法: Novelizer test <example_data.json> [--model flash|pro] [--provider deepseek|bailian]");
            return 1;
        }

        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"❌ 文件不存在: {input}");
            return 1;
        }

        var config = LoadConfig(provider);
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            Console.Error.WriteLine("❌ 未配置 API Key");
            return 1;
        }

        Console.WriteLine($"🔌 平台: {config.Provider}");

        var json = File.ReadAllText(input);
        var entries = JsonSerializer.Deserialize<List<FormattedTextEntry>>(json) ?? [];
        Console.WriteLine($"✅ 已加载 {entries.Count} 条数据");

        var novelInput = MarkdownBuilder.BuildNovelInput(entries);
        Console.WriteLine($"📝 构建输入 ({novelInput.Length} 字符):");
        Console.WriteLine(new string('-', 40));
        Console.WriteLine(novelInput);
        Console.WriteLine(new string('-', 40));

        using var http = new HttpClient();
        var client = new BailianClient(http, config);
        var pipeline = new NovelizerPipeline(client, config);

        if (model is not null)
        {
            // 单模型测试
            var m = model.Contains("flash") ? config.Models.Last(m => m.Contains("flash")) : config.Models.First(m => !m.Contains("flash"));
            var outputPath = Path.ChangeExtension(input, null) + $"_novel_{model}.md";
            await pipeline.ProcessEntriesAsync(entries, m, outputPath, Path.GetFileName(input));
        }
        else
        {
            // 双模型对比
            var proOutput = Path.ChangeExtension(input, null) + $"_novel_{config.Models[0]}.md";
            await pipeline.ProcessEntriesAsync(entries, config.Models[0], proOutput, Path.GetFileName(input));

            if (config.Models.Length > 1)
            {
                var flashOutput = Path.ChangeExtension(input, null) + $"_novel_{config.Models[1]}.md";
                await pipeline.ProcessEntriesAsync(entries, config.Models[1], flashOutput, Path.GetFileName(input));
            }
        }

        return 0;
    }

    /// <summary>
    /// 反照抄验证：从数据库读取孤星第一章 → 生成 Prompt 模式中间产物 MD。
    /// 生成的 MD 可直接用 `Novelizer run` 跑小说化。
    /// </summary>
    static int VerifyAsync(string[] args)
    {
        string? dbPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                dbPath = args[++i];
        }

        // 1. 定位数据库
        dbPath ??= FindAvaloniaDbPath();
        if (!File.Exists(dbPath))
        {
            Console.Error.WriteLine($"❌ 数据库不存在: {dbPath}");
            Console.Error.WriteLine("   请用 --db <path> 指定 arkplot.db 路径，或先运行 Avalonia 生成数据库");
            return 1;
        }
        Console.WriteLine($"📂 数据库: {dbPath}");

        DbFactory.ConfigureForTesting($"Data Source={dbPath}");
        var db = DbFactory.GetClient();

        // 2. 查找孤星第一章 (CW-ST-1)
        var plot = db.Queryable<Plot>()
            .Where(p => p.Title.Contains("CW-ST-1"))
            .First();

        if (plot == null)
        {
            Console.Error.WriteLine("❌ 未找到孤星第一章 (CW-ST-1)。请先在 Avalonia 中解析孤星活动。");
            return 1;
        }

        var entries = db.Queryable<FormattedTextEntry>()
            .Where(e => e.PlotId == plot.Id)
            .OrderBy(e => e.Index)
            .ToList();

        Console.WriteLine($"📖 {plot.Title} ({entries.Count} 条)");

        // 3. 传播 CharacterCode：charslot 条目的 CharacterCode 传播到后续同名对话条目
        PropagateCharacterCode(entries);

        // 4. 从 PicDescription 表填充 PicFacts
        PopulatePicFacts(entries, db);

        // 5. Prompt 模式生成 MD
        var reconstructor = new StoryDocumentBuilder(
            new List<FormattedTextEntry>(entries.Select(e => new FormattedTextEntry(e))),
            enableDescriptions: true,
            outputMode: OutputMode.PromptOptimized);

        var md = new System.Text.StringBuilder();
        md.Append($"## {plot.Title}\r\n\r\n");
        reconstructor.AppendResultToBuilder(md);
        var mdStr = md.ToString();

        // 5. 输出到项目根目录
        var outputDir = FindProjectOutputDir();
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, $"{plot.Title}_prompt.md");
        File.WriteAllText(outputPath, mdStr);

        // 6. 统计
        int Count(string pattern)
        {
            int count = 0, index = 0;
            while ((index = mdStr.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0) { count++; index += pattern.Length; }
            return count;
        }

        Console.WriteLine($"\n📊 统计:");
        Console.WriteLine($"  scene-facts:    {Count("<aside class=\"scene-facts\"")}");
        Console.WriteLine($"  portrait-facts: {Count("<aside class=\"portrait-facts\"")}");
        Console.WriteLine($"  item-facts:     {Count("<aside class=\"item-facts\"")}");
        Console.WriteLine($"  旧 scene-desc:  {Count("class=\"scene-desc\"")}（应为 0）");
        Console.WriteLine($"  旧 portrait-table: {Count("class=\"portrait-table\"")}（应为 0）");

        Console.WriteLine($"\n✅ 已写入: {outputPath} ({mdStr.Length} 字符)");
        Console.WriteLine($"   后续可用: Novelizer run -i \"{outputPath}\" --model flash");

        DbFactory.Reset();
        return 0;
    }

    /// <summary>
    /// 将 charslot/character 条目的 CharacterCode 传播到后续对话条目。
    /// DB 中只有立绘条目有 CharacterCode，对话条目没有——这是运行时由 PrtsPreloader 做的传播。
    /// </summary>
    private static void PropagateCharacterCode(List<FormattedTextEntry> entries)
    {
        // DB 中 CommandSet["name"] 是游戏内部 ID（avg_npc_134），CharacterName 是显示名（精英打扮的男性），
        // 两者完全不同的命名体系。因此用位置邻近策略：
        // charslot 条目的 CharacterCode 传播到紧随其后的第一个同名对话条目。

        var nameToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? pendingCode = null;
        int propagated = 0;

        foreach (var entry in entries)
        {
            // charslot/character 条目：记录待传播的 CharacterCode
            if (entry.Type is "character" or "charactercutin" or "charslot"
                && !string.IsNullOrEmpty(entry.CharacterCode))
            {
                pendingCode = entry.CharacterCode;
            }
            // 对话条目：首次出现的 CharacterName 分配 pendingCode
            else if (!string.IsNullOrEmpty(entry.CharacterName) && string.IsNullOrEmpty(entry.CharacterCode))
            {
                if (nameToCode.TryGetValue(entry.CharacterName, out var knownCode))
                {
                    entry.CharacterCode = knownCode;
                    propagated++;
                }
                else if (pendingCode != null)
                {
                    nameToCode[entry.CharacterName] = pendingCode;
                    entry.CharacterCode = pendingCode;
                    propagated++;
                    pendingCode = null;
                }
            }
        }
        Console.WriteLine($"📊 CharacterCode 传播: {propagated} 条（映射 {nameToCode.Count} 个角色）");
    }

    /// <summary>
    /// 从 PicDescription 表批量填充 entries 的 PicFacts 字段
    /// </summary>
    private static void PopulatePicFacts(List<FormattedTextEntry> entries, SqlSugar.ISqlSugarClient db)
    {
        int matched = 0;
        foreach (var entry in entries)
        {
            // 有 CharacterCode 的条目（含对话条目）按 CharacterCode 查
            if (!string.IsNullOrEmpty(entry.CharacterCode))
            {
                var record = db.Queryable<PicDescription>()
                    .Where(r => r.DedupKey == entry.CharacterCode && r.Source == "Vision")
                    .First();
                if (record?.PicFacts != null)
                {
                    entry.PicFacts = record.PicFacts;
                    matched++;
                    continue;
                }
            }

            // 有 ResourceUrls 的条目按 URL 查（场景图等）
            foreach (var url in entry.ResourceUrls)
            {
                var record = db.Queryable<PicDescription>()
                    .Where(r => (r.DedupKey == url || r.ImageUrl == url) && r.Source == "Vision")
                    .First();
                if (record?.PicFacts != null)
                {
                    entry.PicFacts = string.IsNullOrEmpty(entry.PicFacts)
                        ? record.PicFacts
                        : entry.PicFacts + "\n" + record.PicFacts;
                    matched++;
                }
            }
        }
        Console.WriteLine($"📊 PicFacts 匹配: {matched} 条");
    }

    /// <summary>
    /// 从当前目录向上查找 Avalonia 的 arkplot.db
    /// </summary>
    private static string FindAvaloniaDbPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "ArkPlot.Avalonia", "bin", "Debug", "net9.0", "arkplot.db");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return Path.Combine(AppContext.BaseDirectory, "arkplot.db");
    }

    /// <summary>
    /// 查找项目根目录（ArkPlot.sln 所在位置）用于输出文件
    /// </summary>
    private static string FindProjectOutputDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "ArkPlot.sln")))
                return Path.Combine(dir, "verify_output");
            dir = Path.GetDirectoryName(dir);
        }
        return Path.Combine(AppContext.BaseDirectory, "verify_output");
    }

    static ApiConfig LoadConfig(string? providerOverride)
    {
        var dsKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "";
        var blKey = Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY") ?? "";

        var provider = providerOverride?.ToLowerInvariant() switch
        {
            "deepseek" or "ds" => ApiProvider.DeepSeek,
            "bailian" or "bl" => ApiProvider.Bailian,
            _ => (ApiProvider?)null
        };

        if (provider is null)
        {
            // 未指定 provider，自动检测
            var bothAvailable = !string.IsNullOrEmpty(dsKey) && !string.IsNullOrEmpty(blKey);
            if (bothAvailable)
            {
                throw new InvalidOperationException(
                    "检测到 DEEPSEEK_API_KEY 和 DASHSCOPE_API_KEY 均已配置。请使用 --provider deepseek 或 --provider bailian 明确指定平台。");
            }

            if (!string.IsNullOrEmpty(dsKey))
                provider = ApiProvider.DeepSeek;
            else if (!string.IsNullOrEmpty(blKey))
                provider = ApiProvider.Bailian;
        }

        return provider switch
        {
            ApiProvider.DeepSeek => new ApiConfig
            {
                Provider = ApiProvider.DeepSeek,
                ApiKey = dsKey,
                BaseUrl = "https://api.deepseek.com"
            },
            ApiProvider.Bailian => new ApiConfig
            {
                Provider = ApiProvider.Bailian,
                ApiKey = blKey,
                BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1"
            },
            _ => new ApiConfig()
        };
    }

    static (string input, bool compare, bool force, string? model, string? provider, string? prompt, string? tag, bool multiTurn, int chunkSize, int compressInterval) ParseRunArgs(string[] args)
    {
        var input = "";
        var compare = false;
        var force = false;
        string? model = null;
        string? provider = null;
        string? prompt = null;
        string? tag = null;
        var multiTurn = false;
        var chunkSize = 5_000;
        var compressInterval = 0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--input" or "-i" when i + 1 < args.Length:
                    input = args[++i];
                    break;
                case "--compare" or "-c":
                    compare = true;
                    break;
                case "--force" or "-f":
                    force = true;
                    break;
                case "--model" or "-m" when i + 1 < args.Length:
                    model = args[++i].ToLowerInvariant();
                    break;
                case "--provider" or "-p" when i + 1 < args.Length:
                    provider = args[++i].ToLowerInvariant();
                    break;
                case "--prompt" when i + 1 < args.Length:
                    prompt = args[++i];
                    break;
                case "--tag" or "-t" when i + 1 < args.Length:
                    tag = args[++i];
                    break;
                case "--multi-turn" or "-mt":
                    multiTurn = true;
                    break;
                case "--chunk-size" or "-cs" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var parsed) && parsed > 0)
                        chunkSize = parsed;
                    break;
                case "--compress-interval" or "-ci" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var ci) && ci > 0)
                        compressInterval = ci;
                    break;
                default:
                    if (!args[i].StartsWith("-") && string.IsNullOrEmpty(input))
                        input = args[i];
                    break;
            }
        }

        return (input, compare, force, model, provider, prompt, tag, multiTurn, chunkSize, compressInterval);
    }

    /// <summary>
    /// 加载项目根目录下的 .env 文件，将 KEY=VALUE 行设为环境变量（不覆盖已存在的）
    /// </summary>
    static void LoadEnvFile()
    {
        // 从当前目录向上查找 .env
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var envPath = Path.Combine(dir, ".env");
            if (File.Exists(envPath))
            {
                foreach (var line in File.ReadAllLines(envPath))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                        continue;
                    var eq = trimmed.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = trimmed[..eq].Trim();
                    var value = trimmed[(eq + 1)..].Trim();
                    if (Environment.GetEnvironmentVariable(key) is null)
                        Environment.SetEnvironmentVariable(key, value);
                }
                Console.WriteLine($"📄 已加载 .env: {envPath}");
                return;
            }
            dir = Path.GetDirectoryName(dir);
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("ArkPlot.Novelizer — 百炼 / DeepSeek 小说化工具");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  Novelizer run --input <path> [--compare] [--force] [--model flash|pro] [--provider deepseek|bailian]");
        Console.WriteLine("  Novelizer test <example_data.json> [--model flash|pro] [--provider deepseek|bailian]");
        Console.WriteLine();
        Console.WriteLine("命令:");
        Console.WriteLine("  run     从 .md 文件或 .json (FormattedTextEntry[]) 生成小说");
        Console.WriteLine("  test    用 example_data.json 测试");
        Console.WriteLine("  verify  反照抄端到端验证：孤星第一章 DB → 双模式 MD → 小说对比");
        Console.WriteLine();
        Console.WriteLine("选项:");
        Console.WriteLine("  --input, -i        输入文件(.md/.json) 或目录");
        Console.WriteLine("  --compare, -c      并行调用 pro 和 flash 两个模型进行对比");
        Console.WriteLine("  --force, -f        忽略缓存，强制重新生成");
        Console.WriteLine("  --model, -m        指定模型 (flash / pro)");
        Console.WriteLine("  --provider, -p     指定平台 (deepseek / bailian)");
        Console.WriteLine("  --prompt           自定义 system prompt 文件路径（覆盖默认提示词）");
        Console.WriteLine("  --tag, -t          输出标签（替代模型名出现在输出文件名中）");
        Console.WriteLine("  --multi-turn, -mt        启用多轮对话模式（长章自动拆分为多轮调用）");
        Console.WriteLine("  --chunk-size, -cs        多轮模式下每 chunk 目标字符数（默认 5000）");
        Console.WriteLine("  --compress-interval, -ci 每 N 轮压缩一次上下文（默认 0 = 不压缩）");
        Console.WriteLine();
        Console.WriteLine("verify 专用选项:");
        Console.WriteLine("  --db <path>    arkplot.db 路径（默认自动查找 Avalonia 输出目录）");
        Console.WriteLine();
        Console.WriteLine("平台自动检测:");
        Console.WriteLine("  仅 DEEPSEEK_API_KEY     → 自动使用 DeepSeek (api.deepseek.com)");
        Console.WriteLine("  仅 DASHSCOPE_API_KEY    → 自动使用百炼 (dashscope.aliyuncs.com)");
        Console.WriteLine("  两者都配置              → 必须用 --provider 显式指定");
    }

    static int PrintUsageWithError(string error)
    {
        Console.Error.WriteLine(error);
        Console.Error.WriteLine();
        PrintUsage();
        return 1;
    }
}