namespace ArkPlot.AudioNormalizer.Tests;

public class LoudnessMeasurementTests
{
    private const string SampleFfmpegOutput = """
        [Parsed @ 0x7f8b] lavf_read_frame
        [aac @ 0x7f9b] Input contains NaN/Inf
        [loudnorm @ 0x7fac]
        {
            "input_i" : "-23.45",
            "input_tp" : "-2.10",
            "input_lra" : "12.30",
            "input_thresh" : "-34.67",
            "output_i" : "-16.00",
            "output_tp" : "-3.25",
            "output_lra" : "11.00",
            "output_thresh" : "-27.12",
            "normalization_type" : "dynamic",
            "target_offset" : "0.15"
        }
        """;

    [Fact]
    public void Parse_StandardFfmpegOutput_ReturnsCorrectValues()
    {
        var m = LoudnessMeasurement.Parse(SampleFfmpegOutput);

        Assert.Equal(-23.45, m.InputI, 2);
        Assert.Equal(-2.10, m.InputTp, 2);
        Assert.Equal(12.30, m.InputLra, 2);
        Assert.Equal(-34.67, m.InputThresh, 2);
        Assert.Equal(0.15, m.TargetOffset, 2);
    }

    [Fact]
    public void Parse_JsonOnly_ReturnsCorrectValues()
    {
        var json = """
        {
            "input_i" : "-30.00",
            "input_tp" : "-5.00",
            "input_lra" : "8.00",
            "input_thresh" : "-41.00",
            "output_i" : "-16.00",
            "output_tp" : "-6.00",
            "output_lra" : "8.00",
            "output_thresh" : "-27.00",
            "normalization_type" : "dynamic",
            "target_offset" : "0.00"
        }
        """;

        var m = LoudnessMeasurement.Parse(json);

        Assert.Equal(-30.0, m.InputI, 1);
        Assert.Equal(-5.0, m.InputTp, 1);
        Assert.Equal(8.0, m.InputLra, 1);
        Assert.Equal(-41.0, m.InputThresh, 1);
        Assert.Equal(0.0, m.TargetOffset, 1);
    }

    [Fact]
    public void Parse_NoJsonBlock_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() =>
            LoudnessMeasurement.Parse("no json here at all"));
    }

    [Fact]
    public void Parse_MissingField_ThrowsFormatException()
    {
        var incomplete = """
        {
            "input_i" : "-23.00",
            "input_tp" : "-2.00"
        }
        """;

        Assert.Throws<FormatException>(() =>
            LoudnessMeasurement.Parse(incomplete));
    }

    [Fact]
    public void Parse_NegativeTargetOffset_ReturnsCorrectValue()
    {
        var json = """
        {
            "input_i" : "-10.00",
            "input_tp" : "-1.00",
            "input_lra" : "5.00",
            "input_thresh" : "-21.00",
            "output_i" : "-16.00",
            "output_tp" : "-7.00",
            "output_lra" : "5.00",
            "output_thresh" : "-27.00",
            "normalization_type" : "dynamic",
            "target_offset" : "-0.50"
        }
        """;

        var m = LoudnessMeasurement.Parse(json);
        Assert.Equal(-0.50, m.TargetOffset, 2);
    }
}
