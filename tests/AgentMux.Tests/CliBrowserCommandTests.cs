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
    public void BrowserActionsKeepOptionalFrameTarget()
    {
        var click = Program.ParseBrowserRequestForTests(["click", "--frame", "agentmux-child-frame", "#submit"], out var clickError);
        var fill = Program.ParseBrowserRequestForTests(["fill", "--frame", "agentmux-child-frame", "#prompt", "write", "tests"], out var fillError);
        var type = Program.ParseBrowserRequestForTests(["type", "--frame", "agentmux-child-frame", "#prompt", "write", "tests"], out var typeError);
        var press = Program.ParseBrowserRequestForTests(["press", "Enter", "--selector", "#prompt", "--frame", "agentmux-child-frame"], out var pressError);

        Assert.Equal("", clickError);
        Assert.Equal("", fillError);
        Assert.Equal("", typeError);
        Assert.Equal("", pressError);
        Assert.NotNull(click);
        Assert.NotNull(fill);
        Assert.NotNull(type);
        Assert.NotNull(press);

        Assert.Equal("agentmux-child-frame", JsonSerializer.SerializeToElement(click.Parameters, AgentMuxJson.Options).GetProperty("frame").GetString());
        Assert.Equal("agentmux-child-frame", JsonSerializer.SerializeToElement(fill.Parameters, AgentMuxJson.Options).GetProperty("frame").GetString());
        Assert.Equal("agentmux-child-frame", JsonSerializer.SerializeToElement(type.Parameters, AgentMuxJson.Options).GetProperty("frame").GetString());
        Assert.Equal("agentmux-child-frame", JsonSerializer.SerializeToElement(press.Parameters, AgentMuxJson.Options).GetProperty("frame").GetString());
    }

    [Fact]
    public void BrowserFrameOptionRequiresValue()
    {
        var request = Program.ParseBrowserRequestForTests(["click", "#submit", "--frame"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser click [--frame <name-or-id>] <selector>", error);
    }

    [Fact]
    public void BrowserPressFrameRequiresSelector()
    {
        var request = Program.ParseBrowserRequestForTests(["press", "Enter", "--frame", "agentmux-child-frame"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser press <key> [--selector <selector> [--frame <name-or-id>]]", error);
    }

    [Theory]
    [InlineData("frames")]
    [InlineData("frame-tree")]
    public void BrowserFramesParsesFrameTreeCommand(string command)
    {
        var request = Program.ParseBrowserRequestForTests([command], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserFrameTree, request.Method);
    }

    [Theory]
    [InlineData("console")]
    [InlineData("console-log")]
    public void BrowserConsoleParsesConsoleLogCommand(string command)
    {
        var request = Program.ParseBrowserRequestForTests([command, "--limit", "20"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserConsoleLog, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(20, parameters.GetProperty("limit").GetInt32());
    }

    [Theory]
    [InlineData("console-clear")]
    [InlineData("clear-console")]
    public void BrowserConsoleParsesClearCommand(string command)
    {
        var request = Program.ParseBrowserRequestForTests([command], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserConsoleClear, request.Method);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("true")]
    public void BrowserConsoleRejectsInvalidLimit(string limit)
    {
        var request = Program.ParseBrowserRequestForTests(["console", "--limit", limit], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser console [--limit <count>]", error);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("network-log")]
    public void BrowserNetworkParsesNetworkLogCommand(string command)
    {
        var request = Program.ParseBrowserRequestForTests([command, "--limit", "20"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserNetworkLog, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(20, parameters.GetProperty("limit").GetInt32());
    }

    [Theory]
    [InlineData("network-clear")]
    [InlineData("clear-network")]
    public void BrowserNetworkParsesClearCommand(string command)
    {
        var request = Program.ParseBrowserRequestForTests([command], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserNetworkClear, request.Method);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("true")]
    public void BrowserNetworkRejectsInvalidLimit(string limit)
    {
        var request = Program.ParseBrowserRequestForTests(["network", "--limit", limit], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser network [--limit <count>]", error);
    }

    [Theory]
    [InlineData("response-body")]
    [InlineData("body")]
    public void BrowserResponseBodyParsesRequestId(string command)
    {
        var request = Program.ParseBrowserRequestForTests([command, "1234.56"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserResponseBody, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("1234.56", parameters.GetProperty("requestId").GetString());
    }

    [Theory]
    [InlineData("response-body")]
    [InlineData("body")]
    public void BrowserResponseBodyRequiresRequestId(string command)
    {
        var request = Program.ParseBrowserRequestForTests([command], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser response-body <request-id>", error);
    }

    [Fact]
    public void BrowserResponseBodyRejectsExtraArgs()
    {
        var request = Program.ParseBrowserRequestForTests(["response-body", "1234.56", "extra"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser response-body <request-id>", error);
    }

    [Theory]
    [InlineData("har")]
    [InlineData("har-metadata")]
    public void BrowserHarMetadataSendsAbsolutePath(string command)
    {
        var request = Program.ParseBrowserRequestForTests([command, "network.har.json"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserHarMetadata, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.True(Path.IsPathFullyQualified(parameters.GetProperty("path").GetString()!));
    }

    [Theory]
    [InlineData("har")]
    [InlineData("har-metadata")]
    public void BrowserHarMetadataRequiresPath(string command)
    {
        var request = Program.ParseBrowserRequestForTests([command], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser har <path>", error);
    }

    [Theory]
    [InlineData("downloads")]
    [InlineData("download-log")]
    public void BrowserDownloadsParsesDownloadLogCommand(string command)
    {
        var request = Program.ParseBrowserRequestForTests([command, "--limit", "20"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserDownloads, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(20, parameters.GetProperty("limit").GetInt32());
    }

    [Theory]
    [InlineData("downloads-clear")]
    [InlineData("clear-downloads")]
    public void BrowserDownloadsParsesClearCommand(string command)
    {
        var request = Program.ParseBrowserRequestForTests([command], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserDownloadsClear, request.Method);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("true")]
    public void BrowserDownloadsRejectsInvalidLimit(string limit)
    {
        var request = Program.ParseBrowserRequestForTests(["downloads", "--limit", limit], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser downloads [--limit <count>]", error);
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
        Assert.Equal("Usage: agentmux browser type [--frame <name-or-id>] <selector> <text>", error);
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
        Assert.Equal("Usage: agentmux browser press <key> [--selector <selector> [--frame <name-or-id>]]", error);
    }
}
