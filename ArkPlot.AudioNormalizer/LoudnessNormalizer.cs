using System.Text;
using ArkPlot.Core.Interfaces;
using FFmpeg.NET;
using FFmpeg.NET.Events;

namespace ArkPlot.AudioNormalizer;

/// <summary>
/// 基于 ffmpeg loudnorm 滤镜的音频响度均衡器。
/// 使用「测量 LUFS → 计算差值 → volume 增益」三步法，
/// 比 loudnorm 的 linear=true 两遍法更精确，尤其对短音频（&lt;5秒）。
/// </summary>
public class LoudnessNormalizer : ILoudnessNormalizer
{
    private readonly Engine _engine;
    private readonly Action<string>? _onLog;

    /// <summary>目标整合响度（LUFS）。默认 -16，适合播客/有声书。</summary>
    public double TargetLufs { get; init; } = -16.0;

    /// <summary>True Peak 上限（dBTP）。默认 -1.5。</summary>
    public double TruePeak { get; init; } = -1.5;

    /// <summary>Loudness Range 目标。默认 11。</summary>
    public double LoudnessRange { get; init; } = 11.0;

    /// <summary>
    /// 创建响度均衡器。
    /// ffmpeg 解析优先级：显式路径 → 捆绑目录 → 用户缓存 → 系统 PATH。
    /// </summary>
    /// <param name="ffmpegPath">
    /// ffmpeg 可执行文件路径。
    /// 为 null 时按优先级自动查找。
    /// </param>
    /// <param name="onLog">可选日志回调，用于记录操作过程。</param>
    public LoudnessNormalizer(string? ffmpegPath = null, Action<string>? onLog = null)
    {
        _onLog = onLog;
        var path = ffmpegPath ?? FfmpegResolver.FindFfmpeg(onLog)
            ?? throw new FfmpegNotFoundException();

        if (!File.Exists(path))
            throw new FileNotFoundException("指定的 ffmpeg 可执行文件不存在", path);

        _onLog?.Invoke($"[AudioNormalizer] 使用 ffmpeg: {path}");
        _engine = new Engine(path);
    }

    /// <summary>
    /// 异步创建响度均衡器。
    /// 找不到 ffmpeg 时自动下载（方案 C：运行时下载）。
    /// </summary>
    /// <param name="progress">下载进度回调（0.0 ~ 1.0），仅在触发下载时有回调。</param>
    /// <param name="onLog">可选日志回调。</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>已初始化的 LoudnessNormalizer 实例</returns>
    public static async Task<LoudnessNormalizer> CreateAsync(
        IProgress<double>? progress = null,
        Action<string>? onLog = null,
        CancellationToken cancellationToken = default)
    {
        var path = FfmpegResolver.FindFfmpeg(onLog);
        if (path != null)
            return new LoudnessNormalizer(path, onLog);

        // 自动下载
        onLog?.Invoke("[AudioNormalizer] ffmpeg 未找到，开始自动下载...");
        var downloader = new FfmpegDownloader(onLog: onLog);
        path = await downloader.DownloadAndInstallAsync(progress, cancellationToken);
        return new LoudnessNormalizer(path, onLog);
    }

