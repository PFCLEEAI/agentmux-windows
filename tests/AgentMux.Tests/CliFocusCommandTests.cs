using System.Text.Json;
using AgentMux.Cli;
using AgentMux.Core.Ipc;

namespace AgentMux.Tests;

public sealed class CliFocusCommandTests
{
    [Fact]
    public void FocusRightSendsFocusPaneRpc()
    {
        var request = Program.ParseFocusRequestForTests(["right"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.FocusPane, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("right", parameters.GetProperty("direction").GetString());
    }

    [Fact]
    public void FocusAcceptsNamedDirection()
    {
        var request = Program.ParseFocusRequestForTests(["--direction", "prev"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("previous", parameters.GetProperty("direction").GetString());
    }

    [Theory]
    [InlineData()]
    [InlineData("diagonal")]
    public void FocusRejectsMissingOrInvalidDirection(params string[] args)
    {
        var request = Program.ParseFocusRequestForTests(args, out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux focus <next|previous|left|right|up|down>", error);
    }
}
