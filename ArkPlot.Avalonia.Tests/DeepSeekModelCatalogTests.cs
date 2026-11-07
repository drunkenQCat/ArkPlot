using System.Net;
using System.Text;
using ArkPlot.Avalonia.Services;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

public class DeepSeekModelCatalogTests
{
    [Fact]
    public async Task Catalog_UsesOfficialEndpointAndFiltersByImageInput()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.deepseek.com/models", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-key", request.Headers.Authorization.Parameter);
            return """
                {"object":"list","data":[
                  {"id":"deepseek-flash","input_modalities":["text","image"],"output_modalities":["text"]},
                  {"id":"deepseek-v4-pro","input_modalities":["text"],"output_modalities":["text"]},
                  {"id":"future-vision","input_modalities":["text","image"],"output_modalities":["text"]},
                  {"id":"generator","input_modalities":["text"],"output_modalities":["image"]}
                ]}
                """;
        }));
        var models = await new DeepSeekModelCatalog(http).FetchAsync(" test-key ");
        Assert.Equal(new[] { "deepseek-flash", "deepseek-v4-pro", "future-vision" }, DeepSeekModelCatalog.SelectModels(models, false));
        Assert.Equal(new[] { "deepseek-flash", "future-vision" }, DeepSeekModelCatalog.SelectModels(models, true));
    }

    [Fact]
    public async Task MissingMetadata_OnlyInfersVisionForDocumentedModels()
    {
        using var http = new HttpClient(new Handler(_ => """
            {"data":[{"id":"deepseek-flash"},{"id":"deepseek-v4-flash-vision-exp"},
            {"id":"unknown-vision"},{"id":"explicit-text","input_modalities":["text"]}]}
            """));
        var models = await new DeepSeekModelCatalog(http).FetchAsync("test");
        Assert.Equal(new[] { "deepseek-flash", "deepseek-v4-flash-vision-exp" }, DeepSeekModelCatalog.SelectModels(models, true));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":[]}")]
    [InlineData("{\"data\":[{\"id\":\"\"}]}")]
    public async Task InvalidDirectory_IsRejected(string json)
    {
        using var http = new HttpClient(new Handler(_ => json));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DeepSeekModelCatalog(http).FetchAsync("test"));
    }

    [Fact]
    public async Task AuthenticationFailure_DoesNotReturnAnEmptySuccessfulCatalog()
    {
        using var http = new HttpClient(new Handler(_ => "{}", HttpStatusCode.Unauthorized));
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => new DeepSeekModelCatalog(http).FetchAsync("test"));
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    private sealed class Handler(Func<HttpRequestMessage, string> response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(response(request), Encoding.UTF8, "application/json") });
    }
}
