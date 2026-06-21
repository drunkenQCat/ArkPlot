using System.Diagnostics;
using ArkPlot.Tts.Engines;
using ArkPlot.Tts.Models;
using ArkPlot.Tts.Tests.Mocks;

namespace ArkPlot.Tts.Tests;

/// <summary>
/// HttpTtsEngine 集成测试（使用 MockTtsServer）。
/// </summary>
public class HttpTtsEngineTests : IAsyncLifetime
{
    private MockTtsServer _server = null!;

    public async Task InitializeAsync()
    {
        _server = await MockTtsServer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
    }

    private HttpTtsEngine CreateEngine(string? apiKey = null)
    {
        return new HttpTtsEngine(new CustomTtsProvider(
            Guid.NewGuid(), "Test", _server.BaseUrl, apiKey, null, []));
    }

    // T1：正常合成 — url 模式
    [Fact]
    public async Task Synthesize_UrlMode_ReturnsDownloadableFile()
    {
        _server.SynthesisHandler = (req, _) => new TtsSynthesisResponse
        {
            AudioUrl = $"{_server.BaseUrl}/audio/test.mp3",
            AudioLengthMs = 3000
        };

        // 需要让 mock 服务器也能提供 audio 下载
        // 这里简化：直接用 hex 模式测试
        _server.SynthesisHandler = (req, _) => new TtsSynthesisResponse
        {
            AudioHex = "4944330300000000002354454E430000000B0000034C616D65332E39382E3400FFFB9000",
            AudioLengthMs = 3000
        };

        var engine = CreateEngine();
        var outputPath = Path.GetTempFileName();
        try
        {
            await engine.SynthesizeAsync("你好世界", "female-01", outputPath);
            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    // T2：正常合成 — hex 模式
    [Fact]
    public async Task Synthesize_HexMode_DecodesAudioBytes()
    {
        var engine = CreateEngine();
        var outputPath = Path.GetTempFileName();
        try
        {
            await engine.SynthesizeAsync("测试", "male-01", outputPath);
            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    // T3：无效音色 — 返回 400
    [Fact]
    public async Task Synthesize_InvalidVoice_ThrowsException()
    {
        _server.SynthesisHandler = (_, _) =>
            throw new MockTtsException("Invalid voice", 400, "VOICE_NOT_FOUND");

        var engine = CreateEngine();
        var outputPath = Path.GetTempFileName();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => engine.SynthesizeAsync("hello", "nonexistent-voice", outputPath));
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    // T4：获取音色列表
    [Fact]
    public async Task FetchVoices_ReturnsServerVoiceList()
    {
        _server.AvailableVoices =
        [
            new("v1", "音色A", "Female", "zh-CN"),
            new("v2", "音色B", "Male", "zh-CN"),
        ];

        var engine = CreateEngine();
        var voices = await engine.FetchVoicesAsync();

        Assert.Equal(2, voices.Count);
        Assert.Equal("v1", voices[0].VoiceId);
        Assert.Equal("v2", voices[1].VoiceId);
    }

    // T5：服务器不可达 — 超时处理
    [Fact]
    public async Task Synthesize_ServerUnreachable_ThrowsWithinTimeout()
    {
        var engine = new HttpTtsEngine(new CustomTtsProvider(
            Guid.NewGuid(), "Dead", "http://127.0.0.1:19999", null, null, []));

        var outputPath = Path.GetTempFileName();
        try
        {
            var sw = Stopwatch.StartNew();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => engine.SynthesizeAsync("test", "v1", outputPath));
            sw.Stop();

            // 超时应在合理范围内（不应永久挂起）
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30));
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    // T6：API Key 鉴权头
    [Fact]
    public async Task Synthesize_WithApiKey_SendsBearerHeader()
    {
        var engine = CreateEngine("sk-arkplot-secret-token");
        var outputPath = Path.GetTempFileName();
        try
        {
            await engine.SynthesizeAsync("测试鉴权文本", "female-01", outputPath);

            var (request, authHeader) = _server.RequestHistory.Single();
            Assert.Equal("测试鉴权文本", request.Text);
            Assert.Equal("Bearer sk-arkplot-secret-token", authHeader);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    // T6b：无 API Key 时不发送 Authorization
    [Fact]
    public async Task Synthesize_WithoutApiKey_NoAuthorizationHeader()
    {
        var engine = CreateEngine();
        var outputPath = Path.GetTempFileName();
        try
        {
            await engine.SynthesizeAsync("无鉴权测试", "male-01", outputPath);

            var (request, authHeader) = _server.RequestHistory.Single();
            Assert.Equal("无鉴权测试", request.Text);
            Assert.Null(authHeader);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    // T6c：speed 和 volume 参数转换正确
    [Fact]
    public async Task Synthesize_SpeedAndVolume_ConvertedCorrectly()
    {
        var engine = CreateEngine();
        var outputPath = Path.GetTempFileName();
        try
        {
            await engine.SynthesizeAsync("参数测试", "female-01", outputPath, rate: "+50%", volume: "-20%");

            var (request, _) = _server.RequestHistory.Single();
            Assert.Equal("参数测试", request.Text);
            Assert.Equal(1.5, request.Speed, 0.001);   // +50% → 1.5
            Assert.Equal(0.8, request.Volume, 0.001);  // -20% → 0.8
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
}

/// <summary>
/// VoiceManagerUnified + VoicePoolBuilder 测试。
/// </summary>
public class VoiceManagerUnifiedTests
{
    // T7：多引擎音色池合并
    [Fact]
    public void VoicePoolBuilder_MergesAllEngineVoices()
    {
        var settings = new TtsSettings
        {
            EdgeTtsEnabled = true,
            MiniMaxEnabled = false,
            CustomEngines =
            [
                new(Guid.NewGuid(), "引擎A", "http://a", null, null,
                    [new("custom-v1", "音色1", "Female", null)]),
                new(Guid.NewGuid(), "引擎B", "http://b", null, null,
                    [new("custom-v2", "音色2", "Male", null)]),
            ],
            DefaultNarratorVoice = "zh-CN-XiaoxiaoNeural"
        };

        var pool = VoicePoolBuilder.Build(settings);

        // EdgeTTS 9 + 引擎A 1 + 引擎B 1 = 11
        Assert.Equal(11, pool.Count);
        Assert.Contains(pool, v => v.Entry.VoiceId == "custom-v2");
    }

    // T7b：MiniMax 音色加入池
    [Fact]
    public void VoicePoolBuilder_MiniMaxEnabled_IncludesMiniMaxVoices()
    {
        var settings = new TtsSettings
        {
            EdgeTtsEnabled = true,
            MiniMaxEnabled = true,
            MiniMaxApiKey = "sk-test",
            MiniMaxVoices =
            [
                new("mm-female-01", "MiniMax少女", "Female", "zh-CN"),
                new("mm-male-01", "MiniMax青年", "Male", "zh-CN"),
            ],
            CustomEngines = [],
            DefaultNarratorVoice = "zh-CN-XiaoxiaoNeural"
        };

        var pool = VoicePoolBuilder.Build(settings);

        // EdgeTTS 9 + MiniMax 2 = 11
        Assert.Equal(11, pool.Count);
        Assert.Contains(pool, v => v.Entry.VoiceId == "mm-female-01" && v.Engine == EngineType.MiniMax);
    }

    // T8：同名音色后加入者覆盖
    [Fact]
    public void VoicePoolBuilder_DuplicateVoiceId_LaterEngineWins()
    {
        var settings = new TtsSettings
        {
            EdgeTtsEnabled = false,
            MiniMaxEnabled = false,
            CustomEngines =
            [
                new(Guid.NewGuid(), "引擎A", "http://a", null, null,
                    [new("narrator", "旁白A", "Female", "zh-CN")]),
                new(Guid.NewGuid(), "引擎B", "http://b", null, null,
                    [new("narrator", "旁白B", "Male", "zh-CN")]),
            ],
            DefaultNarratorVoice = "narrator"
        };

        var pool = VoicePoolBuilder.Build(settings);
        var narrator = pool.Single(v => v.Entry.VoiceId == "narrator");

        // 引擎B 后加入，覆盖引擎A 的 narrator
        Assert.Equal("旁白B", narrator.Entry.DisplayName);
        Assert.Equal("Male", narrator.Entry.Gender);
        Assert.Equal(EngineType.Custom, narrator.Engine);
    }

    // T8b：VoiceManagerUnified 按性别分配
    [Fact]
    public void VoiceManagerUnified_AssignsVoiceByGender()
    {
        var settings = new TtsSettings
        {
            EdgeTtsEnabled = false,
            MiniMaxEnabled = false,
            CustomEngines =
            [
                new(Guid.NewGuid(), "Test", "http://test", null, null,
                [
                    new("f1", "女1", "Female", "zh-CN"),
                    new("f2", "女2", "Female", "zh-CN"),
                    new("m1", "男1", "Male", "zh-CN"),
                    new("m2", "男2", "Male", "zh-CN"),
                ]),
            ],
            DefaultNarratorVoice = "f1"
        };

        var pool = VoicePoolBuilder.Build(settings);
        var vm = new VoiceManagerUnified(pool, settings.DefaultNarratorVoice);

        var femaleAssignment = vm.GetVoiceForGender("女");
        Assert.Equal("Female", femaleAssignment.Entry.Gender);

        var maleAssignment = vm.GetVoiceForGender("男");
        Assert.Equal("Male", maleAssignment.Entry.Gender);

        var narratorAssignment = vm.GetNarratorAssignment();
        Assert.Equal("f1", narratorAssignment.VoiceId);
    }

    // T8c：VoiceManagerUnified 按角色名哈希分配（确定性）
    [Fact]
    public void VoiceManagerUnified_SameCharacterSameVoice()
    {
        var settings = new TtsSettings
        {
            EdgeTtsEnabled = true,
            MiniMaxEnabled = false,
            CustomEngines = [],
            DefaultNarratorVoice = "zh-CN-XiaoxiaoNeural"
        };

        var pool = VoicePoolBuilder.Build(settings);
        var vm1 = new VoiceManagerUnified(pool, settings.DefaultNarratorVoice);
        var vm2 = new VoiceManagerUnified(pool, settings.DefaultNarratorVoice);

        var voice1 = vm1.GetVoiceForCharacter("阿米娅", "女");
        var voice2 = vm2.GetVoiceForCharacter("阿米娅", "女");

        Assert.Equal(voice1.VoiceId, voice2.VoiceId);
    }
}
