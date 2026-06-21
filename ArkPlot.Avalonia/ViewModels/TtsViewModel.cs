using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArkPlot.AudioNormalizer;
using ArkPlot.Avalonia.Models;
using ArkPlot.Core.Infrastructure;
using ArkPlot.Core.Interfaces;
using ArkPlot.Core.Model;
using ArkPlot.Tts;
using ArkPlot.Tts.Alignment;
using ArkPlot.Tts.Engines;
using ArkPlot.Tts.Models;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.Wave;
using SqlSugar;

namespace ArkPlot.Avalonia.ViewModels;

public partial class TtsViewModel : ViewModelBase, IDisposable
{
    // ── 小说文件 ──
    [ObservableProperty]
    private ObservableCollection<NovelFileItem> _novelFiles = [];

    [ObservableProperty]
    private string _ttsOutputDir = "";

    // ── 章节 ──
    [ObservableProperty]
    private ObservableCollection<ChapterItem> _chapters = [];

    [ObservableProperty]
    private ChapterItem? _selectedChapter;

    [ObservableProperty]
    private string _searchText = "";

    // ── 片段表格 ──
    [ObservableProperty]
    private ObservableCollection<SegmentRow> _filteredSegments = [];

    [ObservableProperty]
    private SegmentRow? _selectedSegment;

    /// <summary>DataGrid 多选时同步的选中行集合（由 View code-behind 更新）。</summary>
    public ObservableCollection<SegmentRow> SelectedSegmentRows { get; } = [];

