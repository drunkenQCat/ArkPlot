using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ArkPlot.Avalonia.Models;
using ArkPlot.Tts;
using ArkPlot.Tts.Alignment;
using ArkPlot.Tts.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkPlot.Avalonia.ViewModels;

/// <summary>
/// 音色配置面板的独立 ViewModel。
/// 负责：按角色构建配置列表、随机分配、保存到 DB、对外提供 ResolveVoiceSelection 查询。
/// 不持有 TtsPipeline 或立绘状态，通过回调通知外部。
/// </summary>
public partial class VoiceConfigPanelViewModel : ObservableObject
{
    // ── 可绑定属性 ──
    [ObservableProperty]
    private ObservableCollection<VoiceConfigItem> _voiceConfigs = [];

    [ObservableProperty]
    private ObservableCollection<VoiceConfigItem> _filteredVoiceConfigs = [];

    [ObservableProperty]
    private string _voiceConfigSearchText = "";

    [ObservableProperty]
    private VoiceConfigItem? _selectedVoiceConfig;

    // ── 搜索过滤 ──
    partial void OnVoiceConfigSearchTextChanged(string value) => ApplyFilter();

    partial void OnVoiceConfigsChanged(ObservableCollection<VoiceConfigItem> value) => ApplyFilter();

    private void ApplyFilter()
    {
        if (VoiceConfigs == null)
        {
            FilteredVoiceConfigs = [];
            return;
        }

        if (string.IsNullOrWhiteSpace(VoiceConfigSearchText))
        {
            FilteredVoiceConfigs = new ObservableCollection<VoiceConfigItem>(VoiceConfigs);
            return;
        }

        var search = VoiceConfigSearchText.Trim();
        FilteredVoiceConfigs = new ObservableCollection<VoiceConfigItem>(
            VoiceConfigs.Where(c =>
                c.CharacterName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (c.Gender?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (c.SelectedVoice?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            )
        );
    }

    // ── 依赖 ──
    private readonly VoiceManagerUnified _voiceManagerUnified;
    private readonly Action<string> _log;
    private readonly Func<Task> _refreshAudioStatus;
    private readonly Action<VoiceConfigItem> _onSelectedVoiceConfigChanged;

    public VoiceConfigPanelViewModel(
        VoiceManagerUnified voiceManagerUnified,
        Action<string> log,
        Func<Task> refreshAudioStatus,
        Action<VoiceConfigItem> onSelectedVoiceConfigChanged
    )
    {
        _voiceManagerUnified = voiceManagerUnified ?? throw new ArgumentNullException(nameof(voiceManagerUnified));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _refreshAudioStatus = refreshAudioStatus ?? throw new ArgumentNullException(nameof(refreshAudioStatus));
        _onSelectedVoiceConfigChanged = onSelectedVoiceConfigChanged ?? throw new ArgumentNullException(nameof(onSelectedVoiceConfigChanged));
    }

    // ── 选中变化回调 ──
    partial void OnSelectedVoiceConfigChanged(VoiceConfigItem? value)
    {
        if (value != null)
            _onSelectedVoiceConfigChanged(value);
    }

    /// <summary>
    /// 外部在加载/切换小说后调用：传入当前活动的全部对齐条目，重建配置列表。
    /// </summary>
    public void UpdateEntries(IEnumerable<AlignmentEntry> entries)
    {
        BuildVoiceConfigs(entries.ToList());
    }

    /// <summary>
    /// 由 TtsPipeline 调用：根据角色/性别/是否对话，从当前 VoiceConfigs 解析最终音色。
    /// </summary>
    public string ResolveVoiceSelection(string? characterName, string? gender, bool isDialog)
    {
        if (!isDialog || string.IsNullOrWhiteSpace(characterName) || characterName == "(旁白)")
        {
            return VoiceConfigs.FirstOrDefault(c => c.CharacterName == "(旁白)")?.SelectedVoice
                ?? _voiceManagerUnified.GetNarratorAssignment().VoiceId;
        }

        var normalizedCharacterName = NormalizeCharacterName(characterName);
        var configured = VoiceConfigs
            .FirstOrDefault(c =>
                string.Equals(
                    NormalizeCharacterName(c.CharacterName),
                    normalizedCharacterName,
                    StringComparison.Ordinal
                )
            )
            ?.SelectedVoice;
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return _voiceManagerUnified.GetVoiceForCharacter(normalizedCharacterName, gender).VoiceId;
    }

    // ── 命令 ──

    [RelayCommand]
    private void RandomizeVoices()
    {
        if (VoiceConfigs == null || VoiceConfigs.Count == 0)
            return;

        var rng = new Random();
        var randomized = 0;
        foreach (var config in VoiceConfigs)
        {
            if (config.CharacterName == "(旁白)")
                continue;

            var candidateVoices = GetRandomizableVoicePool(config.Gender);
            if (candidateVoices.Count == 0)
                continue;

            config.SelectedVoice = candidateVoices[rng.Next(candidateVoices.Count)];
            randomized++;
        }

        _log($"🎲 已按性别重新随机分配 {randomized} 个角色的音色");
        _log("💡 当前分配会立刻用于生成；如需下次继续沿用，请点击“保存音色配置”。");
    }

    [RelayCommand]
    private async Task SaveVoiceConfigsAsync()
    {
        if (VoiceConfigs == null || VoiceConfigs.Count == 0)
        {
            _log("⚠️ 当前没有可保存的音色配置");
            return;
        }

        var savedCount = 0;
        foreach (var config in VoiceConfigs)
        {
            if (string.IsNullOrWhiteSpace(config.SelectedVoice))
                continue;

            if (config.CharacterName == "(旁白)")
            {
                PersistNarratorVoice(config.SelectedVoice);
                continue;
            }

            _voiceManagerUnified.SetVoiceForCharacter(
                config.CharacterName,
                config.Gender,
                config.SelectedVoice
            );
            savedCount++;
        }

        await _refreshAudioStatus();
        _log($"💾 已保存 {savedCount} 个角色的音色配置");
    }

    // ── 内部构建 ──

    private void BuildVoiceConfigs(List<AlignmentEntry> entries)
    {
        var configs = new List<VoiceConfigItem>();

        var allVoiceIds = _voiceManagerUnified
            .Pool.Select(v => v.Entry.VoiceId)
            .Distinct()
            .ToList();

        var narratorVoice = _voiceManagerUnified.GetNarratorAssignment().VoiceId;
        configs.Add(new VoiceConfigItem("(旁白)", "—", narratorVoice, allVoiceIds, null));

        var characters = entries
            .Where(e => e.IsDialog && !string.IsNullOrEmpty(e.CharacterName))
            .GroupBy(e => NormalizeCharacterName(e.CharacterName)!)
            .Select(g => new
            {
                Name = g.Key,
                Gender = g.Select(e => e.Gender).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "?",
                Code = g.Select(e => e.CharacterCode).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
            })
            .OrderByDescending(c =>
                entries.Count(e => NormalizeCharacterName(e.CharacterName) == c.Name)
            )
            .ToList();

        foreach (var ch in characters)
        {
            var voice = _voiceManagerUnified.GetVoiceForCharacter(ch.Name, ch.Gender).VoiceId;
            configs.Add(new VoiceConfigItem(ch.Name, ch.Gender, voice, allVoiceIds, ch.Code));
        }

        VoiceConfigs = new ObservableCollection<VoiceConfigItem>(configs);
    }

    private List<string> GetRandomizableVoicePool(string? gender)
    {
        var narratorVoice = _voiceManagerUnified.GetNarratorAssignment().VoiceId;
        var basePool = _voiceManagerUnified
            .Pool.Where(v => !string.Equals(v.Entry.VoiceId, narratorVoice, StringComparison.Ordinal))
            .Select(v => v.Entry)
            .ToList();

        var genderPool = basePool
            .Where(v => IsGenderMatch(v.Gender, gender))
            .Select(v => v.VoiceId)
            .Distinct()
            .ToList();

        if (genderPool.Count > 0)
            return genderPool;

        return basePool.Select(v => v.VoiceId).Distinct().ToList();
    }

    private static bool IsGenderMatch(string? voiceGender, string? targetGender)
    {
        if (IsFemaleGender(targetGender)) return IsFemaleGender(voiceGender);
        if (IsMaleGender(targetGender)) return IsMaleGender(voiceGender);
        return true;
    }

    private static bool IsFemaleGender(string? gender)
    {
        if (string.IsNullOrWhiteSpace(gender)) return false;
        var normalized = gender.Trim().ToLowerInvariant();
        return normalized.Contains('女') || normalized is "female" or "f";
    }

    private static bool IsMaleGender(string? gender)
    {
        if (string.IsNullOrWhiteSpace(gender)) return false;
        var normalized = gender.Trim().ToLowerInvariant();
        return normalized.Contains('男') || normalized is "male" or "m";
    }

    private static void PersistNarratorVoice(string narratorVoice)
    {
        if (string.IsNullOrWhiteSpace(narratorVoice))
            return;

        var settings = AppSettings.Load();
        var tts = (settings.Tts ?? TtsSettings.CreateDefaults()) with
        {
            DefaultNarratorVoice = narratorVoice,
        };
        (settings with { Tts = tts }).Save();
    }

    private static string NormalizeCharacterName(string? characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName))
            return string.Empty;

        return new string(characterName.Where(c => !char.IsWhiteSpace(c)).ToArray());
    }
}
