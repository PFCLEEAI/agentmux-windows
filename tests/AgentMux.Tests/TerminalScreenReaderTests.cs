using AgentMux.Core.Terminals;

namespace AgentMux.Tests;

public sealed class TerminalScreenReaderTests
{
    [Fact]
    public void ReadWithoutLineLimitReturnsOriginalText()
    {
        var result = TerminalScreenReader.Read("one\r\ntwo\n", null);

        Assert.Equal("one\r\ntwo\n", result.Text);
        Assert.Null(result.Lines);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void ReadHandlesNullTextAsEmpty()
    {
        var result = TerminalScreenReader.Read(null, 10);

        Assert.Equal("", result.Text);
        Assert.Equal(10, result.Lines);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void ReadHandlesBlankTextAsEmpty()
    {
        var result = TerminalScreenReader.Read("", 10);

        Assert.Equal("", result.Text);
        Assert.Equal(10, result.Lines);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void ReadHandlesOnlyNewlinesWithoutSyntheticFinalBlank()
    {
        var result = TerminalScreenReader.Read("\n\n", 1);

        Assert.Equal("", result.Text);
        Assert.Equal(1, result.Lines);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void ReadReturnsLastRequestedLines()
    {
        var result = TerminalScreenReader.Read("one\ntwo\nthree\nfour", 2);

        Assert.Equal("three\nfour", result.Text);
        Assert.Equal(2, result.Lines);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void ReadNormalizesCrLfWhenLineLimitIsRequested()
    {
        var result = TerminalScreenReader.Read("one\r\ntwo\r\nthree", 2);

        Assert.Equal("two\nthree", result.Text);
        Assert.Equal(2, result.Lines);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void ReadNormalizesCrWhenLineLimitIsRequested()
    {
        var result = TerminalScreenReader.Read("one\rtwo\rthree", 2);

        Assert.Equal("two\nthree", result.Text);
        Assert.Equal(2, result.Lines);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void ReadDoesNotCreateSyntheticTrailingBlankLine()
    {
        var result = TerminalScreenReader.Read("one\ntwo\n", 1);

        Assert.Equal("two", result.Text);
        Assert.Equal(1, result.Lines);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void ReadPreservesRealTrailingBlankLineBeforeFinalNewline()
    {
        var result = TerminalScreenReader.Read("one\ntwo\n\n", 2);

        Assert.Equal("two\n", result.Text);
        Assert.Equal(2, result.Lines);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void ReadReportsNotTruncatedWhenWithinLimit()
    {
        var result = TerminalScreenReader.Read("one\ntwo", 5);

        Assert.Equal("one\ntwo", result.Text);
        Assert.Equal(5, result.Lines);
        Assert.False(result.Truncated);
    }
}
