using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArkPlot.Avalonia.Services;

public record CatalogModel(string Id, string[] Inputs, string[] Outputs, string[] Capabilities,
    string? InferenceProvider, string? OfflineTime);

public record ModelChoice(string Id, string DisplayName)
{
    public static ModelChoice[] ForNovel(IEnumerable<string> ids) => ids.Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(IsKimi)
        .Select(id => new ModelChoice(id, IsKimi(id) ? $"{id}（推荐）" : id)).ToArray();

    private static bool IsKimi(string id) => id.Contains("kimi", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Official paginated model directory; capability metadata is required for selection.</summary>
public class BailianModelCatalog(HttpClient http)
{
    public const string Endpoint = "https://dashscope.aliyuncs.com/api/v1/models";

    public async Task<CatalogModel[]> FetchAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("请先填写百炼 API Key，或配置 DASHSCOPE_API_KEY。");
        var result = new List<CatalogModel>();
        var received = 0;
        for (var page = 1; page <= 100; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{Endpoint}?page_no={page}&page_size=100&service_site=asia-pacific-china&inference_providers=aliyun-bailian");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"百炼模型目录请求失败：HTTP {(int)response.StatusCode}。请检查 API Key 和网络。", null, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = document.RootElement;
            if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
                throw new InvalidOperationException("百炼模型目录返回失败，未更新缓存。");
            if (!root.TryGetProperty("output", out var output) ||
                !output.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array ||
                !output.TryGetProperty("total", out var totalValue) || !totalValue.TryGetInt32(out var total))
                throw new InvalidOperationException("百炼模型目录格式不符合预期，未更新缓存。");
            result.AddRange(ParseModels(models));
            received += models.GetArrayLength();
            if (received >= total)
                return result.DistinctBy(m => m.Id).ToArray();
            if (models.GetArrayLength() == 0)
                throw new InvalidOperationException("百炼模型目录分页不完整，未更新缓存。");
        }
        throw new InvalidOperationException("百炼模型目录分页超出上限，未更新缓存。");
    }

    internal static IEnumerable<CatalogModel> ParseModels(JsonElement models)
    {
        foreach (var model in models.EnumerateArray())
        {
            var id = StringValue(model, "model");
            if (string.IsNullOrWhiteSpace(id)) continue;
            model.TryGetProperty("inference_metadata", out var metadata);
            model.TryGetProperty("inference_offline_info", out var offline);
            yield return new CatalogModel(id, Strings(metadata, "request_modality"),
                Strings(metadata, "response_modality"), Strings(model, "capabilities"),
                StringValue(model, "inference_provider"), StringValue(offline, "offline_time"));
        }
    }

    public static string[] SelectModels(IEnumerable<CatalogModel> models, bool imageInput, DateTimeOffset? now = null)
    {
        var chinaTime = (now ?? DateTimeOffset.UtcNow).ToOffset(TimeSpan.FromHours(8)).DateTime;
        return models.Where(m =>
                SupportsCurrentChatClient(m.Id, imageInput) &&
                (string.IsNullOrEmpty(m.InferenceProvider) || m.InferenceProvider is "aliyun-bailian" or "alibaba-cloud-modelstudio") &&
                Has(m.Inputs, imageInput ? "Image" : "Text") &&
                Has(m.Outputs, "Text") &&
                (imageInput
                    ? (Has(m.Capabilities, "VU") || Has(m.Capabilities, "Multimodal-Omni")) &&
                      !Has(m.Capabilities, "Realtime-Omni") && !m.Id.Contains("realtime", StringComparison.OrdinalIgnoreCase)
                    : m.Outputs.Length == 1 && (Has(m.Capabilities, "TG") || Has(m.Capabilities, "Reasoning"))) &&
                (!DateTime.TryParse(m.OfflineTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var offline) || offline > chinaTime))
            .Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // Modality metadata does not imply ordinary Chat Completions compatibility.
    // Translation/research models need dedicated requests; the other families require
    // streaming that the text and non-Omni vision clients do not yet implement.
    private static bool SupportsCurrentChatClient(string id, bool imageInput) =>
        !id.StartsWith("qwen-mt", StringComparison.OrdinalIgnoreCase) &&
        !id.StartsWith("qwen-deep-research", StringComparison.OrdinalIgnoreCase) &&
        !id.StartsWith("qwq", StringComparison.OrdinalIgnoreCase) &&
        !id.StartsWith("qvq", StringComparison.OrdinalIgnoreCase) &&
        // The novelizer enables thinking; Qwen3-VL needs streaming in that mode.
        (imageInput || !id.StartsWith("qwen3-vl", StringComparison.OrdinalIgnoreCase)) &&
        !(id.StartsWith("qwen3-", StringComparison.OrdinalIgnoreCase) &&
          (id.Length > 6 && char.IsDigit(id[6]) || id.StartsWith("qwen3-next-", StringComparison.OrdinalIgnoreCase)));

    private static bool Has(string[] values, string value) => values.Contains(value, StringComparer.OrdinalIgnoreCase);
    private static string? StringValue(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString() : null;
    private static string[] Strings(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray() : [];
}
