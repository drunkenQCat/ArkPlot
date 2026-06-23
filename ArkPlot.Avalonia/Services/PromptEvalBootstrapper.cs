using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ArkPlot.Avalonia.Models;
using ArkPlot.Core.Infrastructure;
using ArkPlot.Core.Model;
using ArkPlot.Core.Services;
using ArkPlot.Core.Utilities;
using ArkPlot.Core.Utilities.PrtsComponents;
using ArkPlot.Core.Utilities.TagProcessingComponents;
using ArkPlot.Core.Utilities.WorkFlow;
using ArkPlot.Novelizer;
using ArkPlot.Vision;

namespace ArkPlot.Avalonia.Services;

public sealed record PromptEvalBootstrapOptions
{
    public string SourceDbPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "arkplot.db");
    public string SettingsPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "settings.json");
    public string TagsJsonPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "tags.json");
    public string OutputRoot { get; init; } = Path.Combine(AppContext.BaseDirectory, "prompt_eval_bootstrap");
    public IReadOnlyList<string> ActIds { get; init; } = ["act13side", "act25side", "act33side", "act49side"];
    public int ChapterCount { get; init; } = 1;
    public bool EnablePicDesc { get; init; } = true;
    public bool EnableNovelizer { get; init; } = true;
    public bool RefreshPicDesc { get; init; } = true;
    public bool ResetOutputRoot { get; init; } = false;
    public bool ResetWorkingDb { get; init; } = true;
}

public sealed record PromptEvalBootstrapRun(
    string OutputRoot,
    string WorkingDbPath,
    string SettingsPath,
    string TagsJsonPath,
    IReadOnlyList<PromptEvalBootstrapActResult> Acts);

public sealed record PromptEvalBootstrapActResult(
    string ActId,
    string ActName,
    IReadOnlyList<string> StoryCodes,
    string MarkdownPath,
    IReadOnlyList<string> NovelPaths,
    string PicDescSnapshotPath,
    string MetadataPath);

internal sealed record PromptEvalBootstrapMetadata(
    string ActId,
    string ActName,
    string[] StoryCodes,
    string[] ChapterTitles,
    bool PicDescEnabled,
    bool PicDescRefresh,
    string VisionProvider,
    string VisionModel,
    string VisionSystemPrompt,
    string NovelizerProvider,
    string NovelizerModel,
    string NovelizerSystemPrompt);

