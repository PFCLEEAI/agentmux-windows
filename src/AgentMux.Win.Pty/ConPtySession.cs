using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AgentMux.Win.Pty;

public sealed class ConPtySession : IPtySession
{
    private const int ExtendedStartupInfoPresent = 0x00080000;
    private static readonly IntPtr ProcThreadAttributePseudoConsole = 0x00020016;

    private readonly object _gate = new();
    private IntPtr _pseudoConsole;
    private FileStream? _inputWriter;
    private FileStream? _outputReader;
    private Process? _process;
    private CancellationTokenSource? _readerStop;
    private Task? _readerTask;
    private int? _exitCode;
    private bool _exitReported;

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

        lock (_gate)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("PTY session is already running.");
            }

            _exitCode = null;
            _exitReported = false;
        }

        var inputReadForPseudoConsole = CreatePipe(out var inputWrite);
        var outputReadForApp = CreatePipe(out var outputWrite);
        var outputWriteForPseudoConsole = outputWrite;

        try
        {
            var size = new Coord((short)options.Cols, (short)options.Rows);
            var hr = ConPtyNative.CreatePseudoConsole(size, inputReadForPseudoConsole, outputWriteForPseudoConsole, 0, out _pseudoConsole);
            if (hr != 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            inputReadForPseudoConsole.Dispose();
            outputWriteForPseudoConsole.Dispose();

            _inputWriter = new FileStream(inputWrite, FileAccess.Write, 4096, isAsync: false);
            _outputReader = new FileStream(outputReadForApp, FileAccess.Read, 4096, isAsync: false);
            _readerStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _readerTask = Task.Run(() => ReadOutputLoopAsync(_readerStop.Token), CancellationToken.None);

            StartProcess(options, _pseudoConsole);

            var process = _process;
            lock (_gate)
            {
                IsRunning = process is { HasExited: false };
                if (!IsRunning && process is not null && _exitCode is null)
                {
                    _exitCode = process.ExitCode;
                }
            }
        }
        catch
        {
            if (_inputWriter is null)
            {
                inputWrite.Dispose();
            }
            else
            {
                _inputWriter.Dispose();
            }

            if (_outputReader is null)
            {
                outputReadForApp.Dispose();
            }
            else
            {
                _outputReader.Dispose();
            }

            inputReadForPseudoConsole.Dispose();
            outputWriteForPseudoConsole.Dispose();
            DisposeNative();
            throw;
        }

        return Task.CompletedTask;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        var writer = _inputWriter;
        bool isRunning;
        int? exitCode;
        lock (_gate)
        {
            isRunning = IsRunning;
            exitCode = _exitCode;
        }

        if (!isRunning || writer is null)
        {
            var exitDetails = exitCode is null ? string.Empty : $" Process exited with code {exitCode.Value}.";
            throw new InvalidOperationException($"PTY session is not running.{exitDetails}");
        }

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.Write(bytes.Span);
            writer.Flush();
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken = default)
    {
        if (cols <= 0 || rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cols), "Terminal dimensions must be positive.");
        }

        if (_pseudoConsole != IntPtr.Zero)
        {
            var hr = ConPtyNative.ResizePseudoConsole(_pseudoConsole, new Coord((short)cols, (short)rows));
            if (hr != 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? stop;
        Task? reader;

        lock (_gate)
        {
            stop = _readerStop;
            reader = _readerTask;
            IsRunning = false;
        }

        if (stop is not null)
        {
            await stop.CancelAsync().ConfigureAwait(false);
        }

        _inputWriter?.Dispose();
        _outputReader?.Dispose();
        DisposeNative();

        if (reader is not null)
        {
            try
            {
                await reader.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
            catch (IOException)
            {
                // Pipes often close while the pseudoconsole is shutting down.
            }
            catch (UnauthorizedAccessException)
            {
                // A closing synchronous pipe can surface access errors while the reader unwinds.
            }
            catch (TimeoutException)
            {
                // Do not let a blocked pipe reader hang app shutdown.
            }
        }

        _process?.Dispose();
        stop?.Dispose();
    }

    private async Task ReadOutputLoopAsync(CancellationToken cancellationToken)
    {
        var reader = _outputReader;
        if (reader is null)
        {
            return;
        }

        var buffer = new byte[8192];
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            var copy = buffer.AsSpan(0, read).ToArray();
            OutputReceived?.Invoke(copy);
        }
    }

    private void StartProcess(PtyLaunchOptions options, IntPtr pseudoConsole)
    {
        var startupInfo = new StartupInfoEx();
        startupInfo.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();

        var attributeListSize = IntPtr.Zero;
        _ = ConPtyNative.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);
        if (attributeListSize == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not size ConPTY process attribute list.");
        }

        var startupInfoSize = Marshal.SizeOf<StartupInfoEx>();
        var startupInfoPointer = IntPtr.Zero;
        startupInfo.AttributeList = Marshal.AllocHGlobal(attributeListSize);
        try
        {
            if (!ConPtyNative.InitializeProcThreadAttributeList(startupInfo.AttributeList, 1, 0, ref attributeListSize))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not initialize ConPTY process attribute list.");
            }

            if (!ConPtyNative.UpdateProcThreadAttribute(
                    startupInfo.AttributeList,
                    0,
                    ProcThreadAttributePseudoConsole,
                    pseudoConsole,
                    (IntPtr)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not attach pseudoconsole to process attributes.");
            }

            startupInfoPointer = Marshal.AllocHGlobal(startupInfoSize);
            Marshal.StructureToPtr(startupInfo, startupInfoPointer, false);

            var commandLine = new StringBuilder(BuildCommandLine(options.CommandLine));
            if (!ConPtyNative.CreateProcessW(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    ExtendedStartupInfoPresent,
                    IntPtr.Zero,
                    options.WorkingDirectory,
                    startupInfoPointer,
                    out var processInformation))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not start shell: {options.CommandLine}");
            }

            try
            {
                var process = Process.GetProcessById((int)processInformation.dwProcessId);
                process.Exited += (_, _) => MarkProcessExited(process.ExitCode);
                process.EnableRaisingEvents = true;
                _process = process;

                if (process.HasExited)
                {
                    MarkProcessExited(process.ExitCode);
                }
            }
            finally
            {
                ConPtyNative.CloseHandle(processInformation.Thread);
                ConPtyNative.CloseHandle(processInformation.Process);
            }
        }
        finally
        {
            if (startupInfoPointer != IntPtr.Zero)
            {
                Marshal.DestroyStructure<StartupInfoEx>(startupInfoPointer);
                Marshal.FreeHGlobal(startupInfoPointer);
            }

            if (startupInfo.AttributeList != IntPtr.Zero)
            {
                ConPtyNative.DeleteProcThreadAttributeList(startupInfo.AttributeList);
                Marshal.FreeHGlobal(startupInfo.AttributeList);
            }
        }
    }

    private void MarkProcessExited(int exitCode)
    {
        lock (_gate)
        {
            if (_exitReported)
            {
                return;
            }

            _exitCode = exitCode;
            _exitReported = true;
            IsRunning = false;
        }

        Exited?.Invoke(exitCode);
    }

    private void DisposeNative()
    {
        if (_pseudoConsole != IntPtr.Zero)
        {
            ConPtyNative.ClosePseudoConsole(_pseudoConsole);
            _pseudoConsole = IntPtr.Zero;
        }
    }

    private static SafeFileHandle CreatePipe(out SafeFileHandle writeHandle)
    {
        if (!ConPtyNative.CreatePipe(out var readHandle, out writeHandle, IntPtr.Zero, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create pipe.");
        }

        return readHandle;
    }

    private static string BuildCommandLine(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return "pwsh.exe";
        }

        return command;
    }
}

