using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ArkPlot.Avalonia.Models;
using ArkPlot.Novelizer;
using ArkPlot.Vision;

namespace ArkPlot.Avalonia.Services;

public record ModelTestRequest(bool ImageTest, string Provider, string Model, string Prompt, string Input,
    CachedImage? Image, int MaxTokens, bool EnableThinking);
public record ModelTestResult(string Text, string Thinking = "");
public interface ICachedModelTestRunner
{
    Task<ModelTestResult> RunAsync(ModelTestRequest request, CancellationToken ct);
}

public class CachedModelTestRunner : ICachedModelTestRunner
{
    public async Task<ModelTestResult> RunAsync(ModelTestRequest request, CancellationToken ct)
    {
        var settings = AppSettings.Load();
        using var http = new HttpClient();
        using var registration = ct.Register(http.CancelPendingRequests);
        ct.ThrowIfCancellationRequested();
        if (!request.ImageTest)
        {
            var key = settings.Novelizer.GetApiKeyForProvider(request.Provider);
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("请先在设置中填写所选平台的 API Key。");
            var client = new BailianClient(http, new ApiConfig
            {
                BaseUrl = settings.Novelizer.GetBaseUrlForProvider(request.Provider), ApiKey = key,
                Provider = request.Provider switch { "DeepSeek" => ApiProvider.DeepSeek, "百炼" => ApiProvider.Bailian, _ => ApiProvider.Custom },
                MaxTokens = request.MaxTokens, EnableThinking = request.EnableThinking, MaxRetries = 1, TimeoutSeconds = 120
            });
            var result = await client.ChatAsync(request.Model, request.Prompt, request.Input);
            ct.ThrowIfCancellationRequested();
            return new(result.AnswerContent, result.ReasoningContent);
        }

        var image = request.Image ?? throw new InvalidOperationException("请先选择缓存图片。");
        var vision = settings.Vision ?? VisionSettings.CreateDefaults();
        var baseUrl = vision.GetBaseUrlForProvider(request.Provider);
        http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        if (request.Provider == "Ollama")
        {
            using var ollama = new OllamaVisionClient(http, new VisionConfig
            { BaseUrl = baseUrl, Model = request.Model, SystemPrompt = request.Prompt, MaxTokens = request.MaxTokens });
            var bytes = !string.IsNullOrEmpty(image.LocalPath) && File.Exists(image.LocalPath)
                ? await File.ReadAllBytesAsync(image.LocalPath, ct) : await http.GetByteArrayAsync(image.Url, ct);
            var result = await ollama.DescribeImageBase64Async(Convert.ToBase64String(bytes), request.Prompt);
            ct.ThrowIfCancellationRequested();
            return new(result);
        }
        var apiKey = settings.GetVisionApiKey(request.Provider);
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("请先在设置中填写所选平台的 API Key。");
        using var clientVision = new BailianVisionClient(http, new BailianVisionConfig
        {
            ProviderName = request.Provider,
            BaseUrl = baseUrl, ApiKey = apiKey, Model = request.Model, SystemPrompt = request.Prompt,
            MaxTokens = request.MaxTokens, MaxRetries = 1
        });
        var description = !string.IsNullOrEmpty(image.LocalPath) && File.Exists(image.LocalPath)
            ? await clientVision.DescribeImageAsync(image.LocalPath, request.Prompt)
            : await clientVision.DescribeImageUrlAsync(image.Url, request.Prompt);
        ct.ThrowIfCancellationRequested();
        return new(description);
    }
}
