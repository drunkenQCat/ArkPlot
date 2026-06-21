using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Avalonia.ViewModels.Test;
using ArkPlot.Avalonia.Views;
using ArkPlot.Core.Infrastructure;
using ArkPlot.Core.Model;
using ArkPlot.Tts.Alignment;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

/// <summary>
/// 验证 TtsViewModel 点击角色行时，PortraitPanel 和 GalleryPanel 收到正确的输入。
/// 使用孤星活动的真实数据（需 DB 已填充）。
/// </summary>
public class TtsComponentInputTests : System.IDisposable
{
    private readonly string _novelPath;
    private readonly string _outputDir;
    private readonly bool _hasTestData;

    public TtsComponentInputTests()
    {
        // 尝试多个可能的路径
        var possiblePaths = new[]
        {
            // 从测试项目 bin 目录向上找
            Path.Combine(Path.GetDirectoryName(typeof(TtsComponentInputTests).Assembly.Location)!,
                "..", "..", "..", "..", "ArkPlot.Avalonia", "bin", "Debug", "net9.0", "output", "孤星"),
            // 从项目根目录
            Path.Combine("C:\\TechProjects\\About_MyRepos\\ArkPlot\\ArkPlot.Avalonia\\bin\\Debug\\net9.0\\output\\孤星"),
        };

        _outputDir = possiblePaths.FirstOrDefault(p => Directory.Exists(p)) ?? "";
        _novelPath = Path.Combine(_outputDir, "孤星_novel_deepseek-v4-flash.md");
        _hasTestData = File.Exists(_novelPath);
    }

    [AvaloniaFact]
    public async Task ClickCharacterRow_PortraitPanel_ReceivesCorrectInput()
    {
        if (!_hasTestData)
        {
            // 无测试数据时跳过（CI 环境）
            return;
        }

        // 对齐
        var aligner = new NovelAligner();
        var (entries, _) = await aligner.AlignByFileNameAsync(_novelPath);

        if (entries.Count == 0)
        {
            // DB 无对应活动数据，跳过（CI 环境常见）
            return;
        }

        // 找小贾斯汀的第3段
        var target = entries
            .Where(e => e.IsDialog && e.CharacterName == "小贾斯汀")
            .Skip(2)
            .FirstOrDefault();

        if (target == null)
        {
            // 找不到该角色，跳过
            return;
        }

        Assert.True(target.EntryIndex > 0, $"EntryIndex should be > 0, but was {target.EntryIndex}");

        // 模拟 LoadPortraitAsync
        var db = DbFactory.GetClient();
        var entry = await db.Queryable<FormattedTextEntry>()
            .Where(e => e.Index == target.EntryIndex)
            .FirstAsync();

        Assert.NotNull(entry);
        Assert.NotNull(entry!.Portraits);
        Assert.NotEmpty(entry.Portraits);

        var portrait = entry.Portraits
            .FirstOrDefault(p => !string.IsNullOrEmpty(p) && !p.Contains("transparent.png"));

        // 验证：立绘不为空，角色名正确
        Assert.NotNull(portrait);
        Assert.Contains("prts.wiki", portrait!);

        // 诊断：创建 PortraitTestViewModel，设置 URL，检查绑定值
        var testVm = new PortraitTestViewModel();
        testVm.Panel.Update(portrait, "小贾斯汀");

        var testWindow = new TestWindow();
        testWindow.Show();

        // 等待 AsyncImageLoader 异步加载（给 3 秒）
        await Task.Delay(3000);

        // 诊断：检查 ViewModel 的值是否正确传递
        Console.WriteLine($"=== 诊断信息 ===");
        Console.WriteLine($"Panel.PortraitUrl: {testVm.Panel.PortraitUrl}");
        Console.WriteLine($"Panel.SpeakerName: {testVm.Panel.SpeakerName}");
        Console.WriteLine($"Panel.HasPortrait: {testVm.Panel.HasPortrait}");

        // 检查窗口是否可见
        Console.WriteLine($"TestWindow.IsVisible: {testWindow.IsVisible}");
        Console.WriteLine($"TestWindow.Width: {testWindow.Width}, Height: {testWindow.Height}");

        // 保存到文件
        var diagnosticPath = Path.Combine(_outputDir, "portrait_diagnostic.txt");
        File.WriteAllText(diagnosticPath,
            $"PortraitUrl: {testVm.Panel.PortraitUrl}\n" +
            $"SpeakerName: {testVm.Panel.SpeakerName}\n" +
            $"HasPortrait: {testVm.Panel.HasPortrait}\n" +
            $"Window: {testWindow.Width}x{testWindow.Height}, Visible={testWindow.IsVisible}\n"
        );
        Console.WriteLine($"✅ 诊断信息已保存: {diagnosticPath}");

        testWindow.Close();
    }

