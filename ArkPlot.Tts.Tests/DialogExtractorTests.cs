using ArkPlot.Tts.Alignment;
using Xunit;

namespace ArkPlot.Tts.Tests;

public class DialogExtractorTests
{
    [Fact]
    public void ExtractSegments_StraightQuotes_ParsesAsDialog()
    {
        var text = "He said \"Hello world\" and left.";
        var segments = DialogExtractor.ExtractSegments(text);
        
        Assert.Equal(3, segments.Count);
        Assert.Equal("He said", segments[0].Text);
        Assert.False(segments[0].IsDialog);
        Assert.Equal("Hello world", segments[1].Text);
        Assert.True(segments[1].IsDialog);
        Assert.Equal("and left.", segments[2].Text);
        Assert.False(segments[2].IsDialog);
    }

    [Fact]
    public void ExtractSegments_CurvedQuotes_ParsesAsDialog()
    {
        var text = "He said \u201CHello world\u201D and left.";
        var segments = DialogExtractor.ExtractSegments(text);
        
        Assert.Equal(3, segments.Count);
        Assert.Equal("He said", segments[0].Text);
        Assert.False(segments[0].IsDialog);
        Assert.Equal("Hello world", segments[1].Text);
        Assert.True(segments[1].IsDialog);
        Assert.Equal("and left.", segments[2].Text);
        Assert.False(segments[2].IsDialog);
    }

    [Fact]
    public void ExtractSegments_MixedQuotes_ParsesAllAsDialog()
    {
        var text = "\"First\" then \u201Csecond\u201D done.";
        var segments = DialogExtractor.ExtractSegments(text);
        
        Assert.Equal(4, segments.Count);
        Assert.Equal("First", segments[0].Text);
        Assert.True(segments[0].IsDialog);
        Assert.Equal("then", segments[1].Text);
        Assert.False(segments[1].IsDialog);
        Assert.Equal("second", segments[2].Text);
        Assert.True(segments[2].IsDialog);
        Assert.Equal("done.", segments[3].Text);
        Assert.False(segments[3].IsDialog);
    }

    [Fact]
    public void ExtractSegments_MultipleStraightQuotes_ParsesAll()
    {
        var text = "\"One\" and \"Two\" and \"Three\"";
        var segments = DialogExtractor.ExtractSegments(text);
        
        Assert.Equal(5, segments.Count);
        Assert.Equal("One", segments[0].Text);
        Assert.True(segments[0].IsDialog);
        Assert.Equal("and", segments[1].Text);
        Assert.False(segments[1].IsDialog);
        Assert.Equal("Two", segments[2].Text);
        Assert.True(segments[2].IsDialog);
        Assert.Equal("and", segments[3].Text);
        Assert.False(segments[3].IsDialog);
        Assert.Equal("Three", segments[4].Text);
        Assert.True(segments[4].IsDialog);
    }

    [Fact]
    public void ExtractSegments_EmptyText_ReturnsEmpty()
    {
        var segments = DialogExtractor.ExtractSegments("");
        Assert.Empty(segments);
    }

    [Fact]
    public void ExtractSegments_OnlyQuotes_NoExtraSegments()
    {
        var text = "\"Hello\"";
        var segments = DialogExtractor.ExtractSegments(text);
        
        Assert.Single(segments);
        Assert.Equal("Hello", segments[0].Text);
        Assert.True(segments[0].IsDialog);
    }

    [Fact]
    public void ExtractSegments_UnclosedStraightQuote_TreatsAsText()
    {
        var text = "He said \"Hello";
        var segments = DialogExtractor.ExtractSegments(text);
        
        // Unclosed quote: text before becomes narration, remaining (including ") also narration
        Assert.Equal(2, segments.Count);
        Assert.False(segments[0].IsDialog);
        Assert.False(segments[1].IsDialog);
    }

    [Fact]
    public void ExtractDialogs_StraightQuotes_ExtractsAll()
    {
        var text = "\"First\" and \"Second\"";
        var dialogs = DialogExtractor.ExtractDialogs(text);
        
        Assert.Equal(2, dialogs.Count);
        Assert.Contains("First", dialogs);
        Assert.Contains("Second", dialogs);
    }

    [Fact]
    public void ExtractDialogs_MixedQuotes_ExtractsAll()
    {
        var text = "\"Straight\" and \u201CCurved\u201D";
        var dialogs = DialogExtractor.ExtractDialogs(text);
        
        Assert.Equal(2, dialogs.Count);
        Assert.Contains("Straight", dialogs);
        Assert.Contains("Curved", dialogs);
    }
}
