using Xunit;

namespace ArkPlot.Video.Tests;

/// <summary>
/// 所有使用 DbFactory 静态单例的测试必须串行执行。
/// </summary>
[CollectionDefinition("SequentialDb", DisableParallelization = true)]
public class SequentialDbCollection;
