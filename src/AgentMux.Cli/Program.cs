using System.IO.Pipes;
using System.IO;
using System.Text.Json;
using AgentMux.Core.Ipc;
using AgentMux.Core.Models;

namespace AgentMux.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var client = new NamedPipeRpcClient(Environment.GetEnvironmentVariable("AGENTMUX_PIPE"));

        try
        {
            var response = command switch
            {
                "ping" => await client.SendAsync(AgentMuxMethods.Ping).ConfigureAwait(false),
                "status" => await client.SendAsync(AgentMuxMethods.Status).ConfigureAwait(false),
                "tree" => await client.SendAsync(AgentMuxMethods.Tree).ConfigureAwait(false),
                "notify" => await client.SendAsync(AgentMuxMethods.Notify, ParseNotify(args[1..])).ConfigureAwait(false),
                "workspace" => await HandleWorkspaceAsync(client, args[1..]).ConfigureAwait(false),
                "split" => await HandleSplitAsync(client, args[1..]).ConfigureAwait(false),
                "focus" => await HandleFocusAsync(client, args[1..]).ConfigureAwait(false),
                "zoom" => await HandlePaneAsync(client, ["zoom"]).ConfigureAwait(false),
                "close-pane" => await HandlePaneAsync(client, ["close"]).ConfigureAwait(false),
                "pane" => await HandlePaneAsync(client, args[1..]).ConfigureAwait(false),
                "open-url" => await HandleOpenUrlAsync(client, args[1..]).ConfigureAwait(false),
                "browser" or "browse" or "open" => await HandleBrowserAsync(client, args[1..]).ConfigureAwait(false),
                "send" => await client.SendAsync(AgentMuxMethods.SendText, new { text = string.Join(' ', args[1..]) }).ConfigureAwait(false),
                "send-key" => await client.SendAsync(AgentMuxMethods.SendKey, ParseSendKey(args[1..])).ConfigureAwait(false),
                "read-screen" => await client.SendAsync(AgentMuxMethods.ReadScreen, ParseNamed(args[1..])).ConfigureAwait(false),
                _ => AgentMuxResponse.Failure("", $"Unknown command: {command}")
            };

            PrintResponse(response);
            return response.Ok ? 0 : 1;
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or IOException)
        {
            Console.Error.WriteLine($"agentmux: could not reach the running app ({ex.Message})");
            return 1;
        }
    }

    private static async Task<AgentMuxResponse> HandleWorkspaceAsync(NamedPipeRpcClient client, string[] args)
    {
        if (args.Length == 0)
        {
            return AgentMuxResponse.Failure("", "Usage: agentmux workspace <list|create|select>");
        }

        return args[0].ToLowerInvariant() switch
        {
            "list" or "ls" => await client.SendAsync(AgentMuxMethods.WorkspaceList).ConfigureAwait(false),
            "create" or "new" => await client.SendAsync(AgentMuxMethods.WorkspaceCreate, ParseNamed(args[1..])).ConfigureAwait(false),
            "select" => await client.SendAsync(AgentMuxMethods.WorkspaceSelect, ParseNamed(args[1..])).ConfigureAwait(false),
            _ => AgentMuxResponse.Failure("", $"Unknown workspace command: {args[0]}")
        };
    }

    private static async Task<AgentMuxResponse> HandleSplitAsync(NamedPipeRpcClient client, string[] args)
    {
        if (args.Length == 0)
        {
            return AgentMuxResponse.Failure("", "Usage: agentmux split <right|down>");
        }

        return await client.SendAsync(AgentMuxMethods.Split, new { direction = args[0] }).ConfigureAwait(false);
    }

    private static async Task<AgentMuxResponse> HandleBrowserAsync(NamedPipeRpcClient client, string[] args)
    {
        var request = ParseBrowserRequestForTests(args, out var error);
        if (request is null)
        {
            return AgentMuxResponse.Failure("", error);
        }

        return await client.SendAsync(request.Method, request.Parameters).ConfigureAwait(false);
    }

    private static async Task<AgentMuxResponse> HandleFocusAsync(NamedPipeRpcClient client, string[] args)
    {
        var request = ParseFocusRequestForTests(args, out var error);
        if (request is null)
        {
            return AgentMuxResponse.Failure("", error);
        }

        return await client.SendAsync(request.Method, request.Parameters).ConfigureAwait(false);
    }

    private static async Task<AgentMuxResponse> HandlePaneAsync(NamedPipeRpcClient client, string[] args)
    {
        var request = ParsePaneRequestForTests(args, out var error);
        if (request is null)
        {
            return AgentMuxResponse.Failure("", error);
        }

        return await client.SendAsync(request.Method, request.Parameters).ConfigureAwait(false);
    }

    internal static CliRequest? ParseBrowserRequestForTests(string[] args, out string error)
    {
        if (args.Length == 0)
        {
            error = "Usage: agentmux browser open <url>";
            return null;
        }

        if (args[0].Equals("open", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("navigate", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length == 1)
            {
                error = "Usage: agentmux browser open <url>";
                return null;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.OpenUrl, ParseOpenUrl(args[1..]));
        }

        if (args[0].Equals("eval", StringComparison.OrdinalIgnoreCase))
        {
            var named = ParseNamed(args[1..]);
            var script = NamedOrJoined(named, "script");
            if (string.IsNullOrWhiteSpace(script))
            {
                error = "Usage: agentmux browser eval <script>";
                return null;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserEval, new { script });
        }

        if (args[0].Equals("click", StringComparison.OrdinalIgnoreCase))
        {
            var named = ParseNamed(args[1..]);
            var selector = NamedOrJoined(named, "selector");
            if (string.IsNullOrWhiteSpace(selector))
            {
                error = "Usage: agentmux browser click [--frame <name-or-id>] <selector>";
                return null;
            }

            if (!TryReadOptionalFrame(named, "Usage: agentmux browser click [--frame <name-or-id>] <selector>", out var frame, out error))
            {
                return null;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserClick, new { selector, frame });
        }

        if (args[0].Equals("fill", StringComparison.OrdinalIgnoreCase))
        {
            var named = ParseNamed(args[1..]);
            var selectorWasNamed = named.ContainsKey("selector");
            var selector = NamedOrFirst(named, "selector");
            var text = NamedOrRemaining(named, "text", selectorWasNamed ? 0 : 1);
            if (string.IsNullOrWhiteSpace(selector))
            {
                error = "Usage: agentmux browser fill [--frame <name-or-id>] <selector> <text>";
                return null;
            }

            if (!TryReadOptionalFrame(named, "Usage: agentmux browser fill [--frame <name-or-id>] <selector> <text>", out var frame, out error))
            {
                return null;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserFill, new { selector, text = text ?? "", frame });
        }

        if (args[0].Equals("type", StringComparison.OrdinalIgnoreCase))
        {
            var named = ParseNamed(args[1..]);
            var selectorWasNamed = named.ContainsKey("selector");
            var selector = NamedOrFirst(named, "selector");
            var text = NamedOrRemaining(named, "text", selectorWasNamed ? 0 : 1);
            if (string.IsNullOrWhiteSpace(selector) || text is null)
            {
                error = "Usage: agentmux browser type [--frame <name-or-id>] <selector> <text>";
                return null;
            }

            if (!TryReadOptionalFrame(named, "Usage: agentmux browser type [--frame <name-or-id>] <selector> <text>", out var frame, out error))
            {
                return null;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserType, new { selector, text, frame });
        }

        if (args[0].Equals("press", StringComparison.OrdinalIgnoreCase))
        {
            var named = ParseNamed(args[1..]);
            var key = NamedOrFirst(named, "key");
            named.TryGetValue("selector", out var selector);
            if (string.IsNullOrWhiteSpace(key))
            {
                error = "Usage: agentmux browser press <key> [--selector <selector> [--frame <name-or-id>]]";
                return null;
            }

            if (!TryReadOptionalFrame(named, "Usage: agentmux browser press <key> [--selector <selector> [--frame <name-or-id>]]", out var frame, out error))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(frame) && string.IsNullOrWhiteSpace(selector))
            {
                error = "Usage: agentmux browser press <key> [--selector <selector> [--frame <name-or-id>]]";
                return null;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserPress, new { key, selector, frame });
        }

        if (args[0].Equals("screenshot", StringComparison.OrdinalIgnoreCase))
        {
            var named = ParseNamed(args[1..]);
            var path = NamedOrFirst(named, "path");
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Usage: agentmux browser screenshot <path>";
                return null;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserScreenshot, new { path = Path.GetFullPath(path) });
        }

        if (args[0].Equals("frames", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("frame-tree", StringComparison.OrdinalIgnoreCase))
        {
            error = "";
            return new CliRequest(AgentMuxMethods.BrowserFrameTree, new { });
        }

        if (args[0].Equals("wait-for-selector", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("wait", StringComparison.OrdinalIgnoreCase))
        {
            const string usage = "Usage: agentmux browser wait-for-selector <selector> [--state <visible|attached|hidden>] [--timeout-ms <ms>] [--frame <name-or-id>]";
            var named = ParseNamed(args[1..]);
            var selector = NamedOrJoined(named, "selector");
            if (string.IsNullOrWhiteSpace(selector))
            {
                error = usage;
                return null;
            }

            if (!TryReadOptionalFrame(named, usage, out var frame, out error))
            {
                return null;
            }

            named.TryGetValue("state", out var state);
            if (!string.IsNullOrWhiteSpace(state) && !IsBrowserWaitState(state))
            {
                error = usage;
                return null;
            }

            int? timeoutMs = null;
            if (named.TryGetValue("timeout-ms", out var timeoutValue))
            {
                if (!TryParsePositiveInt(timeoutValue, out var parsedTimeout))
                {
                    error = usage;
                    return null;
                }

                timeoutMs = parsedTimeout;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserWaitForSelector, new { selector, state, timeoutMs, frame });
        }

        if (args[0].Equals("console", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("console-log", StringComparison.OrdinalIgnoreCase))
        {
            var named = ParseNamed(args[1..]);
            int? limit = null;
            if (named.TryGetValue("limit", out var limitValue))
            {
                if (!TryParsePositiveInt(limitValue, out var parsedLimit))
                {
                    error = "Usage: agentmux browser console [--limit <count>]";
                    return null;
                }

                limit = parsedLimit;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserConsoleLog, new { limit });
        }

        if (args[0].Equals("console-clear", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("clear-console", StringComparison.OrdinalIgnoreCase))
        {
            error = "";
            return new CliRequest(AgentMuxMethods.BrowserConsoleClear, new { });
        }

        if (args[0].Equals("network", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("network-log", StringComparison.OrdinalIgnoreCase))
        {
            var named = ParseNamed(args[1..]);
            int? limit = null;
            if (named.TryGetValue("limit", out var limitValue))
            {
                if (!TryParsePositiveInt(limitValue, out var parsedLimit))
                {
                    error = "Usage: agentmux browser network [--limit <count>]";
                    return null;
                }

                limit = parsedLimit;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserNetworkLog, new { limit });
        }

        if (args[0].Equals("network-clear", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("clear-network", StringComparison.OrdinalIgnoreCase))
        {
            error = "";
            return new CliRequest(AgentMuxMethods.BrowserNetworkClear, new { });
        }

        if (args[0].Equals("response-body", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("body", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
            {
                error = "Usage: agentmux browser response-body <request-id>";
                return null;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserResponseBody, new { requestId = args[1] });
        }

        if (args[0].Equals("har", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("har-metadata", StringComparison.OrdinalIgnoreCase))
        {
            var named = ParseNamed(args[1..]);
            var path = NamedOrFirst(named, "path");
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Usage: agentmux browser har <path>";
                return null;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserHarMetadata, new { path = Path.GetFullPath(path) });
        }

        if (args[0].Equals("downloads", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("download-log", StringComparison.OrdinalIgnoreCase))
        {
            var named = ParseNamed(args[1..]);
            int? limit = null;
            if (named.TryGetValue("limit", out var limitValue))
            {
                if (!TryParsePositiveInt(limitValue, out var parsedLimit))
                {
                    error = "Usage: agentmux browser downloads [--limit <count>]";
                    return null;
                }

                limit = parsedLimit;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserDownloads, new { limit });
        }

        if (args[0].Equals("downloads-clear", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("clear-downloads", StringComparison.OrdinalIgnoreCase))
        {
            error = "";
            return new CliRequest(AgentMuxMethods.BrowserDownloadsClear, new { });
        }

        if (args.Length == 1)
        {
            error = "";
            return new CliRequest(AgentMuxMethods.OpenUrl, ParseOpenUrl(args));
        }

        error = $"Unknown browser command: {args[0]}";
        return null;
    }

    internal static CliRequest? ParseFocusRequestForTests(string[] args, out string error)
    {
        var named = ParseNamed(args);
        var direction = NamedOrFirst(named, "direction");
        if (!PaneFocusNavigator.TryParseDirection(direction, out var parsedDirection))
        {
            error = "Usage: agentmux focus <next|previous|left|right|up|down>";
            return null;
        }

        error = "";
        return new CliRequest(AgentMuxMethods.FocusPane, new { direction = parsedDirection.ToString().ToLowerInvariant() });
    }

    internal static CliRequest? ParsePaneRequestForTests(string[] args, out string error)
    {
        if (args.Length == 0)
        {
            error = "Usage: agentmux pane <zoom|close|resize>";
            return null;
        }

        if (args[0].Equals("zoom", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("toggle-zoom", StringComparison.OrdinalIgnoreCase))
        {
            error = "";
            return new CliRequest(AgentMuxMethods.ToggleZoom, new { });
        }

        if (args[0].Equals("close", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("close-pane", StringComparison.OrdinalIgnoreCase))
        {
            error = "";
            return new CliRequest(AgentMuxMethods.ClosePane, new { });
        }

        if (args[0].Equals("resize", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("resize-terminal", StringComparison.OrdinalIgnoreCase))
        {
            var named = ParseNamed(args[1..]);
            var colsValue = NamedOrFirst(named, "cols");
            var rowsValue = named.TryGetValue("rows", out var namedRows)
                ? namedRows
                : named.TryGetValue("_arg1", out var second)
                    ? second
                    : null;

            if (!TryParsePositiveInt(colsValue, out var cols)
                || !TryParsePositiveInt(rowsValue, out var rows))
            {
                error = "Usage: agentmux pane resize --cols <cols> --rows <rows>";
                return null;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.ResizeTerminal, new { cols, rows });
        }

        error = $"Unknown pane command: {args[0]}";
        return null;
    }

    private static async Task<AgentMuxResponse> HandleOpenUrlAsync(NamedPipeRpcClient client, string[] args)
    {
        if (args.Length == 0)
        {
            return AgentMuxResponse.Failure("", "Usage: agentmux open-url <url>");
        }

        return await client.SendAsync(AgentMuxMethods.OpenUrl, ParseOpenUrl(args)).ConfigureAwait(false);
    }

    private static object ParseOpenUrl(string[] args)
    {
        var named = ParseNamed(args);
        if (!named.ContainsKey("url") && named.TryGetValue("_arg0", out var positional))
        {
            named["url"] = positional;
        }

        return named;
    }

    private static string? NamedOrJoined(Dictionary<string, string> named, string key)
    {
        return named.TryGetValue(key, out var value) ? value : NamedOrRemaining(named, key, 0);
    }

    private static string? NamedOrFirst(Dictionary<string, string> named, string key)
    {
        return named.TryGetValue(key, out var value)
            ? value
            : named.TryGetValue("_arg0", out var first)
                ? first
                : null;
    }

    private static string? NamedOrRemaining(Dictionary<string, string> named, string key, int skip)
    {
        if (named.TryGetValue(key, out var value))
        {
            return value;
        }

        var values = new List<string>();
        for (var index = skip; named.TryGetValue($"_arg{index}", out var arg); index++)
        {
            values.Add(arg);
        }

        return values.Count == 0 ? null : string.Join(' ', values);
    }

    private static bool TryReadOptionalFrame(Dictionary<string, string> named, string usage, out string? frame, out string error)
    {
        frame = null;
        error = "";
        if (!named.TryGetValue("frame", out var value))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            error = usage;
            return false;
        }

        frame = value;
        return true;
    }

    private static object ParseNotify(string[] args)
    {
        var named = ParseNamed(args);
        named.TryGetValue("title", out var title);
        named.TryGetValue("body", out var body);
        named.TryGetValue("subtitle", out var subtitle);

        if (string.IsNullOrWhiteSpace(body) && named.TryGetValue("_arg0", out var first))
        {
            body = first;
        }

        return new
        {
            title = string.IsNullOrWhiteSpace(title) ? "Terminal" : title,
            subtitle,
            body = body ?? ""
        };
    }

    private static object ParseSendKey(string[] args)
    {
        var named = ParseNamed(args);
        if (!named.ContainsKey("key") && named.TryGetValue("_arg0", out var positional))
        {
            named["key"] = positional;
        }

        return named;
    }

    private static bool TryParsePositiveInt(string? value, out int number)
    {
        return int.TryParse(value, out number) && number > 0;
    }

    private static bool IsBrowserWaitState(string value)
    {
        return value.Equals("visible", StringComparison.OrdinalIgnoreCase)
            || value.Equals("attached", StringComparison.OrdinalIgnoreCase)
            || value.Equals("hidden", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ParseNamed(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positional = 0;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var key = arg[2..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    result[key] = args[++i];
                }
                else
                {
                    result[key] = "true";
                }
            }
            else
            {
                result[$"_arg{positional++}"] = arg;
            }
        }

        return result;
    }

    private static void PrintResponse(AgentMuxResponse response)
    {
        if (!response.Ok)
        {
            Console.Error.WriteLine(response.Error);
            return;
        }

        Console.WriteLine(JsonSerializer.Serialize(response.Result ?? new { ok = true }, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static bool IsHelp(string value) => value is "-h" or "--help" or "help";

    internal sealed record CliRequest(string Method, object Parameters);

    private static void PrintHelp()
    {
        Console.WriteLine("""
        agentmux - CLI for AgentMux Windows

        Usage:
          agentmux ping
          agentmux status
          agentmux tree
          agentmux notify --title "Codex" --body "Waiting"
          agentmux workspace list
          agentmux workspace create --title "API" --cwd "C:\src\api"
          agentmux workspace select --index 0
          agentmux split right
          agentmux split down
          agentmux focus next
          agentmux focus right
          agentmux zoom
          agentmux close-pane
          agentmux pane resize --cols 100 --rows 30
          agentmux open-url https://example.com
          agentmux browser open https://example.com
          agentmux browser eval "document.title"
          agentmux browser click "#submit"
          agentmux browser click --frame agentmux-child-frame "#submit"
          agentmux browser fill "#prompt" "write tests"
          agentmux browser type "#prompt" "write tests"
          agentmux browser press Enter --selector "#prompt" --frame agentmux-child-frame
          agentmux browser screenshot .\browser.png
          agentmux browser frames
          agentmux browser wait-for-selector "#ready" --timeout-ms 5000
          agentmux browser console --limit 20
          agentmux browser console-clear
          agentmux browser network --limit 20
          agentmux browser network-clear
          agentmux browser response-body <request-id>
          agentmux browser har-metadata .\network.har.json
          agentmux browser downloads --limit 20
          agentmux browser downloads-clear
          agentmux send "npm test"
          agentmux send-key Enter
          agentmux read-screen --lines 50
        """);
    }
}
