using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using ArkPlot.Avalonia.Models;
using ArkPlot.Core.Infrastructure;
using ArkPlot.Core.Model;
using ArkPlot.Core.Services;
using ArkPlot.Core.Utilities.PrtsComponents;
using ArkPlot.Tts;
using ArkPlot.Tts.Engines;
using ArkPlot.Tts.Models;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniMax;

namespace ArkPlot.Avalonia.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly string _tagsJsonPath;

    // --- Tab 1: 标签替换 ---
    [ObservableProperty] private ObservableCollection<TagReplacementRule> _dataGrid = new();
    [ObservableProperty] private int _selectedIndex;

    // --- Tab 2: 小说化设置 ---
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _systemPromptText = "";
    [ObservableProperty] private string _deepSeekApiKeyText = "";
    [ObservableProperty] private string _bailianApiKeyText = "";
    [ObservableProperty] private string _selectedProvider = "DeepSeek";
    [ObservableProperty] private string _selectedModel = "deepseek-v4-pro";
    [ObservableProperty] private string[] _providerOptions = NovelizerSettings.BuiltInProviders.Keys.ToArray();
    [ObservableProperty] private string[] _modelOptions = NovelizerSettings.BuiltInProviders["DeepSeek"].Models;
    [ObservableProperty] private string _saveFeedbackText = "";

    // 小说化自定义 Provider
    [ObservableProperty] private ObservableCollection<ProviderConfig> _customProviderList = new();
    [ObservableProperty] private string _editingProviderName = "";
    [ObservableProperty] private string _editingProviderUrl = "";
    [ObservableProperty] private string _editingProviderKey = "";
    [ObservableProperty] private string _editingProviderModelsText = "";
    [ObservableProperty] private int _selectedCustomProviderIndex = -1;

    // --- Tab 3: 图片描述设置 ---
    [ObservableProperty] private string _visionSystemPrompt = VisionSettings.DefaultSystemPrompt;
    [ObservableProperty] private string _visionSelectedProvider = "百炼";
    [ObservableProperty] private string _visionSelectedModel = "qwen3-vl-flash";
    [ObservableProperty] private string _visionOllamaBaseUrl = "http://localhost:11434";
    [ObservableProperty] private string[] _visionProviderOptions = VisionSettings.BuiltInModels.Keys.ToArray();
    [ObservableProperty] private string[] _visionModelOptions = VisionSettings.BuiltInModels["百炼"];
    [ObservableProperty] private bool _isOllamaProvider;
    [ObservableProperty] private string _visionSaveFeedbackText = "";

    // 图片描述自定义 Provider
    [ObservableProperty] private ObservableCollection<ProviderConfig> _visionCustomProviderList = new();
    [ObservableProperty] private string _visionEditingProviderName = "";
    [ObservableProperty] private string _visionEditingProviderUrl = "";
    [ObservableProperty] private string _visionEditingProviderKey = "";
    [ObservableProperty] private string _visionEditingProviderModelsText = "";
    [ObservableProperty] private int _visionSelectedCustomProviderIndex = -1;

    // --- Tab 4: TTS 配置 ---
    [ObservableProperty] private bool _ttsEdgeTtsEnabled = true;
    [ObservableProperty] private bool _ttsMiniMaxEnabled;
    [ObservableProperty] private string _ttsMiniMaxApiKey = "";
    [ObservableProperty] private string _ttsMiniMaxBaseUrl = "https://api.minimax.io/";
    [ObservableProperty] private string _ttsMiniMaxModel = "speech-2.8-hd";
    [ObservableProperty] private string _ttsSaveFeedbackText = "";
    [ObservableProperty] private string _ttsEdgeTtsSummary = "";
    [ObservableProperty] private int _ttsMiniMaxVoiceCount;
    [ObservableProperty] private ObservableCollection<CustomTtsProvider> _ttsCustomEngineList = new();
    [ObservableProperty] private bool _ttsEditingEngineVisible;
    [ObservableProperty] private string _ttsEditingEngineTitle = "";
    [ObservableProperty] private string _ttsEditingEngineName = "";
    [ObservableProperty] private string _ttsEditingEngineUrl = "";
    [ObservableProperty] private string _ttsEditingEngineKey = "";
    [ObservableProperty] private string _ttsEditingEngineModel = "";
    [ObservableProperty] private VoiceEntry[] _ttsEditingEngineVoices = [];
    [ObservableProperty] private int _ttsEditingEngineVoiceCount;
    [ObservableProperty] private ObservableCollection<TtsPoolBreakdownItem> _ttsPoolBreakdown = new();
    [ObservableProperty] private string _ttsPoolTotalText = "";
    [ObservableProperty] private string _ttsPoolGenderText = "";
    [ObservableProperty] private ObservableCollection<VoiceAssignment> _ttsAllVoices = new();
    [ObservableProperty] private VoiceAssignment? _ttsDefaultNarratorVoice;
    // MiniMax voices (editable list for voice manager)
    [ObservableProperty] private ObservableCollection<VoiceEntry> _ttsMiniMaxVoices = new();

    private Guid? _editingEngineId; // null = adding, non-null = editing existing

    // Base URL ComboBox 预填选项
    public string[] MiniMaxBaseUrlOptions => ["https://api.minimax.io/", "https://api.minimaxi.com/"];
    // Model ComboBox 预填选项（来自 MiniMax SDK 文档）
    public string[] MiniMaxModelOptions => ["speech-2.8-hd", "speech-2.8-turbo"];

    public SettingsViewModel(string tagsJsonPath)
    {
        _tagsJsonPath = tagsJsonPath;
    }

    public SettingsViewModel() : this("tags.json")
    {
    }

    [RelayCommand]
    private void LoadSettings()
    {
        LoadTagJson();
        LoadNovelizerSettings();
        LoadVisionSettings();
        LoadTtsSettings();
    }

    // ==================== Tab 1: 标签替换 ====================

    [RelayCommand]
    private void SaveTagJson()
    {
        var data =
            (from item in DataGrid
             let tag = (item.Tag, item.NewTag)
             let tagReg = ($"{item.Tag}_reg", item.Reg)
             from pair in new[] { tag, tagReg }
             orderby pair.Item1
             select pair)
            .ToDictionary(x => x.Item1, x => x.Item2);
        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

        var jsonContent = JsonSerializer.Serialize(data, options);
        File.WriteAllText("tags.json", jsonContent);
        LoadTagJson();
    }

    private void LoadTagJson()
    {
        try
        {
            if (!File.Exists(_tagsJsonPath)) return;
            var jsonContent = File.ReadAllText(_tagsJsonPath);
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent);
            var tagsAndRegs = from pair in data
                              where !pair.Key.EndsWith("_reg")
                              let reg = data![pair.Key + "_reg"]
                              select new TagReplacementRule(pair.Key, reg, pair.Value);
            DataGrid = new ObservableCollection<TagReplacementRule>(tagsAndRegs);
        }
        catch
        {
        }
    }

    [RelayCommand]
    private void AddItem()
    {
        var maxNum = FindMaxIndexOfNewItem();
        var newItem = new TagReplacementRule($"NewItem {maxNum + 1}", "", "");
        DataGrid.Insert(0, newItem);
        SelectedIndex = 0;
    }

    [RelayCommand]
    private void CloseWindow(object? window)
    {
        if (window is Window w)
            w.Close();
    }

    [RelayCommand]
    private void RemoveTag(TagReplacementRule tag)
    {
        DataGrid.Remove(tag);
    }

    private int FindMaxIndexOfNewItem()
    {
        var maxItem =
            (from item in DataGrid
             where item.Tag.Contains("NewItem")
             orderby item.Tag descending
             select item.Tag).FirstOrDefault();
        if (maxItem == null) return 0;
        try
        {
            var maxNum = maxItem.Split(" ")[^1];
            return int.Parse(maxNum);
        }
        catch
        {
            return 0;
        }
    }

    // ==================== Tab 2: 小说化设置 ====================

    partial void OnSelectedProviderChanged(string value)
    {
        var settings = AppSettings.Load();
        var novelizer = settings.Novelizer with { CustomProviders = CustomProviderList.ToArray() };
        ModelOptions = novelizer.GetModelsForProvider(value);
        if (ModelOptions.Length > 0 && !ModelOptions.Contains(SelectedModel))
            SelectedModel = ModelOptions[0];
    }

    private void LoadNovelizerSettings()
    {
        var settings = AppSettings.Load();
        var novelizer = settings.Novelizer;

        SystemPromptText = novelizer.SystemPrompt;

        // 自定义 Provider 列表
        CustomProviderList = new ObservableCollection<ProviderConfig>(novelizer.CustomProviders ?? []);
        RefreshNovelizerProviderOptions();

        SelectedProvider = novelizer.SelectedProvider;
        ModelOptions = novelizer.GetModelsForProvider(SelectedProvider);
        SelectedModel = ModelOptions.Contains(novelizer.SelectedModel) ? novelizer.SelectedModel : (ModelOptions.Length > 0 ? ModelOptions[0] : "");

        DeepSeekApiKeyText = settings.GetApiKey("DeepSeek");
        BailianApiKeyText = settings.GetApiKey("百炼");
    }

    private void RefreshNovelizerProviderOptions()
    {
        var settings = AppSettings.Load();
        var novelizer = settings.Novelizer with { CustomProviders = CustomProviderList.ToArray() };
        ProviderOptions = novelizer.AllProviderNames;
    }

    [RelayCommand]
    private void SaveNovelizerSettings()
    {
        var settings = AppSettings.Load();
        var novelizer = settings.Novelizer with
        {
            SystemPrompt = SystemPromptText,
            SelectedProvider = SelectedProvider,
            SelectedModel = SelectedModel,
            ApiKeys = new Dictionary<string, string>
            {
                ["DeepSeek"] = DeepSeekApiKeyText,
                ["百炼"] = BailianApiKeyText
            },
            CustomProviders = CustomProviderList.ToArray()
        };
        settings = settings with { Novelizer = novelizer };
        settings.Save();

        SaveFeedbackText = "✅ 已保存";
        _ = Task.Delay(2000).ContinueWith(_ => SaveFeedbackText = "");
    }

    [RelayCommand]
    private void RestoreDefaultPrompt()
    {
        SystemPromptText = NovelizerSettings.DefaultSystemPrompt;
    }

    // --- 小说化自定义 Provider CRUD ---

    [RelayCommand]
    private void AddCustomProvider()
    {
        if (string.IsNullOrWhiteSpace(EditingProviderName)) return;
        var models = EditingProviderModelsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var config = new ProviderConfig(EditingProviderName.Trim(), EditingProviderUrl.Trim(), EditingProviderKey.Trim(), models);
        CustomProviderList.Add(config);
        RefreshNovelizerProviderOptions();
        ClearEditingFields();
    }

    [RelayCommand]
    private void UpdateCustomProvider()
    {
        if (SelectedCustomProviderIndex < 0 || SelectedCustomProviderIndex >= CustomProviderList.Count) return;
        var models = EditingProviderModelsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        CustomProviderList[SelectedCustomProviderIndex] = new ProviderConfig(
            EditingProviderName.Trim(), EditingProviderUrl.Trim(), EditingProviderKey.Trim(), models);
        RefreshNovelizerProviderOptions();
    }

    [RelayCommand]
    private void DeleteCustomProvider()
    {
        if (SelectedCustomProviderIndex < 0 || SelectedCustomProviderIndex >= CustomProviderList.Count) return;
        var deletedName = CustomProviderList[SelectedCustomProviderIndex].Name;
        CustomProviderList.RemoveAt(SelectedCustomProviderIndex);
        SelectedCustomProviderIndex = -1;
        // 如果删除的是当前选中的平台，重置为第一个预设平台
        if (SelectedProvider == deletedName)
            SelectedProvider = NovelizerSettings.BuiltInProviders.Keys.First();
        RefreshNovelizerProviderOptions();
        ClearEditingFields();
    }

    partial void OnSelectedCustomProviderIndexChanged(int value)
    {
        if (value >= 0 && value < CustomProviderList.Count)
        {
            var p = CustomProviderList[value];
            EditingProviderName = p.Name;
            EditingProviderUrl = p.BaseUrl;
            EditingProviderKey = p.ApiKey;
            EditingProviderModelsText = string.Join(", ", p.Models);
        }
    }

    private void ClearEditingFields()
    {
        EditingProviderName = "";
        EditingProviderUrl = "";
        EditingProviderKey = "";
        EditingProviderModelsText = "";
    }

    // ==================== Tab 3: 图片描述设置 ====================

    partial void OnVisionSelectedProviderChanged(string value)
    {
        var vision = GetCurrentVisionSettings();
        VisionModelOptions = vision.GetModelsForProvider(value);
        if (VisionModelOptions.Length > 0 && !VisionModelOptions.Contains(VisionSelectedModel))
            VisionSelectedModel = VisionModelOptions[0];
        IsOllamaProvider = value == "Ollama";
    }

    private VisionSettings GetCurrentVisionSettings()
    {
        return new VisionSettings(
            IsPicDescEnabled: false,
            SelectedProvider: VisionSelectedProvider,
            SelectedModel: VisionSelectedModel,
            SystemPrompt: VisionSystemPrompt,
            OllamaBaseUrl: VisionOllamaBaseUrl,
            CustomProviders: VisionCustomProviderList.ToArray()
        );
    }

    private void LoadVisionSettings()
    {
        var settings = AppSettings.Load();
        var vision = settings.Vision ?? VisionSettings.CreateDefaults();

        VisionSystemPrompt = string.IsNullOrEmpty(vision.SystemPrompt) ? VisionSettings.DefaultSystemPrompt : vision.SystemPrompt;
        VisionOllamaBaseUrl = vision.OllamaBaseUrl;

        // 自定义 Provider 列表
        VisionCustomProviderList = new ObservableCollection<ProviderConfig>(vision.CustomProviders ?? []);
        RefreshVisionProviderOptions();

        VisionSelectedProvider = vision.SelectedProvider;
        VisionModelOptions = vision.GetModelsForProvider(VisionSelectedProvider);
        VisionSelectedModel = VisionModelOptions.Contains(vision.SelectedModel) ? vision.SelectedModel : (VisionModelOptions.Length > 0 ? VisionModelOptions[0] : "");
        IsOllamaProvider = VisionSelectedProvider == "Ollama";
    }

    private void RefreshVisionProviderOptions()
    {
        var vision = GetCurrentVisionSettings();
        VisionProviderOptions = vision.AllProviderNames;
    }

    [RelayCommand]
    private void SaveVisionSettings()
    {
        var settings = AppSettings.Load();
        var vision = new VisionSettings(
            IsPicDescEnabled: settings.Vision?.IsPicDescEnabled ?? false,
            SelectedProvider: VisionSelectedProvider,
            SelectedModel: VisionSelectedModel,
            SystemPrompt: VisionSystemPrompt,
            OllamaBaseUrl: VisionOllamaBaseUrl,
            CustomProviders: VisionCustomProviderList.ToArray()
        );
        settings = settings with { Vision = vision };
        settings.Save();

        VisionSaveFeedbackText = "✅ 已保存";
        _ = Task.Delay(2000).ContinueWith(_ => VisionSaveFeedbackText = "");
    }

    [RelayCommand]
    private void RestoreDefaultVisionPrompt()
    {
        VisionSystemPrompt = VisionSettings.DefaultSystemPrompt;
    }

    // --- 图片描述自定义 Provider CRUD ---

    [RelayCommand]
    private void AddVisionCustomProvider()
    {
        if (string.IsNullOrWhiteSpace(VisionEditingProviderName)) return;
        var models = VisionEditingProviderModelsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var config = new ProviderConfig(VisionEditingProviderName.Trim(), VisionEditingProviderUrl.Trim(), VisionEditingProviderKey.Trim(), models);
        VisionCustomProviderList.Add(config);
        RefreshVisionProviderOptions();
        ClearVisionEditingFields();
    }

    [RelayCommand]
    private void UpdateVisionCustomProvider()
    {
        if (VisionSelectedCustomProviderIndex < 0 || VisionSelectedCustomProviderIndex >= VisionCustomProviderList.Count) return;
        var models = VisionEditingProviderModelsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        VisionCustomProviderList[VisionSelectedCustomProviderIndex] = new ProviderConfig(
            VisionEditingProviderName.Trim(), VisionEditingProviderUrl.Trim(), VisionEditingProviderKey.Trim(), models);
        RefreshVisionProviderOptions();
    }

    [RelayCommand]
    private void DeleteVisionCustomProvider()
    {
        if (VisionSelectedCustomProviderIndex < 0 || VisionSelectedCustomProviderIndex >= VisionCustomProviderList.Count) return;
        var deletedName = VisionCustomProviderList[VisionSelectedCustomProviderIndex].Name;
        VisionCustomProviderList.RemoveAt(VisionSelectedCustomProviderIndex);
        VisionSelectedCustomProviderIndex = -1;
        // 如果删除的是当前选中的平台，重置为第一个预设平台
        if (VisionSelectedProvider == deletedName)
            VisionSelectedProvider = VisionSettings.BuiltInModels.Keys.First();
        RefreshVisionProviderOptions();
        ClearVisionEditingFields();
    }

    partial void OnVisionSelectedCustomProviderIndexChanged(int value)
    {
        if (value >= 0 && value < VisionCustomProviderList.Count)
        {
            var p = VisionCustomProviderList[value];
            VisionEditingProviderName = p.Name;
            VisionEditingProviderUrl = p.BaseUrl;
            VisionEditingProviderKey = p.ApiKey;
            VisionEditingProviderModelsText = string.Join(", ", p.Models);
        }
    }

    private void ClearVisionEditingFields()
    {
        VisionEditingProviderName = "";
        VisionEditingProviderUrl = "";
        VisionEditingProviderKey = "";
        VisionEditingProviderModelsText = "";
    }

    // ==================== Tab 4: 数据管理 ====================

    [ObservableProperty] private string _selectedLanguage = "zh_CN";
    [ObservableProperty] private string _dataManagementStatus = "";
    [ObservableProperty] private bool _isDataOperationRunning;

    public string[] LanguageOptions => ["zh_CN", "en_US"];

    [RelayCommand]
    private async Task ForceRefreshData()
    {
        if (IsDataOperationRunning) return;
        IsDataOperationRunning = true;

        try
        {
            var lang = SelectedLanguage;
            DataManagementStatus = $"正在从 GitHub 同步活动列表（{lang}）...";
            var sync = new StorySyncService();
            await sync.DownloadAndSaveAsync(lang);

            DataManagementStatus = "正在从 PRTS Wiki 刷新资源索引...";
            var prts = new PrtsDataProcessor();
            await prts.ForceRefreshAsync(lang);

            DataManagementStatus = "✅ 刷新完成！";
        }
        catch (Exception ex)
        {
            DataManagementStatus = $"❌ 刷新失败：{ex.Message}";
        }
        finally
        {
            IsDataOperationRunning = false;
        }
    }

    [RelayCommand]
    private async Task ResetAllData()
    {
        if (IsDataOperationRunning) return;
        IsDataOperationRunning = true;

        try
        {
            DataManagementStatus = "正在清空所有数据...";
            var db = DbFactory.GetClient();

            db.Deleteable<FormattedTextEntry>().ExecuteCommand();
            db.Deleteable<Plot>().ExecuteCommand();
            db.Deleteable<StoryChapter>().ExecuteCommand();
            db.Deleteable<Act>().ExecuteCommand();
            db.Deleteable<SyncState>().ExecuteCommand();
            db.Deleteable<PrtsResource>().ExecuteCommand();
            db.Deleteable<PrtsPortraitLink>().ExecuteCommand();
            db.Deleteable<PicDescription>().ExecuteCommand();
            db.Deleteable<PrtsData>().ExecuteCommand();

            DataManagementStatus = "✅ 已清空全部数据。建议重启程序。";
        }
        catch (Exception ex)
        {
            DataManagementStatus = $"❌ 清空失败：{ex.Message}";
        }
        finally
        {
            IsDataOperationRunning = false;
        }
    }

    // ==================== Tab 4: TTS 配置 ====================

    private void LoadTtsSettings()
    {
        var settings = AppSettings.Load();
        var tts = settings.Tts ?? TtsSettings.CreateDefaults();

        TtsEdgeTtsEnabled = tts.EdgeTtsEnabled;
        TtsMiniMaxEnabled = tts.MiniMaxEnabled;
        // API Key: settings.json → 环境变量 → 空
        TtsMiniMaxApiKey = !string.IsNullOrEmpty(tts.MiniMaxApiKey)
            ? tts.MiniMaxApiKey
            : Environment.GetEnvironmentVariable("MINIMAX_API_KEY") ?? "";
        TtsMiniMaxBaseUrl = tts.MiniMaxBaseUrl ?? "https://api.minimax.io/";
        TtsMiniMaxModel = tts.MiniMaxModel ?? "speech-2.8-hd";

        // 音色列表：如果有保存的则用保存的，否则预填 MiniMaxVoicePool 精选音色
        if (tts.MiniMaxVoices.Length > 0)
        {
            TtsMiniMaxVoices = new ObservableCollection<VoiceEntry>(tts.MiniMaxVoices);
        }
        else
        {
            TtsMiniMaxVoices = new ObservableCollection<VoiceEntry>(
                MiniMaxVoicePool.AllVoices.Select(v =>
                    new VoiceEntry(v.VoiceId, v.Label, v.Gender, "zh-CN")));
        }

        TtsCustomEngineList = new ObservableCollection<CustomTtsProvider>(tts.CustomEngines);
        TtsEditingEngineVisible = false;
        UpdateTtsPoolDisplay();
    }

    private void UpdateTtsPoolDisplay()
    {
        var breakdown = new List<TtsPoolBreakdownItem>();
        int totalFemale = 0, totalMale = 0, totalOther = 0;

        // EdgeTTS
        if (TtsEdgeTtsEnabled)
        {
            var f = VoicePool.Female.Length;
            var m = VoicePool.Male.Length;
            var count = f + m + 1; // +1 narrator
            breakdown.Add(new TtsPoolBreakdownItem("EdgeTTS", count));
            totalFemale += f + 1; // narrator is female
            totalMale += m;
            TtsEdgeTtsSummary = $"音色：{f}女 {m}男 1旁白";
        }
        else
        {
            TtsEdgeTtsSummary = "已禁用";
        }

        // MiniMax
        TtsMiniMaxVoiceCount = TtsMiniMaxVoices.Count;
        if (TtsMiniMaxEnabled)
        {
            if (string.IsNullOrEmpty(TtsMiniMaxApiKey))
            {
                // 有启用但没有 API Key，显示为"未配置"
                breakdown.Add(new TtsPoolBreakdownItem("MiniMax ⚠ 未配置", TtsMiniMaxVoices.Count));
            }
            else
            {
                breakdown.Add(new TtsPoolBreakdownItem("MiniMax", TtsMiniMaxVoices.Count));
            }
            
            foreach (var v in TtsMiniMaxVoices)
            {
                if (v.Gender == "Female") totalFemale++;
                else if (v.Gender == "Male") totalMale++;
                else totalOther++;
            }
        }

        // Custom engines
        foreach (var engine in TtsCustomEngineList)
        {
            breakdown.Add(new TtsPoolBreakdownItem(engine.Name, engine.Voices.Length));
            foreach (var v in engine.Voices)
            {
                if (v.Gender == "Female") totalFemale++;
                else if (v.Gender == "Male") totalMale++;
                else totalOther++;
            }
        }

        TtsPoolBreakdown = new ObservableCollection<TtsPoolBreakdownItem>(breakdown);

        var total = totalFemale + totalMale + totalOther;
        TtsPoolTotalText = $"合计：{total} 种";
        TtsPoolGenderText = $"女{totalFemale} · 男{totalMale}" +
            (totalOther > 0 ? $" · 其他{totalOther}" : "");

        // Rebuild all-voices list for narrator dropdown
        RebuildAllVoicesList();
    }

    private void RebuildAllVoicesList()
    {
        var allVoices = new List<VoiceAssignment>();

        if (TtsEdgeTtsEnabled)
        {
            foreach (var v in VoicePool.Female)
                allVoices.Add(new VoiceAssignment(new VoiceEntry(v, v, "Female", "zh-CN"), EngineType.EdgeTts, null));
            foreach (var v in VoicePool.Male)
                allVoices.Add(new VoiceAssignment(new VoiceEntry(v, v, "Male", "zh-CN"), EngineType.EdgeTts, null));
            allVoices.Add(new VoiceAssignment(new VoiceEntry(VoicePool.Narrator, VoicePool.Narrator, "Female", "zh-CN"), EngineType.EdgeTts, null));
        }

        if (TtsMiniMaxEnabled)
        {
            foreach (var v in TtsMiniMaxVoices)
                allVoices.Add(new VoiceAssignment(v, EngineType.MiniMax, null));
        }

        foreach (var engine in TtsCustomEngineList)
        {
            foreach (var v in engine.Voices)
                allVoices.Add(new VoiceAssignment(v, EngineType.Custom, engine.Id));
        }

        TtsAllVoices = new ObservableCollection<VoiceAssignment>(allVoices);

        // Try to restore selected narrator
        var settings = AppSettings.Load();
        var narratorId = settings.Tts?.DefaultNarratorVoice ?? "zh-CN-XiaoxiaoNeural";
        TtsDefaultNarratorVoice = allVoices.FirstOrDefault(v => v.VoiceId == narratorId) ?? allVoices.FirstOrDefault();
    }

    [RelayCommand]
    private void SaveTtsSettings()
    {
        var settings = AppSettings.Load();
        var tts = new TtsSettings
        {
            EdgeTtsEnabled = TtsEdgeTtsEnabled,
            MiniMaxEnabled = TtsMiniMaxEnabled,
            MiniMaxApiKey = string.IsNullOrEmpty(TtsMiniMaxApiKey) ? null : TtsMiniMaxApiKey,
            MiniMaxBaseUrl = string.IsNullOrEmpty(TtsMiniMaxBaseUrl) ? null : TtsMiniMaxBaseUrl,
            MiniMaxModel = string.IsNullOrEmpty(TtsMiniMaxModel) ? null : TtsMiniMaxModel,
            MiniMaxVoices = TtsMiniMaxVoices.ToArray(),
            CustomEngines = TtsCustomEngineList.ToArray(),
            DefaultNarratorVoice = TtsDefaultNarratorVoice?.VoiceId ?? "zh-CN-XiaoxiaoNeural"
        };
        settings = settings with { Tts = tts };
        settings.Save();

        TtsSaveFeedbackText = "✅ 所有更改已保存";
        UpdateTtsPoolDisplay();
        _ = Task.Delay(3000).ContinueWith(_ => TtsSaveFeedbackText = "");
    }

    // --- TTS 自定义引擎 CRUD ---

    [RelayCommand]
    private void AddTtsCustomEngine()
    {
        _editingEngineId = null;
        TtsEditingEngineTitle = "添加引擎";
        TtsEditingEngineName = "";
        TtsEditingEngineUrl = "";
        TtsEditingEngineKey = "";
        TtsEditingEngineModel = "";
        TtsEditingEngineVoices = [];
        TtsEditingEngineVoiceCount = 0;
        TtsEditingEngineVisible = true;
    }

    [RelayCommand]
    private void EditTtsCustomEngine(CustomTtsProvider? provider)
    {
        if (provider == null) return;
        _editingEngineId = provider.Id;
        TtsEditingEngineTitle = $"编辑：{provider.Name}";
        TtsEditingEngineName = provider.Name;
        TtsEditingEngineUrl = provider.BaseUrl;
        TtsEditingEngineKey = provider.ApiKey ?? "";
        TtsEditingEngineModel = provider.Model ?? "";
        TtsEditingEngineVoices = provider.Voices;
        TtsEditingEngineVoiceCount = provider.Voices.Length;
        TtsEditingEngineVisible = true;
    }

    [RelayCommand]
    private void SaveEditingEngine()
    {
        if (string.IsNullOrWhiteSpace(TtsEditingEngineName)) return;
        var provider = new CustomTtsProvider(
            _editingEngineId ?? Guid.NewGuid(),
            TtsEditingEngineName.Trim(),
            TtsEditingEngineUrl.Trim(),
            string.IsNullOrWhiteSpace(TtsEditingEngineKey) ? null : TtsEditingEngineKey.Trim(),
            string.IsNullOrWhiteSpace(TtsEditingEngineModel) ? null : TtsEditingEngineModel.Trim(),
            TtsEditingEngineVoices);

        if (_editingEngineId.HasValue)
        {
            // Update existing
            var idx = TtsCustomEngineList.ToList().FindIndex(p => p.Id == _editingEngineId.Value);
            if (idx >= 0) TtsCustomEngineList[idx] = provider;
        }
        else
        {
            // Add new
            TtsCustomEngineList.Add(provider);
        }

        TtsEditingEngineVisible = false;
        UpdateTtsPoolDisplay();
    }

    [RelayCommand]
    private void CancelEditingEngine()
    {
        TtsEditingEngineVisible = false;
    }

    [RelayCommand]
    private void DeleteTtsCustomEngine(CustomTtsProvider? provider)
    {
        if (provider == null) return;
        TtsCustomEngineList.Remove(provider);
        if (_editingEngineId == provider.Id)
            TtsEditingEngineVisible = false;
        UpdateTtsPoolDisplay();
    }

    // --- Voice Manager placeholder commands ---

    [RelayCommand]
    private async Task OpenMiniMaxVoiceManager(object? window)
    {
        if (window is not Window owner) return;

        // 创建 MiniMaxClient 用于获取音色列表
        MiniMaxClient? miniMaxClient = null;
        if (!string.IsNullOrWhiteSpace(TtsMiniMaxApiKey))
        {
            try
            {
                miniMaxClient = new MiniMaxClient(
                    baseUri: !string.IsNullOrWhiteSpace(TtsMiniMaxBaseUrl) ? new Uri(TtsMiniMaxBaseUrl) : null,
                    authorizations: [new EndPointAuthorization
                    {
                        Type = "Http",
                        SchemeId = "HttpBearer",
                        Location = "Header",
                        Name = "Bearer",
                        Value = TtsMiniMaxApiKey,
                    }]);
            }
            catch { /* 创建失败，获取按钮会显示错误 */ }
        }

        var vm = new VoiceManagerViewModel(
            "音色管理 · MiniMax",
            TtsMiniMaxVoices.ToArray(),
            miniMaxClient: miniMaxClient);

        var dialog = new VoiceManagerWindow { DataContext = vm };
        await dialog.ShowDialog(owner);

        miniMaxClient?.Dispose();

        if (vm.Confirmed && vm.Result != null)
        {
            TtsMiniMaxVoices = new ObservableCollection<VoiceEntry>(vm.Result);
        }
    }

    [RelayCommand]
    private async Task OpenEditingEngineVoiceManager(object? window)
    {
        if (window is not Window owner) return;

        var vm = new VoiceManagerViewModel(
            $"音色管理 · {TtsEditingEngineName}",
            TtsEditingEngineVoices);

        // 如果有 URL，创建 HttpTtsEngine 用于获取音色
        HttpTtsEngine? httpEngine = null;
        if (!string.IsNullOrWhiteSpace(TtsEditingEngineUrl))
        {
            try
            {
                var provider = new CustomTtsProvider(
                    _editingEngineId ?? Guid.NewGuid(),
                    TtsEditingEngineName,
                    TtsEditingEngineUrl,
                    string.IsNullOrWhiteSpace(TtsEditingEngineKey) ? null : TtsEditingEngineKey,
                    string.IsNullOrWhiteSpace(TtsEditingEngineModel) ? null : TtsEditingEngineModel,
                    TtsEditingEngineVoices);
                httpEngine = new HttpTtsEngine(provider);
            }
            catch { /* 忽略创建失败，获取按钮会显示错误 */ }
        }

        var dialog = new VoiceManagerWindow { DataContext = vm };
        await dialog.ShowDialog(owner);

        if (vm.Confirmed && vm.Result != null)
        {
            TtsEditingEngineVoices = vm.Result;
            TtsEditingEngineVoiceCount = vm.Result.Length;
        }
    }

    partial void OnTtsEdgeTtsEnabledChanged(bool value) => UpdateTtsPoolDisplay();
    partial void OnTtsMiniMaxEnabledChanged(bool value) => UpdateTtsPoolDisplay();
    partial void OnTtsMiniMaxApiKeyChanged(string value) => UpdateTtsPoolDisplay();
    partial void OnTtsMiniMaxVoicesChanged(ObservableCollection<VoiceEntry> value) => UpdateTtsPoolDisplay();
    partial void OnTtsCustomEngineListChanged(ObservableCollection<CustomTtsProvider> value) => UpdateTtsPoolDisplay();
}

/// <summary>
/// 音色池分项显示数据。
/// </summary>
public record TtsPoolBreakdownItem(string EngineName, int Count);
