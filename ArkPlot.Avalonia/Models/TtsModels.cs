using System;
using System.IO;
using ArkPlot.Avalonia.ViewModels;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkPlot.Avalonia.Models;

/// <summary>小说文件选择项。</summary>
public partial class NovelFileItem : ObservableObject
{
    public string FilePath { get; }
    public string FileName { get; }

    [ObservableProperty]
    private bool _isSelected;

    public NovelFileItem(string filePath)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
    }
}

/// <summary>音色配置项（per-character）。</summary>
public partial class VoiceConfigItem : ObservableObject
{
    public string CharacterName { get; }
    public string Gender { get; }

    public IBrush GenderColor =>
        Gender switch
        {
            "男" => Brushes.DeepSkyBlue,
            "女" => Brushes.HotPink,
            _ => Brushes.DarkGoldenrod,
        };
    public string? CharacterCode { get; }

    /// <summary>初登场章节标题。</summary>
    public string FirstAppearanceChapter { get; }

    /// <summary>初登场在章节内的片段序号（1-based）。</summary>
    public int FirstAppearanceSegmentIndex { get; }

    [ObservableProperty]
    private string _selectedVoice;

    public List<string> AvailableVoices { get; }

    public VoiceConfigItem(
        string characterName,
        string gender,
        string selectedVoice,
        List<string> availableVoices,
        string? characterCode = null,
        string firstAppearanceChapter = "",
        int firstAppearanceSegmentIndex = 0
    )
    {
        CharacterName = characterName;
        Gender = gender;
        CharacterCode = characterCode;
        _selectedVoice = selectedVoice;
        AvailableVoices = availableVoices;
        FirstAppearanceChapter = firstAppearanceChapter;
        FirstAppearanceSegmentIndex = firstAppearanceSegmentIndex;
    }
}

/// <summary>章节项。</summary>
public partial class ChapterItem : ObservableObject
{
    public string Title { get; }
    public int Index { get; }
    public string DisplayText => $"{Index + 1}. {Title}";

    public ChapterItem(string title, int index)
    {
        Title = title;
        Index = index;
    }
}

/// <summary>片段行（表格中的一行）。</summary>
public partial class SegmentRow : ObservableObject, IDisposable
{
    public int Index { get; set; }
    public string CharacterName { get; set; } = "";
    public string SegmentType { get; set; } = "";
    public string NovelText { get; set; } = "";
    public string? CharacterCode { get; set; }
    public string? Gender { get; set; }
    public string ChapterTitle { get; set; } = "";

    /// <summary>对应的 FormattedTextEntry.Index，用于 Gallery 联动。</summary>
    public int EntryIndex { get; set; } = -1;

    /// <summary>是否为场景占位行（背景图切换处）。</summary>
    public bool IsScenePlaceholder { get; set; }

    /// <summary>场景描述（PicDesc）。</summary>
    public string SceneDescription { get; set; } = "";

    /// <summary>场景背景图 URL。</summary>
    public string SceneBackground { get; set; } = "";

    /// <summary>行级音频播放器（懒初始化，有音频时才创建）。</summary>
    private AudioPlayerViewModel? _audioPlayer;
    public AudioPlayerViewModel AudioPlayer =>
        _audioPlayer ??= new AudioPlayerViewModel(filePathProvider: () => AudioFilePath);

    // P0: 已移除 OnAudioFilePathChanged — 不再在 setter 中触发 NAudio I/O
    // LoadFile 延迟到用户点击播放时由 TtsViewModel.PlayAudioFile 调用

    [ObservableProperty]
    private bool _hasAudio;

    [ObservableProperty]
    private string _audioFilePath = "";

    [ObservableProperty]
    private string _durationText = "";

    [ObservableProperty]
    private double _audioOpacity = 0.3;

    [ObservableProperty]
    private string _audioStatus = "— — — — —";

    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>
    /// P3: 批量更新音频状态，合并为一次 PropertyChanged 通知。
    /// 避免 500 行 × 5 属性 = 2500 个 PropertyChanged 事件风暴。
    /// 故意直接操作字段（绕过 [ObservableProperty] setter）以跳过逐个通知。
    /// </summary>
#pragma warning disable MVVMTK0034
    public void UpdateAudioState(string filePath)
    {
        _audioFilePath = filePath;
        _audioStatus = "▂▃▅▆▇▅▃";
        _audioOpacity = 1.0;
        _durationText = "";
        _hasAudio = true;
        OnPropertyChanged(string.Empty);
    }
#pragma warning restore MVVMTK0034

    [RelayCommand]
    public void PlaySingle()
    {
        // 单段播放由 ViewModel 处理
    }

    public void Dispose()
    {
        _audioPlayer?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>背景图项（Gallery 用）。</summary>
public record BackgroundItem(
    string ImageUrl,
    string? PicDescription,
    int EntryIndex,
    List<string> ContextDialogs,
    long PlotId,
    string ChapterTitle
);
