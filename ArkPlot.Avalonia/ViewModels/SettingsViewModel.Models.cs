using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkPlot.Avalonia.ViewModels;

public partial class SettingsViewModel
{
    private readonly string _modelCacheDirectory;
    private readonly Func<string, string, Task<CatalogModel[]>> _fetchModelCatalog;
    private CatalogModel[] _catalogModels = [];
    private CatalogModel[] _deepSeekCatalogModels = [];
    private bool _settingsLoaded;
    [ObservableProperty] private bool _isModelCatalogLoading;
    [ObservableProperty] private string _modelCatalogStatus = "百炼模型列表会自动获取，也可手动刷新。";
    [ObservableProperty] private string _deepSeekModelCatalogStatus = "DeepSeek 模型列表会自动获取，也可手动刷新。";
    [ObservableProperty] private ModelChoice[] _novelModelChoices = ModelChoice.ForNovel(NovelizerSettings.BuiltInProviders["DeepSeek"].Models);
    [ObservableProperty] private ModelChoice? _selectedNovelModelChoice;

    partial void OnModelOptionsChanged(string[] value)
    {
        NovelModelChoices = ModelChoice.ForNovel(value);
        SelectedNovelModelChoice = NovelModelChoices.FirstOrDefault(m => m.Id == SelectedModel)
            ?? NovelModelChoices.FirstOrDefault();
    }

    partial void OnSelectedModelChanged(string value) =>
        SelectedNovelModelChoice = NovelModelChoices.FirstOrDefault(m => m.Id == value);

    partial void OnSelectedNovelModelChoiceChanged(ModelChoice? value)
    {
        if (value != null) SelectedModel = value.Id;
    }

    public bool HasNovelRemoteCatalog => SelectedProvider is "百炼" or "DeepSeek";
    public bool HasVisionRemoteCatalog => VisionSelectedProvider is "百炼" or "DeepSeek";
    public string NovelModelCatalogStatus => SelectedProvider == "DeepSeek" ? DeepSeekModelCatalogStatus : ModelCatalogStatus;
    public string VisionModelCatalogStatus => VisionSelectedProvider == "DeepSeek" ? DeepSeekModelCatalogStatus : ModelCatalogStatus;

    partial void OnModelCatalogStatusChanged(string value) => NotifyCatalogStatus();
    partial void OnDeepSeekModelCatalogStatusChanged(string value) => NotifyCatalogStatus();
    private void NotifyCatalogStatus()
    {
        OnPropertyChanged(nameof(NovelModelCatalogStatus));
        OnPropertyChanged(nameof(VisionModelCatalogStatus));
    }

    private string[] GetNovelizerModels(NovelizerSettings settings, string provider) => provider switch
    {
        "百炼" => BailianModelCatalog.SelectModels(_catalogModels, imageInput: false),
        "DeepSeek" when _deepSeekCatalogModels.Length > 0 => DeepSeekModelCatalog.SelectModels(_deepSeekCatalogModels, false),
        _ => settings.GetModelsForProvider(provider).ToArray()
    };

    private string[] GetVisionModels(VisionSettings settings, string provider) => provider switch
    {
        "百炼" => BailianModelCatalog.SelectModels(_catalogModels, imageInput: true),
        "DeepSeek" when _deepSeekCatalogModels.Length > 0 => DeepSeekModelCatalog.SelectModels(_deepSeekCatalogModels, true),
        _ => settings.GetModelsForProvider(provider)
    };

    [RelayCommand]
    private Task RefreshNovelModels() => RefreshModelCatalog(SelectedProvider);

    [RelayCommand]
    private Task RefreshVisionModels() => RefreshModelCatalog(VisionSelectedProvider);

    private bool NeedsModelCatalog(string provider) => provider switch
    {
        "百炼" => _catalogModels.Length == 0,
        "DeepSeek" => _deepSeekCatalogModels.Length == 0,
        _ => false
    };

    private void SetCatalogStatus(string provider, string status)
    {
        if (provider == "DeepSeek") DeepSeekModelCatalogStatus = status;
        else ModelCatalogStatus = status;
    }

