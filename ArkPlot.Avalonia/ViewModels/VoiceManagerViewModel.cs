using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArkPlot.Tts.Models;
using ArkPlot.Tts.Engines;
using MiniMax;

namespace ArkPlot.Avalonia.ViewModels;

/// <summary>
/// 音色管理弹窗 ViewModel（MiniMax 和自定义引擎共用）。
/// </summary>
public partial class VoiceManagerViewModel : ObservableObject
{
    private readonly HttpTtsEngine? _httpEngine;
    private readonly MiniMaxClient? _miniMaxClient;

    /// <summary>请求关闭窗口事件。</summary>
    public event Action? RequestClose;

    [ObservableProperty] private string _title = "音色管理";
    [ObservableProperty] private ObservableCollection<VoiceRowViewModel> _voices = new();
    [ObservableProperty] private bool _isFetching;
    [ObservableProperty] private string _fetchStatus = "";
    [ObservableProperty] private bool _canFetch;

    /// <summary>获取按钮是否可用（有连接且非获取中）。</summary>
    public bool CanFetchVoices => CanFetch && !IsFetching;

    /// <summary>性别下拉选项。</summary>
    public string[] GenderOptions => ["Female", "Male", "Unknown", "Special"];

    /// <summary>确认后的最终结果。</summary>
    public VoiceEntry[]? Result { get; private set; }

    /// <summary>用户是否点击了保存。</summary>
    public bool Confirmed { get; private set; }

    /// <summary>
    /// 创建音色管理 ViewModel。
    /// </summary>
    public VoiceManagerViewModel(string title, VoiceEntry[] currentVoices,
        HttpTtsEngine? httpEngine = null,
        MiniMaxClient? miniMaxClient = null)
    {
        Title = title;
        _httpEngine = httpEngine;
        _miniMaxClient = miniMaxClient;
        CanFetch = httpEngine != null || miniMaxClient != null;

        foreach (var v in currentVoices)
        {
            Voices.Add(new VoiceRowViewModel
            {
                VoiceId = v.VoiceId,
                DisplayName = v.DisplayName,
                Gender = v.Gender,
                Locale = v.Locale ?? "zh-CN",
                IsSelected = true
            });
        }
    }

    partial void OnIsFetchingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanFetchVoices));
    }

    [RelayCommand]
    private void AddRow()
    {
        Voices.Add(new VoiceRowViewModel
        {
            VoiceId = "",
            DisplayName = "",
            Gender = "Female",
            Locale = "zh-CN",
            IsSelected = true
        });
    }

    [RelayCommand]
    private void RemoveRow(VoiceRowViewModel? row)
    {
        if (row != null)
            Voices.Remove(row);
    }

    [RelayCommand]
    private async Task FetchVoices()
    {
        if (!CanFetch)
        {
            FetchStatus = "⚠ 无可用服务器连接（需要 API Key）";
            return;
        }

        IsFetching = true;
        FetchStatus = "正在获取...";

        try
        {
            VoiceEntry[] serverVoices;

            if (_miniMaxClient != null)
            {
                // MiniMax SDK 获取音色
                var response = await _miniMaxClient.Speech.GetVoicesAsync(
                    GetVoicesRequestVoiceType.System);

                if (response.SystemVoice == null)
                {
                    FetchStatus = "❌ MiniMax 返回空音色列表";
                    return;
                }

                serverVoices = response.SystemVoice
                    .Where(v => v.VoiceId != null)
                    .Select(v => new VoiceEntry(
                        v.VoiceId!,
                        v.VoiceName ?? v.VoiceId!,
                        "Unknown",  // SDK 不返回性别
                        null))
                    .ToArray();
            }
            else if (_httpEngine != null)
            {
                // HTTP API 获取音色
                var fetched = await _httpEngine.FetchVoicesAsync();
                serverVoices = fetched.ToArray();
            }
            else
            {
                FetchStatus = "⚠ 无可用服务器连接";
                return;
            }

            // Upsert: 服务端音色更新已有行，同时保留本地补充/未选中行。
            var existingRows = Voices.ToList();
            var existingMap = existingRows
                .Where(v => !string.IsNullOrWhiteSpace(v.VoiceId))
                .GroupBy(v => v.VoiceId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var matchedRows = new HashSet<VoiceRowViewModel>();

            var merged = new ObservableCollection<VoiceRowViewModel>();
            foreach (var sv in serverVoices)
            {
                if (existingMap.TryGetValue(sv.VoiceId, out var existing))
                {
                    existing.DisplayName = sv.DisplayName;
                    if (sv.Gender != "Unknown")
                        existing.Gender = sv.Gender;
                    if (sv.Locale != null)
                        existing.Locale = sv.Locale;
                    matchedRows.Add(existing);
                    merged.Add(existing);
                }
                else
                {
                    merged.Add(new VoiceRowViewModel
                    {
                        VoiceId = sv.VoiceId,
                        DisplayName = sv.DisplayName,
                        Gender = sv.Gender,
                        Locale = sv.Locale ?? "zh-CN",
                        IsSelected = false
                    });
                }
            }

            foreach (var localRow in existingRows)
            {
                if (!matchedRows.Contains(localRow))
                    merged.Add(localRow);
            }

            Voices = merged;
            FetchStatus = $"✅ 获取成功：{serverVoices.Length} 个音色";
        }
        catch (Exception ex)
        {
            FetchStatus = $"❌ 获取失败：{ex.Message}";
        }
        finally
        {
            IsFetching = false;
        }
    }

    [RelayCommand]
    private void Save()
    {
        var selectedRows = Voices
            .Where(v => v.IsSelected)
            .ToList();

        var emptyVoiceId = selectedRows.FirstOrDefault(v => string.IsNullOrWhiteSpace(v.VoiceId));
        if (emptyVoiceId != null)
        {
            FetchStatus = "❌ 保存失败：选中的音色必须填写 Voice ID";
            Confirmed = false;
            return;
        }

        var emptyDisplayName = selectedRows.FirstOrDefault(v => string.IsNullOrWhiteSpace(v.DisplayName));
        if (emptyDisplayName != null)
        {
            FetchStatus = "❌ 保存失败：选中的音色必须填写显示名";
            Confirmed = false;
            return;
        }

        var duplicateVoiceIds = selectedRows
            .GroupBy(v => v.VoiceId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        if (duplicateVoiceIds.Length > 0)
        {
            FetchStatus = $"❌ 保存失败：存在重复的 Voice ID：{string.Join(", ", duplicateVoiceIds)}";
            Confirmed = false;
            return;
        }

        Result = selectedRows
            .Select(v => new VoiceEntry(v.VoiceId.Trim(), v.DisplayName.Trim(), v.Gender, v.Locale?.Trim()))
            .ToArray();
        FetchStatus = "";
        Confirmed = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Confirmed = false;
        RequestClose?.Invoke();
    }
}

/// <summary>
/// 音色行 ViewModel（DataGrid 行数据）。
/// </summary>
public partial class VoiceRowViewModel : ObservableObject
{
    [ObservableProperty] private string _voiceId = "";
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _gender = "Female";
    [ObservableProperty] private string _locale = "zh-CN";
    [ObservableProperty] private bool _isSelected = true;
}
