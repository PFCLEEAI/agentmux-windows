namespace AgentMux.Win.Pty;

public sealed class PtyLaunchOptions
{
    /// <summary>
    /// Full command line used to start the shell. Quote paths with spaces before assigning.
    /// </summary>
    public string ShellPath { get; set; } = "pwsh.exe";
    public string WorkingDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public int Cols { get; set; } = 120;
    public int Rows { get; set; } = 30;
}

public interface IPtySession : IAsyncDisposable
{
    string Id { get; }
    bool IsRunning { get; }
    event Action<ReadOnlyMemory<byte>>? OutputReceived;
    event Action<int>? Exited;
    Task StartAsync(PtyLaunchOptions options, CancellationToken cancellationToken = default);
    Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default);
    Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken = default);
}
