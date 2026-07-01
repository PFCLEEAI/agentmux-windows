using System.Globalization;
using System.Text;
using AgentMux.Win.Pty;

namespace AgentMux.Windows.Tests;

public sealed class ConPtySmokeTests
{
    [Fact]
    public async Task CmdEchoesInputThroughConPty()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var output = new StringBuilder();
        var sawReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sawSmoke = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var session = new ConPtySession();
        session.Exited += exitCode => output.Append(CultureInfo.InvariantCulture, $" [exit:{exitCode}] ");
        session.OutputReceived += bytes =>
        {
            var text = Encoding.UTF8.GetString(bytes.Span);
            output.Append(text);
            if (output.ToString().Contains("AGENTMUX_READY", StringComparison.Ordinal))
            {
                sawReady.TrySetResult();
            }

            if (output.ToString().Contains("AGENTMUX_SMOKE", StringComparison.Ordinal))
            {
                sawSmoke.TrySetResult();
            }
        };

        await session.StartAsync(new PtyLaunchOptions
        {
            ShellPath = "cmd.exe /Q /K echo AGENTMUX_READY",
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Cols = 120,
            Rows = 30
        });

        await sawReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.WriteAsync("echo AGENTMUX_SMOKE\r\n"u8.ToArray());

        var completed = await Task.WhenAny(sawSmoke.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        if (completed != sawSmoke.Task)
        {
            throw new TimeoutException($"Timed out waiting for ConPTY echo. Captured output: {output}");
        }
    }
}
