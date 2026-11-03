using System.Net;
using System.Text;
using System.Text.Json;
using ArkPlot.Avalonia.Services;
using ArkPlot.Avalonia.ViewModels;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

public class BailianModelCatalogTests
{
    private static CatalogModel Model(string id, string[] inputs, string[] outputs, string[] capabilities,
        string? provider = "aliyun-bailian", string? offline = null) => new(id, inputs, outputs, capabilities, provider, offline);

    [Fact]
    public void SelectModels_UsesModalitiesCapabilitiesProviderAndOfflineTime()
    {
        CatalogModel[] models = [
            Model("text", ["Text"], ["Text"], ["TG"]),
            Model("vision", ["Text", "Image"], ["Text"], ["VU", "TG"]),
            Model("image-generator", ["Text", "Image"], ["Image"], ["IG"]),
            Model("reranker", ["Text", "Image"], ["Text"], ["TR"]),
            Model("omni", ["Text", "Image"], ["Text", "Audio"], ["Multimodal-Omni"]),
            Model("qwen3.8-omni-flash", ["Text", "Image", "Audio", "Video"], ["Text"], ["Multimodal-Omni"]),
            Model("qwen3.8-omni-flash-realtime", ["Text", "Image"], ["Text"], ["Realtime-Omni", "Multimodal-Omni"]),
            Model("offline", ["Text", "Image"], ["Text"], ["TG", "VU"], offline: "2026-01-01 00:00:00"),
            Model("future-offline", ["Text", "Image"], ["Text"], ["TG", "VU"], offline: "2027-01-01 00:00:00"),
            Model("external", ["Text", "Image"], ["Text"], ["TG", "VU"], provider: "moonshot-ai"),
            Model("missing-metadata", [], [], ["TG"])
        ];
        var now = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(["text", "vision", "future-offline"], BailianModelCatalog.SelectModels(models, false, now));
        Assert.Equal(["vision", "omni", "qwen3.8-omni-flash", "future-offline"], BailianModelCatalog.SelectModels(models, true, now));
    }

    [Fact]
    public void SelectModels_ExcludesUnadaptedProtocolsButRetainsOrdinaryAndOmniModels()
    {
        string[] unsupported = ["qwen-mt-plus", "qwen-mt-flash", "qwen-deep-research", "qwen-deep-research-2025-12-15",
            "qwq-plus", "qvq-max", "qwen3-32b", "qwen3-235b-a22b-thinking-2507", "qwen3-next-80b-a3b-thinking"];
        var models = unsupported.Concat(["kimi-k2.5", "qwen3-vl-plus", "qwen3-coder-flash", "qwen3.8-omni-flash"])
            .Select(id => Model(id, ["Text", "Image"], ["Text"], ["TG", "VU", "Multimodal-Omni"])).ToArray();
        var expected = new[] { "kimi-k2.5", "qwen3-vl-plus", "qwen3-coder-flash", "qwen3.8-omni-flash" };
        Assert.Equal(expected.Where(id => id != "qwen3-vl-plus"), BailianModelCatalog.SelectModels(models, false));
        Assert.Equal(expected, BailianModelCatalog.SelectModels(models, true));
    }

    [Fact]
    public async Task FetchAsync_ReadsEveryPageEvenWhenServerCapsPageSize()
    {
        var requests = new List<string>();
        using var http = new HttpClient(new FakeHandler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-key", request.Headers.Authorization?.Parameter);
            requests.Add(request.RequestUri!.Query);
            var model = new { model = $"model-{requests.Count}", capabilities = new[] { "TG" },
                inference_metadata = new { request_modality = new[] { "Text" }, response_modality = new[] { "Text" } } };
            return JsonSerializer.Serialize(new { success = true, output = new { total = 2, models = new[] { model } } });
        }));
        var models = await new BailianModelCatalog(http).FetchAsync("test-key");
        Assert.Equal(2, models.Length);
        Assert.Contains("page_no=2", requests[1]);
    }

    [Theory]
    [InlineData("{\"success\":false}")]
    [InlineData("{\"data\":[{\"id\":\"unknown\"}]}")]
    [InlineData("{\"output\":{\"total\":2,\"models\":[]}}")]
    public async Task FetchAsync_RejectsFailureMalformedOrIncompleteDirectory(string json)
    {
        using var http = new HttpClient(new FakeHandler(_ => json));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new BailianModelCatalog(http).FetchAsync("test-key"));
    }

    [Fact]
    public void NovelChoices_PutsKimiFirstAndKeepsRecommendationOutOfModelId()
    {
        var vm = new SettingsViewModel();
        vm.ModelOptions = ["qwen", "kimi-k2.5", "deepseek", "kimi-k2.6"];
        Assert.Equal(["kimi-k2.5", "kimi-k2.6", "qwen", "deepseek"], vm.NovelModelChoices.Select(c => c.Id));
        Assert.Contains("推荐", vm.NovelModelChoices[0].DisplayName);
        vm.SelectedNovelModelChoice = vm.NovelModelChoices[1];
        Assert.Equal("kimi-k2.6", vm.SelectedModel);
        vm.SelectedModel = "qwen";
        Assert.Equal("qwen", vm.SelectedNovelModelChoice?.Id);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(response(request), Encoding.UTF8, "application/json") });
    }
}
