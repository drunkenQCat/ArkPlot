using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Core.Infrastructure;
using ArkPlot.Tts;
using ArkPlot.Tts.Model;
using ArkPlot.Tts.Models;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

public class TtsVoiceConfigTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;
    private readonly AppSettings? _backupSettings;

    public TtsVoiceConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"arkplot_tts_voice_config_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        if (File.Exists(_settingsPath))
            _backupSettings = AppSettings.Load();

        var settings = new AppSettings(
            NovelizerSettings.CreateDefaults(),
            VisionSettings.CreateDefaults(),
            TtsSettings.CreateDefaults());
        settings.Save();

        DbFactory.ConfigureForTesting("Data Source=:memory:");
    }

    [Fact]
    public void RandomizeVoices_UsesGenderSpecificPools()
    {
        var vm = new TtsViewModel(_tempDir)
        {
            VoiceConfigs = new ObservableCollection<VoiceConfigItem>
            {
                new("(旁白)", "—", VoicePool.Narrator, [VoicePool.Narrator]),
                new("阿米娅", "女", VoicePool.Male[0], [.. VoicePool.Female, .. VoicePool.Male]),
                new("博士", "男", VoicePool.Female[0], [.. VoicePool.Female, .. VoicePool.Male])
            }
        };

        vm.RandomizeVoicesCommand.Execute(null);

        Assert.Equal(VoicePool.Narrator, vm.VoiceConfigs[0].SelectedVoice);
        Assert.True(VoicePool.IsFemaleVoice(vm.VoiceConfigs[1].SelectedVoice));
        Assert.True(VoicePool.IsMaleVoice(vm.VoiceConfigs[2].SelectedVoice));
    }

    [Fact]
    public async Task SaveVoiceConfigs_PersistsSelectionsAndKeepsSingleRowPerCharacter()
    {
        var narratorVoice = VoicePool.Female[0];
        var vm = new TtsViewModel(_tempDir)
        {
            VoiceConfigs = new ObservableCollection<VoiceConfigItem>
            {
                new("(旁白)", "—", narratorVoice, [VoicePool.Narrator, .. VoicePool.Female]),
                new("阿米娅", "女", VoicePool.Female[1], [.. VoicePool.Female, .. VoicePool.Male]),
                new("博士", "男", VoicePool.Male[1], [.. VoicePool.Female, .. VoicePool.Male])
            }
        };

        await vm.SaveVoiceConfigsCommand.ExecuteAsync(null);

        vm.VoiceConfigs[1].SelectedVoice = VoicePool.Female[2];
        await vm.SaveVoiceConfigsCommand.ExecuteAsync(null);

        var db = DbFactory.GetClient();
        var maps = db.Queryable<CharacterVoiceMap>()
            .OrderBy(m => m.CharacterName)
            .ToList();

        Assert.Equal(2, maps.Count);
        Assert.Equal(VoicePool.Female[2], maps.Single(m => m.CharacterName == "阿米娅").Voice);
        Assert.Equal(VoicePool.Male[1], maps.Single(m => m.CharacterName == "博士").Voice);

        var savedSettings = AppSettings.Load();
        Assert.Equal(narratorVoice, savedSettings.Tts?.DefaultNarratorVoice);
    }

    public void Dispose()
    {
        DbFactory.Reset();

        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
        }

        if (_backupSettings != null)
            _backupSettings.Save();
        else if (File.Exists(_settingsPath))
            File.Delete(_settingsPath);
    }
}