public sealed class PromptEvalBootstrapper
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<PromptEvalBootstrapRun> RunAsync(
        PromptEvalBootstrapOptions options,
        Action<string>? onLog = null)
    {
        Log(onLog, $"[Bootstrap] 输出目录：{options.OutputRoot}");
        ValidateInputs(options);

        if (options.ResetOutputRoot)
            await TryResetOutputRootAsync(options.OutputRoot, onLog);

        Directory.CreateDirectory(options.OutputRoot);
        var workDir = Path.Combine(options.OutputRoot, "_work");
        Directory.CreateDirectory(workDir);

        var workingDbPath = Path.Combine(workDir, "arkplot.db");
        PrepareWorkingDb(options.SourceDbPath, workingDbPath, options.ResetWorkingDb);

        var settings = AppSettings.LoadFromFile(options.SettingsPath);
        var copiedSettingsPath = Path.Combine(workDir, "settings.snapshot.json");
        settings.SaveToFile(copiedSettingsPath);
        File.Copy(options.TagsJsonPath, Path.Combine(workDir, Path.GetFileName(options.TagsJsonPath)), overwrite: true);

        DbFactory.ConfigureForTesting($"Data Source={workingDbPath}");
        _ = DbFactory.GetClient();

        IDisposable? visionDisposable = null;
        PicDescService? picDescService = null;

        try
        {
            var sync = new StorySyncService();
            var acts = await EnsureActsAsync(sync, onLog);

            var prts = new PrtsDataProcessor();
            await prts.EnsureSyncedAsync("zh_CN");
            Log(onLog, "[Bootstrap] PRTS 资源索引已就绪");

            picDescService = CreatePicDescService(settings, options, onLog, out visionDisposable);
            picDescService?.InitializeCleanup();

            var results = new List<PromptEvalBootstrapActResult>();
            foreach (var actId in options.ActIds)
            {
                results.Add(await RunSingleActAsync(
                    actId,
                    acts,
                    sync,
                    settings,
                    options,
                    picDescService,
                    onLog));
            }

            var run = new PromptEvalBootstrapRun(
                options.OutputRoot,
                workingDbPath,
                copiedSettingsPath,
                options.TagsJsonPath,
                results);

            var summaryPath = Path.Combine(options.OutputRoot, "bootstrap_summary.json");
            await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(run, JsonOptions));
            Log(onLog, $"[Bootstrap] 完成。汇总文件：{summaryPath}");
            return run;
        }
        finally
        {
            picDescService?.Dispose();
            visionDisposable?.Dispose();
            DbFactory.Reset();
        }
    }

    private static void ValidateInputs(PromptEvalBootstrapOptions options)
    {
        if (!File.Exists(options.SourceDbPath))
            throw new FileNotFoundException($"源数据库不存在: {options.SourceDbPath}");
        if (!File.Exists(options.SettingsPath))
            throw new FileNotFoundException($"settings.json 不存在: {options.SettingsPath}");
        if (!File.Exists(options.TagsJsonPath))
            throw new FileNotFoundException($"tags.json 不存在: {options.TagsJsonPath}");
    }

    private static void PrepareWorkingDb(string sourceDbPath, string workingDbPath, bool resetWorkingDb)
    {
        if (resetWorkingDb || !File.Exists(workingDbPath))
            File.Copy(sourceDbPath, workingDbPath, overwrite: true);
    }

    private static async Task TryResetOutputRootAsync(string outputRoot, Action<string>? onLog)
    {
        if (!Directory.Exists(outputRoot))
            return;

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                Directory.Delete(outputRoot, recursive: true);
                return;
            }
            catch (IOException ex) when (attempt < maxAttempts)
            {
                Log(onLog, $"[Bootstrap] 清理输出目录失败，准备重试 ({attempt}/{maxAttempts}): {ex.Message}");
                await Task.Delay(1000 * attempt);
            }
            catch (UnauthorizedAccessException ex) when (attempt < maxAttempts)
            {
                Log(onLog, $"[Bootstrap] 清理输出目录失败，准备重试 ({attempt}/{maxAttempts}): {ex.Message}");
                await Task.Delay(1000 * attempt);
            }
            catch (IOException ex)
            {
                Log(onLog, $"[Bootstrap] 输出目录存在被占用文件，跳过整目录清理并继续覆盖写出: {ex.Message}");
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                Log(onLog, $"[Bootstrap] 输出目录存在被占用文件，跳过整目录清理并继续覆盖写出: {ex.Message}");
                return;
            }
        }
    }

    private static async Task<List<Act>> EnsureActsAsync(StorySyncService sync, Action<string>? onLog)
    {
        var acts = sync.GetActsFromDb("zh_CN");
        if (acts.Count > 0)
            return acts;

        Log(onLog, "[Bootstrap] 工作库缺少 zh_CN 活动索引，开始同步剧情表");
        await sync.DownloadAndSaveAsync("zh_CN");
        acts = sync.GetActsFromDb("zh_CN");
        if (acts.Count == 0)
            throw new InvalidOperationException("同步后仍未获得任何 zh_CN 活动索引。");

        return acts;
    }

    private static PicDescService? CreatePicDescService(
        AppSettings settings,
        PromptEvalBootstrapOptions options,
        Action<string>? onLog,
        out IDisposable? disposable)
    {
        disposable = null;
        if (!options.EnablePicDesc)
            return null;

        var vision = settings.Vision ?? VisionSettings.CreateDefaults();
        var providerName = vision.SelectedProvider;
        var model = vision.SelectedModel;
        var systemPrompt = string.IsNullOrWhiteSpace(vision.SystemPrompt)
            ? VisionSettings.DefaultSystemPrompt
            : vision.SystemPrompt;

        Func<string, Task<string>>? describeByUrl = null;

        if (providerName == "Ollama")
        {
            var visionConfig = new VisionConfig
            {
                BaseUrl = vision.OllamaBaseUrl,
                Model = model,
                SystemPrompt = systemPrompt,
                TimeoutSeconds = 120,
                MaxTokens = 2048
            };
            var ollamaClient = new OllamaVisionClient(visionConfig, onLog);
            disposable = ollamaClient;
            describeByUrl = async url =>
            {
                using var http = new HttpClient();
                var bytes = await http.GetByteArrayAsync(url);
                var base64 = Convert.ToBase64String(bytes);
                return await ollamaClient.DescribeImageBase64Async(base64);
            };
        }
        else
        {
            var apiKey = ResolveVisionApiKey(settings, vision);
            var baseUrl = vision.GetBaseUrlForProvider(providerName);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Log(onLog, $"[Bootstrap] Vision API Key 为空，跳过 PicDesc 生成。provider={providerName}");
                return null;
            }

            var visionConfig = new BailianVisionConfig
            {
                ApiKey = apiKey,
                BaseUrl = baseUrl,
                Model = model,
                SystemPrompt = systemPrompt,
                TimeoutSeconds = 120,
                MaxTokens = 2048
            };
            var bailianClient = new BailianVisionClient(visionConfig, onLog);
            disposable = bailianClient;
            describeByUrl = async url => await bailianClient.DescribeImageUrlAsync(url);
        }

        Log(onLog, $"[Bootstrap] PicDesc 已启用。provider={providerName}, model={model}, refresh={options.RefreshPicDesc}");
        return new PicDescService(describeByUrl, debugMode: options.RefreshPicDesc);
    }

    private static string ResolveVisionApiKey(AppSettings settings, VisionSettings vision)
    {
        var direct = vision.GetApiKeyForProvider(vision.SelectedProvider);
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        return settings.GetApiKey(vision.SelectedProvider);
    }

    private static async Task<PromptEvalBootstrapActResult> RunSingleActAsync(
        string actId,
        List<Act> acts,
        StorySyncService sync,
        AppSettings settings,
        PromptEvalBootstrapOptions options,
        PicDescService? picDescService,
        Action<string>? onLog)
    {
        var act = acts.FirstOrDefault(a => a.ActId == actId)
            ?? throw new InvalidOperationException($"活动不存在: {actId}");
        var chapters = sync.GetChaptersByActId(act.Id)
            .OrderBy(c => c.StorySort)
            .ThenBy(c => c.Id)
            .Take(options.ChapterCount)
            .ToList();
        if (chapters.Count == 0)
            throw new InvalidOperationException($"活动没有章节: {actId}");

        var chapterTitles = chapters.Select(BuildChapterTitle).ToList();
        Log(onLog, $"[Bootstrap] {act.Name}: {string.Join(", ", chapterTitles)}");

        var storyLoader = new AkpStoryLoader(act, sync.GetChaptersByActId(act.Id));
        await storyLoader.GetAllChapters(chapterTitles);
        await Task.Run(storyLoader.GetPreloadInfo);
        await storyLoader.ParseAllDocuments(options.TagsJsonPath);

        var mdContent = await AkpProcessor.ExportPlotsAsync(storyLoader.ContentTable, picDescService, options.EnablePicDesc);
        var mdWithTitle = $"# {act.Name}\n\n{mdContent}";

        var actOutputDir = Path.Combine(options.OutputRoot, act.Name);
        Directory.CreateDirectory(actOutputDir);
        var markdown = new Plot(act.Name, new StringBuilder(mdWithTitle));
        AkpProcessor.WriteMd(actOutputDir, markdown);
        var markdownPath = Path.Combine(actOutputDir, $"{act.Name}.md");

        var novelPaths = new List<string>();
        if (options.EnableNovelizer)
            novelPaths.AddRange(await RunNovelizerAsync(settings, actOutputDir, onLog));

        var picDescSnapshotPath = await WritePicDescSnapshotAsync(storyLoader.ContentTable, actOutputDir);
        var metadataPath = await WriteMetadataAsync(act, chapters, settings, options, actOutputDir);

        return new PromptEvalBootstrapActResult(
            act.ActId,
            act.Name,
            chapters.Select(c => c.StoryCode).ToList(),
            markdownPath,
            novelPaths,
            picDescSnapshotPath,
            metadataPath);
    }

    private static async Task<IReadOnlyList<string>> RunNovelizerAsync(
        AppSettings settings,
        string outputDir,
        Action<string>? onLog)
    {
        var novelizer = settings.Novelizer;
        var selectedProviderName = novelizer.SelectedProvider;
        var apiKey = novelizer.GetApiKeyForProvider(selectedProviderName);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Log(onLog, $"[Bootstrap] Novelizer API Key 为空，跳过小说生成。provider={selectedProviderName}");
            return [];
        }

        var baseUrl = novelizer.GetBaseUrlForProvider(selectedProviderName);
        var provider = selectedProviderName switch
        {
            "DeepSeek" => ApiProvider.DeepSeek,
            "百炼" => ApiProvider.Bailian,
            _ => ApiProvider.Custom
        };

        var config = new ApiConfig
        {
            Provider = provider,
            ApiKey = apiKey,
            BaseUrl = baseUrl
        };
        using var http = new HttpClient();
        var client = new BailianClient(http, config, onLog);
        var pipeline = new NovelizerPipeline(
            client, config, onLog,
            systemPrompt: novelizer.SystemPrompt,
            enableMultiTurn: novelizer.EnableMultiTurn,
            chunkSize: novelizer.ChunkSize,
            compressInterval: novelizer.CompressInterval);
        await pipeline.BatchProcessAsync(outputDir, [novelizer.SelectedModel], force: false);

        return Directory.GetFiles(outputDir, "*_novel_*.md")
            .OrderBy(p => p)
            .ToList();
    }

    private static async Task<string> WritePicDescSnapshotAsync(
        List<PlotManager> plotManagers,
        string outputDir)
    {
        var db = DbFactory.GetClient();
        var imageUrls = plotManagers
            .SelectMany(pm => pm.CurrentPlot.TextVariants)
            .SelectMany(entry => entry.ResourceUrls)
            .Where(IsImageUrl)
            .Distinct()
            .ToList();

        var records = db.Queryable<PicDescription>()
            .Where(pd => imageUrls.Contains(pd.ImageUrl))
            .OrderBy(pd => pd.ImageUrl)
            .ToList();

        var path = Path.Combine(outputDir, "picdesc.snapshot.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(records, JsonOptions));
        return path;
    }

    private static async Task<string> WriteMetadataAsync(
        Act act,
        List<StoryChapter> chapters,
        AppSettings settings,
        PromptEvalBootstrapOptions options,
        string outputDir)
    {
        var vision = settings.Vision ?? VisionSettings.CreateDefaults();
        var metadata = new PromptEvalBootstrapMetadata(
            act.ActId,
            act.Name,
            chapters.Select(c => c.StoryCode).ToArray(),
            chapters.Select(BuildChapterTitle).ToArray(),
            options.EnablePicDesc,
            options.RefreshPicDesc,
            vision.SelectedProvider,
            vision.SelectedModel,
            string.IsNullOrWhiteSpace(vision.SystemPrompt) ? VisionSettings.DefaultSystemPrompt : vision.SystemPrompt,
            settings.Novelizer.SelectedProvider,
            settings.Novelizer.SelectedModel,
            settings.Novelizer.SystemPrompt);

        var path = Path.Combine(outputDir, "bootstrap.metadata.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(metadata, JsonOptions));
        return path;
    }

    private static string BuildChapterTitle(StoryChapter chapter)
    {
        var variation = chapter.StoryId.Contains("variation", StringComparison.OrdinalIgnoreCase)
            ? ExtractVariationNumber(chapter.StoryId)
            : "";
        return $"{chapter.StoryCode} {chapter.StoryName} {chapter.AvgTag}{variation}".Trim();
    }

    private static string ExtractVariationNumber(string storyId)
    {
        var idx = storyId.IndexOf("variation", StringComparison.OrdinalIgnoreCase);
        return idx < 0 ? "" : storyId[(idx + "variation".Length)..];
    }

    private static bool IsImageUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var cleanUrl = url.Split('?')[0].ToLowerInvariant();
        var ext = Path.GetExtension(cleanUrl);
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" or ".apng" or ".avif";
    }

    private static void Log(Action<string>? onLog, string message)
    {
        Console.WriteLine(message);
        onLog?.Invoke(message);
    }
}
