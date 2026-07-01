using System.Threading;
using System.Windows;
using AgentMux.Core.Models;
using AgentMux.Win.App.Views;

namespace AgentMux.App.Tests;

public sealed class MainWindowSmokeTests
{
    [Fact]
    public async Task MainWindowRendersSplitPaneTreeOnStaThread()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await WpfTestHost.RunAsync(() =>
        {
            EnsureApplicationResources();

            var window = new MainWindow();
            try
            {
                window.InitializeForSmokeTest();

                Assert.True(window.HasButtonForSmokeTest("Split right"));
                Assert.True(window.HasButtonForSmokeTest("Split down"));
                Assert.Equal(1, window.PaneCountForSmokeTest);
                Assert.Equal(1, window.RenderedTerminalPaneCountForSmokeTest);

                Assert.True(window.SplitActivePaneForSmokeTest(SplitDirection.Right));
                Assert.Equal(2, window.PaneCountForSmokeTest);
                Assert.Equal(2, window.RenderedTerminalPaneCountForSmokeTest);

                window.SetActivePaneTextForSmokeTest("AGENTMUX_UI_SMOKE");
                Assert.Equal(2, window.RenderedTerminalPaneCountForSmokeTest);
                Assert.True(window.RenderedTextContainsForSmokeTest("AGENTMUX_UI_SMOKE"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void EnsureApplicationResources()
    {
        if (Application.Current is not null)
        {
            return;
        }

        var app = new global::AgentMux.Win.App.App
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        app.InitializeComponent();
    }

    private static class WpfTestHost
    {
        public static async Task RunAsync(Action action)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
                finally
                {
                    Application.Current?.Shutdown();
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }
}
