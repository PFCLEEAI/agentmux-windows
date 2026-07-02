using System.Globalization;
using System.IO;
using System.IO.Pipes;
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
                "notifications" or "notification" => await HandleNotificationsAsync(client, args[1..]).ConfigureAwait(false),
                "log" => await HandleWorkspaceLogAsync(client, args[1..]).ConfigureAwait(false),
                "list-log" => await HandleWorkspaceListLogAsync(client, args[1..]).ConfigureAwait(false),
                "clear-log" => await HandleWorkspaceClearLogAsync(client, args[1..]).ConfigureAwait(false),
                "set-status" => await HandleWorkspaceSetStatusAsync(client, args[1..]).ConfigureAwait(false),
                "list-status" => await HandleWorkspaceListStatusAsync(client, args[1..]).ConfigureAwait(false),
                "clear-status" => await HandleWorkspaceClearStatusAsync(client, args[1..]).ConfigureAwait(false),
                "set-progress" => await HandleWorkspaceSetProgressAsync(client, args[1..]).ConfigureAwait(false),
                "clear-progress" => await HandleWorkspaceClearProgressAsync(client, args[1..]).ConfigureAwait(false),
                "workspace" => await HandleWorkspaceAsync(client, args[1..]).ConfigureAwait(false),
                "surface" or "surfaces" or "tab" or "tabs" => await HandleSurfaceAsync(client, args[1..]).ConfigureAwait(false),
                "split" => await HandleSplitAsync(client, args[1..]).ConfigureAwait(false),
                "focus" => await HandleFocusAsync(client, args[1..]).ConfigureAwait(false),
                "zoom" => await HandlePaneAsync(client, ["zoom"]).ConfigureAwait(false),
                "close-pane" => await HandlePaneAsync(client, ["close"]).ConfigureAwait(false),
                "pane" => await HandlePaneAsync(client, args[1..]).ConfigureAwait(false),
                "open-url" => await HandleOpenUrlAsync(client, args[1..]).ConfigureAwait(false),
                "browser" or "browse" or "open" => await HandleBrowserAsync(client, args[1..]).ConfigureAwait(false),
                "send" => await client.SendAsync(AgentMuxMethods.SendText, new { text = string.Join(' ', args[1..]) }).ConfigureAwait(false),
                "send-key" => await client.SendAsync(AgentMuxMethods.SendKey, ParseSendKey(args[1..])).ConfigureAwait(false),
                "read-screen" => await HandleReadScreenAsync(client, args[1..]).ConfigureAwait(false),
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
        var request = ParseWorkspaceRequestForTests(args, out var error);
        if (request is null)
        {
            return AgentMuxResponse.Failure("", error);
        }

        return await client.SendAsync(request.Method, request.Parameters).ConfigureAwait(false);
    }

    private static async Task<AgentMuxResponse> HandleWorkspaceLogAsync(NamedPipeRpcClient client, string[] args)
    {
        var request = ParseWorkspaceLogRequestForTests(args, out var error);
        if (request is null)
        {
            return AgentMuxResponse.Failure("", error);
        }

        return await client.SendAsync(request.Method, request.Parameters).ConfigureAwait(false);
    }

    private static async Task<AgentMuxResponse> HandleWorkspaceListLogAsync(NamedPipeRpcClient client, string[] args)
    {
        var request = ParseWorkspaceListLogRequestForTests(args, out var error);
        if (request is null)
        {
            return AgentMuxResponse.Failure("", error);
        }

        return await client.SendAsync(request.Method, request.Parameters).ConfigureAwait(false);
    }

    private static async Task<AgentMuxResponse> HandleWorkspaceClearLogAsync(NamedPipeRpcClient client, string[] args)
    {
        var request = ParseWorkspaceClearLogRequestForTests(args, out var error);
        if (request is null)
        {
            return AgentMuxResponse.Failure("", error);
        }

        return await client.SendAsync(request.Method, request.Parameters).ConfigureAwait(false);
    }

    private static async Task<AgentMuxResponse> HandleWorkspaceSetStatusAsync(NamedPipeRpcClient client, string[] args)
    {
        var request = ParseWorkspaceSetStatusRequestForTests(args, out var error);
        if (request is null)
        {
            return AgentMuxResponse.Failure("", error);
        }

        return await client.SendAsync(request.Method, request.Parameters).ConfigureAwait(false);
    }

    private static async Task<AgentMuxResponse> HandleWorkspaceListStatusAsync(NamedPipeRpcClient client, string[] args)
    {
        var request = ParseWorkspaceListStatusRequestForTests(args, out var error);
        if (request is null)
        {
            return AgentMuxResponse.Failure("", error);
        }

        return await client.SendAsync(request.Method, request.Parameters).ConfigureAwait(false);
    }

    private static async Task<AgentMuxResponse> HandleWorkspaceClearStatusAsync(NamedPipeRpcClient client, string[] args)
    {
        var request = ParseWorkspaceClearStatusRequestForTests(args, out var error);
        if (request is null)
        {
            return AgentMuxResponse.Failure("", error);
        }

        return await client.SendAsync(request.Method, request.Parameters).ConfigureAwait(false);
    }

    private static async Task<AgentMuxResponse> HandleWorkspaceSetProgressAsync(NamedPipeRpcClient client, string[] args)
    {
        var request = ParseWorkspaceSetProgressRequestForTests(args, out var error);
        if (request is null)
        {
            return AgentMuxResponse.Failure("", error);
        }

        return await client.SendAsync(request.Method, request.Parameters).ConfigureAwait(false);
    }

    private static async Task<AgentMuxResponse> HandleWorkspaceClearProgressAsync(NamedPipeRpcClient client, string[] args)
    {
        var request = ParseWorkspaceClearProgressRequestForTests(args, out var error);
        if (request is null)
        {
            return AgentMuxResponse.Failure("", error);
        }

        return await client.SendAsync(request.Method, request.Parameters).ConfigureAwait(false);
    }

    private static async Task<AgentMuxResponse> HandleNotificationsAsync(NamedPipeRpcClient client, string[] args)
    {
        var request = ParseNotificationsRequestForTests(args, out var error);
        if (request is null)
        {
            return AgentMuxResponse.Failure("", error);
        }

        return await client.SendAsync(request.Method, request.Parameters).ConfigureAwait(false);
    }

    private static async Task<AgentMuxResponse> HandleSurfaceAsync(NamedPipeRpcClient client, string[] args)
    {
        var request = ParseSurfaceRequestForTests(args, out var error);
        if (request is null)
        {
            return AgentMuxResponse.Failure("", error);
        }

        return await client.SendAsync(request.Method, request.Parameters).ConfigureAwait(false);
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

    private static async Task<AgentMuxResponse> HandleReadScreenAsync(NamedPipeRpcClient client, string[] args)
    {
        var request = ParseReadScreenRequestForTests(args, out var error);
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

        if (args[0].Equals("text", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("inner-text", StringComparison.OrdinalIgnoreCase))
        {
            const string usage = "Usage: agentmux browser text [--selector <css>] [--frame <name-or-id>] [--max-chars <count>]";
            var named = ParseNamed(args[1..]);
            var selector = NamedOrJoined(named, "selector");
            if (!TryReadOptionalFrame(named, usage, out var frame, out error))
            {
                return null;
            }

            int? maxChars = null;
            if (named.TryGetValue("max-chars", out var maxCharsValue))
            {
                if (!TryParsePositiveInt(maxCharsValue, out var parsedMaxChars))
                {
                    error = usage;
                    return null;
                }

                maxChars = parsedMaxChars;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserText, new { selector, frame, maxChars });
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

        if (args[0].Equals("wait-load", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("wait-load-state", StringComparison.OrdinalIgnoreCase))
        {
            const string usage = "Usage: agentmux browser wait-load [--state <domcontentloaded|load|network-idle>] [--timeout-ms <ms>]";
            var named = ParseNamed(args[1..]);
            named.TryGetValue("state", out var state);
            if (!string.IsNullOrWhiteSpace(state) && !IsBrowserLoadState(state))
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
            return new CliRequest(AgentMuxMethods.BrowserWaitForLoad, new { state, timeoutMs });
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

        if (args[0].Equals("trace", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("tracing", StringComparison.OrdinalIgnoreCase))
        {
            const string usage = "Usage: agentmux browser trace <path> [--duration-ms <ms>] [--max-bytes <bytes>]";
            var named = ParseNamed(args[1..]);
            var path = NamedOrFirst(named, "path");
            if (string.IsNullOrWhiteSpace(path))
            {
                error = usage;
                return null;
            }

            int? durationMs = null;
            if (named.TryGetValue("duration-ms", out var durationValue))
            {
                if (!TryParsePositiveInt(durationValue, out var parsedDuration))
                {
                    error = usage;
                    return null;
                }

                durationMs = parsedDuration;
            }

            int? maxBytes = null;
            if (named.TryGetValue("max-bytes", out var maxBytesValue))
            {
                if (!TryParsePositiveInt(maxBytesValue, out var parsedMaxBytes))
                {
                    error = usage;
                    return null;
                }

                maxBytes = parsedMaxBytes;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserTrace, new { path = Path.GetFullPath(path), durationMs, maxBytes });
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

        if (args[0].Equals("route", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("routes", StringComparison.OrdinalIgnoreCase))
        {
            return ParseBrowserRouteRequest(args[1..], out error);
        }

        if (args.Length == 1)
        {
            error = "";
            return new CliRequest(AgentMuxMethods.OpenUrl, ParseOpenUrl(args));
        }

        error = $"Unknown browser command: {args[0]}";
        return null;
    }

    private static CliRequest? ParseBrowserRouteRequest(string[] args, out string error)
    {
        const string usage = "Usage: agentmux browser route <list|block|fulfill|clear>";
        if (args.Length == 0)
        {
            error = usage;
            return null;
        }

        if (args[0].Equals("list", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("ls", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length != 1)
            {
                error = "Usage: agentmux browser route list";
                return null;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserRouteList, new { });
        }

        if (args[0].Equals("clear", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length != 1)
            {
                error = "Usage: agentmux browser route clear";
                return null;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserRouteClear, new { });
        }

        if (args[0].Equals("block", StringComparison.OrdinalIgnoreCase))
        {
            var named = ParseNamed(args[1..]);
            var urlContains = NamedOrFirst(named, "url-contains");
            if (string.IsNullOrWhiteSpace(urlContains))
            {
                error = "Usage: agentmux browser route block --url-contains <text>";
                return null;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserRouteBlock, new { urlContains });
        }

        if (args[0].Equals("fulfill", StringComparison.OrdinalIgnoreCase))
        {
            var named = ParseNamed(args[1..]);
            var urlContains = NamedOrFirst(named, "url-contains");
            if (string.IsNullOrWhiteSpace(urlContains))
            {
                error = "Usage: agentmux browser route fulfill --url-contains <text> [--status <code>] [--content-type <type>] [--body <text>]";
                return null;
            }

            var status = 200;
            if (named.TryGetValue("status", out var statusValue)
                && !TryParseHttpStatus(statusValue, out status))
            {
                error = "Usage: agentmux browser route fulfill --url-contains <text> [--status <code>] [--content-type <type>] [--body <text>]";
                return null;
            }

            var contentType = named.TryGetValue("content-type", out var parsedContentType) && !string.IsNullOrWhiteSpace(parsedContentType)
                ? parsedContentType
                : "text/plain";
            var body = NamedOrRemaining(named, "body", 1) ?? "";

            error = "";
            return new CliRequest(AgentMuxMethods.BrowserRouteFulfill, new { urlContains, status, contentType, body });
        }

        error = usage;
        return null;
    }

    internal static CliRequest? ParseWorkspaceRequestForTests(string[] args, out string error)
    {
        if (args.Length == 0)
        {
            error = "Usage: agentmux workspace <list|create|select>";
            return null;
        }

        if (args[0].Equals("list", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("ls", StringComparison.OrdinalIgnoreCase))
        {
            error = "";
            return new CliRequest(AgentMuxMethods.WorkspaceList, new { });
        }

        if (args[0].Equals("create", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("new", StringComparison.OrdinalIgnoreCase))
        {
            error = "";
            return new CliRequest(AgentMuxMethods.WorkspaceCreate, ParseNamed(args[1..]));
        }

        if (args[0].Equals("select", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("use", StringComparison.OrdinalIgnoreCase))
        {
            var named = ParseNamed(args[1..]);
            if (!named.ContainsKey("index") && named.TryGetValue("_arg0", out var positional))
            {
                named["index"] = positional;
            }

            var hasIndex = named.TryGetValue("index", out var indexValue);
            var hasId = named.TryGetValue("id", out var idValue)
                && !string.IsNullOrWhiteSpace(idValue)
                && !idValue.Equals("true", StringComparison.OrdinalIgnoreCase);
            if (hasIndex && hasId)
            {
                error = "Usage: agentmux workspace select --index <n>|--id <workspace-id>";
                return null;
            }

            if (hasIndex)
            {
                if (!TryParseNonNegativeInt(indexValue, out var index))
                {
                    error = "Usage: agentmux workspace select --index <n>|--id <workspace-id>";
                    return null;
                }

                error = "";
                return new CliRequest(AgentMuxMethods.WorkspaceSelect, new { index });
            }

            if (!hasId)
            {
                error = "Usage: agentmux workspace select --index <n>|--id <workspace-id>";
                return null;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.WorkspaceSelect, new { id = idValue });
        }

        if (args[0].Equals("ports", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("port", StringComparison.OrdinalIgnoreCase))
        {
            const string usage = "Usage: agentmux workspace ports [--index <n>|--id <workspace-id>] <port...>|--ports <csv>|clear";
            var named = ParseNamed(args[1..]);
            if (named.TryGetValue("index", out var indexValue) && !TryParseNonNegativeInt(indexValue, out _))
            {
                error = usage;
                return null;
            }

            var hasIndex = named.ContainsKey("index");
            var hasIdFlag = named.ContainsKey("id");
            var hasId = named.TryGetValue("id", out var idValue)
                && !string.IsNullOrWhiteSpace(idValue)
                && !idValue.Equals("true", StringComparison.OrdinalIgnoreCase);
            if (hasIdFlag && !hasId)
            {
                error = usage;
                return null;
            }

            if (hasIndex && hasId)
            {
                error = usage;
                return null;
            }

            var positional = ReadPositional(named);
            var clear = positional.Any(value => value.Equals("clear", StringComparison.OrdinalIgnoreCase))
                || (named.TryGetValue("clear", out var clearValue) && clearValue.Equals("true", StringComparison.OrdinalIgnoreCase));
            var portText = named.TryGetValue("ports", out var portsValue)
                ? portsValue
                : string.Join(',', positional.Where(value => !value.Equals("clear", StringComparison.OrdinalIgnoreCase)));
            int[] ports = [];
            if (!clear && !TryParsePortList(portText, out ports))
            {
                error = usage;
                return null;
            }

            var parameters = new Dictionary<string, object?>
            {
                ["ports"] = clear ? Array.Empty<int>() : ports
            };
            if (hasIndex)
            {
                parameters["index"] = int.Parse(indexValue!, CultureInfo.InvariantCulture);
            }

            if (hasId)
            {
                parameters["id"] = idValue;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.WorkspaceSetPorts, parameters);
        }

        if (args[0].Equals("pr", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("pull-request", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("pull", StringComparison.OrdinalIgnoreCase))
        {
            const string usage = "Usage: agentmux workspace pr [--index <n>|--id <workspace-id>] <number>|set <number>|--number <n>|clear [--status <unknown|open|draft|merged|closed>] [--url <url>]";
            var named = ParseNamed(args[1..]);
            if (named.TryGetValue("index", out var indexValue) && !TryParseNonNegativeInt(indexValue, out _))
            {
                error = usage;
                return null;
            }

            var hasIndex = named.ContainsKey("index");
            var hasIdFlag = named.ContainsKey("id");
            var hasId = named.TryGetValue("id", out var idValue)
                && !string.IsNullOrWhiteSpace(idValue)
                && !idValue.Equals("true", StringComparison.OrdinalIgnoreCase);
            if (hasIdFlag && !hasId)
            {
                error = usage;
                return null;
            }

            if (hasIndex && hasId)
            {
                error = usage;
                return null;
            }

            var positional = ReadPositional(named);
            var clear = positional.Any(value => value.Equals("clear", StringComparison.OrdinalIgnoreCase))
                || (named.TryGetValue("clear", out var clearValue) && clearValue.Equals("true", StringComparison.OrdinalIgnoreCase));

            var parameters = new Dictionary<string, object?>();
            if (clear)
            {
                var unexpected = named.ContainsKey("number")
                    || named.ContainsKey("status")
                    || named.ContainsKey("url")
                    || positional.Any(value => !value.Equals("clear", StringComparison.OrdinalIgnoreCase));
                if (unexpected)
                {
                    error = usage;
                    return null;
                }

                parameters["clear"] = true;
            }
            else
            {
                var numberTokens = positional
                    .Where(value => !value.Equals("set", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var hasNamedNumber = named.TryGetValue("number", out var parsedNumberValue);
                var numberText = hasNamedNumber
                    ? parsedNumberValue
                    : numberTokens.Length == 1 ? numberTokens[0] : null;
                if (numberText is null
                    || (hasNamedNumber && numberTokens.Length > 0)
                    || numberTokens.Length > 1
                    || !TryParsePullRequestNumber(numberText, out var pullRequestNumber))
                {
                    error = usage;
                    return null;
                }

                parameters["number"] = pullRequestNumber;
                if (named.TryGetValue("status", out var statusValue))
                {
                    if (!TryNormalizePullRequestStatus(statusValue, out var status))
                    {
                        error = usage;
                        return null;
                    }

                    parameters["status"] = status;
                }

                if (named.TryGetValue("url", out var urlValue))
                {
                    if (!TryNormalizePullRequestUrl(urlValue, out var url))
                    {
                        error = usage;
                        return null;
                    }

                    parameters["url"] = url;
                }
            }

            if (hasIndex)
            {
                parameters["index"] = int.Parse(indexValue!, CultureInfo.InvariantCulture);
            }

            if (hasId)
            {
                parameters["id"] = idValue;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.WorkspaceSetPullRequest, parameters);
        }

        error = $"Unknown workspace command: {args[0]}";
        return null;
    }

    internal static CliRequest? ParseWorkspaceLogRequestForTests(string[] args, out string error)
    {
        const string usage = "Usage: agentmux log <message> [--level <info|warn|error|debug>] [--source <text>] [--workspace <id-or-index>]";
        var named = ParseNamed(args);
        var message = NamedOrRemaining(named, "message", 0);
        if (string.IsNullOrWhiteSpace(message)
            || (named.ContainsKey("message") && message.Equals("true", StringComparison.OrdinalIgnoreCase)))
        {
            error = usage;
            return null;
        }

        var level = "info";
        if (named.TryGetValue("level", out var levelValue)
            && !TryNormalizeWorkspaceLogLevel(levelValue, out level))
        {
            error = usage;
            return null;
        }

        var parameters = new Dictionary<string, object?>
        {
            ["message"] = message,
            ["level"] = level
        };

        if (named.TryGetValue("source", out var source))
        {
            if (string.IsNullOrWhiteSpace(source) || source.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                error = usage;
                return null;
            }

            parameters["source"] = source;
        }

        if (!TryAddWorkspaceTarget(named, parameters, usage, out error))
        {
            return null;
        }

        error = "";
        return new CliRequest(AgentMuxMethods.WorkspaceLog, parameters);
    }

    internal static CliRequest? ParseWorkspaceListLogRequestForTests(string[] args, out string error)
    {
        const string usage = "Usage: agentmux list-log [--limit <count>] [--workspace <id-or-index>]";
        var named = ParseNamed(args);
        if (ReadPositional(named).Length > 0)
        {
            error = usage;
            return null;
        }

        var parameters = new Dictionary<string, object?>();
        if (named.TryGetValue("limit", out var limitValue))
        {
            if (!TryParseStrictPositiveInt(limitValue, out var limit))
            {
                error = usage;
                return null;
            }

            parameters["limit"] = limit;
        }

        if (!TryAddWorkspaceTarget(named, parameters, usage, out error))
        {
            return null;
        }

        error = "";
        return new CliRequest(AgentMuxMethods.WorkspaceListLog, parameters);
    }

    internal static CliRequest? ParseWorkspaceClearLogRequestForTests(string[] args, out string error)
    {
        const string usage = "Usage: agentmux clear-log [--workspace <id-or-index>]";
        var named = ParseNamed(args);
        if (ReadPositional(named).Length > 0)
        {
            error = usage;
            return null;
        }

        var parameters = new Dictionary<string, object?>();
        if (!TryAddWorkspaceTarget(named, parameters, usage, out error))
        {
            return null;
        }

        error = "";
        return new CliRequest(AgentMuxMethods.WorkspaceClearLog, parameters);
    }

    internal static CliRequest? ParseWorkspaceSetStatusRequestForTests(string[] args, out string error)
    {
        const string usage = "Usage: agentmux set-status <key> <text> [--icon <text>] [--color <text>] [--workspace <id-or-index>]";
        var named = ParseNamed(args);
        var positionals = ReadPositional(named);
        var keyWasNamed = named.ContainsKey("key");
        var textWasNamed = named.ContainsKey("text");
        if (textWasNamed && positionals.Length > (keyWasNamed ? 0 : 1))
        {
            error = usage;
            return null;
        }

        var key = keyWasNamed
            ? named["key"]
            : positionals.Length > 0 ? positionals[0] : null;
        var text = named.TryGetValue("text", out var namedText)
            ? namedText
            : NamedOrRemaining(named, "text", keyWasNamed ? 0 : 1);

        if (string.IsNullOrWhiteSpace(key)
            || key.Equals("true", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(text)
            || text.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            error = usage;
            return null;
        }

        var parameters = new Dictionary<string, object?>
        {
            ["key"] = key,
            ["text"] = text
        };

        if (named.TryGetValue("icon", out var icon))
        {
            if (string.IsNullOrWhiteSpace(icon) || icon.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                error = usage;
                return null;
            }

            parameters["icon"] = icon;
        }

        if (named.TryGetValue("color", out var color))
        {
            if (string.IsNullOrWhiteSpace(color) || color.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                error = usage;
                return null;
            }

            parameters["color"] = color;
        }

        if (!TryAddWorkspaceTarget(named, parameters, usage, out error))
        {
            return null;
        }

        error = "";
        return new CliRequest(AgentMuxMethods.WorkspaceSetStatus, parameters);
    }

    internal static CliRequest? ParseWorkspaceListStatusRequestForTests(string[] args, out string error)
    {
        const string usage = "Usage: agentmux list-status [--workspace <id-or-index>]";
        var named = ParseNamed(args);
        if (ReadPositional(named).Length > 0)
        {
            error = usage;
            return null;
        }

        var parameters = new Dictionary<string, object?>();
        if (!TryAddWorkspaceTarget(named, parameters, usage, out error))
        {
            return null;
        }

        error = "";
        return new CliRequest(AgentMuxMethods.WorkspaceListStatus, parameters);
    }

    internal static CliRequest? ParseWorkspaceClearStatusRequestForTests(string[] args, out string error)
    {
        const string usage = "Usage: agentmux clear-status <key|--all> [--workspace <id-or-index>]";
        var named = ParseNamed(args);
        var positionals = ReadPositional(named);
        var hasAllFlag = named.ContainsKey("all");
        var all = hasAllFlag
            && named.TryGetValue("all", out var allValue)
            && allValue.Equals("true", StringComparison.OrdinalIgnoreCase);
        if (hasAllFlag && !all)
        {
            error = usage;
            return null;
        }

        var key = named.TryGetValue("key", out var namedKey)
            ? namedKey
            : positionals.Length == 1 ? positionals[0] : null;
        if (positionals.Length > 1
            || (all && !string.IsNullOrWhiteSpace(key))
            || (!all && (string.IsNullOrWhiteSpace(key) || key.Equals("true", StringComparison.OrdinalIgnoreCase))))
        {
            error = usage;
            return null;
        }

        var parameters = new Dictionary<string, object?>();
        if (all)
        {
            parameters["all"] = true;
        }
        else
        {
            parameters["key"] = key;
        }

        if (!TryAddWorkspaceTarget(named, parameters, usage, out error))
        {
            return null;
        }

        error = "";
        return new CliRequest(AgentMuxMethods.WorkspaceClearStatus, parameters);
    }

    internal static CliRequest? ParseWorkspaceSetProgressRequestForTests(string[] args, out string error)
    {
        const string usage = "Usage: agentmux set-progress <0..1> [--label <text>] [--workspace <id-or-index>]";
        var named = ParseNamed(args);
        if (!NamedKeysAreAllowed(named, ["value", "label", "workspace", "index", "id"]))
        {
            error = usage;
            return null;
        }

        var positionals = ReadPositional(named);
        var valueWasNamed = named.ContainsKey("value");
        if ((valueWasNamed && positionals.Length > 0) || (!valueWasNamed && positionals.Length != 1))
        {
            error = usage;
            return null;
        }

        var valueText = valueWasNamed ? named["value"] : positionals[0];
        if (!double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !double.IsFinite(value)
            || value is < 0 or > 1)
        {
            error = usage;
            return null;
        }

        var parameters = new Dictionary<string, object?>
        {
            ["value"] = value
        };

        if (named.TryGetValue("label", out var label))
        {
            if (string.IsNullOrWhiteSpace(label) || label.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                error = usage;
                return null;
            }

            parameters["label"] = label;
        }

        if (!TryAddWorkspaceTarget(named, parameters, usage, out error))
        {
            return null;
        }

        error = "";
        return new CliRequest(AgentMuxMethods.WorkspaceSetProgress, parameters);
    }

    internal static CliRequest? ParseWorkspaceClearProgressRequestForTests(string[] args, out string error)
    {
        const string usage = "Usage: agentmux clear-progress [--workspace <id-or-index>]";
        var named = ParseNamed(args);
        if (!NamedKeysAreAllowed(named, ["workspace", "index", "id"]))
        {
            error = usage;
            return null;
        }

        if (ReadPositional(named).Length > 0)
        {
            error = usage;
            return null;
        }

        var parameters = new Dictionary<string, object?>();
        if (!TryAddWorkspaceTarget(named, parameters, usage, out error))
        {
            return null;
        }

        error = "";
        return new CliRequest(AgentMuxMethods.WorkspaceClearProgress, parameters);
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

    internal static CliRequest? ParseSurfaceRequestForTests(string[] args, out string error)
    {
        if (args.Length == 0)
        {
            error = "Usage: agentmux surface <list|create|select>";
            return null;
        }

        if (args[0].Equals("list", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("ls", StringComparison.OrdinalIgnoreCase))
        {
            error = "";
            return new CliRequest(AgentMuxMethods.SurfaceList, new { });
        }

        if (args[0].Equals("create", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("new", StringComparison.OrdinalIgnoreCase))
        {
            error = "";
            return new CliRequest(AgentMuxMethods.SurfaceCreate, ParseNamed(args[1..]));
        }

        if (args[0].Equals("select", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("use", StringComparison.OrdinalIgnoreCase))
        {
            var named = ParseNamed(args[1..]);
            if (!named.ContainsKey("index") && named.TryGetValue("_arg0", out var positional))
            {
                named["index"] = positional;
            }

            var hasIndex = named.TryGetValue("index", out var indexValue);
            var hasId = named.TryGetValue("id", out var idValue)
                && !string.IsNullOrWhiteSpace(idValue)
                && !idValue.Equals("true", StringComparison.OrdinalIgnoreCase);
            if (hasIndex && hasId)
            {
                error = "Usage: agentmux surface select --index <n>|--id <surface-id>";
                return null;
            }

            if (hasIndex)
            {
                if (!TryParseNonNegativeInt(indexValue, out var index))
                {
                    error = "Usage: agentmux surface select --index <n>|--id <surface-id>";
                    return null;
                }

                error = "";
                return new CliRequest(AgentMuxMethods.SurfaceSelect, new { index });
            }

            if (!hasId)
            {
                error = "Usage: agentmux surface select --index <n>|--id <surface-id>";
                return null;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.SurfaceSelect, new { id = idValue });
        }

        error = $"Unknown surface command: {args[0]}";
        return null;
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

    internal static CliRequest? ParseNotificationsRequestForTests(string[] args, out string error)
    {
        if (args.Length == 0)
        {
            error = "Usage: agentmux notifications <list|clear|jump-latest>";
            return null;
        }

        if (args[0].Equals("list", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("ls", StringComparison.OrdinalIgnoreCase))
        {
            var named = ParseNamed(args[1..]);
            int? limit = null;
            if (named.TryGetValue("limit", out var limitValue))
            {
                if (!TryParsePositiveInt(limitValue, out var parsedLimit))
                {
                    error = "Usage: agentmux notifications list [--limit <count>]";
                    return null;
                }

                limit = parsedLimit;
            }

            error = "";
            return new CliRequest(AgentMuxMethods.NotificationsList, new { limit });
        }

        if (args[0].Equals("clear", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("clear-all", StringComparison.OrdinalIgnoreCase))
        {
            error = "";
            return new CliRequest(AgentMuxMethods.NotificationsClear, new { });
        }

        if (args[0].Equals("jump-latest", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("jump", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("open-latest", StringComparison.OrdinalIgnoreCase))
        {
            error = "";
            return new CliRequest(AgentMuxMethods.NotificationsJumpLatest, new { });
        }

        error = $"Unknown notifications command: {args[0]}";
        return null;
    }

    internal static CliRequest? ParseReadScreenRequestForTests(string[] args, out string error)
    {
        if (args.Length == 0)
        {
            error = "";
            return new CliRequest(AgentMuxMethods.ReadScreen, new { lines = (int?)null });
        }

        if (args.Length != 2
            || !args[0].Equals("--lines", StringComparison.OrdinalIgnoreCase)
            || !TryParseStrictPositiveInt(args[1], out var lines))
        {
            error = "Usage: agentmux read-screen [--lines <count>]";
            return null;
        }

        error = "";
        return new CliRequest(AgentMuxMethods.ReadScreen, new { lines });
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

    internal static object ParseSendKeyForTests(string[] args)
    {
        return ParseSendKey(args);
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

    private static bool TryParseHttpStatus(string? value, out int number)
    {
        return int.TryParse(value, out number) && number is >= 100 and <= 599;
    }

    private static bool TryParseStrictPositiveInt(string? value, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => character < '0' || character > '9'))
        {
            return false;
        }

        return int.TryParse(value, out number) && number > 0;
    }

    private static bool TryParseNonNegativeInt(string? value, out int number)
    {
        return int.TryParse(value, out number) && number >= 0;
    }

    private static bool TryParsePortList(string? value, out int[] ports)
    {
        ports = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parsed = new SortedSet<int>();
        foreach (var token in value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(token, out var port) || port is < 1 or > 65535)
            {
                return false;
            }

            parsed.Add(port);
        }

        if (parsed.Count == 0 || parsed.Count > 20)
        {
            return false;
        }

        ports = parsed.ToArray();
        return true;
    }

    private static bool TryNormalizeWorkspaceLogLevel(string? value, out string level)
    {
        level = "";
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is not ("info" or "warn" or "error" or "debug"))
        {
            return false;
        }

        level = normalized;
        return true;
    }

    private static bool TryAddWorkspaceTarget(
        Dictionary<string, string> named,
        Dictionary<string, object?> parameters,
        string usage,
        out string error)
    {
        error = "";
        var hasWorkspace = named.TryGetValue("workspace", out var workspaceValue);
        var hasIndex = named.ContainsKey("index");
        var hasIdFlag = named.ContainsKey("id");
        var hasId = named.TryGetValue("id", out var idValue)
            && !string.IsNullOrWhiteSpace(idValue)
            && !idValue.Equals("true", StringComparison.OrdinalIgnoreCase);

        if (hasIdFlag && !hasId)
        {
            error = usage;
            return false;
        }

        var targetCount = (hasWorkspace ? 1 : 0) + (hasIndex ? 1 : 0) + (hasId ? 1 : 0);
        if (targetCount > 1)
        {
            error = usage;
            return false;
        }

        if (hasWorkspace)
        {
            if (string.IsNullOrWhiteSpace(workspaceValue) || workspaceValue.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                error = usage;
                return false;
            }

            if (TryParseNonNegativeInt(workspaceValue, out var workspaceIndex))
            {
                parameters["index"] = workspaceIndex;
            }
            else
            {
                parameters["id"] = workspaceValue;
            }
        }

        if (hasIndex)
        {
            if (!named.TryGetValue("index", out var indexValue) || !TryParseNonNegativeInt(indexValue, out var index))
            {
                error = usage;
                return false;
            }

            parameters["index"] = index;
        }

        if (hasId)
        {
            parameters["id"] = idValue;
        }

        return true;
    }

    private static bool TryParsePullRequestNumber(string? value, out int number)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number)
            && number is >= 1 and <= 9999999;
    }

    private static bool TryNormalizePullRequestStatus(string? value, out string status)
    {
        status = "";
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is not ("unknown" or "open" or "draft" or "merged" or "closed"))
        {
            return false;
        }

        status = normalized;
        return true;
    }

    private static bool TryNormalizePullRequestUrl(string? value, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 2048
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsoluteUri.Length > 2048)
        {
            return false;
        }

        url = uri.AbsoluteUri;
        return true;
    }

    private static bool IsBrowserWaitState(string value)
    {
        return value.Equals("visible", StringComparison.OrdinalIgnoreCase)
            || value.Equals("attached", StringComparison.OrdinalIgnoreCase)
            || value.Equals("hidden", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBrowserLoadState(string value)
    {
        return value.Equals("domcontentloaded", StringComparison.OrdinalIgnoreCase)
            || value.Equals("load", StringComparison.OrdinalIgnoreCase)
            || value.Equals("network-idle", StringComparison.OrdinalIgnoreCase);
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

    private static string[] ReadPositional(Dictionary<string, string> named)
    {
        var values = new List<string>();
        for (var index = 0; named.TryGetValue($"_arg{index}", out var value); index++)
        {
            values.Add(value);
        }

        return values.ToArray();
    }

    private static bool NamedKeysAreAllowed(Dictionary<string, string> named, string[] allowedKeys)
    {
        foreach (var key in named.Keys)
        {
            if (key.StartsWith("_arg", StringComparison.Ordinal))
            {
                continue;
            }

            if (!allowedKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
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
          agentmux notifications list --limit 20
          agentmux notifications jump-latest
          agentmux notifications clear
          agentmux log "Server started" --level info --source server
          agentmux list-log --limit 20
          agentmux clear-log
          agentmux set-status build "Running tests" --icon checkmark --color "#22c55e"
          agentmux list-status
          agentmux clear-status build
          agentmux clear-status --all
          agentmux set-progress 0.75 --label "Deploying"
          agentmux clear-progress
          agentmux workspace list
          agentmux workspace create --title "API" --cwd "C:\src\api"
          agentmux workspace select --index 0
          agentmux workspace pr 123 --status open --url https://github.com/org/repo/pull/123
          agentmux workspace ports 3000 5173
          agentmux surface list
          agentmux surface create --title "Tests"
          agentmux surface select --index 0
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
          agentmux browser text --selector "main" --max-chars 10000
          agentmux browser click "#submit"
          agentmux browser click --frame agentmux-child-frame "#submit"
          agentmux browser fill "#prompt" "write tests"
          agentmux browser type "#prompt" "write tests"
          agentmux browser press Enter --selector "#prompt" --frame agentmux-child-frame
          agentmux browser screenshot .\browser.png
          agentmux browser frames
          agentmux browser wait-for-selector "#ready" --timeout-ms 5000
          agentmux browser wait-load --state network-idle --timeout-ms 5000
          agentmux browser console --limit 20
          agentmux browser console-clear
          agentmux browser network --limit 20
          agentmux browser network-clear
          agentmux browser response-body <request-id>
          agentmux browser har-metadata .\network.har.json
          agentmux browser trace .\browser-trace.json --duration-ms 500
          agentmux browser downloads --limit 20
          agentmux browser downloads-clear
          agentmux browser route list
          agentmux browser route block --url-contains "/api/private"
          agentmux browser route fulfill --url-contains "/api/mock" --status 200 --content-type text/plain --body "mocked"
          agentmux browser route clear
          agentmux send "npm test"
          agentmux send-key Enter
          agentmux send-key Ctrl+C
          agentmux send-key PageDown
          agentmux send-key F5
          agentmux send-key Alt+Left
          agentmux read-screen --lines 50
        """);
    }
}