internal static partial class ConPtyNative
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int CreatePseudoConsole(
        Coord size,
        SafeFileHandle input,
        SafeFileHandle output,
        uint flags,
        out IntPtr pseudoConsole);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int ResizePseudoConsole(IntPtr pseudoConsole, Coord size);

    [LibraryImport("kernel32.dll")]
    internal static partial void ClosePseudoConsole(IntPtr pseudoConsole);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreatePipe(
        out SafeFileHandle readPipe,
        out SafeFileHandle writePipe,
        IntPtr pipeAttributes,
        int size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitializeProcThreadAttributeList(
        IntPtr attributeList,
        int attributeCount,
        int flags,
        ref IntPtr size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateProcThreadAttribute(
        IntPtr attributeList,
        uint flags,
        IntPtr attribute,
        IntPtr value,
        IntPtr size,
        IntPtr previousValue,
        IntPtr returnSize);

    [LibraryImport("kernel32.dll")]
    internal static partial void DeleteProcThreadAttributeList(IntPtr attributeList);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcessW(
        string? applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        int creationFlags,
        IntPtr environment,
        string? currentDirectory,
        IntPtr startupInfo,
        out ProcessInformation processInformation);
}

[StructLayout(LayoutKind.Sequential)]
internal struct Coord
{
    public Coord(short x, short y)
    {
        X = x;
        Y = y;
    }

    public short X;
    public short Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct StartupInfo
{
    public int cb;
    public IntPtr Reserved;
    public IntPtr Desktop;
    public IntPtr Title;
    public int X;
    public int Y;
    public int XSize;
    public int YSize;
    public int XCountChars;
    public int YCountChars;
    public int FillAttribute;
    public int Flags;
    public short ShowWindow;
    public short Reserved2;
    public IntPtr Reserved2Pointer;
    public IntPtr StdInput;
    public IntPtr StdOutput;
    public IntPtr StdError;
}

[StructLayout(LayoutKind.Sequential)]
internal struct StartupInfoEx
{
    public StartupInfo StartupInfo;
    public IntPtr AttributeList;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessInformation
{
    public IntPtr Process;
    public IntPtr Thread;
    public uint dwProcessId;
    public uint dwThreadId;
}
