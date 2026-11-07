using System.Net;
using System.Text;
using Xunit;

namespace ArkPlot.Novelizer.Tests;

public class ChapterSplitterTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void BookTitleAndSeparator_AreNotChapters(string newline)
    {
        var input = string.Join(newline, "# 孤星", "", "---", "", "## CW-1 行动前", "甲：出发。", "## CW-2 行动后", "乙：到了。");
        var chapters = ChapterSplitter.SplitChapters(MarkdownBuilder.PreprocessMdContent(input));
        Assert.Equal(new[] { "CW-1 行动前", "CW-2 行动后" }, chapters.Select(c => c.Title));
        Assert.Equal(new[] { 0, 1 }, chapters.Select(c => c.Index));
        Assert.Equal("甲：出发。", chapters[0].Body);
        Assert.Equal("孤星", ChapterSplitter.GetDocumentTitle(input));
    }

    [Theory]
    [InlineData("# 只有书名")]
    [InlineData("# 只有书名\n\n---\n\n## 空章\n### 小节")]
    [InlineData("")]
    public void HeadingOnlyInput_DoesNotCreateGenerationTasks(string input) =>
        Assert.Empty(ChapterSplitter.SplitChapters(input));

    [Fact]
    public void RealPrefaceAndHeadingFreeContent_ArePreserved()
    {
        var chapters = ChapterSplitter.SplitChapters("# 书名\n序言正文。\n## 第一章\n第一章正文。");
        Assert.Equal("序言", chapters[0].Title);
        Assert.Equal("序言正文。", chapters[0].Body);
        var plain = Assert.Single(ChapterSplitter.SplitChapters("甲：第一句。\n乙：第二句。"));
        Assert.Equal("甲：第一句。\n乙：第二句。", plain.Body);
    }

    [Fact]
    public async Task Pipeline_OnlyCallsModelForRealChaptersAndRetainsBookTitle()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arkplot-title-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "story.md");
            await File.WriteAllTextAsync(input, "# 孤星\n\n---\n\n## 第一章\n第一章剧情。\n## 第二章\n第二章剧情。");
            var handler = new CountingHandler();
            using var http = new HttpClient(handler);
            var config = new ApiConfig { Provider = ApiProvider.Custom, ApiKey = "test", BaseUrl = "https://example.invalid", MaxRetries = 0 };
            var pipeline = new NovelizerPipeline(new BailianClient(http, config), config);
            var output = await pipeline.ProcessMdFileAsync(input, "test", dir);
            Assert.Equal(2, handler.Calls);
            var novel = await File.ReadAllTextAsync(output);
            Assert.StartsWith("# 孤星\n\n", novel);
            Assert.DoesNotContain("## 孤星", novel);
            Assert.Contains("第一章", novel);
            Assert.Contains("第二章", novel);
        }
        finally { Directory.Delete(dir, true); }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("""{"choices":[{"message":{"content":"生成的小说正文。"}}]}""", Encoding.UTF8, "application/json") });
        }
    }
}