    private async Task RefreshModelCatalog(string provider)
    {
        if (IsModelCatalogLoading || provider is not ("百炼" or "DeepSeek")) return;
        IsModelCatalogLoading = true;
        SetCatalogStatus(provider, $"正在获取 {provider} 模型目录…");
        try
        {
            var enteredKey = provider == "DeepSeek" ? DeepSeekApiKeyText : BailianApiKeyText;
            var key = !string.IsNullOrWhiteSpace(enteredKey) ? enteredKey : AppSettings.Load().GetApiKey(provider);
            var models = await _fetchModelCatalog(provider, key);
            var textModels = SelectCatalogModels(provider, models, false);
            var imageModels = SelectCatalogModels(provider, models, true);
            if (textModels.Length == 0)
                throw new InvalidOperationException("目录没有返回可用的文本模型，已保留现有列表。");
            if (provider == "DeepSeek") _deepSeekCatalogModels = models;
            else _catalogModels = models;
            ApplyModelCatalog(provider);
            var status = $"{provider} 已更新：{textModels.Length} 个文本模型，{imageModels.Length} 个图片理解模型。请保存要使用的模型。";
            SetCatalogStatus(provider, status);
            try
            {
                var path = Path.Combine(_modelCacheDirectory, provider == "DeepSeek" ? "deepseek-model-catalog.json" : "bailian-model-catalog.json");
                var tempPath = path + ".tmp";
                await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(models));
                File.Move(tempPath, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetCatalogStatus(provider, status + " 本次列表可用，但本地缓存保存失败。");
            }
        }
        catch (Exception ex)
        {
            SetCatalogStatus(provider, $"获取失败：{ex.Message} 已保留现有模型列表，请检查 API Key 和网络后刷新。");
        }
        finally { IsModelCatalogLoading = false; }
    }

    private async Task<CatalogModel[]> FetchModelCatalogAsync(string provider, string key)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try { return await FetchCatalogAsync(http, provider, key); }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException { StatusCode: null })
        {
            SetCatalogStatus(provider, "当前网络无法访问模型目录，正在尝试直连…");
            using var direct = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(30) };
            return await FetchCatalogAsync(direct, provider, key);
        }
    }

    internal static Task<CatalogModel[]> FetchCatalogAsync(HttpClient http, string provider, string key) => provider == "DeepSeek"
        ? new DeepSeekModelCatalog(http).FetchAsync(key) : new BailianModelCatalog(http).FetchAsync(key);

    internal static string[] SelectCatalogModels(string provider, CatalogModel[] models, bool imageInput) => provider == "DeepSeek"
        ? DeepSeekModelCatalog.SelectModels(models, imageInput) : BailianModelCatalog.SelectModels(models, imageInput);

    private void ApplyModelCatalog(string provider)
    {
        if (SelectedProvider == provider)
        {
            var previous = SelectedModel;
            ModelOptions = GetNovelizerModels(AppSettings.Load().Novelizer, provider);
            if (provider == "DeepSeek") previous = DeepSeekModelCatalog.NormalizeModelId(previous, ModelOptions);
            SelectedModel = ModelOptions.Contains(previous) ? previous : NovelModelChoices.FirstOrDefault()?.Id ?? "";
        }
        if (VisionSelectedProvider == provider)
        {
            var previous = VisionSelectedModel;
            VisionModelOptions = GetVisionModels(GetCurrentVisionSettings(), provider);
            if (provider == "DeepSeek") previous = DeepSeekModelCatalog.NormalizeModelId(previous, VisionModelOptions);
            VisionSelectedModel = VisionModelOptions.Contains(previous) ? previous : VisionModelOptions.FirstOrDefault() ?? "";
        }
    }

    internal static CatalogModel[] ReadModelCache(string path)
    {
        try
        {
            return (JsonSerializer.Deserialize<CatalogModel[]>(File.ReadAllText(path)) ?? [])
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Id) && m.Inputs != null && m.Outputs != null && m.Capabilities != null)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
}
