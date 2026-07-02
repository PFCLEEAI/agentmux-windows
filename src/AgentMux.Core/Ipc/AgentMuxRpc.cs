using System.Text.Json;

namespace AgentMux.Core.Ipc;

public static class AgentMuxPipe
{
    public const string DefaultName = "agentmux";

    public static string ForCurrentUser()
    {
        var user = Environment.UserName.Replace('\\', '_').Replace('/', '_');
        return $"{DefaultName}-{user}";
    }
}

public sealed class AgentMuxRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Method { get; set; } = "";
    public JsonElement? Params { get; set; }
}

public sealed class AgentMuxResponse
{
    public string Id { get; set; } = "";
    public bool Ok { get; set; }
    public object? Result { get; set; }
    public string? Error { get; set; }

    public static AgentMuxResponse Success(string id, object? result = null) => new()
    {
        Id = id,
        Ok = true,
        Result = result
    };

    public static AgentMuxResponse Failure(string id, string error) => new()
    {
        Id = id,
        Ok = false,
        Error = error
    };
}

public static class AgentMuxMethods
{
    public const string Ping = "ping";
    public const string Status = "status";
    public const string Tree = "tree";
    public const string Notify = "notify";
    public const string NotificationsList = "notifications.list";
    public const string NotificationsClear = "notifications.clear";
    public const string NotificationsJumpLatest = "notifications.jump_latest";
    public const string WorkspaceList = "workspace.list";
    public const string WorkspaceCreate = "workspace.create";
    public const string WorkspaceSelect = "workspace.select";
    public const string SurfaceList = "surface.list";
    public const string SurfaceCreate = "surface.create";
    public const string SurfaceSelect = "surface.select";
    public const string Split = "split";
    public const string SendText = "surface.send_text";
    public const string SendKey = "surface.send_key";
    public const string ResizeTerminal = "surface.resize_terminal";
    public const string ReadScreen = "surface.read_screen";
    public const string FocusPane = "surface.focus_pane";
    public const string ToggleZoom = "surface.toggle_zoom";
    public const string ClosePane = "surface.close_pane";
    public const string OpenUrl = "surface.open_url";
    public const string BrowserEval = "surface.eval_js";
    public const string BrowserText = "surface.browser_text";
    public const string BrowserClick = "surface.click_selector";
    public const string BrowserFill = "surface.fill_selector";
    public const string BrowserType = "surface.browser_type_text";
    public const string BrowserPress = "surface.browser_press_key";
    public const string BrowserScreenshot = "surface.capture_screenshot";
    public const string BrowserFrameTree = "surface.browser_frame_tree";
    public const string BrowserWaitForSelector = "surface.browser_wait_for_selector";
    public const string BrowserWaitForLoad = "surface.browser_wait_for_load";
    public const string BrowserConsoleLog = "surface.browser_console_log";
    public const string BrowserConsoleClear = "surface.browser_clear_console_log";
    public const string BrowserNetworkLog = "surface.browser_network_log";
    public const string BrowserNetworkClear = "surface.browser_clear_network_log";
    public const string BrowserResponseBody = "surface.browser_response_body";
    public const string BrowserHarMetadata = "surface.browser_har_metadata";
    public const string BrowserDownloads = "surface.browser_downloads";
    public const string BrowserDownloadsClear = "surface.browser_clear_downloads";
    public const string BrowserRouteList = "surface.browser_route_list";
    public const string BrowserRouteBlock = "surface.browser_route_block";
    public const string BrowserRouteFulfill = "surface.browser_route_fulfill";
    public const string BrowserRouteClear = "surface.browser_route_clear";
    public const string BrowserTrace = "surface.browser_trace";
}

public static class AgentMuxJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}
