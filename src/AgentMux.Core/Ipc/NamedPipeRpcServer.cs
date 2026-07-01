using System.IO.Pipes;
using System.Text.Json;

namespace AgentMux.Core.Ipc;

public sealed class NamedPipeRpcServer : IAsyncDisposable
{
    internal const PipeOptions ServerPipeOptions = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    private readonly string _pipeName;
    private readonly Func<AgentMuxRequest, CancellationToken, Task<AgentMuxResponse>> _handler;
    private CancellationTokenSource? _stop;
    private Task? _serverTask;

    public NamedPipeRpcServer(
        Func<AgentMuxRequest, CancellationToken, Task<AgentMuxResponse>> handler,
        string? pipeName = null)
    {
        _handler = handler;
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? AgentMuxPipe.ForCurrentUser() : pipeName;
    }

    public void Start()
    {
        if (_serverTask is not null)
        {
            return;
        }

        _stop = new CancellationTokenSource();
        _serverTask = Task.Run(() => RunAsync(_stop.Token));
    }

    public async ValueTask DisposeAsync()
    {
        if (_stop is null)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);

        if (_serverTask is not null)
        {
            try
            {
                await _serverTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var stream = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                ServerPipeOptions);

            await stream.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            _ = Task.Run(() => HandleConnectionAsync(stream, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream, leaveOpen: true);
            await using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };

            var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }

            AgentMuxResponse response;
            try
            {
                var request = JsonSerializer.Deserialize<AgentMuxRequest>(requestLine, AgentMuxJson.Options);
                if (request is null || string.IsNullOrWhiteSpace(request.Method))
                {
                    response = AgentMuxResponse.Failure("", "Invalid request.");
                }
                else
                {
                    response = await _handler(request, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                response = AgentMuxResponse.Failure("", ex.Message);
            }

            var json = JsonSerializer.Serialize(response, AgentMuxJson.Options);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }
}
