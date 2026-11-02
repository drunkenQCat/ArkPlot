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
    private static readonly string CatalogCachePath = Path.Combine(AppContext.BaseDirectory, "bailian-model-catalog.json");
    private CatalogModel[] _catalogModels = ReadModelCache();
    private bool _settingsLoaded;
    [ObservableProperty] private bool _isModelCatalogLoading;
    [ObservableProperty] private string _modelCatalogStatus = "百炼模型列表会自动获取，也可手动刷新。";
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

    private string[] GetNovelizerModels(NovelizerSettings settings, string provider) => provider == "百炼"
        ? BailianModelCatalog.SelectModels(_catalogModels, imageInput: false)
        : settings.GetModelsForProvider(provider).ToArray();

    private string[] GetVisionModels(VisionSettings settings, string provider) => provider == "百炼"
        ? BailianModelCatalog.SelectModels(_catalogModels, imageInput: true)
        : settings.GetModelsForProvider(provider);

    [RelayCommand]
    private async Task RefreshBailianModels()
    {
        if (IsModelCatalogLoading) return;
        IsModelCatalogLoading = true;
        ModelCatalogStatus = "正在获取百炼模型目录…";
        try
        {
            var key = !string.IsNullOrWhiteSpace(BailianApiKeyText)
                ? BailianApiKeyText : AppSettings.Load().Novelizer.GetApiKeyForProvider("百炼");
            CatalogModel[] models;
            var usedDirectFallback = false;
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            {
                try { models = await new BailianModelCatalog(http).FetchAsync(key); }
                catch (Exception ex) when (ex is OperationCanceledException || ex is HttpRequestException { StatusCode: null })
                {
                    ModelCatalogStatus = "当前网络无法访问模型目录，正在尝试直连…";
                    using var direct = new HttpClient(new HttpClientHandler { UseProxy = false })
                    { Timeout = TimeSpan.FromSeconds(30) };
                    models = await new BailianModelCatalog(direct).FetchAsync(key);
                    usedDirectFallback = true;
                }
            }
            if (BailianModelCatalog.SelectModels(models, false).Length == 0 ||
                BailianModelCatalog.SelectModels(models, true).Length == 0)
                throw new InvalidOperationException("目录没有返回符合条件的文本／图片理解模型，已保留现有列表。");
            _catalogModels = models;
            ApplyModelCatalog();
            ModelCatalogStatus = $"已更新：{BailianModelCatalog.SelectModels(models, false).Length} 个文本模型，{BailianModelCatalog.SelectModels(models, true).Length} 个图片理解模型。请保存要使用的模型。";
            if (usedDirectFallback) ModelCatalogStatus += " 本次通过直连获取。";
            try
            {
                var tempPath = CatalogCachePath + ".tmp";
                await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(models));
                File.Move(tempPath, CatalogCachePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ModelCatalogStatus += " 本次列表可用，但本地缓存保存失败。";
            }
        }
        catch (Exception ex)
        {
            ModelCatalogStatus = $"获取失败：{ex.Message} " +
                (_catalogModels.Length > 0 ? "已保留上次成功的模型列表。" : "尚无可用缓存，请检查 API Key 和网络后刷新。");
        }
        finally { IsModelCatalogLoading = false; }
    }

    private void ApplyModelCatalog()
    {
        if (SelectedProvider == "百炼")
        {
            var previous = SelectedModel;
            ModelOptions = BailianModelCatalog.SelectModels(_catalogModels, false);
            SelectedModel = ModelOptions.Contains(previous) ? previous : NovelModelChoices.FirstOrDefault()?.Id ?? "";
        }
        if (VisionSelectedProvider == "百炼")
        {
            var previous = VisionSelectedModel;
            VisionModelOptions = BailianModelCatalog.SelectModels(_catalogModels, true);
            VisionSelectedModel = VisionModelOptions.Contains(previous) ? previous : VisionModelOptions.FirstOrDefault() ?? "";
        }
    }

    private static CatalogModel[] ReadModelCache()
    {
        try
        {
            return (JsonSerializer.Deserialize<CatalogModel[]>(File.ReadAllText(CatalogCachePath)) ?? [])
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Id) && m.Inputs != null && m.Outputs != null && m.Capabilities != null)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
}
