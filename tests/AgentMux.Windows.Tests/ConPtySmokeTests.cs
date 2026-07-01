using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using AgentMux.Win.Pty;

namespace AgentMux.Windows.Tests;

public sealed class ConPtySmokeTests
{
    [Fact]
    public async Task CommandOutputFlowsThroughConPty()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        await using var probe = await StartAndCaptureAsync("cmd.exe /D /Q /C set /A 20+22");

        await probe.WaitForOutputAsync("42", TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TestHostEchoesInputThroughConPty()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var hostPath = ResolveTestHostPath();
        await using var probe = await StartAndCaptureAsync(QuoteCommand(hostPath));

        await probe.WaitForOutputAsync("AGENTMUX_READY", TimeSpan.FromSeconds(5));
        await probe.Session.WriteAsync("AGENTMUX_SMOKE\r\n"u8.ToArray());

        await probe.WaitForOutputAsync("ECHO:AGENTMUX_SMOKE", TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task LiveSessionResizeKeepsInputOutputWorking()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var hostPath = ResolveTestHostPath();
        await using var probe = await StartAndCaptureAsync(QuoteCommand(hostPath));

        await probe.WaitForOutputAsync("AGENTMUX_READY", TimeSpan.FromSeconds(5));
        await probe.Session.ResizeAsync(80, 20);
        await probe.Session.WriteAsync("size\r\n"u8.ToArray());
        await probe.WaitForOutputAsync("SIZE:80x20", TimeSpan.FromSeconds(10));
        await probe.Session.WriteAsync("AGENTMUX_AFTER_RESIZE\r\n"u8.ToArray());

        await probe.WaitForOutputAsync("ECHO:AGENTMUX_AFTER_RESIZE", TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RawControlSequencesFlowThroughConPty()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var hostPath = ResolveTestHostPath();
        await using var probe = await StartAndCaptureAsync($"{QuoteCommand(hostPath)} --raw-bytes");

        await probe.WaitForOutputAsync("AGENTMUX_RAW_READY", TimeSpan.FromSeconds(5));
        await probe.Session.WriteAsync(new byte[] { 0x1b, 0x5b, 0x36, 0x7e });
        await probe.WaitForOutputAsync("RAW:1B", TimeSpan.FromSeconds(10));
        await probe.WaitForOutputAsync("RAW:5B", TimeSpan.FromSeconds(10));
        await probe.WaitForOutputAsync("RAW:36", TimeSpan.FromSeconds(10));
        await probe.WaitForOutputAsync("RAW:7E", TimeSpan.FromSeconds(10));

        await probe.Session.WriteAsync(new byte[] { 0x04 });
        await probe.WaitForOutputAsync("RAW:04", TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task DisposeAsyncClosesCooperativeDirectChild()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var hostPath = ResolveTestHostPath();
        var probe = await StartAndCaptureAsync($"{QuoteCommand(hostPath)} --cleanup-cooperative");
        Process? process = null;
        try
        {
            await probe.WaitForOutputAsync("AGENTMUX_CLEANUP_COOPERATIVE_READY:", TimeSpan.FromSeconds(5));
            var processId = AssertStartedProcess(probe.Session);
            await probe.WaitForOutputAsync($"AGENTMUX_CLEANUP_COOPERATIVE_READY:{processId}", TimeSpan.FromSeconds(5));
            process = Process.GetProcessById(processId);
            var startTime = TryGetStartTimeUtc(process);

            await DisposeSessionWithGuardAsync(probe.Session, TimeSpan.FromSeconds(5));

            Assert.True(await WaitForCapturedProcessExitAsync(process, TimeSpan.FromSeconds(2)), $"Process {processId} did not exit after cooperative disposal.");
            process.Dispose();
            process = null;
            AssertProcessIdGoneOrReused(processId, startTime);
            Assert.Equal(0, probe.Session.DirectChildExitCodeForTests);
            Assert.False(probe.Session.LastDisposeKillAttemptedForTests);
            Assert.InRange(probe.ExitEventCount, 0, 1);
        }
        finally
        {
            process?.Dispose();
            await probe.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposeAsyncKillsStubbornDirectChildAfterTimeout()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var hostPath = ResolveTestHostPath();
        var probe = await StartAndCaptureAsync($"{QuoteCommand(hostPath)} --cleanup-stubborn");
        Process? process = null;
        try
        {
            await probe.WaitForOutputAsync("AGENTMUX_CLEANUP_STUBBORN_READY:", TimeSpan.FromSeconds(5));
            var processId = AssertStartedProcess(probe.Session);
            await probe.WaitForOutputAsync($"AGENTMUX_CLEANUP_STUBBORN_READY:{processId}", TimeSpan.FromSeconds(5));
            process = Process.GetProcessById(processId);
            var startTime = TryGetStartTimeUtc(process);

            await DisposeSessionWithGuardAsync(probe.Session, TimeSpan.FromSeconds(5));

            Assert.True(await WaitForCapturedProcessExitAsync(process, TimeSpan.FromSeconds(2)), $"Process {processId} did not exit after stubborn disposal.");
            process.Dispose();
            process = null;
            AssertProcessIdGoneOrReused(processId, startTime);
            Assert.True(probe.Session.LastDisposeKillAttemptedForTests);
            Assert.InRange(probe.ExitEventCount, 0, 1);
        }
        finally
        {
            process?.Dispose();
            await probe.DisposeAsync();
        }
    }

    private static async Task<ConPtyProbe> StartAndCaptureAsync(string commandLine)
    {
        var probe = new ConPtyProbe();
        probe.Session.Exited += probe.RecordExit;
        probe.Session.OutputReceived += bytes => probe.Append(Encoding.UTF8.GetString(bytes.Span));

        await probe.Session.StartAsync(new PtyLaunchOptions
        {
            CommandLine = commandLine,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Cols = 120,
            Rows = 30
        });

        return probe;
    }

    private static string ResolveTestHostPath()
    {
        var configuration = typeof(ConPtySmokeTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Release";
        var path = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "AgentMux.Pty.TestHost",
            "bin",
            configuration,
            "net9.0-windows10.0.17763.0",
            "AgentMux.Pty.TestHost.exe");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("ConPTY test host was not built.", path);
        }

        return path;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgentMux.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate repository root from {AppContext.BaseDirectory}.");
    }

    private static string QuoteCommand(string path)
    {
        return $"\"{path}\"";
    }

    private static int AssertStartedProcess(ConPtySession session)
    {
        var processId = session.DirectChildProcessIdForTests;
        Assert.True(processId is > 0);
        return processId.Value;
    }

    private static async Task DisposeSessionWithGuardAsync(ConPtySession session, TimeSpan timeout)
    {
        await session.DisposeAsync().AsTask().WaitAsync(timeout);
    }

    private static async Task<bool> WaitForCapturedProcessExitAsync(Process process, TimeSpan timeout)
    {
        try
        {
            if (process.HasExited)
            {
                return true;
            }

            using var stop = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(stop.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return true;
        }
    }

    private static DateTime? TryGetStartTimeUtc(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    private static void AssertProcessIdGoneOrReused(int processId, DateTime? originalStartTimeUtc)
    {
        if (originalStartTimeUtc is null)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return;
            }

            var currentStartTimeUtc = TryGetStartTimeUtc(process);
            if (currentStartTimeUtc is null)
            {
                return;
            }

            Assert.NotEqual(originalStartTimeUtc.Value, currentStartTimeUtc.Value);
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }
    }

    private sealed class ConPtyProbe : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly StringBuilder _output = new();
        private readonly List<Waiter> _waiters = [];
        private int _exitEventCount;

        public ConPtySession Session { get; } = new();
        public int ExitEventCount
        {
            get
            {
                lock (_gate)
                {
                    return _exitEventCount;
                }
            }
        }

        public async Task WaitForOutputAsync(string marker, TimeSpan timeout)
        {
            var outputTask = OutputContaining(marker);
            var completed = await Task.WhenAny(outputTask, Task.Delay(timeout));
            if (completed != outputTask)
            {
                throw new TimeoutException($"Timed out waiting for '{marker}'. Captured output: {CapturedOutput}");
            }

            await outputTask;
        }

        private Task OutputContaining(string marker)
        {
            lock (_gate)
            {
                if (_output.ToString().Contains(marker, StringComparison.Ordinal))
                {
                    return Task.CompletedTask;
                }

                var waiter = new Waiter(marker);
                _waiters.Add(waiter);
                return waiter.Task;
            }
        }

        public void Append(string value)
        {
            lock (_gate)
            {
                _output.Append(value);
                var text = _output.ToString();
                for (var i = _waiters.Count - 1; i >= 0; i--)
                {
                    var waiter = _waiters[i];
                    if (text.Contains(waiter.Marker, StringComparison.Ordinal))
                    {
                        waiter.Complete();
                        _waiters.RemoveAt(i);
                    }
                }
            }
        }

        public void RecordExit(int exitCode)
        {
            lock (_gate)
            {
                _exitEventCount++;
            }

            Append(string.Format(CultureInfo.InvariantCulture, " [exit:{0}] ", exitCode));
        }

        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync();
        }

        private string CapturedOutput
        {
            get
            {
                lock (_gate)
                {
                    return _output.ToString();
                }
            }
        }

        private sealed class Waiter(string marker)
        {
            private readonly TaskCompletionSource _source = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public string Marker { get; } = marker;
            public Task Task => _source.Task;
            public void Complete() => _source.TrySetResult();
        }
    }
}
