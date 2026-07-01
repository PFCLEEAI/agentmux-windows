using AgentMux.Core.Ipc;

namespace AgentMux.Tests;

public sealed class TerminalKeyEncoderTests
{
    [Theory]
    [InlineData("Enter", "\r")]
    [InlineData("Tab", "\t")]
    [InlineData("Escape", "\u001b")]
    [InlineData("Backspace", "\u007f")]
    [InlineData("Delete", "\u001b[3~")]
    [InlineData("ArrowUp", "\u001b[A")]
    [InlineData("ArrowDown", "\u001b[B")]
    [InlineData("ArrowRight", "\u001b[C")]
    [InlineData("ArrowLeft", "\u001b[D")]
    [InlineData("Ctrl+C", "\u0003")]
    [InlineData("ctrlc", "\u0003")]
    [InlineData("enter", "\r")]
    public void EncodesSupportedTerminalKeys(string key, string expected)
    {
        Assert.True(TerminalKeyEncoder.TryEncode(key, out var sequence));
        Assert.Equal(expected, sequence);
    }

    [Fact]
    public void RejectsUnknownTerminalKey()
    {
        Assert.False(TerminalKeyEncoder.TryEncode("LaunchRocket", out var sequence));
        Assert.Equal("", sequence);
    }
}
