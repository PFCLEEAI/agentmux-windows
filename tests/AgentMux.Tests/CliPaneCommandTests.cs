using AgentMux.Cli;
using AgentMux.Core.Ipc;

namespace AgentMux.Tests;

public sealed class CliPaneCommandTests
{
    [Theory]
    [InlineData("zoom", AgentMuxMethods.ToggleZoom)]
    [InlineData("toggle-zoom", AgentMuxMethods.ToggleZoom)]
    [InlineData("close", AgentMuxMethods.ClosePane)]
    [InlineData("close-pane", AgentMuxMethods.ClosePane)]
    public void PaneCommandParsesSupportedActions(string action, string method)
    {
        var request = Program.ParsePaneRequestForTests([action], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(method, request.Method);
    }

    [Fact]
    public void PaneCommandRejectsMissingAction()
    {
        var request = Program.ParsePaneRequestForTests([], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux pane <zoom|close>", error);
    }

    [Fact]
    public void PaneCommandRejectsUnknownAction()
    {
        var request = Program.ParsePaneRequestForTests(["rotate"], out var error);

        Assert.Null(request);
        Assert.Equal("Unknown pane command: rotate", error);
    }
}
