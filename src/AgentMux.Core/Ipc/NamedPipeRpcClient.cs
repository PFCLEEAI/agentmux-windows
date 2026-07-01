using System.IO.Pipes;
using System.Text.Json;

namespace AgentMux.Core.Ipc;

public sealed class NamedPipeRpcClient
{
    private readonly string _pipeName;
    private readonly int _timeoutMs;

    public NamedPipeRpcClient(string? pipeName = null, int timeoutMs = 5000)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? AgentMuxPipe.ForCurrentUser() : pipeName;
        _timeoutMs = timeoutMs;
    }

    public async Task<AgentMuxResponse> SendAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        var request = new AgentMuxRequest
        {
            Method = method,
            Params = parameters is null
                ? null
                : JsonSerializer.SerializeToElement(parameters, AgentMuxJson.Options)
        };

        await using var stream = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeoutMs);

        await stream.ConnectAsync(timeout.Token).ConfigureAwait(false);

        await using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(stream, leaveOpen: true);

        var json = JsonSerializer.Serialize(request, AgentMuxJson.Options);
        await writer.WriteLineAsync(json.AsMemory(), timeout.Token).ConfigureAwait(false);

        var responseLine = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(responseLine))
        {
            return AgentMuxResponse.Failure(request.Id, "Empty response from AgentMux.");
        }

        return JsonSerializer.Deserialize<AgentMuxResponse>(responseLine, AgentMuxJson.Options)
            ?? AgentMuxResponse.Failure(request.Id, "Invalid response from AgentMux.");
    }
}
