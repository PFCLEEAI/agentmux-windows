using System.Runtime.InteropServices;

namespace AgentMux.Win.Pty;

public sealed class ConPtySession : IPtySession
{
    public string Id { get; } = $"pty-{Guid.NewGuid():N}";
    public bool IsRunning { get; private set; }

    public event Action<ReadOnlyMemory<byte>>? OutputReceived;
    public event Action<int>? Exited;

    public Task StartAsync(PtyLaunchOptions options, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            throw new PlatformNotSupportedException("ConPTY requires Windows 10 October 2018 Update or newer.");
        }

        IsRunning = true;
        OutputReceived?.Invoke("AgentMux ConPTY scaffold ready.\r\n"u8.ToArray());
        return Task.CompletedTask;
    }

    public Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("PTY session is not running.");
        }

        OutputReceived?.Invoke(bytes);
        return Task.CompletedTask;
    }

    public Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken = default)
    {
        if (cols <= 0 || rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cols), "Terminal dimensions must be positive.");
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (IsRunning)
        {
            IsRunning = false;
            Exited?.Invoke(0);
        }

        return ValueTask.CompletedTask;
    }
}

internal static partial class ConPtyNative
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int CreatePseudoConsole(
        Coord size,
        IntPtr input,
        IntPtr output,
        uint flags,
        out IntPtr pseudoConsole);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int ResizePseudoConsole(IntPtr pseudoConsole, Coord size);

    [LibraryImport("kernel32.dll")]
    internal static partial void ClosePseudoConsole(IntPtr pseudoConsole);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct Coord
{
    public Coord(short x, short y)
    {
        X = x;
        Y = y;
    }

    public short X { get; }
    public short Y { get; }
}
