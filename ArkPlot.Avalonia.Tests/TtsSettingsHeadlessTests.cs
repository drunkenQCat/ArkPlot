using System.Collections.ObjectModel;
using System.IO;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Tts;
using ArkPlot.Tts.Models;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

/// <summary>
/// TTS 配置无头测试（v2 — 双栏布局 UI）
/// </summary>
public class TtsSettingsHeadlessTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _originalSettingsPath;
    private AppSettings? _backupSettings;

    public TtsSettingsHeadlessTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"arkplot-tts-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _originalSettingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        if (File.Exists(_originalSettingsPath))
            _backupSettings = AppSettings.Load();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
        if (_backupSettings != null)
            _backupSettings.Save();
        else if (File.Exists(_originalSettingsPath))
            File.Delete(_originalSettingsPath);
    }

    private SettingsViewModel CreateVm() =>
        new(Path.Combine(_testDir, "tags.json"));

    [Fact]
    public void LoadTtsSettings_LoadsDefaults_WhenNoTtsConfig()
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        if (File.Exists(settingsPath)) File.Delete(settingsPath);

        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);

        Assert.True(vm.TtsEdgeTtsEnabled);
        Assert.False(vm.TtsMiniMaxEnabled);
        // API Key: settings.json 无配置时，从环境变量读取（可能为空也可能有值）
        var envKey = Environment.GetEnvironmentVariable("MINIMAX_API_KEY") ?? "";
        Assert.Equal(envKey, vm.TtsMiniMaxApiKey);
        Assert.Equal("https://api.minimax.io/", vm.TtsMiniMaxBaseUrl);
        Assert.Equal("speech-2.8-hd", vm.TtsMiniMaxModel);
        Assert.Empty(vm.TtsCustomEngineList);
        Assert.False(vm.TtsEditingEngineVisible);
    }

    [Fact]
    public void LoadTtsSettings_LoadsSavedValues()
    {
        var engineId = Guid.NewGuid();
        var settings = AppSettings.Load() with
        {
            Tts = new TtsSettings
            {
                EdgeTtsEnabled = true,
                MiniMaxEnabled = true,
                MiniMaxApiKey = "test-minimax-key",
                MiniMaxBaseUrl = "https://api.minimaxi.com/",
                MiniMaxModel = "speech-2.8-turbo",
                MiniMaxVoices =
                [
                    new VoiceEntry("mm-f01", "少女", "Female", "zh-CN"),
                    new VoiceEntry("mm-m01", "青年", "Male", "zh-CN")
                ],
                CustomEngines =
                [
                    new CustomTtsProvider(engineId, "本地TTS", "http://localhost:5144",
                        null, null, [new VoiceEntry("f01", "少女", "Female", "zh-CN")])
                ],
                DefaultNarratorVoice = "zh-CN-XiaoxiaoNeural"
            }
        };
        settings.Save();

        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);

        Assert.True(vm.TtsMiniMaxEnabled);
        Assert.Equal("test-minimax-key", vm.TtsMiniMaxApiKey);
        Assert.Equal("https://api.minimaxi.com/", vm.TtsMiniMaxBaseUrl);
        Assert.Equal("speech-2.8-turbo", vm.TtsMiniMaxModel);
        Assert.Equal(2, vm.TtsMiniMaxVoices.Count);
        Assert.Single(vm.TtsCustomEngineList);
        Assert.Equal("本地TTS", vm.TtsCustomEngineList[0].Name);
    }

    [Fact]
    public void SaveTtsSettings_PersistsToSettingsFile()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);

        vm.TtsEdgeTtsEnabled = true;
        vm.TtsMiniMaxEnabled = true;
        vm.TtsMiniMaxApiKey = "sk-test-key";
        vm.TtsMiniMaxBaseUrl = "https://api.minimax.io/";
        vm.TtsMiniMaxModel = "speech-2.8-hd";

        vm.SaveTtsSettingsCommand.Execute(null);

        var reloaded = AppSettings.Load();
        Assert.NotNull(reloaded.Tts);
        Assert.True(reloaded.Tts.EdgeTtsEnabled);
        Assert.True(reloaded.Tts.MiniMaxEnabled);
        Assert.Equal("sk-test-key", reloaded.Tts.MiniMaxApiKey);
        Assert.Equal("https://api.minimax.io/", reloaded.Tts.MiniMaxBaseUrl);
        Assert.Equal("speech-2.8-hd", reloaded.Tts.MiniMaxModel);
    }

    [Fact]
    public void SaveTtsSettings_ShowsFeedbackMessage()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);
        Assert.Equal("", vm.TtsSaveFeedbackText);

        vm.SaveTtsSettingsCommand.Execute(null);

        Assert.Equal("✅ 所有更改已保存", vm.TtsSaveFeedbackText);
    }

    [Fact]
    public void SaveTtsSettings_EmptyApiKeyBecomesNull()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);
        vm.TtsMiniMaxApiKey = "";

        vm.SaveTtsSettingsCommand.Execute(null);

        var reloaded = AppSettings.Load();
        Assert.Null(reloaded.Tts?.MiniMaxApiKey);
    }

    [Fact]
    public void SaveTtsSettings_EmptyBaseUrlBecomesNull()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);
        vm.TtsMiniMaxBaseUrl = "";

        vm.SaveTtsSettingsCommand.Execute(null);

        var reloaded = AppSettings.Load();
        Assert.Null(reloaded.Tts?.MiniMaxBaseUrl);
    }

    // --- Custom engine CRUD ---

    [Fact]
    public void AddTtsCustomEngine_OpensEditingPanel()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);

        vm.AddTtsCustomEngineCommand.Execute(null);

        Assert.True(vm.TtsEditingEngineVisible);
        Assert.Equal("添加引擎", vm.TtsEditingEngineTitle);
        Assert.Equal("", vm.TtsEditingEngineName);
    }

    [Fact]
    public void SaveEditingEngine_AddsNewEngine()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);
        Assert.Empty(vm.TtsCustomEngineList);

        // Open add panel
        vm.AddTtsCustomEngineCommand.Execute(null);

        // Fill in details
        vm.TtsEditingEngineName = "测试引擎";
        vm.TtsEditingEngineUrl = "http://test.example.com";
        vm.TtsEditingEngineKey = "test-key";
        vm.TtsEditingEngineVoices =
        [
            new VoiceEntry("v1", "音色1", "Female", "zh-CN"),
            new VoiceEntry("v2", "音色2", "Male", "zh-CN")
        ];

        // Save
        vm.SaveEditingEngineCommand.Execute(null);

        Assert.False(vm.TtsEditingEngineVisible);
        Assert.Single(vm.TtsCustomEngineList);
        Assert.Equal("测试引擎", vm.TtsCustomEngineList[0].Name);
        Assert.Equal("http://test.example.com", vm.TtsCustomEngineList[0].BaseUrl);
        Assert.Equal("test-key", vm.TtsCustomEngineList[0].ApiKey);
        Assert.Equal(2, vm.TtsCustomEngineList[0].Voices.Length);
    }

    [Fact]
    public void EditTtsCustomEngine_LoadsEngineDetails()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);

        var engine = new CustomTtsProvider(
            Guid.NewGuid(), "测试引擎", "http://test.com", "secret-key", "model-x",
            [new VoiceEntry("v1", "音色1", "Female", "zh-CN")]);
        vm.TtsCustomEngineList.Add(engine);

        vm.EditTtsCustomEngineCommand.Execute(engine);

        Assert.True(vm.TtsEditingEngineVisible);
        Assert.Equal("编辑：测试引擎", vm.TtsEditingEngineTitle);
        Assert.Equal("测试引擎", vm.TtsEditingEngineName);
        Assert.Equal("http://test.com", vm.TtsEditingEngineUrl);
        Assert.Equal("secret-key", vm.TtsEditingEngineKey);
        Assert.Equal("model-x", vm.TtsEditingEngineModel);
        Assert.Single(vm.TtsEditingEngineVoices);
    }

    [Fact]
    public void SaveEditingEngine_UpdatesExistingEngine()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);

        var originalId = Guid.NewGuid();
        var original = new CustomTtsProvider(originalId, "原始", "http://original.com", null, null, []);
        vm.TtsCustomEngineList.Add(original);

        // Open edit
        vm.EditTtsCustomEngineCommand.Execute(original);
        vm.TtsEditingEngineName = "更新名称";
        vm.TtsEditingEngineUrl = "http://updated.com";
        vm.TtsEditingEngineVoices = [new VoiceEntry("v1", "新音色", "Male", "zh-CN")];

        vm.SaveEditingEngineCommand.Execute(null);

        Assert.Single(vm.TtsCustomEngineList);
        Assert.Equal(originalId, vm.TtsCustomEngineList[0].Id);
        Assert.Equal("更新名称", vm.TtsCustomEngineList[0].Name);
        Assert.Equal("http://updated.com", vm.TtsCustomEngineList[0].BaseUrl);
        Assert.Single(vm.TtsCustomEngineList[0].Voices);
    }

    [Fact]
    public void DeleteTtsCustomEngine_RemovesFromList()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);

        var e1 = new CustomTtsProvider(Guid.NewGuid(), "引擎1", "http://a.com", null, null, []);
        var e2 = new CustomTtsProvider(Guid.NewGuid(), "引擎2", "http://b.com", null, null, []);
        vm.TtsCustomEngineList.Add(e1);
        vm.TtsCustomEngineList.Add(e2);

        vm.DeleteTtsCustomEngineCommand.Execute(e2);

        Assert.Single(vm.TtsCustomEngineList);
        Assert.Equal("引擎1", vm.TtsCustomEngineList[0].Name);
    }

    [Fact]
    public void CancelEditingEngine_HidesPanel()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);

        vm.AddTtsCustomEngineCommand.Execute(null);
        Assert.True(vm.TtsEditingEngineVisible);

        vm.CancelEditingEngineCommand.Execute(null);
        Assert.False(vm.TtsEditingEngineVisible);
    }

    // --- Voice pool display ---

    [Fact]
    public void TtsPoolBreakdown_ShowsEdgeTts()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);
        vm.TtsEdgeTtsEnabled = true;

        Assert.Contains(vm.TtsPoolBreakdown, b => b.EngineName == "EdgeTTS" && b.Count == 9);
        Assert.Equal("音色：4女 4男 1旁白", vm.TtsEdgeTtsSummary);
    }

    [Fact]
    public void TtsPoolBreakdown_HidesEdgeTtsWhenDisabled()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);
        vm.TtsEdgeTtsEnabled = false;

        Assert.DoesNotContain(vm.TtsPoolBreakdown, b => b.EngineName == "EdgeTTS");
        Assert.Equal("已禁用", vm.TtsEdgeTtsSummary);
    }

    [Fact]
    public void TtsPoolBreakdown_IncludesMiniMaxWhenConfigured()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);

        vm.TtsMiniMaxEnabled = true;
        vm.TtsMiniMaxApiKey = "sk-test";
        vm.TtsMiniMaxVoices = new ObservableCollection<VoiceEntry>
        {
            new("f1", "少女", "Female", "zh-CN"),
            new("m1", "青年", "Male", "zh-CN")
        };

        Assert.Contains(vm.TtsPoolBreakdown, b => b.EngineName == "MiniMax" && b.Count == 2);
    }

    [Fact]
    public void TtsPoolBreakdown_IncludesCustomEngines()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);

        vm.TtsCustomEngineList = new ObservableCollection<CustomTtsProvider>
        {
            new CustomTtsProvider(
                Guid.NewGuid(), "我的引擎", "http://test.com", null, null,
                [new VoiceEntry("v1", "音色1", "Female", null)])
        };

        Assert.Contains(vm.TtsPoolBreakdown, b => b.EngineName == "我的引擎" && b.Count == 1);
    }

    [Fact]
    public void TtsPoolGenderText_CalculatesCorrectly()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);
        vm.TtsEdgeTtsEnabled = true;

        // EdgeTTS: 4F + 4M + 1 narrator (F) = 5F, 4M
        Assert.Contains("女", vm.TtsPoolGenderText);
        Assert.Contains("男", vm.TtsPoolGenderText);
    }

    [Fact]
    public void TtsAllVoices_ContainsEdgeTtsVoices()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);
        vm.TtsEdgeTtsEnabled = true;

        Assert.True(vm.TtsAllVoices.Count >= 9);
        Assert.Contains(vm.TtsAllVoices, v => v.Engine == EngineType.EdgeTts);
    }

    [Fact]
    public void TtsDefaultNarratorVoice_IsSet()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);

        Assert.NotNull(vm.TtsDefaultNarratorVoice);
        Assert.Equal("zh-CN-XiaoxiaoNeural", vm.TtsDefaultNarratorVoice.VoiceId);
    }

    [Fact]
    public void MiniMaxBaseUrlOptions_ReturnsExpectedValues()
    {
        var vm = CreateVm();
        Assert.Contains("https://api.minimax.io/", vm.MiniMaxBaseUrlOptions);
        Assert.Contains("https://api.minimaxi.com/", vm.MiniMaxBaseUrlOptions);
    }

    [Fact]
    public void MiniMaxModelOptions_ReturnsExpectedValues()
    {
        var vm = CreateVm();
        Assert.Contains("speech-2.8-hd", vm.MiniMaxModelOptions);
        Assert.Contains("speech-2.8-turbo", vm.MiniMaxModelOptions);
    }

    [Fact]
    public void SaveTtsSettings_PersistsMiniMaxVoices()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);

        vm.TtsMiniMaxVoices = new ObservableCollection<VoiceEntry>
        {
            new("f1", "少女", "Female", "zh-CN"),
            new("m1", "青年", "Male", "zh-CN")
        };

        vm.SaveTtsSettingsCommand.Execute(null);

        var reloaded = AppSettings.Load();
        Assert.Equal(2, reloaded.Tts!.MiniMaxVoices.Length);
        Assert.Equal("f1", reloaded.Tts.MiniMaxVoices[0].VoiceId);
    }

    [Fact]
    public void SaveTtsSettings_PersistsDefaultNarratorVoice()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);

        // Select a different narrator
        var mmVoice = new VoiceAssignment(
            new VoiceEntry("mm-narrator", "旁白", "Female", "zh-CN"),
            EngineType.MiniMax, null);
        vm.TtsAllVoices.Add(mmVoice);
        vm.TtsDefaultNarratorVoice = mmVoice;

        vm.SaveTtsSettingsCommand.Execute(null);

        var reloaded = AppSettings.Load();
        Assert.Equal("mm-narrator", reloaded.Tts!.DefaultNarratorVoice);
    }

    [Fact]
    public void DeleteTtsCustomEngine_ClosesEditingPanelIfDeletingEdited()
    {
        var vm = CreateVm();
        vm.LoadSettingsCommand.Execute(null);

        var engine = new CustomTtsProvider(Guid.NewGuid(), "引擎X", "http://x.com", null, null, []);
        vm.TtsCustomEngineList.Add(engine);

        // Start editing
        vm.EditTtsCustomEngineCommand.Execute(engine);
        Assert.True(vm.TtsEditingEngineVisible);

        // Delete the engine being edited
        vm.DeleteTtsCustomEngineCommand.Execute(engine);

        Assert.Empty(vm.TtsCustomEngineList);
        Assert.False(vm.TtsEditingEngineVisible);
    }
}
