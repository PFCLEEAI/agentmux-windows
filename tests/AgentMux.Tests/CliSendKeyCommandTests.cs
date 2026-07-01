using System.Text.Json;
using AgentMux.Cli;
using AgentMux.Core.Ipc;

namespace AgentMux.Tests;

public sealed class CliSendKeyCommandTests
{
    [Fact]
    public void SendKeyParsesPositionalKey()
    {
        var parameters = JsonSerializer.SerializeToElement(
            Program.ParseSendKeyForTests(["Enter"]),
            AgentMuxJson.Options);

        Assert.Equal("Enter", parameters.GetProperty("key").GetString());
    }

    [Fact]
    public void SendKeyParsesNamedKey()
    {
        var parameters = JsonSerializer.SerializeToElement(
            Program.ParseSendKeyForTests(["--key", "PageDown"]),
            AgentMuxJson.Options);

        Assert.Equal("PageDown", parameters.GetProperty("key").GetString());
    }

    [Fact]
    public void SendKeyDoesNotCombineMultiplePositionalsIntoAChord()
    {
        var parameters = JsonSerializer.SerializeToElement(
            Program.ParseSendKeyForTests(["Ctrl", "C"]),
            AgentMuxJson.Options);

        Assert.Equal("Ctrl", parameters.GetProperty("key").GetString());
        Assert.Equal("C", parameters.GetProperty("_arg1").GetString());
    }
}
