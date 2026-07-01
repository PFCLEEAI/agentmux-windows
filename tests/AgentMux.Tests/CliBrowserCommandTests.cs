using System.Text.Json;
using AgentMux.Cli;
using AgentMux.Core.Ipc;

namespace AgentMux.Tests;

public sealed class CliBrowserCommandTests
{
    [Fact]
    public void BrowserFillKeepsPositionalText()
    {
        var request = Program.ParseBrowserRequestForTests(["fill", "#prompt", "write", "tests"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserFill, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("#prompt", parameters.GetProperty("selector").GetString());
        Assert.Equal("write tests", parameters.GetProperty("text").GetString());
    }

    [Fact]
    public void BrowserFillKeepsTextWhenSelectorIsNamed()
    {
        var request = Program.ParseBrowserRequestForTests(["fill", "--selector", "#prompt", "write", "tests"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserFill, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("#prompt", parameters.GetProperty("selector").GetString());
        Assert.Equal("write tests", parameters.GetProperty("text").GetString());
    }

    [Fact]
    public void BrowserScreenshotSendsAbsolutePath()
    {
        var request = Program.ParseBrowserRequestForTests(["screenshot", "browser.png"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserScreenshot, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.True(Path.IsPathFullyQualified(parameters.GetProperty("path").GetString()!));
    }

    [Fact]
    public void BrowserTypeKeepsPositionalText()
    {
        var request = Program.ParseBrowserRequestForTests(["type", "#prompt", "write", "tests"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserType, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("#prompt", parameters.GetProperty("selector").GetString());
        Assert.Equal("write tests", parameters.GetProperty("text").GetString());
    }

    [Fact]
    public void BrowserTypeRequiresText()
    {
        var request = Program.ParseBrowserRequestForTests(["type", "#prompt"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser type <selector> <text>", error);
    }

    [Fact]
    public void BrowserPressKeepsOptionalSelector()
    {
        var request = Program.ParseBrowserRequestForTests(["press", "Enter", "--selector", "#prompt"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserPress, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("Enter", parameters.GetProperty("key").GetString());
        Assert.Equal("#prompt", parameters.GetProperty("selector").GetString());
    }

    [Fact]
    public void BrowserPressKeepsKeyWithoutSelector()
    {
        var request = Program.ParseBrowserRequestForTests(["press", "Enter"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserPress, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("Enter", parameters.GetProperty("key").GetString());
        Assert.Equal(JsonValueKind.Null, parameters.GetProperty("selector").ValueKind);
    }

    [Fact]
    public void BrowserPressRequiresKey()
    {
        var request = Program.ParseBrowserRequestForTests(["press"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser press <key> [--selector <selector>]", error);
    }
}
