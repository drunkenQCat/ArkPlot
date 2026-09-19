using ArkPlot.Core.Services;
using ArkPlot.Cli.Infrastructure;

namespace ArkPlot.Cli.Pipeline;

/// <summary>
/// Step 7: 创建视觉客户端 + PicDescService。
/// 注意：不在此处创建 MdReconstructor（会清空 charslot 的 MdText）。
/// MdReconstructor 由 MarkdownExporter 统一创建，一次完成 PicDesc + 立绘表 + 导出。
/// </summary>
public static class PicDescRunner
{
    public static PicDescService? Run()
    {
        Console.WriteLine("[7/8] 正在初始化 PicDescService...");

        Func<string, Task<string>>? describeByUrl = null;
        VisionClientResult? visionClient = null;

        if (CliOptions.UseMockVision)
        {
            Console.WriteLine("    🧪 使用 Mock 视觉描述（不消耗 API 配额）");
            describeByUrl = url => Task.FromResult(
                $"[Mock描述] 角色立于荒野，背景是罗德岛舰桥。URL={Path.GetFileName(new Uri(url).LocalPath)}");
        }
        else if (CliOptions.EnableVision)
        {
            try
            {
                visionClient = VisionClientFactory.Create();
                describeByUrl = visionClient?.DescribeByUrl;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ⚠️ 视觉客户端初始化失败：{ex.Message}");
            }
        }

        if (!CliOptions.EnablePicDesc)
        {
            Console.WriteLine("    ⏭️ PicDesc 已关闭，跳过描述生成。");
            visionClient?.Disposable?.Dispose();
            return null;
        }

        // 不 using — 生命周期交给 caller（CliPipeline）管理
        // Mock 模式下跳过写库，避免将假描述污染真实缓存
        var picDescService = new PicDescService(
            describeByUrl,
            debugMode: CliOptions.DebugMode,
            skipCache: CliOptions.UseMockVision);
        picDescService.InitializeCleanup();

        var dbStats = picDescService.GetStats();
        Console.WriteLine($"    ✅ PicDescService 初始化完成（DB 记录数：{dbStats.DbCount}）");

        // 注：visionClient 的 Disposable 在 Pipeline 结束时由 caller 释放
        // 此处保留引用避免 GC 回收
        _ = visionClient;

        return picDescService;
    }
}
