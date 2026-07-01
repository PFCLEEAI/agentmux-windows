using System.Text.Json;
using AgentMux.Cli;
using AgentMux.Core.Ipc;

namespace AgentMux.Tests;

public sealed class CliReadScreenCommandTests
{
    [Fact]
    public void ReadScreenParsesWithoutLineLimit()
    {
        var request = Program.ParseReadScreenRequestForTests([], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.ReadScreen, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.True(parameters.TryGetProperty("lines", out var lines));
        Assert.Equal(JsonValueKind.Null, lines.ValueKind);
    }

    [Fact]
    public void ReadScreenParsesLineLimit()
    {
        var request = Program.ParseReadScreenRequestForTests(["--lines", "50"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.ReadScreen, request.Method);

        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(50, parameters.GetProperty("lines").GetInt32());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+5")]
    [InlineData(" 5 ")]
    [InlineData("many")]
    [InlineData("true")]
    public void ReadScreenRejectsInvalidLineLimit(string lines)
    {
        var request = Program.ParseReadScreenRequestForTests(["--lines", lines], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux read-screen [--lines <count>]", error);
    }

    [Fact]
    public void ReadScreenRejectsBareLinesFlag()
    {
        var request = Program.ParseReadScreenRequestForTests(["--lines"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux read-screen [--lines <count>]", error);
    }

    [Fact]
    public void ReadScreenRejectsUnexpectedArguments()
    {
        string[][] cases =
        [
            ["50"],
            ["--foo", "bar"],
            ["--lines=50"],
            ["--lines", "50", "extra"],
            ["--lines", "50", "--lines", "10"]
        ];

        foreach (var args in cases)
        {
            var request = Program.ParseReadScreenRequestForTests(args, out var error);

            Assert.Null(request);
            Assert.Equal("Usage: agentmux read-screen [--lines <count>]", error);
        }
    }
}
