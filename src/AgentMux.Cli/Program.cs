using System.IO.Pipes;
using System.Text.Json;
using AgentMux.Core.Ipc;

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
          agentmux send "npm test"
          agentmux send-key Enter
          agentmux read-screen --lines 50
        """);
    }
}
