using ArkPlot.Avalonia.Services;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

public class LogStressOptionsTests
{
    [Fact]
    public void NormalStartupDoesNotEnableStress()
        => Assert.Null(LogStressOptions.Parse([]));

    [Fact]
    public void OptInUsesBoundedDefaults()
    {
        var options = LogStressOptions.Parse(["--log-stress"]);
        Assert.NotNull(options);
        Assert.Equal(50000, options.Count);
        Assert.False(options.ExitWhenDone);
    }

    [Fact]
    public void SupportsAutomatedBoundedRun()
    {
        var options = LogStressOptions.Parse(["--log-stress", "--log-stress-count", "2000",
            "--log-stress-batch", "50", "--log-stress-interval", "1", "--log-stress-exit"]);
        Assert.Equal(new LogStressOptions(2000, 50, 1, ExitWhenDone: true), options);
    }

    [Theory]
    [InlineData("--log-stress-count", "0")]
    [InlineData("--log-stress-count", "-1")]
    [InlineData("--log-stress-count", "2147483648")]
    [InlineData("--log-stress-batch", "1001")]
    [InlineData("--log-stress-interval", "x")]
    [InlineData("--log-stress-report", "")]
    public void RejectsUnsafeOrInvalidValues(string name, string value)
        => Assert.Throws<System.ArgumentException>(() => LogStressOptions.Parse(["--log-stress", name, value]));

    [Theory]
    [InlineData("--log-stress-batch")]
    [InlineData("--log-stress-unknown")]
    public void RejectsMissingOrUnknownOptions(string name)
        => Assert.Throws<System.ArgumentException>(() => LogStressOptions.Parse(["--log-stress", name]));

    [Fact]
    public void RejectsModifiersWithoutOptIn()
        => Assert.Throws<System.ArgumentException>(() => LogStressOptions.Parse(["--log-stress-exit"]));

    [Fact]
    public void DefaultValueModifierStillRequiresOptIn()
        => Assert.Throws<System.ArgumentException>(() => LogStressOptions.Parse(["--log-stress-count", "50000"]));
}