    /// <summary>
    /// 对音频文件做响度均衡，输出到指定路径。
    /// 策略：测量 LUFS → 计算增益 → volume 滤镜精确应用 → True Peak 限制。
    /// </summary>
    public async Task NormalizeAsync(
        string inputFile,
        string outputFile,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputFile))
            throw new FileNotFoundException("输入音频文件不存在", inputFile);

        var dir = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _onLog?.Invoke($"[AudioNormalizer] 开始处理: {Path.GetFileName(inputFile)}");

        // Step 1: 测量输入 LUFS
        var measurement = await MeasureAsync(inputFile, cancellationToken);
        _onLog?.Invoke($"[AudioNormalizer] 测量结果: InputI={measurement.InputI:F2} LUFS, TP={measurement.InputTp:F2} dBTP");

        // Step 2: 计算所需增益
        var gainDb = TargetLufs - measurement.InputI;
        _onLog?.Invoke($"[AudioNormalizer] 目标 {TargetLufs:F1} LUFS, 需增益 {gainDb:F2} dB");

        // Step 3: volume 精确增益 + alimiter 限制 true peak
        var filter = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"volume={gainDb:F2}dB,alimiter=limit=1:level=0:attack=5:release=50:level_in=1:level_out=1");

        var args = $"-y -i \"{inputFile}\" -af \"{filter}\" \"{outputFile}\"";
        await RunFfmpegAsync(args, cancellationToken);

        _onLog?.Invoke($"[AudioNormalizer] 完成: {Path.GetFileName(outputFile)}");
    }

    /// <summary>
    /// 测量音频的响度参数（LUFS/True Peak/LRA）。
    /// </summary>
    public async Task<LoudnessMeasurement> MeasureAsync(
        string inputFile,
        CancellationToken cancellationToken = default)
    {
        _onLog?.Invoke($"[AudioNormalizer] 测量 LUFS: {Path.GetFileName(inputFile)}");
        var sb = new StringBuilder();

        void OnData(object? sender, ConversionDataEventArgs e)
            => sb.AppendLine(e.Data);
        void OnError(object? sender, ConversionErrorEventArgs e)
        {
            sb.AppendLine(e.Exception?.Message);
            _onLog?.Invoke($"[AudioNormalizer] ffmpeg stderr: {e.Exception?.Message}");
        }

        _engine.Data += OnData;
        _engine.Error += OnError;
        try
        {
            var args = $"-i \"{inputFile}\" -af \"loudnorm=print_format=json\" -f null {NullDevice}";

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(TimeSpan.FromMinutes(30));

            await _engine.ExecuteAsync(args, linked.Token);
        }
        finally
        {
            _engine.Data -= OnData;
            _engine.Error -= OnError;
        }

        var measurement = LoudnessMeasurement.Parse(sb.ToString());
        _onLog?.Invoke($"[AudioNormalizer] 测量完成: {measurement.InputI:F2} LUFS");
        return measurement;
    }

    /// <summary>
    /// 用已知测量值做精确归一化（volume + alimiter 方式）。
    /// </summary>
    public async Task ApplyAsync(
        string inputFile,
        string outputFile,
        LoudnessMeasurement measurement,
        CancellationToken cancellationToken = default)
    {
        var gainDb = TargetLufs - measurement.InputI;
        _onLog?.Invoke($"[AudioNormalizer] 应用增益 {gainDb:F2} dB: {Path.GetFileName(inputFile)}");

        var filter = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"volume={gainDb:F2}dB,alimiter=limit=1:level=0:attack=5:release=50:level_in=1:level_out=1");

        var args = $"-y -i \"{inputFile}\" -af \"{filter}\" \"{outputFile}\"";
        await RunFfmpegAsync(args, cancellationToken);
    }

    /// <summary>
    /// 将 WAV 编码为目标格式。
    /// </summary>
    internal async Task EncodeAsync(
        string inputWav,
        string outputFile,
        CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(outputFile).ToLowerInvariant();
        var codecArgs = ext switch
        {
            ".mp3" => "-c:a libmp3lame -b:a 192k",
            ".ogg" => "-c:a libvorbis -q:a 6",
            ".m4a" or ".aac" => "-c:a aac -b:a 192k",
            _ => "-c:a copy"
        };

        var args = $"-y -i \"{inputWav}\" {codecArgs} \"{outputFile}\"";
        await RunFfmpegAsync(args, cancellationToken);
    }

    private async Task RunFfmpegAsync(string args, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromMinutes(60));
        await _engine.ExecuteAsync(args, linked.Token);
    }

    internal static bool IsLosslessAudioFormat(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".wav" or ".flac" or ".aiff" or ".aif" or ".pcm";
    }

    private static string NullDevice =>
        OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
}

/// <summary>
/// 找不到 ffmpeg 时抛出的异常。
/// 提示用户使用 <see cref="LoudnessNormalizer.CreateAsync"/> 自动下载。
/// </summary>
public class FfmpegNotFoundException : FileNotFoundException
{
    public FfmpegNotFoundException()
        : base("找不到 ffmpeg。请安装 ffmpeg 并加入 PATH，或使用 LoudnessNormalizer.CreateAsync() 自动下载。")
    {
    }
}
