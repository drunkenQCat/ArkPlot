using System.Net;
using ArkPlot.Novelizer;
using Xunit;

namespace ArkPlot.Novelizer.Tests;

public class NovelBatchOutcomeTests
{
    private sealed class FailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("invalid key") });
    }

    [Fact]
    public async Task FailedChapter_IsReportedAndNeverCachedAsSuccessful()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arkplot-outcome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "story.md");
            await File.WriteAllTextAsync(input, "## 第一章\n\n阿米娅：博士，我们出发吧。\n");
            var config = new ApiConfig { Provider = ApiProvider.Custom, ApiKey = "fake", BaseUrl = "http://localhost", MaxRetries = 0 };
            using var http = new HttpClient(new FailureHandler());
            var pipeline = new NovelizerPipeline(new BailianClient(http, config), config);
            var result = await pipeline.BatchProcessAsync(dir, ["test-model"], false, onlyFile: input);
            Assert.NotEmpty(result.Errors);
            Assert.Empty(result.NovelFiles);
            Assert.Empty(result.EpubFiles);
            Assert.Empty(Directory.GetFiles(dir, "*_novel_*.md"));
            Assert.Single(Directory.GetFiles(Path.Combine(dir, "incomplete"), "*.md"));
            Assert.Null(new ChapterCache(dir).Check(input, "test-model", false));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task MissingInput_HasExplicitFailureEvenWithHistoricalNovel()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arkplot-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "old_novel_test.md"), "old novel");
            var config = new ApiConfig();
            using var http = new HttpClient(new FailureHandler());
            var pipeline = new NovelizerPipeline(new BailianClient(http, config), config);
            var result = await pipeline.BatchProcessAsync(dir, ["test"], false, onlyFile: Path.Combine(dir, "missing.md"));
            Assert.NotEmpty(result.Errors);
            Assert.Empty(result.NovelFiles);
        }
        finally { Directory.Delete(dir, true); }
    }
}
