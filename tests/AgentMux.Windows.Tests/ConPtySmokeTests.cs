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
        var sawSmoke = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var session = new ConPtySession();
        session.OutputReceived += bytes =>
        {
            var text = Encoding.UTF8.GetString(bytes.Span);
            output.Append(text);
            if (output.ToString().Contains("AGENTMUX_SMOKE", StringComparison.Ordinal))
            {
                sawSmoke.TrySetResult();
            }
        };

        await session.StartAsync(new PtyLaunchOptions
        {
            ShellPath = "cmd.exe",
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Cols = 120,
            Rows = 30
        });

        await session.WriteAsync("echo AGENTMUX_SMOKE\r"u8.ToArray());
        await sawSmoke.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
