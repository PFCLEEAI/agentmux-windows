using AgentMux.Cli;
using AgentMux.Core.Ipc;
using System.Text.Json;

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

    [Theory]
    [InlineData("resize", "84", "24")]
    [InlineData("resize-terminal", "100", "30")]
    public void PaneCommandParsesResizeWithNamedDimensions(string action, string cols, string rows)
    {
        var request = Program.ParsePaneRequestForTests([action, "--cols", cols, "--rows", rows], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.ResizeTerminal, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(int.Parse(cols), parameters.GetProperty("cols").GetInt32());
        Assert.Equal(int.Parse(rows), parameters.GetProperty("rows").GetInt32());
    }

    [Fact]
    public void PaneCommandParsesResizeWithPositionalDimensions()
    {
        var request = Program.ParsePaneRequestForTests(["resize", "84", "24"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.ResizeTerminal, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(84, parameters.GetProperty("cols").GetInt32());
        Assert.Equal(24, parameters.GetProperty("rows").GetInt32());
    }

    [Theory]
    [InlineData("0", "24")]
    [InlineData("84", "-1")]
    [InlineData("wide", "24")]
    public void PaneCommandRejectsInvalidResizeDimensions(string cols, string rows)
    {
        var request = Program.ParsePaneRequestForTests(["resize", "--cols", cols, "--rows", rows], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux pane resize --cols <cols> --rows <rows>", error);
    }

    [Fact]
    public void PaneCommandRejectsMissingAction()
    {
        var request = Program.ParsePaneRequestForTests([], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux pane <zoom|close|resize>", error);
    }

    [Fact]
    public void PaneCommandRejectsUnknownAction()
    {
        var request = Program.ParsePaneRequestForTests(["rotate"], out var error);

        Assert.Null(request);
        Assert.Equal("Unknown pane command: rotate", error);
    }
}
