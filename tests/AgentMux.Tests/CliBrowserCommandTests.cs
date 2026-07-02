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

    [Theory]
    [InlineData("back", AgentMuxMethods.BrowserBack)]
    [InlineData("forward", AgentMuxMethods.BrowserForward)]
    [InlineData("reload", AgentMuxMethods.BrowserReload)]
    [InlineData("url", AgentMuxMethods.BrowserGetUrl)]
    [InlineData("get-url", AgentMuxMethods.BrowserGetUrl)]
    public void BrowserNavigationParsesNoArgumentCommands(string command, string method)
    {
        var request = Program.ParseBrowserRequestForTests([command], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(method, request.Method);
    }

    [Theory]
    [InlineData("back", "Usage: agentmux browser back")]
    [InlineData("forward", "Usage: agentmux browser forward")]
    [InlineData("reload", "Usage: agentmux browser reload")]
    [InlineData("url", "Usage: agentmux browser url")]
    [InlineData("get-url", "Usage: agentmux browser url")]
    public void BrowserNavigationRejectsExtraArguments(string command, string usage)
    {
        var request = Program.ParseBrowserRequestForTests([command, "--surface", "surface:1"], out var error);

        Assert.Null(request);
        Assert.Equal(usage, error);
    }

    [Theory]
    [InlineData("title", AgentMuxMethods.BrowserGetTitle)]
    [InlineData("url", AgentMuxMethods.BrowserGetUrl)]
    public void BrowserGetParsesNoSelectorCommands(string kind, string method)
    {
        var request = Program.ParseBrowserRequestForTests(["get", kind], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(method, request.Method);
    }

    [Theory]
    [InlineData("text", AgentMuxMethods.BrowserGetText)]
    [InlineData("html", AgentMuxMethods.BrowserGetHtml)]
    [InlineData("value", AgentMuxMethods.BrowserGetValue)]
    [InlineData("count", AgentMuxMethods.BrowserGetCount)]
    [InlineData("box", AgentMuxMethods.BrowserGetBox)]
    public void BrowserGetParsesSelectorCommands(string kind, string method)
    {
        var request = Program.ParseBrowserRequestForTests([
            "get",
            kind,
            "#target",
            "--frame",
            "agentmux-child-frame"
        ], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(method, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("#target", parameters.GetProperty("selector").GetString());
        Assert.Equal("agentmux-child-frame", parameters.GetProperty("frame").GetString());
    }

    [Fact]
    public void BrowserGetParsesNamedSelector()
    {
        var request = Program.ParseBrowserRequestForTests(["get", "text", "--selector", "main"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserGetText, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("main", parameters.GetProperty("selector").GetString());
    }

    [Fact]
    public void BrowserGetParsesAttribute()
    {
        var request = Program.ParseBrowserRequestForTests([
            "get",
            "attr",
            "img",
            "--attr",
            "src",
            "--frame",
            "agentmux-child-frame"
        ], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserGetAttribute, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("img", parameters.GetProperty("selector").GetString());
        Assert.Equal("src", parameters.GetProperty("attr").GetString());
        Assert.Equal("agentmux-child-frame", parameters.GetProperty("frame").GetString());
    }

    [Theory]
    [InlineData("styles")]
    [InlineData("style")]
    public void BrowserGetParsesStyleProperty(string command)
    {
        var request = Program.ParseBrowserRequestForTests([
            "get",
            command,
            ".button",
            "--property",
            "color"
        ], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserGetStyle, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(".button", parameters.GetProperty("selector").GetString());
        Assert.Equal("color", parameters.GetProperty("property").GetString());
    }

    [Fact]
    public void BrowserGetRejectsUnknownKind()
    {
        var request = Program.ParseBrowserRequestForTests(["get", "snapshot"], out var error);

        Assert.Null(request);
        Assert.Equal("Unknown browser get command: snapshot", error);
    }

    [Theory]
    [InlineData("text", "Usage: agentmux browser get text <selector>")]
    [InlineData("html", "Usage: agentmux browser get html <selector>")]
    [InlineData("value", "Usage: agentmux browser get value <selector>")]
    [InlineData("count", "Usage: agentmux browser get count <selector>")]
    [InlineData("box", "Usage: agentmux browser get box <selector>")]
    public void BrowserGetRequiresSelector(string kind, string usage)
    {
        var request = Program.ParseBrowserRequestForTests(["get", kind], out var error);

        Assert.Null(request);
        Assert.Equal(usage, error);
    }

    [Fact]
    public void BrowserGetAttributeRequiresAttr()
    {
        var request = Program.ParseBrowserRequestForTests(["get", "attr", "img"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser get attr <selector> --attr <name>", error);
    }

    [Fact]
    public void BrowserGetStyleRequiresProperty()
    {
        var request = Program.ParseBrowserRequestForTests(["get", "styles", ".button"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser get styles <selector> --property <name>", error);
    }

    [Fact]
    public void BrowserGetRejectsUnsupportedSurfaceOption()
    {
        var request = Program.ParseBrowserRequestForTests(["get", "text", "main", "--surface", "surface:1"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser get text <selector>", error);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("inner-text")]
    public void BrowserTextParsesSelectorFrameAndMaxChars(string command)
    {
        var request = Program.ParseBrowserRequestForTests([
            command,
            "--selector",
            "main",
            "--frame",
            "agentmux-child-frame",
            "--max-chars",
            "250"
        ], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserText, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("main", parameters.GetProperty("selector").GetString());
        Assert.Equal("agentmux-child-frame", parameters.GetProperty("frame").GetString());
        Assert.Equal(250, parameters.GetProperty("maxChars").GetInt32());
    }

    [Fact]
    public void BrowserTextParsesDefaults()
    {
        var request = Program.ParseBrowserRequestForTests(["text"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserText, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(JsonValueKind.Null, parameters.GetProperty("selector").ValueKind);
        Assert.Equal(JsonValueKind.Null, parameters.GetProperty("frame").ValueKind);
        Assert.Equal(JsonValueKind.Null, parameters.GetProperty("maxChars").ValueKind);
    }

    [Fact]
    public void BrowserTextParsesPositionalSelector()
    {
        var request = Program.ParseBrowserRequestForTests(["text", "main", ".content"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserText, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("main .content", parameters.GetProperty("selector").GetString());
    }

    [Fact]
    public void BrowserTextFrameOptionRequiresValue()
    {
        var request = Program.ParseBrowserRequestForTests(["text", "--frame"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser text [--selector <css>] [--frame <name-or-id>] [--max-chars <count>]", error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("true")]
    public void BrowserTextRejectsInvalidMaxChars(string maxChars)
    {
        var request = Program.ParseBrowserRequestForTests(["text", "--max-chars", maxChars], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser text [--selector <css>] [--frame <name-or-id>] [--max-chars <count>]", error);
    }

    [Fact]
    public void BrowserActionsKeepOptionalFrameTarget()
    {
        var click = Program.ParseBrowserRequestForTests(["click", "--frame", "agentmux-child-frame", "#submit"], out var clickError);
        var hover = Program.ParseBrowserRequestForTests(["hover", "--frame", "agentmux-child-frame", "#submit"], out var hoverError);
        var focus = Program.ParseBrowserRequestForTests(["focus", "--frame", "agentmux-child-frame", "#prompt"], out var focusError);
        var fill = Program.ParseBrowserRequestForTests(["fill", "--frame", "agentmux-child-frame", "#prompt", "write", "tests"], out var fillError);
        var type = Program.ParseBrowserRequestForTests(["type", "--frame", "agentmux-child-frame", "#prompt", "write", "tests"], out var typeError);
        var press = Program.ParseBrowserRequestForTests(["press", "Enter", "--selector", "#prompt", "--frame", "agentmux-child-frame"], out var pressError);

        Assert.Equal("", clickError);
        Assert.Equal("", hoverError);
        Assert.Equal("", focusError);
        Assert.Equal("", fillError);
        Assert.Equal("", typeError);
        Assert.Equal("", pressError);
        Assert.NotNull(click);
        Assert.NotNull(hover);
        Assert.NotNull(focus);
        Assert.NotNull(fill);
        Assert.NotNull(type);
        Assert.NotNull(press);
        Assert.Equal(AgentMuxMethods.BrowserHover, hover.Method);
        Assert.Equal(AgentMuxMethods.BrowserFocus, focus.Method);

        Assert.Equal("agentmux-child-frame", JsonSerializer.SerializeToElement(click.Parameters, AgentMuxJson.Options).GetProperty("frame").GetString());
        Assert.Equal("agentmux-child-frame", JsonSerializer.SerializeToElement(hover.Parameters, AgentMuxJson.Options).GetProperty("frame").GetString());
        Assert.Equal("agentmux-child-frame", JsonSerializer.SerializeToElement(focus.Parameters, AgentMuxJson.Options).GetProperty("frame").GetString());
        Assert.Equal("agentmux-child-frame", JsonSerializer.SerializeToElement(fill.Parameters, AgentMuxJson.Options).GetProperty("frame").GetString());
        Assert.Equal("agentmux-child-frame", JsonSerializer.SerializeToElement(type.Parameters, AgentMuxJson.Options).GetProperty("frame").GetString());
        Assert.Equal("agentmux-child-frame", JsonSerializer.SerializeToElement(press.Parameters, AgentMuxJson.Options).GetProperty("frame").GetString());
    }

    [Theory]
    [InlineData("hover", AgentMuxMethods.BrowserHover)]
    [InlineData("focus", AgentMuxMethods.BrowserFocus)]
    public void BrowserHoverFocusParseSelector(string command, string method)
    {
        var request = Program.ParseBrowserRequestForTests([command, ".menu", "button"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(method, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(".menu button", parameters.GetProperty("selector").GetString());
        Assert.Equal(JsonValueKind.Null, parameters.GetProperty("frame").ValueKind);
    }

    [Theory]
    [InlineData("hover")]
    [InlineData("focus")]
    public void BrowserHoverFocusRequireSelector(string command)
    {
        var request = Program.ParseBrowserRequestForTests([command], out var error);

        Assert.Null(request);
        Assert.Equal($"Usage: agentmux browser {command} [--frame <name-or-id>] <selector>", error);
    }

    [Theory]
    [InlineData("hover")]
    [InlineData("focus")]
    public void BrowserHoverFocusFrameOptionRequiresValue(string command)
    {
        var request = Program.ParseBrowserRequestForTests([command, "#submit", "--frame"], out var error);

        Assert.Null(request);
        Assert.Equal($"Usage: agentmux browser {command} [--frame <name-or-id>] <selector>", error);
    }

    [Theory]
    [InlineData("hover", "--surface")]
    [InlineData("hover", "--snapshot-after")]
    [InlineData("focus", "--surface")]
    [InlineData("focus", "--snapshot-after")]
    public void BrowserHoverFocusRejectUnsupportedNamedFlags(string command, string flag)
    {
        var request = Program.ParseBrowserRequestForTests([command, "#submit", flag, "true"], out var error);

        Assert.Null(request);
        Assert.Equal($"Usage: agentmux browser {command} [--frame <name-or-id>] <selector>", error);
    }

    [Theory]
    [InlineData("visible", AgentMuxMethods.BrowserIsVisible)]
    [InlineData("enabled", AgentMuxMethods.BrowserIsEnabled)]
    [InlineData("checked", AgentMuxMethods.BrowserIsChecked)]
    public void BrowserIsParseSelector(string state, string method)
    {
        var request = Program.ParseBrowserRequestForTests(["is", state, ".menu", "button"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(method, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(".menu button", parameters.GetProperty("selector").GetString());
        Assert.Equal(JsonValueKind.Null, parameters.GetProperty("frame").ValueKind);
    }

    [Fact]
    public void BrowserIsKeepsOptionalFrameTarget()
    {
        var request = Program.ParseBrowserRequestForTests(["is", "visible", "--frame", "agentmux-child-frame", "#submit"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserIsVisible, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("#submit", parameters.GetProperty("selector").GetString());
        Assert.Equal("agentmux-child-frame", parameters.GetProperty("frame").GetString());
    }

    [Fact]
    public void BrowserIsAcceptsNamedSelector()
    {
        var request = Program.ParseBrowserRequestForTests(["is", "enabled", "--selector", "#submit"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserIsEnabled, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("#submit", parameters.GetProperty("selector").GetString());
    }

    [Theory]
    [InlineData("is")]
    [InlineData("is visible")]
    [InlineData("is detached #submit")]
    [InlineData("is visible #submit --frame")]
    public void BrowserIsRejectsInvalidShape(string commandLine)
    {
        var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var request = Program.ParseBrowserRequestForTests(args, out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser is <visible|enabled|checked> [--frame <name-or-id>] <selector>", error);
    }

    [Theory]
    [InlineData("--surface")]
    [InlineData("--snapshot-after")]
    public void BrowserIsRejectsUnsupportedNamedFlags(string flag)
    {
        var request = Program.ParseBrowserRequestForTests(["is", "visible", "#submit", flag, "true"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser is <visible|enabled|checked> [--frame <name-or-id>] <selector>", error);
    }

    [Fact]
    public void BrowserFindParsesRoleNameExactAndFrame()
    {
        var request = Program.ParseBrowserRequestForTests([
            "find",
            "role",
            "button",
            "--name",
            "Submit",
            "--exact",
            "--frame",
            "agentmux-child-frame"
        ], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserFindRole, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("button", parameters.GetProperty("role").GetString());
        Assert.Equal("Submit", parameters.GetProperty("name").GetString());
        Assert.True(parameters.GetProperty("exact").GetBoolean());
        Assert.Equal("agentmux-child-frame", parameters.GetProperty("frame").GetString());
    }

    [Theory]
    [InlineData("text", AgentMuxMethods.BrowserFindText, "text")]
    [InlineData("label", AgentMuxMethods.BrowserFindLabel, "label")]
    [InlineData("placeholder", AgentMuxMethods.BrowserFindPlaceholder, "placeholder")]
    public void BrowserFindParsesTextLikeQueries(string kind, string method, string property)
    {
        var request = Program.ParseBrowserRequestForTests(["find", kind, "Hello", "World", "--exact"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(method, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("Hello World", parameters.GetProperty(property).GetString());
        Assert.True(parameters.GetProperty("exact").GetBoolean());
        Assert.Equal(JsonValueKind.Null, parameters.GetProperty("frame").ValueKind);
    }

    [Fact]
    public void BrowserFindParsesExactBeforeText()
    {
        var request = Program.ParseBrowserRequestForTests(["find", "text", "--exact", "Hello"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserFindText, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("Hello", parameters.GetProperty("text").GetString());
        Assert.True(parameters.GetProperty("exact").GetBoolean());
    }

    [Fact]
    public void BrowserFindParsesExplicitExactFalseBeforeText()
    {
        var request = Program.ParseBrowserRequestForTests(["find", "text", "--exact", "false", "Hello"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserFindText, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("Hello", parameters.GetProperty("text").GetString());
        Assert.False(parameters.GetProperty("exact").GetBoolean());
    }

    [Fact]
    public void BrowserFindParsesTestId()
    {
        var request = Program.ParseBrowserRequestForTests(["find", "testid", "login-submit", "--frame", "agentmux-child-frame"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserFindTestId, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("login-submit", parameters.GetProperty("testId").GetString());
        Assert.Equal("agentmux-child-frame", parameters.GetProperty("frame").GetString());
    }

    [Theory]
    [InlineData("first", AgentMuxMethods.BrowserFindFirst)]
    [InlineData("last", AgentMuxMethods.BrowserFindLast)]
    public void BrowserFindParsesSelectorKinds(string kind, string method)
    {
        var request = Program.ParseBrowserRequestForTests(["find", kind, ".item", "button"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(method, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(".item button", parameters.GetProperty("selector").GetString());
    }

    [Fact]
    public void BrowserFindParsesNthSelectorAndZeroBasedIndex()
    {
        var request = Program.ParseBrowserRequestForTests(["find", "nth", ".item", "button", "2"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserFindNth, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(".item button", parameters.GetProperty("selector").GetString());
        Assert.Equal(2, parameters.GetProperty("index").GetInt32());
    }

    [Fact]
    public void BrowserFindParsesNthNamedSelectorAndIndex()
    {
        var request = Program.ParseBrowserRequestForTests(["find", "nth", "--selector", ".item", "--index", "0"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserFindNth, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(".item", parameters.GetProperty("selector").GetString());
        Assert.Equal(0, parameters.GetProperty("index").GetInt32());
    }

    [Theory]
    [InlineData("find")]
    [InlineData("find nope")]
    [InlineData("find role")]
    [InlineData("find role --role")]
    [InlineData("find text")]
    [InlineData("find label")]
    [InlineData("find placeholder")]
    [InlineData("find testid")]
    [InlineData("find first")]
    [InlineData("find last")]
    [InlineData("find nth .item")]
    [InlineData("find nth .item -1")]
    [InlineData("find nth .item nope")]
    [InlineData("find text Hello --frame")]
    public void BrowserFindRejectsInvalidShapes(string commandLine)
    {
        var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var request = Program.ParseBrowserRequestForTests(args, out var error);

        Assert.Null(request);
        Assert.Contains("Usage: agentmux browser find", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("role", "--surface")]
    [InlineData("text", "--snapshot-after")]
    [InlineData("label", "--surface")]
    [InlineData("placeholder", "--snapshot-after")]
    [InlineData("testid", "--exact")]
    [InlineData("first", "--snapshot-after")]
    [InlineData("last", "--surface")]
    [InlineData("nth", "--surface")]
    public void BrowserFindRejectsUnsupportedNamedFlags(string kind, string flag)
    {
        var args = kind == "nth"
            ? new[] { "find", kind, ".item", "0", flag, "true" }
            : new[] { "find", kind, "target", flag, "true" };
        var request = Program.ParseBrowserRequestForTests(args, out var error);

        Assert.Null(request);
        Assert.Contains("Usage: agentmux browser find", error, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserSelectParsesSelectorValueAndFrame()
    {
        var request = Program.ParseBrowserRequestForTests([
            "select",
            "#country",
            "US",
            "--frame",
            "agentmux-child-frame"
        ], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserSelect, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("#country", parameters.GetProperty("selector").GetString());
        Assert.Equal("US", parameters.GetProperty("value").GetString());
        Assert.Equal("agentmux-child-frame", parameters.GetProperty("frame").GetString());
    }

    [Fact]
    public void BrowserSelectParsesNamedSelectorAndValue()
    {
        var request = Program.ParseBrowserRequestForTests([
            "select",
            "--selector",
            "#country",
            "--value",
            "United States"
        ], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserSelect, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("#country", parameters.GetProperty("selector").GetString());
        Assert.Equal("United States", parameters.GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Null, parameters.GetProperty("frame").ValueKind);
    }

    [Fact]
    public void BrowserSelectParsesNamedTrueValue()
    {
        var request = Program.ParseBrowserRequestForTests([
            "select",
            "--selector",
            "#feature-enabled",
            "--value",
            "true"
        ], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserSelect, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("#feature-enabled", parameters.GetProperty("selector").GetString());
        Assert.Equal("true", parameters.GetProperty("value").GetString());
    }

    [Fact]
    public void BrowserSelectParsesNamedSelectorAndPositionalValueWithSpaces()
    {
        var request = Program.ParseBrowserRequestForTests([
            "select",
            "--selector",
            "#country",
            "United",
            "States"
        ], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserSelect, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("#country", parameters.GetProperty("selector").GetString());
        Assert.Equal("United States", parameters.GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("true")]
    public void BrowserSelectParsesExplicitPositionalOptionValues(string value)
    {
        var request = Program.ParseBrowserRequestForTests(["select", "#country", value], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserSelect, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("#country", parameters.GetProperty("selector").GetString());
        Assert.Equal(value, parameters.GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("select")]
    [InlineData("select #country")]
    [InlineData("select --selector #country")]
    [InlineData("select #country --value")]
    [InlineData("select #country US --frame")]
    [InlineData("select #country US --surface surface:1")]
    [InlineData("select #country US --snapshot-after")]
    public void BrowserSelectRejectsInvalidShapes(string commandLine)
    {
        var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var request = Program.ParseBrowserRequestForTests(args, out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser select [--frame <name-or-id>] <selector> <value>", error);
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
    [InlineData("wait-for-selector")]
    [InlineData("wait")]
    public void BrowserWaitParsesSelectorStateTimeoutAndFrame(string command)
    {
        var request = Program.ParseBrowserRequestForTests([
            command,
            "#ready",
            "--state",
            "attached",
            "--timeout-ms",
            "2500",
            "--frame",
            "agentmux-child-frame"
        ], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserWaitForSelector, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("#ready", parameters.GetProperty("selector").GetString());
        Assert.Equal("attached", parameters.GetProperty("state").GetString());
        Assert.Equal(2500, parameters.GetProperty("timeoutMs").GetInt32());
        Assert.Equal("agentmux-child-frame", parameters.GetProperty("frame").GetString());
    }

    [Fact]
    public void BrowserWaitParsesSelectorOnly()
    {
        var request = Program.ParseBrowserRequestForTests(["wait", "#ready"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserWaitForSelector, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("#ready", parameters.GetProperty("selector").GetString());
        Assert.Equal(JsonValueKind.Null, parameters.GetProperty("state").ValueKind);
        Assert.Equal(JsonValueKind.Null, parameters.GetProperty("timeoutMs").ValueKind);
        Assert.Equal(JsonValueKind.Null, parameters.GetProperty("frame").ValueKind);
    }

    [Fact]
    public void BrowserWaitRequiresSelector()
    {
        var request = Program.ParseBrowserRequestForTests(["wait-for-selector", "--timeout-ms", "100"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser wait-for-selector <selector> [--state <visible|attached|hidden>] [--timeout-ms <ms>] [--frame <name-or-id>]", error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("true")]
    public void BrowserWaitRejectsInvalidTimeout(string timeoutMs)
    {
        var request = Program.ParseBrowserRequestForTests(["wait-for-selector", "#ready", "--timeout-ms", timeoutMs], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser wait-for-selector <selector> [--state <visible|attached|hidden>] [--timeout-ms <ms>] [--frame <name-or-id>]", error);
    }

    [Fact]
    public void BrowserWaitRejectsInvalidState()
    {
        var request = Program.ParseBrowserRequestForTests(["wait-for-selector", "#ready", "--state", "detached"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser wait-for-selector <selector> [--state <visible|attached|hidden>] [--timeout-ms <ms>] [--frame <name-or-id>]", error);
    }

    [Fact]
    public void BrowserWaitFrameOptionRequiresValue()
    {
        var request = Program.ParseBrowserRequestForTests(["wait-for-selector", "#ready", "--frame"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser wait-for-selector <selector> [--state <visible|attached|hidden>] [--timeout-ms <ms>] [--frame <name-or-id>]", error);
    }

    [Theory]
    [InlineData("wait-load")]
    [InlineData("wait-load-state")]
    public void BrowserWaitLoadParsesStateAndTimeout(string command)
    {
        var request = Program.ParseBrowserRequestForTests([
            command,
            "--state",
            "network-idle",
            "--timeout-ms",
            "2500"
        ], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserWaitForLoad, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("network-idle", parameters.GetProperty("state").GetString());
        Assert.Equal(2500, parameters.GetProperty("timeoutMs").GetInt32());
    }

    [Fact]
    public void BrowserWaitLoadParsesDefaults()
    {
        var request = Program.ParseBrowserRequestForTests(["wait-load"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserWaitForLoad, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal(JsonValueKind.Null, parameters.GetProperty("state").ValueKind);
        Assert.Equal(JsonValueKind.Null, parameters.GetProperty("timeoutMs").ValueKind);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("true")]
    public void BrowserWaitLoadRejectsInvalidTimeout(string timeoutMs)
    {
        var request = Program.ParseBrowserRequestForTests(["wait-load", "--timeout-ms", timeoutMs], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser wait-load [--state <domcontentloaded|load|network-idle>] [--timeout-ms <ms>]", error);
    }

    [Theory]
    [InlineData("visible")]
    [InlineData("attached")]
    [InlineData("networkidle")]
    public void BrowserWaitLoadRejectsInvalidState(string state)
    {
        var request = Program.ParseBrowserRequestForTests(["wait-load", "--state", state], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser wait-load [--state <domcontentloaded|load|network-idle>] [--timeout-ms <ms>]", error);
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
    [InlineData("trace")]
    [InlineData("tracing")]
    public void BrowserTraceSendsAbsolutePathAndOptions(string command)
    {
        var request = Program.ParseBrowserRequestForTests([
            command,
            "browser-trace.json",
            "--duration-ms",
            "750",
            "--max-bytes",
            "4096"
        ], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserTrace, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.True(Path.IsPathFullyQualified(parameters.GetProperty("path").GetString()!));
        Assert.Equal(750, parameters.GetProperty("durationMs").GetInt32());
        Assert.Equal(4096, parameters.GetProperty("maxBytes").GetInt32());
    }

    [Fact]
    public void BrowserTraceUsesOptionalDefaults()
    {
        var request = Program.ParseBrowserRequestForTests(["trace", "browser-trace.json"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserTrace, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.True(Path.IsPathFullyQualified(parameters.GetProperty("path").GetString()!));
        Assert.Equal(JsonValueKind.Null, parameters.GetProperty("durationMs").ValueKind);
        Assert.Equal(JsonValueKind.Null, parameters.GetProperty("maxBytes").ValueKind);
    }

    [Fact]
    public void BrowserTraceRequiresPath()
    {
        var request = Program.ParseBrowserRequestForTests(["trace"], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser trace <path> [--duration-ms <ms>] [--max-bytes <bytes>]", error);
    }

    [Theory]
    [InlineData("--duration-ms", "0")]
    [InlineData("--duration-ms", "-1")]
    [InlineData("--duration-ms", "abc")]
    [InlineData("--duration-ms", "true")]
    [InlineData("--max-bytes", "0")]
    [InlineData("--max-bytes", "-1")]
    [InlineData("--max-bytes", "abc")]
    [InlineData("--max-bytes", "true")]
    public void BrowserTraceRejectsInvalidNumericOptions(string option, string value)
    {
        var request = Program.ParseBrowserRequestForTests(["trace", "browser-trace.json", option, value], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser trace <path> [--duration-ms <ms>] [--max-bytes <bytes>]", error);
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

    [Theory]
    [InlineData("route")]
    [InlineData("routes")]
    public void BrowserRouteParsesListAndClear(string command)
    {
        var list = Program.ParseBrowserRequestForTests([command, "list"], out var listError);
        var clear = Program.ParseBrowserRequestForTests([command, "clear"], out var clearError);

        Assert.Equal("", listError);
        Assert.Equal("", clearError);
        Assert.NotNull(list);
        Assert.NotNull(clear);
        Assert.Equal(AgentMuxMethods.BrowserRouteList, list.Method);
        Assert.Equal(AgentMuxMethods.BrowserRouteClear, clear.Method);
    }

    [Fact]
    public void BrowserRouteParsesBlockUrlContains()
    {
        var request = Program.ParseBrowserRequestForTests(["route", "block", "--url-contains", "/api/private"], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserRouteBlock, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("/api/private", parameters.GetProperty("urlContains").GetString());
    }

    [Fact]
    public void BrowserRouteParsesFulfillOptions()
    {
        var request = Program.ParseBrowserRequestForTests([
            "route",
            "fulfill",
            "--url-contains",
            "/api/mock",
            "--status",
            "201",
            "--content-type",
            "application/json",
            "--body",
            "{\"ok\":true}"
        ], out var error);

        Assert.Equal("", error);
        Assert.NotNull(request);
        Assert.Equal(AgentMuxMethods.BrowserRouteFulfill, request.Method);
        var parameters = JsonSerializer.SerializeToElement(request.Parameters, AgentMuxJson.Options);
        Assert.Equal("/api/mock", parameters.GetProperty("urlContains").GetString());
        Assert.Equal(201, parameters.GetProperty("status").GetInt32());
        Assert.Equal("application/json", parameters.GetProperty("contentType").GetString());
        Assert.Equal("{\"ok\":true}", parameters.GetProperty("body").GetString());
    }

    [Theory]
    [InlineData("99")]
    [InlineData("600")]
    [InlineData("abc")]
    public void BrowserRouteFulfillRejectsInvalidStatus(string status)
    {
        var request = Program.ParseBrowserRequestForTests([
            "route",
            "fulfill",
            "--url-contains",
            "/api/mock",
            "--status",
            status
        ], out var error);

        Assert.Null(request);
        Assert.Equal("Usage: agentmux browser route fulfill --url-contains <text> [--status <code>] [--content-type <type>] [--body <text>]", error);
    }

    [Theory]
    [InlineData("block")]
    [InlineData("fulfill")]
    public void BrowserRouteRequiresUrlContains(string action)
    {
        var request = Program.ParseBrowserRequestForTests(["route", action], out var error);

        Assert.Null(request);
        Assert.StartsWith($"Usage: agentmux browser route {action}", error, StringComparison.Ordinal);
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