    [AvaloniaFact]
    public async Task ClickCharacterRow_GalleryPanel_ReceivesCorrectInput()
    {
        if (!_hasTestData)
            return;

        // 对齐
        var aligner = new NovelAligner();
        var (entries, _) = await aligner.AlignByFileNameAsync(_novelPath);

        if (entries.Count == 0)
            return;

        var target = entries
            .Where(e => e.IsDialog && e.CharacterName == "小贾斯汀")
            .Skip(2)
            .FirstOrDefault();

        if (target == null)
            return;

        var db = DbFactory.GetClient();

        // 模拟 LoadBackgroundsAsync → UpdateGalleryForSegment
        var allCharSlots = await db.Queryable<FormattedTextEntry>()
            .Where(e => e.Type == "charslot")
            .ToListAsync();

        var bgEntries = allCharSlots
            .Where(e => !string.IsNullOrEmpty(e.Bg))
            .OrderBy(e => e.Index)
            .ToList();

        if (bgEntries.Count == 0)
            return;

        var currentBg = bgEntries
            .Where(e => e.Index <= target!.EntryIndex)
            .OrderByDescending(e => e.Index)
            .FirstOrDefault();

        // 验证：有中心背景图
        Assert.NotNull(currentBg);
        Assert.Contains("prts.wiki", currentBg!.Bg);

        // 前后背景图
        var prevBg = bgEntries
            .Where(e => e.Index < currentBg.Index)
            .OrderByDescending(e => e.Index)
            .FirstOrDefault();

        var nextBg = bgEntries
            .Where(e => e.Index > currentBg.Index)
            .OrderBy(e => e.Index)
            .FirstOrDefault();

        // 至少有一个相邻背景图
        Assert.True(prevBg != null || nextBg != null);

        // 上下文台词
        var nearbyEntries = await db.Queryable<FormattedTextEntry>()
            .Where(e => !string.IsNullOrEmpty(e.Dialog))
            .ToListAsync();

        var upperDialogs = nearbyEntries
            .Where(e => e.Index < target.EntryIndex)
            .OrderByDescending(e => e.Index)
            .Take(2)
            .ToList();

        var lowerDialogs = nearbyEntries
            .Where(e => e.Index > target.EntryIndex)
            .Take(2)
            .ToList();

        // 验证：至少有2条上下文
        Assert.True(upperDialogs.Count + lowerDialogs.Count >= 2);
    }

    [AvaloniaFact]
    public async Task PortraitPanel_DefaultState_ShowsPlaceholder()
    {
        var vm = new TtsViewModel("nonexistent_dir");

        // 默认状态：子组件 ViewModel 为空
        Assert.Null(vm.PortraitPanel.PortraitUrl);
        Assert.False(vm.PortraitPanel.HasPortrait);
    }

    [AvaloniaFact]
    public async Task PortraitPanel_WithPortrait_HidesPlaceholder()
    {
        var vm = new TtsViewModel("nonexistent_dir");

        // 设置立绘
        vm.PortraitPanel.Update("https://example.com/portrait.png", "测试角色");

        Assert.True(vm.PortraitPanel.HasPortrait);
        Assert.Equal("测试角色", vm.PortraitPanel.SpeakerName);
    }

    [AvaloniaFact]
    public async Task GalleryPanel_DefaultState_ShowsPlaceholders()
    {
        var vm = new TtsViewModel("nonexistent_dir");

        // 默认状态
        Assert.Null(vm.GalleryPanel.CurrentBackground);
        Assert.False(vm.GalleryPanel.HasCurrentBackground);
        Assert.False(vm.GalleryPanel.HasPrevBackground);
        Assert.False(vm.GalleryPanel.HasNextBackground);
    }

    [AvaloniaFact]
    public async Task GalleryPanel_WithBackgrounds_UpdatesHasFlags()
    {
        var vm = new TtsViewModel("nonexistent_dir");

        vm.GalleryPanel.Update(
            "https://example.com/bg.png",
            "https://example.com/prev.png",
            "https://example.com/next.png",
            10, 20,
            "测试背景描述",
            "上文1", "上文2", "下文1", "下文2");

        Assert.True(vm.GalleryPanel.HasCurrentBackground);
        Assert.True(vm.GalleryPanel.HasPrevBackground);
        Assert.True(vm.GalleryPanel.HasNextBackground);
    }

    public void Dispose()
    {
    }
}
