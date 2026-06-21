namespace ArkPlot.AudioNormalizer;

/// <summary>
/// ffmpeg loudnorm 滤镜第一遍测量结果。
/// 字段名与 loudnorm JSON 输出一一对应。
/// </summary>
public record LoudnessMeasurement(
    double InputI,
    double InputTp,
    double InputLra,
    double InputThresh,
    double TargetOffset)
{
    /// <summary>
    /// 从 loudnorm 的 JSON stderr 输出解析测量值。
    /// </summary>
    public static LoudnessMeasurement Parse(string json)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 从 stderr 可能包含多行文本，提取最后一个 { } 块
        var start = json.LastIndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new FormatException($"无法从 ffmpeg 输出中找到 loudnorm JSON 块:\n{json}");

        var block = json[(start + 1)..end];
        foreach (var line in block.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var colonIdx = line.IndexOf(':');
            if (colonIdx < 0) continue;

            var key = line[..colonIdx].Trim().Trim('"');
            var val = line[(colonIdx + 1)..].Trim().Trim('"', ',');
            dict[key] = val;
        }

        return new LoudnessMeasurement(
            InputI: ParseDouble(dict, "input_i"),
            InputTp: ParseDouble(dict, "input_tp"),
            InputLra: ParseDouble(dict, "input_lra"),
            InputThresh: ParseDouble(dict, "input_thresh"),
            TargetOffset: ParseDouble(dict, "target_offset"));
    }

    private static double ParseDouble(Dictionary<string, string> dict, string key)
    {
        if (!dict.TryGetValue(key, out var s) || !double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"loudnorm JSON 中缺少或无法解析字段: {key}");
        return v;
    }
}
