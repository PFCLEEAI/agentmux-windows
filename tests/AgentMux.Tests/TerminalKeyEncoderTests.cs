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
    [InlineData("Insert", "\u001b[2~")]
    [InlineData("ArrowUp", "\u001b[A")]
    [InlineData("ArrowDown", "\u001b[B")]
    [InlineData("ArrowRight", "\u001b[C")]
    [InlineData("ArrowLeft", "\u001b[D")]
    [InlineData("Home", "\u001b[H")]
    [InlineData("End", "\u001b[F")]
    [InlineData("PageUp", "\u001b[5~")]
    [InlineData("PgDn", "\u001b[6~")]
    [InlineData("Shift+Tab", "\u001b[Z")]
    [InlineData("F1", "\u001bOP")]
    [InlineData("F4", "\u001bOS")]
    [InlineData("F5", "\u001b[15~")]
    [InlineData("F12", "\u001b[24~")]
    [InlineData("Ctrl+C", "\u0003")]
    [InlineData("ctrlc", "\u0003")]
    [InlineData("Control-D", "\u0004")]
    [InlineData("Ctrl+Z", "\u001a")]
    [InlineData("Ctrl+Space", "\u0000")]
    [InlineData("Ctrl+[", "\u001b")]
    [InlineData("Ctrl+\\", "\u001c")]
    [InlineData("Ctrl+]", "\u001d")]
    [InlineData("Ctrl+^", "\u001e")]
    [InlineData("Ctrl+_", "\u001f")]
    [InlineData("Ctrl+?", "\u007f")]
    [InlineData("Alt+Enter", "\u001b\r")]
    [InlineData("Meta+F2", "\u001b\u001bOQ")]
    [InlineData("Alt+x", "\u001bx")]
    [InlineData("Alt+X", "\u001bX")]
    [InlineData("Alt+1", "\u001b1")]
    [InlineData("Alt+[", "\u001b[")]
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("\u001b[A")]
    [InlineData("[A")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+1")]
    [InlineData("Ctrl+Enter")]
    [InlineData("Ctrl+Alt+Delete")]
    [InlineData("Shift+Enter")]
    [InlineData("Enter:3")]
    [InlineData("Alt")]
    [InlineData("Alt+")]
    [InlineData("Alt+LaunchRocket")]
    [InlineData("Alt+Ctrl")]
    [InlineData("Alt+Ctrl+Delete")]
    [InlineData("F13")]
    public void RejectsAmbiguousOrUnsupportedKeyForms(string? key)
    {
        Assert.False(TerminalKeyEncoder.TryEncode(key, out var sequence));
        Assert.Equal("", sequence);
    }
}
