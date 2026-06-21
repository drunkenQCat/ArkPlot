using System.Threading;
using System.Threading.Tasks;

namespace ArkPlot.Core.Interfaces;

/// <summary>
/// 音频响度均衡接口。
/// 由 ArkPlot.AudioNormalizer 的 LoudnessNormalizer 实现。
/// </summary>
public interface ILoudnessNormalizer
{
    /// <summary>
    /// 对音频文件做响度均衡，输出到指定路径。
    /// </summary>
    /// <param name="inputFile">输入音频文件路径</param>
    /// <param name="outputFile">输出音频文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task NormalizeAsync(string inputFile, string outputFile, CancellationToken cancellationToken = default);
}