    // ── 状态 ──
    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayPrevCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlayNextCommand))]
    private bool _isPlaying;

    [ObservableProperty]
    private string _playButtonText = "▶ 从选中行开始连播";
    private bool _isPaused; // 暂停状态标志

    // 暂停时 IsPlaying=false 但 _isPaused=true，需要区分状态
    partial void OnIsPlayingChanged(bool value)
    {
        if (value)
            PlayButtonText = "⏸ 暂停";
        else if (!_isPaused)
            PlayButtonText = "▶ 从选中行开始连播";
    }

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressText = "就绪";

    [ObservableProperty]
    private string _totalProgressText = "";

    // ── 子组件 ViewModel ──
    public PortraitPanelViewModel PortraitPanel { get; } = new();
    public GalleryPanelViewModel GalleryPanel { get; } = new();
    public VoiceConfigPanelViewModel VoiceConfigPanel { get; }

    // ── 事件监听：DataGrid 选中行变化 ──
    partial void OnSelectedSegmentChanged(SegmentRow? value)
    {
        if (value == null)
            return;
        Log($"[诊断] 选中行 #{value.Index}: {value.CharacterName}, EntryIndex={value.EntryIndex}");
        UpdateComponentsForSegment(value);
    }

    private void UpdateComponentsForSegment(SegmentRow seg)
    {
        var portraitUrl = GetPortraitUrl(seg);
        #region debug-point portrait-selection-input
        Log(
            $"[诊断] 立绘输入: EntryIndex={seg.EntryIndex}, Chapter={seg.ChapterTitle}, Character={seg.CharacterName}, Code={seg.CharacterCode ?? "(null)"}"
        );
        #endregion
        Log($"[诊断] 立绘: {portraitUrl ?? "(null)"}, 角色: {seg.CharacterName}");
        PortraitPanel.Update(portraitUrl, seg.CharacterName);
        UpdateGalleryForSegment(seg);
    }

    private void UpdatePortraitForVoiceConfig(VoiceConfigItem config)
    {
        if (string.IsNullOrEmpty(config.CharacterCode))
        {
            PortraitPanel.Clear();
            return;
        }

        var entry = _allEntries.FirstOrDefault(e =>
            e.CharacterCode == config.CharacterCode && e.Portraits != null && e.Portraits.Count > 0
        );

        if (entry?.Portraits != null && entry.Portraits.Count > 0)
        {
            var portraitUrl = SelectPortraitUrl(
                entry.Portraits,
                config.CharacterCode ?? entry.CharacterCode
            );
            PortraitPanel.Update(portraitUrl, config.CharacterName);
        }
        else
        {
            PortraitPanel.Clear();
        }
    }

    // ── 日志 ──
    [ObservableProperty]
    private string _logText = "";

    // ── 内部 ──
    private CancellationTokenSource? _generateCts;
    private CancellationTokenSource? _audioRefreshCts;
    private CancellationTokenSource? _playCts;
    private List<AlignmentEntry> _allEntries = [];
    private List<BackgroundItem> _backgrounds = [];
    private readonly VoiceManagerUnified _voiceManagerUnified;
    private readonly ITtsEngine _ttsEngine;
    private readonly string _outputBaseDir;
    private bool _isInitialSelection = true;
    private ILoudnessNormalizer? _normalizer;

    // ── ffmpeg 通知条 ──
    [ObservableProperty]
    private bool _showFfmpegNotice;

    [ObservableProperty]
    private string _ffmpegNoticeText = "";

    [ObservableProperty]
    private bool _isDownloadingFfmpeg;

    public TtsViewModel(string outputBaseDir)
    {
        _outputBaseDir = outputBaseDir;
        TtsOutputDir = Path.Combine(outputBaseDir, "tts");

        // 从 TtsSettings 构建统一音色池
        var ttsSettings = AppSettings.Load().Tts ?? TtsSettings.CreateDefaults();
        var pool = VoicePoolBuilder.Build(ttsSettings);
        _voiceManagerUnified = new VoiceManagerUnified(
            pool,
            ttsSettings.DefaultNarratorVoice,
            DbFactory.GetClient()
        );
        _ttsEngine = new RoutedTtsEngine(ttsSettings, _voiceManagerUnified);

        VoiceConfigPanel = new VoiceConfigPanelViewModel(
            _voiceManagerUnified,
            Log,
            RefreshAudioStatusAsync,
            UpdatePortraitForVoiceConfig
        );

        ScanNovelFiles();
    }

    /// <summary>
    /// P4: 由 TtsWindow.Opened 事件调用，确保在 UI 线程上初始化。
    /// 不再使用构造函数中的 fire-and-forget Task.Run。
    /// </summary>
    public async Task InitializeAsync()
    {
        await Task.Delay(100); // 让窗口先渲染完毕
        await CheckFfmpegAsync();
        await LoadAlignmentAsync();
    }

    /// <summary>
    /// 检测 ffmpeg 是否可用，不可用时显示通知条。
    /// </summary>
    private async Task CheckFfmpegAsync()
    {
        await Task.Run(() =>
        {
            var ffmpegPath = FfmpegResolver.FindFfmpeg();
            if (ffmpegPath != null)
            {
                _normalizer = new LoudnessNormalizer(ffmpegPath, onLog: Log);
                Log($"[AudioNormalizer] ffmpeg 已就绪: {Path.GetFileName(ffmpegPath)}");
            }
            else
            {
                ShowFfmpegNotice = true;
                FfmpegNoticeText = "响度均衡需要 ffmpeg，当前未安装";
                Log("[AudioNormalizer] ffmpeg 未找到，响度均衡已禁用。点击通知条下载。");
            }
        });
    }

    [RelayCommand]
    private async Task DownloadFfmpegAsync()
    {
        IsDownloadingFfmpeg = true;
        FfmpegNoticeText = "正在下载 ffmpeg...";

        try
        {
            var progress = new Progress<double>(p =>
            {
                FfmpegNoticeText = $"正在下载 ffmpeg... {p:P0}";
            });

            var normalizer = await LoudnessNormalizer.CreateAsync(progress, onLog: Log);
            _normalizer = normalizer;

            ShowFfmpegNotice = false;
            Log("[AudioNormalizer] ffmpeg 下载安装完成，响度均衡已启用");
        }
        catch (Exception ex)
        {
            FfmpegNoticeText = $"下载失败: {ex.Message}";
            Log($"[AudioNormalizer] ffmpeg 下载失败: {ex.Message}");
        }
        finally
        {
            IsDownloadingFfmpeg = false;
        }
    }

    [RelayCommand]
    private void DismissFfmpegNotice()
    {
        ShowFfmpegNotice = false;
    }

    // ════════════════════════════════════════════
    // 小说文件扫描
    // ════════════════════════════════════════════

    private void ScanNovelFiles()
    {
        if (!Directory.Exists(_outputBaseDir))
            return;

        var files = Directory.GetFiles(_outputBaseDir, "*_novel_*.md");
        var items = files.Select(f => new NovelFileItem(f)).ToList();

        // 默认只选第一个
        if (items.Count > 0)
            items[0].IsSelected = true;

        NovelFiles = new ObservableCollection<NovelFileItem>(items);

        // 单选：选中一个时取消其他；切换文件时自动触发 alignment
        foreach (var item in items)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(NovelFileItem.IsSelected) && item.IsSelected)
                {
                    foreach (var other in NovelFiles)
                    {
                        if (other != item)
                            other.IsSelected = false;
                    }

                    // 切换小说文件时自动重新对齐（跳过初始选中，因为 InitializeAsync 已处理）
                    if (!_isInitialSelection)
                        _ = LoadAlignmentAsync(); // async void fire-and-forget from UI thread
                }
            };
        }

        _isInitialSelection = false;
    }

    // ════════════════════════════════════════════
    // 对齐 + 加载
    // ════════════════════════════════════════════

    [RelayCommand]
    private async Task LoadAlignmentAsync()
    {
        var selectedFiles = NovelFiles.Where(f => f.IsSelected).ToList();
        if (selectedFiles.Count == 0)
            return;

        var totalSw = Stopwatch.StartNew();
        Log("正在对齐小说文件...");

        try
        {
            // P1/P4: 重计算（对齐 + 背景图加载）放到后台线程
            var ttsOutputDir = TtsOutputDir;
            var (newEntries, newBackgrounds, allChapters) = await Task.Run(async () =>
            {
                var entries = new List<AlignmentEntry>();
                var backgrounds = new List<BackgroundItem>();
                var chapters = new List<ChapterItem>();

                foreach (var file in selectedFiles)
                {
                    var sw = Stopwatch.StartNew();
                    var aligner = new NovelAligner();
                    var alignCacheDir = Path.Combine(ttsOutputDir, "_align_cache");
                    var (fileEntries, stats) = await aligner.AlignByFileNameAsync(
                        file.FilePath,
                        alignCacheDir
                    );
                    sw.Stop();
                    Log(
                        $"[perf] AlignByFileNameAsync({Path.GetFileName(file.FilePath)}): {sw.ElapsedMilliseconds}ms"
                    );

                    Log(
                        $"{Path.GetFileName(file.FilePath)}: "
                            + $"{stats.AlignedDialogs}/{stats.TotalDialogs} 对话已对齐"
                    );

                    entries.AddRange(fileEntries);

                    var chapterTitles = fileEntries
                        .Select(e => e.ChapterTitle)
                        .Where(t => !string.IsNullOrEmpty(t))
                        .Distinct()
                        .ToList();

                    foreach (var title in chapterTitles)
                    {
                        if (!chapters.Any(c => c.Title == title))
                            chapters.Add(new ChapterItem(title, chapters.Count));
                    }

                    sw.Restart();
                    var fileBackgrounds = await LoadBackgroundsAsync(fileEntries);
                    sw.Stop();
                    Log($"[perf] LoadBackgroundsAsync: {sw.ElapsedMilliseconds}ms");

                    backgrounds.AddRange(fileBackgrounds);
                }

                return (entries, backgrounds, chapters);
            });

            // 回到 UI 线程：只做最终的集合赋值
            _allEntries = newEntries;
            _backgrounds = newBackgrounds;

            Chapters = new ObservableCollection<ChapterItem>(allChapters);
            if (Chapters.Count > 0)
            {
                SelectedChapter = Chapters[0];
                await LoadSegmentsForChapterAsync();
            }
            VoiceConfigPanel.UpdateEntries(_allEntries);

            totalSw.Stop();
            Log(
                $"[perf] TOTAL LoadAlignmentAsync: {totalSw.ElapsedMilliseconds}ms | "
                    + $"{allChapters.Count} 章节, {_allEntries.Count} 片段"
            );
        }
        catch (Exception ex)
        {
            Log($"❌ 对齐失败: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                Log($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        }
    }

    /// <summary>
    /// 加载背景图数据。返回结果列表，不直接修改 _backgrounds。
    /// P2: 所有 DB 查询使用 Select 投影，只取需要的列，避免 SELECT *。
    /// </summary>
    private async Task<List<BackgroundItem>> LoadBackgroundsAsync(List<AlignmentEntry> entries)
    {
        var result = new List<BackgroundItem>();
        try
        {
            var db = DbFactory.GetClient();

            var chapterTitles = entries
                .Select(e => e.ChapterTitle)
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct()
                .ToList();

            // P2: 投影只取 Id 和 Title
            var plots =
                chapterTitles.Count > 0
                    ? await db.Queryable<Plot>()
                        .Where(p => chapterTitles.Contains(p.Title))
                        .Select(p => new { p.Id, p.Title })
                        .ToListAsync()
                    : [];

            var plotIdToTitle = plots.ToDictionary(p => p.Id, p => p.Title ?? "");
            var plotIds = plots.Select(p => p.Id).ToList();

            // P2: 投影只取 PlotId, Bg, Index, CharacterCode（避免加载 OriginalText/MdText/Dialog 等大 TEXT 列）
            var allPlotEntries =
                plotIds.Count > 0
                    ? await db.Queryable<FormattedTextEntry>()
                        .Where(e => plotIds.Contains(e.PlotId) && !string.IsNullOrEmpty(e.Bg))
                        .OrderBy(e => e.PlotId)
                        .OrderBy(e => e.Index)
                        .Select(e => new
                        {
                            e.PlotId,
                            e.Bg,
                            e.Index,
                            e.CharacterCode,
                        })
                        .ToListAsync()
                    : [];

            var bgAnchors = new List<BgAnchorDto>();
            foreach (var group in allPlotEntries.GroupBy(e => e.PlotId))
            {
                string? lastBg = null;
                foreach (var e in group.OrderBy(x => x.Index))
                {
                    if (string.IsNullOrEmpty(e.Bg))
                        continue;
                    if (e.Bg == lastBg)
                        continue;
                    bgAnchors.Add(new BgAnchorDto(e.PlotId, e.Bg, e.Index, e.CharacterCode));
                    lastBg = e.Bg;
                }
            }

            Log(
                $"[诊断] Gallery 背景加载: chapters={chapterTitles.Count}, plots={plotIds.Count}, entries(withBg)={allPlotEntries.Count}, anchors={bgAnchors.Count}"
            );

            // P2: 投影只取 DedupKey 和 PicDesc（避免加载 ImageUrl/Source/时间戳等）
            var picDescs = await db.Queryable<PicDescription>()
                .Select(p => new { p.DedupKey, p.PicDesc })
                .ToListAsync();
            var picDescMap = picDescs.ToDictionary(p => p.DedupKey ?? "", p => p.PicDesc ?? "");

            foreach (var anchor in bgAnchors.OrderBy(a => a.PlotId).ThenBy(a => a.Index))
            {
                var picDesc = "";
                if (
                    !string.IsNullOrEmpty(anchor.CharacterCode)
                    && picDescMap.TryGetValue(anchor.CharacterCode, out var desc)
                )
                    picDesc = desc;

                result.Add(
                    new BackgroundItem(
                        anchor.Bg,
                        picDesc,
                        anchor.Index,
                        [],
                        anchor.PlotId,
                        plotIdToTitle.GetValueOrDefault(anchor.PlotId, "")
                    )
                );
            }

            Log($"[诊断] Gallery 背景加载: result={result.Count}");
        }
        catch (Exception ex)
        {
            Log($"背景图加载失败: {ex.Message}");
        }
        return result;
    }

    /// <summary>背景锚点轻量 DTO，避免加载 FormattedTextEntry 全表列。</summary>
    private record BgAnchorDto(long PlotId, string Bg, int Index, string? CharacterCode);

    // ════════════════════════════════════════════
    // 章节 + 搜索
    // ════════════════════════════════════════════

    partial void OnSelectedChapterChanged(ChapterItem? value)
    {
        _ = LoadSegmentsForChapterAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = LoadSegmentsForChapterAsync();
    }

    /// <summary>
    /// P1: 将 SegmentRow 对象创建移到 Task.Run 后台线程，
    /// 只有最终的 ObservableCollection 赋值留在 UI 线程。
    /// </summary>
    private async Task LoadSegmentsForChapterAsync()
    {
        if (SelectedChapter == null)
            return;

        var chapterTitle = SelectedChapter.Title;
        var searchText = SearchText;
        var allEntries = _allEntries;

        // 后台线程：LINQ 过滤 + 创建 SegmentRow 对象
        var rows = await Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            var entries = allEntries.Where(e => e.ChapterTitle == chapterTitle);

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var search = searchText.Trim();
                entries = entries.Where(e =>
                    (e.NovelText?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (
                        e.CharacterName?.Contains(search, StringComparison.OrdinalIgnoreCase)
                        ?? false
                    )
                );
            }

            var result = entries
                .Select(
                    (e, i) =>
                        new SegmentRow
                        {
                            Index = i + 1,
                            CharacterName = e.IsDialog ? (e.CharacterName ?? "?") : "(旁白)",
                            SegmentType = e.IsDialog ? "对话" : "旁白",
                            NovelText = e.NovelText ?? "",
                            CharacterCode = e.CharacterCode,
                            Gender = e.Gender,
                            ChapterTitle = e.ChapterTitle,
                            EntryIndex = e.EntryIndex,
                            HasAudio = false,
                            AudioOpacity = 0.3,
                            AudioStatus = "— — — — —",
                        }
                )
                .ToList();

            sw.Stop();
            Log(
                $"[perf] LoadSegments: {result.Count} rows={sw.ElapsedMilliseconds}ms (background)"
            );
            return result;
        });

        // UI 线程：赋值 + 触发音频扫描 + 订阅事件
        FilteredSegments = new ObservableCollection<SegmentRow>(rows);

        // 订阅所有音频条的播放事件（互斥）
        // 注意：AudioPlayer 是懒加载的，这里会触发初始化
        foreach (var seg in rows)
        {
            SubscribeAudioPlayerEvents(seg);
        }

        _ = RefreshAudioStatusAsync();
    }

    [RelayCommand]
    private void PrevChapter()
    {
        if (SelectedChapter == null || Chapters.Count == 0)
            return;
        var idx = SelectedChapter.Index;
        if (idx > 0)
            SelectedChapter = Chapters[idx - 1];
    }

    [RelayCommand]
    private void NextChapter()
    {
        if (SelectedChapter == null || Chapters.Count == 0)
            return;
        var idx = SelectedChapter.Index;
        if (idx < Chapters.Count - 1)
            SelectedChapter = Chapters[idx + 1];
    }

    // ════════════════════════════════════════════
    // TTS 生成
    // ════════════════════════════════════════════

    [RelayCommand]
    private async Task StartGenerateAsync()
    {
        if (FilteredSegments.Count == 0)
        {
            Log("⚠️ 当前没有可生成的片段");
            return;
        }

        IsGenerating = true;
        _generateCts = new CancellationTokenSource();
        var ct = _generateCts.Token;

        try
        {
            Directory.CreateDirectory(TtsOutputDir);
            var cacheDir = Path.Combine(TtsOutputDir, "_tts_cache");

            var cache = new TtsCacheService(cacheDir);

            using var pipeline = new TtsPipeline(_ttsEngine, new VoiceManager(DbFactory.GetClient()), cache, normalizer: _normalizer);

            var segments = new List<TtsSegment>();
            var segmentIndices = new List<int>();

            foreach (var row in FilteredSegments)
            {
                var isDialog = row.SegmentType != "旁白";
                var voice = VoiceConfigPanel.ResolveVoiceSelection(row.CharacterName, row.Gender, isDialog);
                segments.Add(
                    new TtsSegment(
                        row.NovelText,
                        voice,
                        isDialog ? $"{row.CharacterName}({row.Gender ?? "?"})" : "旁白",
                        row.ChapterTitle
                    )
                );
                segmentIndices.Add(row.Index);
            }

            Log($"🎵 生成整章: {segments.Count} 个片段");
            ProgressValue = 0;

            var total = segments.Count;
            var completed = 0;

            var progress = new Progress<string>(msg => Log(msg));

            var fileProgress = new Progress<(int Index, string FilePath)>(tuple =>
            {
                completed++;
                ProgressValue = (double)completed / total * 100;
                ProgressText = $"进度: {completed}/{total}";

                if (tuple.Index >= 0 && tuple.Index < FilteredSegments.Count)
                {
                    var row = FilteredSegments[tuple.Index];
                    row.HasAudio = true;
                    row.AudioFilePath = tuple.FilePath;
                    row.AudioOpacity = 1.0;
                    row.AudioStatus = "▂▃▅▆▇▅▃";
                    // AudioFileReader 延迟到播放时读取时长
                    row.DurationText = "";
                }
            });

            var result = await pipeline.SynthesizeSegmentsAsync(
                segments,
                segmentIndices,
                TtsOutputDir,
                1000,
                ct,
                progress,
                fileProgress
            );

            Log($"✅ 完成: {result.Count} 个片段已生成");
            ProgressValue = 100;
            TotalProgressText = $"已生成 {result.Count} 个片段";
        }
        catch (OperationCanceledException)
        {
            Log("⚠️ 生成已取消");
        }
        catch (Exception ex)
        {
            Log($"❌ 生成失败: {ex.Message}");
        }
        finally
        {
            IsGenerating = false;
            _generateCts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _generateCts?.Cancel();
    }

    [RelayCommand]
    private async Task GenerateSelectedSegmentsAsync()
    {
        var rows = SelectedSegmentRows.ToList();
        if (rows.Count == 0 && SelectedSegment != null)
            rows.Add(SelectedSegment);
        if (rows.Count == 0)
        {
            Log("⚠️ 请先选择要生成的行");
            return;
        }

        var selectedFiles = NovelFiles.Where(f => f.IsSelected).Select(f => f.FilePath).ToList();
        if (selectedFiles.Count == 0)
        {
            Log("⚠️ 请选择至少一个小说文件");
            return;
        }

        if (_allEntries.Count == 0)
            await LoadAlignmentAsync();

        IsGenerating = true;
        _generateCts = new CancellationTokenSource();
        var ct = _generateCts.Token;

        try
        {
            Directory.CreateDirectory(TtsOutputDir);
            var cacheDir = Path.Combine(TtsOutputDir, "_tts_cache");

            var cache = new TtsCacheService(cacheDir);

            using var pipeline = new TtsPipeline(_ttsEngine, new VoiceManager(DbFactory.GetClient()), cache, normalizer: _normalizer);

            var segments = new List<TtsSegment>();
            var segmentIndices = new List<int>();
            var rowByIndex = new Dictionary<int, SegmentRow>();

            foreach (var row in rows)
            {
                var isDialog = row.SegmentType != "旁白";
                var voice = VoiceConfigPanel.ResolveVoiceSelection(row.CharacterName, row.Gender, isDialog);
                var seg = new TtsSegment(
                    row.NovelText,
                    voice,
                    isDialog ? $"{row.CharacterName}({row.Gender ?? "?"})" : "旁白",
                    row.ChapterTitle
                );
                segments.Add(seg);
                segmentIndices.Add(row.Index);
                rowByIndex[row.Index] = row;
            }

            Log($"🎤 生成选中行: {rows.Count} 个片段");

            var progress = new Progress<string>(msg => Log(msg));

            var fileProgress = new Progress<(int Index, string FilePath)>(tuple =>
            {
                if (
                    tuple.Index >= 0
                    && tuple.Index < segmentIndices.Count
                    && rowByIndex.TryGetValue(segmentIndices[tuple.Index], out var row)
                )
                {
                    row.HasAudio = true;
                    row.AudioFilePath = tuple.FilePath;
                    row.AudioOpacity = 1.0;
                    row.AudioStatus = "▂▃▅▆▇▅▃";
                    row.DurationText = "";
                }
            });

            var result = await pipeline.SynthesizeSegmentsAsync(
                segments,
                segmentIndices,
                TtsOutputDir,
                1000,
                ct,
                progress,
                fileProgress
            );

            Log($"✅ 完成: {result.Count}/{rows.Count} 个片段已生成");
        }
        catch (OperationCanceledException)
        {
            Log("⚠️ 生成已取消");
        }
        catch (Exception ex)
        {
            Log($"❌ 生成失败: {ex.Message}");
        }
        finally
        {
            IsGenerating = false;
            _generateCts = null;
        }
    }

    // ════════════════════════════════════════════
    // 播放
    // ════════════════════════════════════════════

    private SegmentRow? _activeSegment; // 当前活跃（播放中或暂停中）的段落
    private List<SegmentRow>? _playbackSegments; // 连播列表
    private int _playbackIndex = -1; // 当前在连播列表中的位置
    private int _playbackGeneration; // 代际计数器，用于解决上一句/下一句的竞态条件

    /// <summary>订阅音频条的播放事件，实现互斥。</summary>
    private void SubscribeAudioPlayerEvents(SegmentRow seg)
    {
        seg.AudioPlayer.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AudioPlayerViewModel.IsPlaying))
            {
                // 当这个音频条开始播放时，停止之前的活跃段落（O(1)）
                if (seg.AudioPlayer.IsPlaying)
                {
                    SetActiveSegment(seg);
                }
            }
        };
    }

    [RelayCommand]
    private void PlayFromSelected()
    {
        // 如果有暂停的段落 → 恢复它
        if (_activeSegment is { } active && active.IsPlaying && _isPaused)
        {
            active.AudioPlayer.TogglePlayCommand.Execute(null);
            _isPaused = false;
            PlayButtonText = "⏸ 暂停";
            return;
        }

        // 如果有播放中的段落 → 暂停它
        if (_activeSegment is { } playing && playing.IsPlaying && !_isPaused)
        {
            playing.AudioPlayer.TogglePlayCommand.Execute(null);
            _isPaused = true;
            PlayButtonText = "▶ 继续";
            return;
        }

        // 否则：开始新的连播
        if (SelectedSegment == null)
        {
            Log("⚠️ 请先选择一行");
            return;
        }

        var startIdx = SelectedSegment.Index;
        var segments = FilteredSegments.Where(s => s.Index >= startIdx && s.HasAudio).ToList();

        if (segments.Count == 0)
        {
            Log("⚠️ 从选中位置开始没有已生成的音频");
            return;
        }

        // 停止所有其他段落
        StopAllSegments();

        _ = PlayLoopAsync(segments);
    }

    private async Task PlayLoopAsync(List<SegmentRow> segments)
    {
        // 递增代际计数器，使旧的 finally 块失效
        var generation = ++_playbackGeneration;

        IsPlaying = true;
        _isPaused = false;
        PlayButtonText = "⏸ 暂停";
        _playCts = new CancellationTokenSource();
        var ct = _playCts.Token;

        // 保存连播列表和起始索引（用于上一句/下一句）
        _playbackSegments = segments;
        _playbackIndex = 0;

        try
        {
            for (int i = 0; i < segments.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var seg = segments[i];
                _playbackIndex = i;

                // 设置当前活跃段落（自动停止之前的）
                SetActiveSegment(seg);

                seg.IsPlaying = true;
                SelectedSegment = seg;

                await PlayAudioFile(seg.AudioFilePath, ct);

                seg.IsPlaying = false;
                _activeSegment = null;
            }
        }
        catch (OperationCanceledException)
        {
            // 停止
        }
        finally
        {
            // 只有当这是当前代际时才执行清理
            // 上一句/下一句会启动新循环（generation+1），旧循环的 finally 跳过清理
            if (generation == _playbackGeneration)
            {
                IsPlaying = false;
                _isPaused = false;
                _activeSegment = null;
                _playbackSegments = null;
                _playbackIndex = -1;
                foreach (var seg in FilteredSegments)
                    seg.IsPlaying = false;
                _playCts = null;
            }
        }
    }

    [RelayCommand]
    private void StopPlay()
    {
        // 递增代际使旧循环的 finally 失效，然后手动清理
        _playbackGeneration++;
        StopAllSegments();
        _playCts?.Cancel();
        IsPlaying = false;
        _playbackSegments = null;
        _playbackIndex = -1;
    }

    /// <summary>上一句（只在连播中可用）</summary>
    /// <remarks>故意使用 async void：RelayCommand 需要 void 签名以保持按钮可用，
    /// 内部通过 SwitchToSegmentAsync 实现异步播放链。</remarks>
