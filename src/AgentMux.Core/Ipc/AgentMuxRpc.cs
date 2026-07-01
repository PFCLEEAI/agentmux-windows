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
    public const string WorkspaceList = "workspace.list";
    public const string WorkspaceCreate = "workspace.create";
    public const string WorkspaceSelect = "workspace.select";
    public const string Split = "split";
    public const string SendText = "surface.send_text";
    public const string SendKey = "surface.send_key";
    public const string ReadScreen = "surface.read_screen";
    public const string FocusPane = "surface.focus_pane";
    public const string OpenUrl = "surface.open_url";
    public const string BrowserEval = "surface.eval_js";
    public const string BrowserClick = "surface.click_selector";
    public const string BrowserFill = "surface.fill_selector";
    public const string BrowserScreenshot = "surface.capture_screenshot";
}

public static class AgentMuxJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}
