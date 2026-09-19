using System;
using System.IO;
using System.Threading.Tasks;

namespace ArkPlot.Vision;

/// <summary>
/// Mock 视觉客户端：不调用任何 API，返回确定性的假描述与假思考过程，
/// 用于开发调试时在日志中展示「图片样貌 + 描述结果 + 思考过程」的完整链路。
/// </summary>
public class MockVisionClient : IDisposable
{
    /// <summary>模拟一次图片描述调用，返回确定性假描述。</summary>
    public async Task<string> DescribeImageUrlAsync(string imageUrl, string? userPrompt = null)
    {
        await Task.Delay(30); // 轻微延迟，模拟网络往返
        var name = SafeFileName(imageUrl);
        return $"[Mock描述] 画面中人物立于荒野，背景是罗德岛舰桥，天光黯淡。来源图：{name}";
    }

    /// <summary>生成一段模拟的视觉模型思考过程。</summary>
    public static string BuildThinking(string imageUrl)
    {
        var name = SafeFileName(imageUrl);
        return $"(Mock 视觉思考) 对图片 {name} 进行视觉分析：\n" +
               "1. 识别画面主体与背景层次；\n" +
               "2. 提取角色外貌、服装风格与场景色调；\n" +
               "3. 转化为适合小说创作的中文描述文本。";
    }

    private static string SafeFileName(string url)
    {
        try
        {
            return Path.GetFileName(new Uri(url).LocalPath);
        }
        catch
        {
            return url;
        }
    }

    public void Dispose() { }
}