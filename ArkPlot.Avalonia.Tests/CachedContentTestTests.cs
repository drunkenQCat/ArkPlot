using ArkPlot.Avalonia.Services;
using ArkPlot.Avalonia.ViewModels.Test;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

public class CachedContentTestTests
{
    [Fact]
    public void ImageTest_OffersDeepSeekAndRefreshesOnlyRemoteCatalogProviders()
    {
        using var vm = new CachedContentTestViewModel(true, new FakeCache(), new FakeRunner());
        Assert.Contains("DeepSeek", vm.Providers);
        vm.SelectedProvider = "DeepSeek";
        Assert.Contains(vm.Models, model => model.Id == "deepseek-flash");
        Assert.True(vm.RefreshModelsCommand.CanExecute(null));
        vm.SelectedProvider = "Ollama";
        Assert.False(vm.RefreshModelsCommand.CanExecute(null));
    }

    [Fact]
    public async Task TextTest_LoadsCachedChapterAndOnlyRunsOnCommand()
    {
        var cache = new FakeCache();
        var runner = new FakeRunner();
        using var vm = new CachedContentTestViewModel(false, cache, runner);
        Assert.Contains("缓存对白", vm.InputText);
        Assert.Equal(0, runner.Calls);
        vm.InputText = "编辑后的测试文本";
        vm.SystemPrompt = "测试提示词";
        vm.SelectedModel = new ModelChoice("kimi-k2.5", "kimi-k2.5（推荐）");
        await vm.RunTestCommand.ExecuteAsync(null);
        Assert.Equal("编辑后的测试文本", runner.Request!.Input);
        Assert.Equal("kimi-k2.5", runner.Request.Model);
        Assert.Equal("测试提示词", runner.Request.Prompt);
        Assert.Equal("测试结果", vm.OutputText);
        Assert.Equal("思考", vm.ThinkingText);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task ImageTest_UsesSelectedCachedImageAndKeepsHistoricalDescription()
    {
        var runner = new FakeRunner();
        using var vm = new CachedContentTestViewModel(true, new FakeCache(), runner);
        Assert.Equal("历史描述", vm.CachedDescription);
        vm.SelectedModel = new ModelChoice("qwen3.8-omni-flash", "qwen3.8-omni-flash");
        await vm.RunTestCommand.ExecuteAsync(null);
        Assert.True(runner.Request!.ImageTest);
        Assert.Equal("https://example.invalid/portrait.png", runner.Request.Image!.Url);
        Assert.Equal("历史描述", vm.CachedDescription);
        Assert.Equal("测试结果", vm.OutputText);
    }

    [Fact]
    public async Task CancelTest_CancelsActiveRequestAndRestoresControls()
    {
        var runner = new FakeRunner { WaitForCancel = true };
        using var vm = new CachedContentTestViewModel(false, new FakeCache(), runner);
        vm.SelectedModel = new("test", "test");
        var running = vm.RunTestCommand.ExecuteAsync(null);
        Assert.True(vm.IsRunning);
        vm.CancelTestCommand.Execute(null);
        await running;
        Assert.False(vm.IsRunning);
        Assert.Contains("取消", vm.StatusText);
        Assert.Empty(vm.OutputText);
    }

    [Fact]
    public void PortraitTest_DrawsFromCacheAndAvoidsImmediateRepeat()
    {
        var vm = new PortraitTestViewModel(new FakeCache());
        var first = vm.Panel.SpeakerName;
        vm.LoadSampleCommand.Execute(null);
        Assert.NotEqual(first, vm.Panel.SpeakerName);
        Assert.Contains(vm.Panel.SpeakerName, new[] { "甲", "乙" });
        Assert.Contains("缓存活动", vm.StatusText);
        vm.ClearCommand.Execute(null);
        Assert.False(vm.Panel.HasPortrait);
    }

    [Fact]
    public void EmptyCache_ShowsActionableStatus()
    {
        using var vm = new CachedContentTestViewModel(false, new FakeCache { Empty = true }, new FakeRunner());
        Assert.Empty(vm.Activities);
        Assert.Empty(vm.InputText);
        Assert.Contains("暂无", vm.StatusText);
        var portrait = new PortraitTestViewModel(new FakeCache { Empty = true });
        Assert.False(portrait.Panel.HasPortrait);
        Assert.Contains("没有", portrait.StatusText);
    }

    private sealed class FakeCache : ITestCacheSource
    {
        public bool Empty { get; init; }
        public List<CachedChoice> GetActivities(bool images) => Empty ? [] : [new() { Id = 1, Name = "缓存活动" }];
        public List<CachedChoice> GetChapters(long id) => [new() { Id = 2, Name = "缓存章节" }];
        public string GetText(long id) => "甲：缓存对白";
        public List<CachedImage> GetImages(long id) => [new("https://example.invalid/portrait.png", "立绘", "历史描述")];
        public List<CachedCharacter> GetCharacters() => Empty ? [] : [
            new("a", "甲", "https://example.invalid/a.png", "缓存活动 / 缓存章节"),
            new("b", "乙", "https://example.invalid/b.png", "缓存活动 / 缓存章节")];
    }

    private sealed class FakeRunner : ICachedModelTestRunner
    {
        public int Calls;
        public ModelTestRequest? Request;
        public bool WaitForCancel { get; init; }
        public async Task<ModelTestResult> RunAsync(ModelTestRequest request, CancellationToken ct)
        {
            Calls++; Request = request;
            if (WaitForCancel) await Task.Delay(Timeout.Infinite, ct);
            return new("测试结果", "思考");
        }
    }
}
