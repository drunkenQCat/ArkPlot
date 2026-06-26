using System.Text;

namespace ArkPlot.Video.Tests;

/// <summary>
/// Mock 音频生成器：用不同频率的正弦波模拟不同角色的语音。
/// 每字时长 = charDurationMs 纯音 + gapMs 静音（电报节奏）。
/// </summary>
public static class MockAudio
{
    private static readonly int SampleRate = 8000; // 测试用低采样率加速
    private static readonly short BitsPerSample = 16;
    private static readonly short Channels = 1;

    /// <summary>每字符的纯音时长（毫秒）- 测试用短值加速生成</summary>
    public static int CharDurationMs { get; set; } = 20;

    /// <summary>字符间静音时长（毫秒）- 测试用短值加速生成</summary>
    public static int GapMs { get; set; } = 10;

    /// <summary>
    /// 生成一段正弦波 WAV 文件。
    /// </summary>
    /// <param name="filePath">输出路径</param>
    /// <param name="text">文本内容，长度决定音频时长</param>
    /// <param name="frequencyHz">正弦波频率（Hz）</param>
    /// <param name="volume">音量 0.0~1.0</param>
    public static void GenerateSineWav(string filePath, string text, double frequencyHz, double volume = 0.5)
    {
        // 限制文本长度，避免溢出
        var maxLen = Math.Min(text.Length, 500);
        var totalMs = maxLen * CharDurationMs + Math.Max(0, maxLen - 1) * GapMs;
        var totalSamples = (int)((long)totalMs * SampleRate / 1000);

        var samples = new short[totalSamples];

        for (int i = 0; i < maxLen; i++)
        {
            var charStartSample = (int)((long)i * (CharDurationMs + GapMs) * SampleRate / 1000);
            var charSamples = CharDurationMs * SampleRate / 1000;

            for (int j = 0; j < charSamples && charStartSample + j < totalSamples; j++)
            {
                double t = (double)j / SampleRate;
                var value = (short)(short.MaxValue * volume * Math.Sin(2 * Math.PI * frequencyHz * t));
                // 防止溢出（频率过高时可能产生 >short.MaxValue 的采样值）
                samples[charStartSample + j] = value > short.MaxValue ? short.MaxValue : value < short.MinValue ? short.MinValue : value;
            }
        }

        WriteWav(filePath, samples);
    }

    private static void WriteWav(string path, short[] samples)
    {
        var dataSize = samples.Length * sizeof(short);
        var fileSize = 36 + dataSize;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // RIFF header
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(fileSize);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);                    // chunk size
        bw.Write((short)1);              // PCM
        bw.Write(Channels);              // mono
        bw.Write(SampleRate);
        bw.Write(SampleRate * Channels * BitsPerSample / 8); // byte rate
        bw.Write((short)(Channels * BitsPerSample / 8));     // block align
        bw.Write(BitsPerSample);

        // data chunk
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
        foreach (var s in samples)
            bw.Write(s);

        bw.Flush();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ms.ToArray());
    }
}