#pragma warning disable MVVMTK0039
    [RelayCommand(CanExecute = nameof(IsPlaying))]
    private async void PlayPrev()
    {
        if (_playbackSegments == null || _playbackIndex <= 0)
            return;

        _playbackIndex--;
        await SwitchToSegmentAsync(_playbackIndex);
    }

    /// <summary>下一句（只在连播中可用）</summary>
    [RelayCommand(CanExecute = nameof(IsPlaying))]
    private async void PlayNext()
    {
        if (_playbackSegments == null || _playbackIndex >= _playbackSegments.Count - 1)
            return;

        _playbackIndex++;
        await SwitchToSegmentAsync(_playbackIndex);
    }
#pragma warning restore MVVMTK0039

    /// <summary>切换到指定段落播放（停止当前的，开始新的）</summary>
    private async Task SwitchToSegmentAsync(int index)
    {
        if (_playbackSegments == null || index < 0 || index >= _playbackSegments.Count)
            return;

        // 递增代际计数器，使旧 PlayLoopAsync 的 finally 失效
        _playbackGeneration++;

        // 取消当前播放（只取消 PlayAudioFile 的 WaitAsync）
        _playCts?.Cancel();
        _playCts?.Dispose();
        _playCts = new CancellationTokenSource();
        var ct = _playCts.Token;

        var seg = _playbackSegments[index];

        // 停止之前的段落
        SetActiveSegment(seg);
        seg.IsPlaying = true;
        SelectedSegment = seg;

        try
        {
            await PlayAudioFile(seg.AudioFilePath, ct);
        }
        catch (OperationCanceledException)
        {
            return; // 被取消了（用户又点了切换）
        }

        seg.IsPlaying = false;

        // 如果没被取消，自动播下一句
        if (
            !ct.IsCancellationRequested
            && _playbackSegments != null
            && index < _playbackSegments.Count - 1
        )
        {
            _playbackIndex = index + 1;
            await SwitchToSegmentAsync(_playbackIndex);
        }
        else if (!ct.IsCancellationRequested)
        {
            // 最后一句播完，停止
            IsPlaying = false;
            _playbackSegments = null;
            _playbackIndex = -1;
        }
    }

    /// <summary>停止所有段落并重置位置。</summary>
    private void StopAllSegments()
    {
        // 只需要停止当前活跃的段落（如果有的话）
        if (_activeSegment != null)
        {
            if (_activeSegment.HasAudio)
            {
                _activeSegment.AudioPlayer.StopCommand.Execute(null);
            }
            _activeSegment.IsPlaying = false;
        }
        _activeSegment = null;
        _isPaused = false;
    }

    /// <summary>设置当前活跃段落（自动停止之前的活跃段落）。</summary>
    private void SetActiveSegment(SegmentRow seg)
    {
        // 停止之前的活跃段落（O(1) 复杂度）
        if (_activeSegment != null && _activeSegment != seg)
        {
            if (_activeSegment.HasAudio)
            {
                _activeSegment.AudioPlayer.StopCommand.Execute(null);
            }
            _activeSegment.IsPlaying = false;
        }

        // 设置新的活跃段落
        _activeSegment = seg;
    }

    private async Task PlayAudioFile(string filePath, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        var seg = FilteredSegments.FirstOrDefault(s => s.AudioFilePath == filePath);
        if (seg == null)
            return;
        var player = seg.AudioPlayer;

        try
        {
            player.LoadFile(filePath);

            // #4: AudioFileReader 延迟读取时长（只在播放时才读）
            if (string.IsNullOrEmpty(seg.DurationText))
            {
                try
                {
                    using var reader = new AudioFileReader(filePath);
                    seg.DurationText = reader.TotalTime.TotalSeconds.ToString("F1") + "s";
                }
                catch
                {
                    seg.DurationText = "";
                }
            }

            player.TogglePlayCommand.Execute(null);

            // 等待播放完成（自然结束或手动停止），同时支持取消
            await player.WaitForCompletionAsync().WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            player.StopCommand.Execute(null);
            throw;
        }
        catch
        {
            player.StopCommand.Execute(null);
        }
    }

    private void UpdateGalleryForSegment(SegmentRow seg)
    {
        if (_backgrounds.Count == 0)
        {
            GalleryPanel.Clear();
            return;
        }

        #region debug-point gallery-bg-select-input
        Log(
            $"[诊断] Gallery 输入: Chapter={seg.ChapterTitle}, EntryIndex={seg.EntryIndex}, Character={seg.CharacterName}, SegmentType={seg.SegmentType}"
        );
        #endregion

        static bool IsBlackBg(string? url) =>
            string.Equals(
                url,
                "https://media.prts.wiki/8/8a/Avg_bg_bg_black.png",
                StringComparison.Ordinal
            );

        var effectiveEntryIndex = seg.EntryIndex;
        if (effectiveEntryIndex < 0)
        {
            var segIndex = FilteredSegments.IndexOf(seg);
            for (int i = segIndex - 1; i >= 0; i--)
            {
                if (
                    FilteredSegments[i].EntryIndex >= 0
                    && string.Equals(
                        FilteredSegments[i].ChapterTitle,
                        seg.ChapterTitle,
                        StringComparison.Ordinal
                    )
                )
                {
                    effectiveEntryIndex = FilteredSegments[i].EntryIndex;
                    break;
                }
            }
        }

        var chapterBgs = _backgrounds
            .Where(b => string.Equals(b.ChapterTitle, seg.ChapterTitle, StringComparison.Ordinal))
            .ToList();

        if (chapterBgs.Count == 0)
        {
            GalleryPanel.Clear();
            return;
        }

        // 找到当前片段最近的背景图
        var bg = chapterBgs
            .Where(b => b.EntryIndex <= effectiveEntryIndex)
            .OrderByDescending(b => b.EntryIndex)
            .FirstOrDefault(b => !IsBlackBg(b.ImageUrl));

        if (bg == null)
            bg = chapterBgs
                .Where(b => b.EntryIndex <= effectiveEntryIndex)
                .OrderByDescending(b => b.EntryIndex)
                .FirstOrDefault();

        if (bg == null)
            bg = chapterBgs.FirstOrDefault();
        if (bg == null)
        {
            GalleryPanel.Clear();
            return;
        }

        var bgIdx = chapterBgs.IndexOf(bg);

        // 更新 GalleryPanel
        string? prevBg = null;
        for (int i = bgIdx - 1; i >= 0; i--)
        {
            if (!IsBlackBg(chapterBgs[i].ImageUrl))
            {
                prevBg = chapterBgs[i].ImageUrl;
                break;
            }
        }

        string? nextBg = null;
        for (int i = bgIdx + 1; i < chapterBgs.Count; i++)
        {
            if (!IsBlackBg(chapterBgs[i].ImageUrl))
            {
                nextBg = chapterBgs[i].ImageUrl;
                break;
            }
        }

        var (upper1, upper2, lower1, lower2) = GetContextTexts(bg);

        #region debug-point gallery-bg-select-result
        Log(
            $"[诊断] Gallery 命中: BgChapter={bg.ChapterTitle}, BgEntryIndex={bg.EntryIndex}, EffectiveEntryIndex={effectiveEntryIndex}, Current={bg.ImageUrl}, Prev={prevBg ?? "(null)"}, Next={nextBg ?? "(null)"}"
        );
        #endregion

        GalleryPanel.Update(
            bg.ImageUrl,
            prevBg,
            nextBg,
            bg.PicDescription ?? "",
            upper1,
            upper2,
            lower1,
            lower2
        );
    }

    private (string, string, string, string) GetContextTexts(BackgroundItem bg)
    {
        // 从 FormattedTextEntry 获取上下文
        var nearbyEntries = _allEntries
            .Where(e =>
                e.EntryIndex >= 0
                && string.Equals(e.ChapterTitle, bg.ChapterTitle, StringComparison.Ordinal)
            )
            .OrderBy(e => e.EntryIndex)
            .ToList();

        var upper = nearbyEntries
            .Where(e => e.EntryIndex < bg.EntryIndex && e.IsDialog)
            .OrderByDescending(e => e.EntryIndex)
            .Take(2)
            .Reverse()
            .Select(e => e.NovelText ?? "")
            .ToList();

        var lower = nearbyEntries
            .Where(e => e.EntryIndex > bg.EntryIndex && e.IsDialog)
            .Take(2)
            .Select(e => e.NovelText ?? "")
            .ToList();

        return (
            upper.Count > 0 ? upper[^1] : "",
            upper.Count > 1 ? upper[0] : "",
            lower.Count > 0 ? lower[0] : "",
            lower.Count > 1 ? lower[1] : ""
        );
    }

    // ════════════════════════════════════════════
    // 导出
    // ════════════════════════════════════════════

    [RelayCommand]
    private async Task ExportCurrentChapter()
    {
        if (SelectedChapter == null)
            return;

        var pattern = sanitized(SelectedChapter.Title);
        var files = Directory.Exists(TtsOutputDir)
            ? Directory
                .GetFiles(TtsOutputDir, "*.mp3")
                .Where(f =>
                    Path.GetFileName(f).Contains(pattern, StringComparison.OrdinalIgnoreCase)
                )
                .ToArray()
            : [];

        if (files.Length == 0)
        {
            Log("⚠️ 当前章节尚无已生成的 MP3");
            return;
        }

        var exportDir = Path.Combine(
            _outputBaseDir,
            "tts_export",
            sanitized(SelectedChapter.Title)
        );
        Directory.CreateDirectory(exportDir);

        foreach (var f in files)
            File.Copy(f, Path.Combine(exportDir, Path.GetFileName(f)), overwrite: true);

        Log($"📥 导出 {files.Length} 个 MP3 → {exportDir}");
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ExportAllChapters()
    {
        var files = Directory.Exists(TtsOutputDir) ? Directory.GetFiles(TtsOutputDir, "*.mp3") : [];

        if (files.Length == 0)
        {
            Log("⚠️ 尚无已生成的 MP3");
            return;
        }

        var exportDir = Path.Combine(_outputBaseDir, "tts_export");
        Directory.CreateDirectory(exportDir);

        foreach (var f in files)
            File.Copy(f, Path.Combine(exportDir, Path.GetFileName(f)), overwrite: true);

        Log($"📥 导出 {files.Length} 个 MP3 → {exportDir}");
        await Task.CompletedTask;
    }

    // ════════════════════════════════════════════
    // 辅助
    // ════════════════════════════════════════════

    private void Log(string msg)
    {
        LogText += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
    }

    /// <summary>
    /// 异步扫描缓存目录，通过内容哈希匹配音频文件并标记可播放的片段。
    /// 支持取消（快速切换文件时取消旧刷新）和批量 UI 更新（一次 Dispatcher.Post）。
    /// </summary>
    private async Task RefreshAudioStatusAsync()
    {
        var cacheDir = Path.Combine(TtsOutputDir, "_tts_cache");
        if (!Directory.Exists(cacheDir))
            return;

        // 取消上一次刷新，防止快速切换时旧结果覆盖新数据
        _audioRefreshCts?.Cancel();
        _audioRefreshCts?.Dispose();
        _audioRefreshCts = new CancellationTokenSource();
        var ct = _audioRefreshCts.Token;

        try
        {
            var swMatch = Stopwatch.StartNew();
            var segments = FilteredSegments.ToList();

            // ── 后台：预加载缓存 + 哈希匹配 ──
            var matches = await Task.Run(
                () =>
                {
                    ct.ThrowIfCancellationRequested();
                    var sw = Stopwatch.StartNew();

                    var cacheFileNames = Directory
                        .EnumerateFiles(cacheDir, "*.mp3")
                        .Select(f => Path.GetFileName(f).ToLowerInvariant())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    sw.Stop();
                    Log(
                        $"[perf] RefreshAudioStatus preload: {sw.ElapsedMilliseconds}ms, cache={cacheFileNames.Count} files"
                    );

                    ct.ThrowIfCancellationRequested();

                    var result = new List<(SegmentRow Seg, string FilePath)>();

                    foreach (var seg in segments)
                    {
                        ct.ThrowIfCancellationRequested();

                        var isDialog = seg.SegmentType != "旁白";
                        var voice = VoiceConfigPanel.ResolveVoiceSelection(seg.CharacterName, seg.Gender, isDialog);
                        var sanitizedText = TextSanitizer.Sanitize(seg.NovelText);
                        if (string.IsNullOrWhiteSpace(sanitizedText))
                            continue;

                        var cacheKey = TtsCacheService.GetCacheKey(sanitizedText, voice);
                        var cacheFileName = $"{cacheKey}.mp3".ToLowerInvariant();
                        if (cacheFileNames.Contains(cacheFileName))
                            result.Add((seg, Path.Combine(cacheDir, cacheFileName)));
                    }

                    return result;
                },
                ct
            );

            // ── UI 线程：一次批量更新（P3: 使用 UpdateAudioState 单次通知） ──
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var (seg, filePath) in matches)
                    seg.UpdateAudioState(filePath);
            });

            swMatch.Stop();
            Log(
                $"[perf] RefreshAudioStatus: matched={matches.Count}, total={swMatch.ElapsedMilliseconds}ms"
            );
        }
        catch
        {
            throw;
        }
    }

    private static string sanitized(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(text.Where(c => !invalid.Contains(c)));
    }

    /// <summary>从 _allEntries 获取当前选中角色的立绘 URL。</summary>
    private string? GetPortraitUrl(SegmentRow seg)
    {
        if (seg.EntryIndex < 0)
            return null;

        var entry = FindPortraitEntry(seg);
        if (entry?.Portraits == null || entry.Portraits.Count == 0)
            return null;

        #region debug-point portrait-selection-candidates
        Log(
            $"[诊断] 立绘候选: EntryIndex={seg.EntryIndex}, Character={entry.CharacterName ?? "(null)"}, Code={entry.CharacterCode ?? "(null)"}, Portraits=[{string.Join(", ", entry.Portraits)}]"
        );
        #endregion

        var portraitUrl = SelectPortraitUrl(
            entry.Portraits,
            seg.CharacterCode ?? entry.CharacterCode
        );

        #region debug-point portrait-selection-result
        Log(
            $"[诊断] 立绘命中: EntryIndex={seg.EntryIndex}, Character={seg.CharacterName}, Code={seg.CharacterCode ?? "(null)"}, Selected={portraitUrl ?? "(null)"}"
        );
        #endregion

        return portraitUrl;
    }

    private AlignmentEntry? FindPortraitEntry(SegmentRow seg)
    {
        var exactMatch = _allEntries.FirstOrDefault(e =>
            e.EntryIndex == seg.EntryIndex
            && string.Equals(e.ChapterTitle, seg.ChapterTitle, StringComparison.Ordinal)
            && string.Equals(e.CharacterName, seg.CharacterName, StringComparison.Ordinal)
            && string.Equals(e.CharacterCode, seg.CharacterCode, StringComparison.OrdinalIgnoreCase)
        );
        if (exactMatch != null)
            return exactMatch;

        var chapterMatch = _allEntries.FirstOrDefault(e =>
            e.EntryIndex == seg.EntryIndex
            && string.Equals(e.ChapterTitle, seg.ChapterTitle, StringComparison.Ordinal)
            && string.Equals(e.CharacterName, seg.CharacterName, StringComparison.Ordinal)
        );
        if (chapterMatch != null)
            return chapterMatch;

        return _allEntries.FirstOrDefault(e => e.EntryIndex == seg.EntryIndex);
    }

    private static string? SelectPortraitUrl(IEnumerable<string>? portraits, string? characterCode)
    {
        if (portraits == null)
            return null;

        var candidates = portraits
            .Where(p => !string.IsNullOrEmpty(p) && !p.Contains("transparent.png"))
            .ToList();
        if (candidates.Count == 0)
            return null;

        var normalizedCode = NormalizeCharacterCode(characterCode);
        if (!string.IsNullOrEmpty(normalizedCode))
        {
            var matched = candidates.FirstOrDefault(p =>
                p.Contains(normalizedCode, StringComparison.OrdinalIgnoreCase)
            );
            if (!string.IsNullOrEmpty(matched))
                return matched;
        }

        return candidates[0];
    }

    private static string? NormalizeCharacterCode(string? characterCode)
    {
        if (string.IsNullOrWhiteSpace(characterCode))
            return null;

        var normalized = characterCode.Trim().ToLowerInvariant();
        var hashIndex = normalized.IndexOf('#');
        return hashIndex >= 0 ? normalized[..hashIndex] : normalized;
    }

    public void Dispose()
    {
        _generateCts?.Cancel();
        _generateCts?.Dispose();
        _audioRefreshCts?.Cancel();
        _audioRefreshCts?.Dispose();
        _playCts?.Cancel();
        _playCts?.Dispose();
        (_ttsEngine as IDisposable)?.Dispose();
        foreach (var seg in FilteredSegments)
            seg.Dispose();
        GC.SuppressFinalize(this);
    }
}
