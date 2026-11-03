using System;
using System.Collections.Generic;
using System.Linq;
using ArkPlot.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkPlot.Avalonia.ViewModels.Test;

public partial class PortraitTestViewModel : ViewModelBase
{
    private readonly ITestCacheSource _cache;
    private List<CachedCharacter> _characters = [];
    private string? _previousKey;
    public PortraitPanelViewModel Panel { get; } = new();
    [ObservableProperty] private string _statusText = "";

    public PortraitTestViewModel() : this(new TestCacheSource()) { }
    public PortraitTestViewModel(ITestCacheSource cache)
    {
        _cache = cache;
        RefreshCache();
    }

    [RelayCommand]
    private void RefreshCache()
    {
        try
        {
            _characters = _cache.GetCharacters();
            if (_characters.Count == 0)
            {
                Panel.Clear();
                StatusText = "没有可用的缓存角色立绘，请先加载并解析剧情。";
                return;
            }
            LoadSample();
        }
        catch (Exception ex) { StatusText = $"读取角色缓存失败：{ex.Message}"; }
    }

    [RelayCommand]
    private void LoadSample()
    {
        if (_characters.Count == 0) { StatusText = "没有可用的缓存角色，请先刷新缓存。"; return; }
        var candidates = _characters.Where(c => c.Key != _previousKey).ToList();
        if (candidates.Count == 0) candidates = _characters;
        var character = candidates[Random.Shared.Next(candidates.Count)];
        _previousKey = character.Key;
        Panel.Update(character.PortraitUrl, character.Name);
        StatusText = $"已缓存 {_characters.Count} 个角色；当前来源：{character.Source}";
    }

    [RelayCommand]
    private void Clear() => Panel.Clear();
}
