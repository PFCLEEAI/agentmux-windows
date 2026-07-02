using System.Text.Json;
using System.IO.Pipes;
using System.Reflection;
using AgentMux.Core.Ipc;

namespace AgentMux.Tests;

public sealed class RpcContractTests
{
    [Fact]
    public void RequestRoundTripsWithParams()
    {
        var request = new AgentMuxRequest
        {
            Id = "req-1",
            Method = AgentMuxMethods.Notify,
            Params = JsonSerializer.SerializeToElement(new { title = "Codex", body = "Waiting" }, AgentMuxJson.Options)
        };

        var json = JsonSerializer.Serialize(request, AgentMuxJson.Options);
        var parsed = JsonSerializer.Deserialize<AgentMuxRequest>(json, AgentMuxJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal("req-1", parsed.Id);
        Assert.Equal(AgentMuxMethods.Notify, parsed.Method);
        Assert.Equal("Codex", parsed.Params?.GetProperty("title").GetString());
    }

    [Fact]
    public void PipeNameIsUserScoped()
    {
        var pipe = AgentMuxPipe.ForCurrentUser();

        Assert.StartsWith("agentmux-", pipe, StringComparison.Ordinal);
        Assert.DoesNotContain('\\', pipe);
        Assert.DoesNotContain('/', pipe);
    }

    [Fact]
    public void NamedPipeStreamsUseCurrentUserOption()
    {
        Assert.True(ReadPipeOptions(typeof(NamedPipeRpcServer), "ServerPipeOptions").HasFlag(PipeOptions.CurrentUserOnly));
        Assert.True(ReadPipeOptions(typeof(NamedPipeRpcClient), "ClientPipeOptions").HasFlag(PipeOptions.CurrentUserOnly));
    }

    private static PipeOptions ReadPipeOptions(Type type, string fieldName)
    {
        var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return (PipeOptions)field.GetRawConstantValue()!;
    }

    [Fact]
    public void SendKeyMethodIsStable()
    {
        Assert.Equal("surface.send_key", AgentMuxMethods.SendKey);
    }

    [Fact]
    public void ResizeTerminalMethodIsStable()
    {
        Assert.Equal("surface.resize_terminal", AgentMuxMethods.ResizeTerminal);
    }

    [Fact]
    public void ReadScreenMethodIsStable()
    {
        Assert.Equal("surface.read_screen", AgentMuxMethods.ReadScreen);
    }

    [Fact]
    public void FocusPaneMethodIsStable()
    {
        Assert.Equal("surface.focus_pane", AgentMuxMethods.FocusPane);
    }

    [Fact]
    public void PaneActionMethodsAreStable()
    {
        Assert.Equal("surface.toggle_zoom", AgentMuxMethods.ToggleZoom);
        Assert.Equal("surface.close_pane", AgentMuxMethods.ClosePane);
    }

    [Fact]
    public void OpenUrlMethodIsStable()
    {
        Assert.Equal("surface.open_url", AgentMuxMethods.OpenUrl);
    }

    [Fact]
    public void BrowserAutomationMethodsAreStable()
    {
        Assert.Equal("browser.back", AgentMuxMethods.BrowserBack);
        Assert.Equal("browser.forward", AgentMuxMethods.BrowserForward);
        Assert.Equal("browser.reload", AgentMuxMethods.BrowserReload);
        Assert.Equal("browser.url.get", AgentMuxMethods.BrowserGetUrl);
        Assert.Equal("browser.get.text", AgentMuxMethods.BrowserGetText);
        Assert.Equal("browser.get.html", AgentMuxMethods.BrowserGetHtml);
        Assert.Equal("browser.get.value", AgentMuxMethods.BrowserGetValue);
        Assert.Equal("browser.get.attr", AgentMuxMethods.BrowserGetAttribute);
        Assert.Equal("browser.get.count", AgentMuxMethods.BrowserGetCount);
        Assert.Equal("browser.get.box", AgentMuxMethods.BrowserGetBox);
        Assert.Equal("browser.get.styles", AgentMuxMethods.BrowserGetStyle);
        Assert.Equal("browser.get.title", AgentMuxMethods.BrowserGetTitle);
        Assert.Equal("surface.eval_js", AgentMuxMethods.BrowserEval);
        Assert.Equal("surface.browser_text", AgentMuxMethods.BrowserText);
        Assert.Equal("surface.click_selector", AgentMuxMethods.BrowserClick);
        Assert.Equal("surface.fill_selector", AgentMuxMethods.BrowserFill);
        Assert.Equal("surface.browser_type_text", AgentMuxMethods.BrowserType);
        Assert.Equal("surface.browser_press_key", AgentMuxMethods.BrowserPress);
        Assert.Equal("surface.capture_screenshot", AgentMuxMethods.BrowserScreenshot);
        Assert.Equal("surface.browser_frame_tree", AgentMuxMethods.BrowserFrameTree);
        Assert.Equal("surface.browser_wait_for_selector", AgentMuxMethods.BrowserWaitForSelector);
        Assert.Equal("surface.browser_wait_for_load", AgentMuxMethods.BrowserWaitForLoad);
        Assert.Equal("surface.browser_console_log", AgentMuxMethods.BrowserConsoleLog);
        Assert.Equal("surface.browser_clear_console_log", AgentMuxMethods.BrowserConsoleClear);
        Assert.Equal("surface.browser_network_log", AgentMuxMethods.BrowserNetworkLog);
        Assert.Equal("surface.browser_clear_network_log", AgentMuxMethods.BrowserNetworkClear);
        Assert.Equal("surface.browser_response_body", AgentMuxMethods.BrowserResponseBody);
        Assert.Equal("surface.browser_har_metadata", AgentMuxMethods.BrowserHarMetadata);
        Assert.Equal("surface.browser_downloads", AgentMuxMethods.BrowserDownloads);
        Assert.Equal("surface.browser_clear_downloads", AgentMuxMethods.BrowserDownloadsClear);
        Assert.Equal("surface.browser_route_list", AgentMuxMethods.BrowserRouteList);
        Assert.Equal("surface.browser_route_block", AgentMuxMethods.BrowserRouteBlock);
        Assert.Equal("surface.browser_route_fulfill", AgentMuxMethods.BrowserRouteFulfill);
        Assert.Equal("surface.browser_route_clear", AgentMuxMethods.BrowserRouteClear);
        Assert.Equal("surface.browser_trace", AgentMuxMethods.BrowserTrace);
    }

    [Fact]
    public void NotificationMethodsAreStable()
    {
        Assert.Equal("notifications.list", AgentMuxMethods.NotificationsList);
        Assert.Equal("notifications.clear", AgentMuxMethods.NotificationsClear);
        Assert.Equal("notifications.jump_latest", AgentMuxMethods.NotificationsJumpLatest);
    }

    [Fact]
    public void WorkspaceMethodsAreStable()
    {
        Assert.Equal("workspace.list", AgentMuxMethods.WorkspaceList);
        Assert.Equal("workspace.create", AgentMuxMethods.WorkspaceCreate);
        Assert.Equal("workspace.select", AgentMuxMethods.WorkspaceSelect);
        Assert.Equal("workspace.set_ports", AgentMuxMethods.WorkspaceSetPorts);
        Assert.Equal("workspace.set_pull_request", AgentMuxMethods.WorkspaceSetPullRequest);
        Assert.Equal("workspace.log", AgentMuxMethods.WorkspaceLog);
        Assert.Equal("workspace.list_log", AgentMuxMethods.WorkspaceListLog);
        Assert.Equal("workspace.clear_log", AgentMuxMethods.WorkspaceClearLog);
        Assert.Equal("workspace.set_status", AgentMuxMethods.WorkspaceSetStatus);
        Assert.Equal("workspace.list_status", AgentMuxMethods.WorkspaceListStatus);
        Assert.Equal("workspace.clear_status", AgentMuxMethods.WorkspaceClearStatus);
        Assert.Equal("workspace.set_progress", AgentMuxMethods.WorkspaceSetProgress);
        Assert.Equal("workspace.clear_progress", AgentMuxMethods.WorkspaceClearProgress);
    }

    [Fact]
    public void SurfaceMethodsAreStable()
    {
        Assert.Equal("surface.list", AgentMuxMethods.SurfaceList);
        Assert.Equal("surface.create", AgentMuxMethods.SurfaceCreate);
        Assert.Equal("surface.select", AgentMuxMethods.SurfaceSelect);
    }
}
