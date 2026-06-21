# ArkPlot.AudioNormalizer

音频响度均衡模块，基于 ffmpeg `loudnorm` + `volume` + `alimiter` 滤镜，将输出音频归一化到目标 LUFS（默认 -16）。

## 工作原理

```
输入音频 → loudnorm 测量 LUFS → 计算增益差值 → volume 精确增益 → alimiter 限峰 → 输出
```

**为什么不用 loudnorm 两遍法？**
loudnorm 的 `linear=true` 模式对短音频（<5 秒）严重不准（最大偏差 7.5 LUFS）。
本项目改用「测量 + volume 增益」方案，98 个真实 TTS 文件实测 **全部在 ±2 LUFS 内**。

## 依赖

- .NET 9.0+
- [xFFmpeg.NET](https://www.nuget.org/packages/xFFmpeg.NET) 7.4.0
- **ffmpeg 运行时**（不捆绑，按需下载或从 PATH 查找）

## 快速开始

### 最简用法

```csharp
var normalizer = new LoudnessNormalizer();
await normalizer.NormalizeAsync("input.mp3", "output.mp3");
```

### 带日志记录

```csharp
void Log(string msg) => logger.LogInformation(msg);

var normalizer = new LoudnessNormalizer(onLog: Log);
await normalizer.NormalizeAsync("input.mp3", "output.mp3");
// 输出:
// [FfmpegResolver] 在 PATH 中找到: C:\tools\ffmpeg.exe
// [AudioNormalizer] 使用 ffmpeg: C:\tools\ffmpeg.exe
// [AudioNormalizer] 开始处理: input.mp3
// [AudioNormalizer] 测量 LUFS: input.mp3
// [AudioNormalizer] 测量完成: -23.50 LUFS
// [AudioNormalizer] 目标 -16.0 LUFS, 需增益 7.50 dB
// [AudioNormalizer] 完成: output.mp3
```

### 自动下载 ffmpeg（方案 C）

找不到 ffmpeg 时自动从官方源下载到 `~/.arkplot/ffmpeg/`，后续复用：

```csharp
var normalizer = await LoudnessNormalizer.CreateAsync(
    progress: new Progress<double>(p => Console.WriteLine($"下载 ffmpeg: {p:P0}")),
    onLog: msg => logger.LogDebug(msg));

await normalizer.NormalizeAsync("input.mp3", "output.mp3");
```

### 显式指定 ffmpeg 路径

```csharp
var normalizer = new LoudnessNormalizer(@"C:\tools\ffmpeg.exe", onLog: Log);
```

### 自定义目标响度

```csharp
var normalizer = new LoudnessNormalizer(onLog: Log)
{
    TargetLufs = -14.0,      // EBU R128 广播标准
    TruePeak = -1.0,          // True Peak 上限 (dBTP)
    LoudnessRange = 11.0      // LRA 目标
};
```

### 分步操作

```csharp
void Log(string msg) => Console.WriteLine(msg);

var normalizer = new LoudnessNormalizer(onLog: Log);

// 只测量
var m = await normalizer.MeasureAsync("input.mp3");
Console.WriteLine($"输入: {m.InputI:F1} LUFS, TP: {m.InputTp:F1} dBTP");

// 用已有测量值归一化
await normalizer.ApplyAsync("input.mp3", "output.mp3", m);
```

## ffmpeg 查找优先级

```
1. 构造函数显式传入的路径
2. 应用目录下的 ffmpeg[.exe]（捆绑版）
3. ~/.arkplot/ffmpeg/<version>/<rid>/（运行时下载缓存）
4. 系统 PATH 环境变量
```

## 三端下载源

| 平台 | 下载源 | 格式 | 体积（约） |
|------|--------|------|------------|
| Windows x64 | gyan.dev essentials | zip | ~130 MB |
| Linux x64 | johnvansickle.com static | tar.xz | ~80 MB |
| Linux arm64 | johnvansickle.com static | tar.xz | ~80 MB |
| macOS x64/arm64 | evermeet.cx | zip | ~100 MB |

缓存目录结构：

```
~/.arkplot/ffmpeg/
└── 7.0.2/
    ├── win-x64/ffmpeg.exe
    ├── linux-x64/ffmpeg
    ├── linux-arm64/ffmpeg
    └── osx-x64/ffmpeg
```

## API 参考

| 类型 | 说明 |
|------|------|
| `LoudnessNormalizer` | 核心类，响度均衡。支持 `onLog` 参数记录操作日志 |
| `LoudnessMeasurement` | loudnorm 测量结果（InputI/TP/LRA/Thresh/Offset） |
| `FfmpegResolver` | ffmpeg 路径三级查找。`FindFfmpeg(onLog)` 支持日志 |
| `FfmpegDownloader` | ffmpeg 运行时下载 + 解压 + 校验。支持 `onLog` 记录下载进度和状态 |
| `FfmpegNotFoundException` | 找不到 ffmpeg 时抛出 |

## 日志集成

所有核心类都支持 `Action<string>? onLog` 参数，可接入任意日志框架：

```csharp
// Microsoft.Extensions.Logging
var normalizer = new LoudnessNormalizer(
    onLog: msg => logger.LogInformation(msg));

// Serilog
var normalizer = new LoudnessNormalizer(
    onLog: Log.Information);

// Console
var normalizer = new LoudnessNormalizer(
    onLog: Console.WriteLine);
```

**日志标签：**
- `[FfmpegResolver]` — ffmpeg 路径查找过程
- `[FfmpegDownloader]` — 下载、解压、安装过程
- `[AudioNormalizer]` — 响度测量和归一化过程

## 实测数据（98 个 TTS MP3）

| 指标 | 值 |
|------|-----|
| 通过率（±2 LUFS） | **98/98 (100%)** |
| 平均输出 LUFS | -16.75 |
| 最大偏差 | 1.91 LUFS |
| 单文件耗时 | ~250ms |

## UI 集成示例

```csharp
void Log(string msg) => logger.LogInformation(msg);

try
{
    var normalizer = await LoudnessNormalizer.CreateAsync(
        progress: new Progress<double>(p => DownloadProgress = p),
        onLog: Log);
    await normalizer.NormalizeAsync(inputPath, outputPath);
}
catch (FfmpegNotFoundException)
{
    if (await Dialog.Confirm("需要下载 ffmpeg (~100MB)，是否继续？"))
    {
        var downloader = new FfmpegDownloader(onLog: Log);
        var ffmpegPath = await downloader.DownloadAndInstallAsync(
            new Progress<double>(p => DownloadProgress = p));
        var normalizer = new LoudnessNormalizer(ffmpegPath, onLog: Log);
        await normalizer.NormalizeAsync(inputPath, outputPath);
    }
}
```
