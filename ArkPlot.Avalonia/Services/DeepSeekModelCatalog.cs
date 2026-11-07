using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArkPlot.Avalonia.Services;

/// <summary>DeepSeek's OpenAI-compatible directory, including input/output modality metadata.</summary>
public class DeepSeekModelCatalog(HttpClient http)
{
    public const string Endpoint = "https://api.deepseek.com/models";

    public async Task<CatalogModel[]> FetchAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("请先填写 DeepSeek API Key，或配置 DEEPSEEK_API_KEY。");
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"DeepSeek 模型目录请求失败：HTTP {(int)response.StatusCode}。请检查 API Key 和网络。", null, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!document.RootElement.TryGetProperty("data", out var models) || models.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("DeepSeek 模型目录格式不符合预期，未更新缓存。");
        var result = new List<CatalogModel>();
        foreach (var model in models.EnumerateArray())
        {
            if (model.ValueKind != JsonValueKind.Object || !model.TryGetProperty("id", out var idValue) ||
                idValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(idValue.GetString())) continue;
            var id = idValue.GetString()!;
            // Metadata is optional. Only documented model IDs may infer image support;
            // a new/unknown model without metadata must never appear in the vision picker.
            var inputs = Strings(model, "input_modalities") ??
                (id is "deepseek-flash" or "deepseek-v4-flash-vision-exp" ? ["text", "image"] : ["text"]);
            var outputs = Strings(model, "output_modalities") ?? ["text"];
            result.Add(new CatalogModel(id, inputs, outputs, [], null, null));
        }
        if (result.Count == 0) throw new InvalidOperationException("DeepSeek 模型目录为空，已保留现有列表。");
        return result.DistinctBy(m => m.Id).ToArray();
    }

    public static string[] SelectModels(IEnumerable<CatalogModel> models, bool imageInput) => models
        .Where(m => m.Inputs.Contains(imageInput ? "image" : "text", StringComparer.OrdinalIgnoreCase) &&
                    m.Outputs.Contains("text", StringComparer.OrdinalIgnoreCase))
        .Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static string NormalizeModelId(string id, string[] available) =>
        id is "deepseek-v4-flash" or "deepseek-v4-flash-vision-exp" &&
        !available.Contains(id) && available.Contains("deepseek-flash") ? "deepseek-flash" : id;

    private static string[]? Strings(JsonElement model, string key) =>
        model.TryGetProperty(key, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray()
            : null;
}
