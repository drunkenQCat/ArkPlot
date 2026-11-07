using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.Services;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Avalonia.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

public class DeepSeekSettingsTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
    private readonly string? _backup;
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "arkplot-models-" + Guid.NewGuid().ToString("N"));
    private static readonly CatalogModel[] Models = [
        new("deepseek-flash", ["text", "image"], ["text"], [], null, null),
        new("deepseek-v4-pro", ["text"], ["text"], [], null, null),
        new("future-vision", ["text", "image"], ["text"], [], null, null)
    ];

    public DeepSeekSettingsTests()
    {
        _backup = File.Exists(_settingsPath) ? File.ReadAllText(_settingsPath) : null;
        Directory.CreateDirectory(_cacheDir);
        new AppSettings(NovelizerSettings.CreateDefaults() with
        { ApiKeys = new() { ["DeepSeek"] = "saved-ds", ["百炼"] = "keep-bailian" } }, VisionSettings.CreateDefaults()).Save();
    }

    [Fact]
    public async Task Refresh_UsesEnteredKeyAndUpdatesBothPickersWithSeparateCapabilityFilters()
    {
        var vm = new SettingsViewModel("tags.json", (provider, key) =>
        {
            Assert.Equal("DeepSeek", provider);
            Assert.Equal("entered-ds", key);
            return Task.FromResult(Models);
        }, _cacheDir) { VisionSelectedProvider = "DeepSeek", DeepSeekApiKeyText = "entered-ds", SelectedModel = "deepseek-v4-pro" };
        await vm.RefreshVisionModelsCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.ModelOptions.Length);
        Assert.Equal(new[] { "deepseek-flash", "future-vision" }, vm.VisionModelOptions);
        Assert.Equal("deepseek-v4-pro", vm.SelectedModel);
        Assert.Equal("deepseek-flash", vm.VisionSelectedModel);
        Assert.Contains("2 个图片理解模型", vm.VisionModelCatalogStatus);

        var offline = new SettingsViewModel("tags.json", (_, _) => throw new Exception("must not fetch"), _cacheDir);
        offline.SelectedProvider = "百炼";
        offline.SelectedProvider = "DeepSeek";
        offline.VisionSelectedProvider = "DeepSeek";
        Assert.Equal(vm.ModelOptions, offline.ModelOptions);
        Assert.Equal(vm.VisionModelOptions, offline.VisionModelOptions);
        Assert.Equal("saved-ds", AppSettings.Load().GetApiKey("DeepSeek")); // Refresh does not save credentials.
    }

    [Fact]
    public async Task RefreshFailure_KeepsPreviousModelsAndSelection()
    {
        var calls = 0;
        var vm = new SettingsViewModel("tags.json", (_, _) => ++calls == 1
            ? Task.FromResult(Models) : throw new HttpRequestException("HTTP 401"), _cacheDir)
            { VisionSelectedProvider = "DeepSeek", DeepSeekApiKeyText = "test", SelectedModel = "deepseek-v4-flash" };
        await vm.RefreshNovelModelsCommand.ExecuteAsync(null);
        Assert.Equal("deepseek-flash", vm.SelectedModel);
        vm.VisionSelectedModel = "future-vision";
        await vm.RefreshVisionModelsCommand.ExecuteAsync(null);
        Assert.Equal("future-vision", vm.VisionSelectedModel);
        Assert.Equal(2, vm.VisionModelOptions.Length);
        Assert.Contains("已保留现有模型列表", vm.VisionModelCatalogStatus);
        Assert.False(vm.IsModelCatalogLoading);
    }

    [Fact]
    public void SavingVisionKey_SharesDeepSeekCredentialAndPreservesOtherProviders()
    {
        var vm = new SettingsViewModel { VisionSelectedProvider = "DeepSeek", VisionSelectedModel = "deepseek-flash", DeepSeekApiKeyText = "new-ds" };
        vm.SaveVisionSettingsCommand.Execute(null);
        var saved = AppSettings.Load();
        Assert.Equal("new-ds", saved.Novelizer.GetApiKeyForProvider("DeepSeek"));
        Assert.Equal("keep-bailian", saved.GetApiKey("百炼"));
        Assert.Equal("DeepSeek", saved.Vision!.SelectedProvider);
        Assert.Equal("https://api.deepseek.com", saved.Vision.GetBaseUrlForProvider("DeepSeek"));
        Assert.Contains("deepseek-flash", saved.Vision.GetModelsForProvider("DeepSeek"));
    }

    [Fact]
    public void VisionCredential_UsesDeepSeekEnvironmentInsteadOfDashscope()
    {
        var oldDs = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        var oldBailian = Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", "ds-env");
            Environment.SetEnvironmentVariable("DASHSCOPE_API_KEY", "bailian-env");
            Assert.Equal("ds-env", VisionSettings.CreateDefaults().GetApiKeyForProvider("DeepSeek"));
            Assert.Equal("saved-ds", AppSettings.Load().GetVisionApiKey("DeepSeek"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", oldDs);
            Environment.SetEnvironmentVariable("DASHSCOPE_API_KEY", oldBailian);
        }
    }

    [AvaloniaFact]
    public void VisionPage_ShowsDeepSeekCredentialAndProviderSpecificRefresh()
    {
        var vm = new SettingsViewModel { SelectedTabIndex = 2, VisionSelectedProvider = "DeepSeek" };
        var window = new SettingsWindow { DataContext = vm };
        Interaction.GetBehaviors(window).Clear();
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "DeepSeek API Key");
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && ReferenceEquals(button.Command, vm.RefreshVisionModelsCommand));
        }
        finally { window.Close(); }
    }

    public void Dispose()
    {
        if (_backup == null) File.Delete(_settingsPath);
        else File.WriteAllText(_settingsPath, _backup);
        Directory.Delete(_cacheDir, true);
    }
}
