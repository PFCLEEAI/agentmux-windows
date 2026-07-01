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
}
