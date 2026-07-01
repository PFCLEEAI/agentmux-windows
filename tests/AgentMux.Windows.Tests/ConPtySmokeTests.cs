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

    private static async Task<ConPtyProbe> StartAndCaptureAsync(string commandLine)
    {
        var probe = new ConPtyProbe();
        probe.Session.Exited += exitCode =>
            probe.Append(string.Format(CultureInfo.InvariantCulture, " [exit:{0}] ", exitCode));
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

    private sealed class ConPtyProbe : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly StringBuilder _output = new();
        private readonly List<Waiter> _waiters = [];

        public ConPtySession Session { get; } = new();

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
