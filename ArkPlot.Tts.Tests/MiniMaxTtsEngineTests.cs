using ArkPlot.Tts.Engines;
using Xunit;

namespace ArkPlot.Tts.Tests;

public class MiniMaxTtsEngineTests
{
    // ========== ConvertRateToSpeed 参数转换测试 ==========

    [Theory]
    [InlineData("+0%", 1.0f)]
    [InlineData("+10%", 1.1f)]
    [InlineData("+50%", 1.5f)]
    [InlineData("+100%", 2.0f)]
    [InlineData("-5%", 0.95f)]
    [InlineData("-20%", 0.8f)]
    [InlineData("-50%", 0.5f)]
    public void ConvertRateToSpeed_ValidRates_ReturnsCorrectFloat(string rate, float expected)
    {
        var result = MiniMaxTtsEngine.ConvertRateToSpeed(rate);
        Assert.Equal(expected, result, 0.001f);
    }

    [Theory]
    [InlineData("")]
    [InlineData("+")]
    [InlineData("invalid")]
    [InlineData("+10")]  // 缺少 %
    public void ConvertRateToSpeed_InvalidRates_ReturnsDefault(string rate)
    {
        var result = MiniMaxTtsEngine.ConvertRateToSpeed(rate);
        Assert.Equal(1.0f, result, 0.001f);
    }

    [Fact]
    public void ConvertRateToSpeed_Null_ReturnsDefault()
    {
        var result = MiniMaxTtsEngine.ConvertRateToSpeed(null!);
        Assert.Equal(1.0f, result, 0.001f);
    }

    [Fact]
    public void ConvertRateToSpeed_ZeroPercent_ReturnsOne()
    {
        var result = MiniMaxTtsEngine.ConvertRateToSpeed("+0%");
        Assert.Equal(1.0f, result, 0.001f);
    }
}

public class MiniMaxVoicePoolTests
{
    [Fact]
    public void MalePool_HasExpectedCount()
    {
        Assert.Equal(13, MiniMaxVoicePool.Male.Length);
    }

    [Fact]
    public void FemalePool_HasExpectedCount()
    {
        Assert.Equal(16, MiniMaxVoicePool.Female.Length);
    }

    [Fact]
    public void SpecialPool_HasExpectedCount()
    {
        Assert.Equal(2, MiniMaxVoicePool.Special.Length);
    }

    [Fact]
    public void Narrator_IsDefined()
    {
        Assert.NotNull(MiniMaxVoicePool.Narrator);
        Assert.NotEmpty(MiniMaxVoicePool.Narrator);
    }

    [Fact]
    public void AllVoices_ContainsMaleFemaleSpecialAndNarrator()
    {
        Assert.Equal(32, MiniMaxVoicePool.AllVoices.Length); // 13M + 16F + 2S + 1N
    }

    [Fact]
    public void IsFemaleVoice_Narrator_ReturnsTrue()
    {
        Assert.True(MiniMaxVoicePool.IsFemaleVoice(MiniMaxVoicePool.Narrator));
    }

    [Fact]
    public void IsFemaleVoice_FemaleVoice_ReturnsTrue()
    {
        Assert.True(MiniMaxVoicePool.IsFemaleVoice(MiniMaxVoicePool.Female[0]));
    }

    [Fact]
    public void IsFemaleVoice_MaleVoice_ReturnsFalse()
    {
        Assert.False(MiniMaxVoicePool.IsFemaleVoice(MiniMaxVoicePool.Male[0]));
    }

    [Fact]
    public void IsMaleVoice_MaleVoice_ReturnsTrue()
    {
        Assert.True(MiniMaxVoicePool.IsMaleVoice(MiniMaxVoicePool.Male[0]));
    }

    [Fact]
    public void IsMaleVoice_FemaleVoice_ReturnsFalse()
    {
        Assert.False(MiniMaxVoicePool.IsMaleVoice(MiniMaxVoicePool.Female[0]));
    }

    [Fact]
    public void IsMaleVoice_Narrator_ReturnsFalse()
    {
        Assert.False(MiniMaxVoicePool.IsMaleVoice(MiniMaxVoicePool.Narrator));
    }

    [Fact]
    public void AllVoices_EachHasValidGender()
    {
        foreach (var entry in MiniMaxVoicePool.AllVoices)
        {
            Assert.Contains(entry.Gender, new[] { "Male", "Female", "Special" });
            Assert.NotNull(entry.VoiceId);
            Assert.NotEmpty(entry.VoiceId);
            Assert.NotNull(entry.Label);
            Assert.NotEmpty(entry.Label);
        }
    }
}

public class FallbackTtsEngineTests
{
    [Fact]
    public async Task PrimarySucceeds_FallbackNotCalled()
    {
        var primary = new MockTtsEngine();
        var fallback = new MockTtsEngine();
        var engine = new FallbackTtsEngine(primary, fallback);

        var outputPath = Path.GetTempFileName();
        await engine.SynthesizeAsync("测试", "zh-CN-XiaoxiaoNeural", outputPath);

        Assert.Equal(1, primary.SynthesizeCallCount);
        Assert.Equal(0, fallback.SynthesizeCallCount);
        Assert.Equal(0, engine.FallbackCount);
        Assert.True(File.Exists(outputPath));
        File.Delete(outputPath);
    }

    [Fact]
    public async Task PrimaryFails_FallbackUsed()
    {
        var primary = new MockTtsEngine { ShouldFail = true };
        var fallback = new MockTtsEngine();
        var engine = new FallbackTtsEngine(primary, fallback);

        var outputPath = Path.GetTempFileName();
        await engine.SynthesizeAsync("测试", "zh-CN-XiaoxiaoNeural", outputPath);

        Assert.Equal(1, primary.SynthesizeCallCount);
        Assert.Equal(1, fallback.SynthesizeCallCount);
        Assert.Equal(1, engine.FallbackCount);
        Assert.True(File.Exists(outputPath));
        File.Delete(outputPath);
    }

    [Fact]
    public async Task PrimaryFails_OnFallbackCallbackInvoked()
    {
        string? fallbackMessage = null;
        var primary = new MockTtsEngine { ShouldFail = true, FailMessage = "API quota exceeded" };
        var fallback = new MockTtsEngine();
        var engine = new FallbackTtsEngine(primary, fallback, msg => fallbackMessage = msg);

        var outputPath = Path.GetTempFileName();
        await engine.SynthesizeAsync("测试", "zh-CN-XiaoxiaoNeural", outputPath);

        Assert.NotNull(fallbackMessage);
        Assert.Contains("API quota exceeded", fallbackMessage);
        File.Delete(outputPath);
    }

    [Fact]
    public async Task MultipleFailures_IncrementsCount()
    {
        var primary = new MockTtsEngine { ShouldFail = true };
        var fallback = new MockTtsEngine();
        var engine = new FallbackTtsEngine(primary, fallback);

        for (int i = 0; i < 3; i++)
        {
            var outputPath = Path.GetTempFileName();
            await engine.SynthesizeAsync("测试", "zh-CN-XiaoxiaoNeural", outputPath);
            File.Delete(outputPath);
        }

        Assert.Equal(3, engine.FallbackCount);
    }

    [Fact]
    public async Task ListVoicesAsync_DelegatesToPrimary()
    {
        var primary = new MockTtsEngine();
        var fallback = new MockTtsEngine();
        var engine = new FallbackTtsEngine(primary, fallback);

        var voices = await engine.ListVoicesAsync();
        Assert.NotEmpty(voices);
    }
}