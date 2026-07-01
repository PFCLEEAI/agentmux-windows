using System.Threading;
using System.Windows;
using System.Windows.Threading;
using AgentMux.Core.Models;
using AgentMux.Win.App.Controls;
using AgentMux.Win.App.Views;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AgentMux.App.Tests;

public sealed class MainWindowSmokeTests
{
    private static readonly string SmokeArtifactDirectoryPath = ResolveSmokeArtifactDirectory();

    [Fact]
    public async Task MainWindowRendersSplitPaneTreeOnStaThread()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await WpfTestHost.RunAsync(async () =>
        {
            EnsureApplicationResources();

            var window = new MainWindow();
            try
            {
                window.InitializeForSmokeTest();

                Assert.True(window.HasButtonForSmokeTest("Split right"));
                Assert.True(window.HasButtonForSmokeTest("Split down"));
                Assert.True(window.HasButtonForSmokeTest("Browser"));
                Assert.Equal(1, window.PaneCountForSmokeTest);
                Assert.Equal(1, window.RenderedTerminalPaneCountForSmokeTest);

                Assert.True(window.SplitActivePaneForSmokeTest(SplitDirection.Right));
                Assert.Equal(2, window.PaneCountForSmokeTest);
                Assert.Equal(2, window.RenderedTerminalPaneCountForSmokeTest);
                var activeAfterSplit = window.ActivePaneIdForSmokeTest;

                Assert.True(window.CycleActivePaneForSmokeTest(reverse: false));
                Assert.NotEqual(activeAfterSplit, window.ActivePaneIdForSmokeTest);
                var leftPane = window.ActivePaneIdForSmokeTest;

                Assert.True(window.HandlePaneFocusShortcutForSmokeTest(System.Windows.Input.Key.Right));
                Assert.Equal(activeAfterSplit, window.ActivePaneIdForSmokeTest);

                Assert.False(window.HandlePaneFocusShortcutForSmokeTest(System.Windows.Input.Key.Right));
                Assert.Equal(activeAfterSplit, window.ActivePaneIdForSmokeTest);

                Assert.True(window.HandlePaneFocusShortcutForSmokeTest(System.Windows.Input.Key.Left));
                Assert.Equal(leftPane, window.ActivePaneIdForSmokeTest);
                Assert.False(window.HandlePaneFocusShortcutForSmokeTest(System.Windows.Input.Key.A));
                Assert.Equal(leftPane, window.ActivePaneIdForSmokeTest);

                Assert.True(window.SplitActivePaneForSmokeTest(SplitDirection.Down));
                var bottomLeftPane = window.ActivePaneIdForSmokeTest;
                Assert.True(window.HandlePaneFocusShortcutForSmokeTest(System.Windows.Input.Key.Up));
                var topLeftPane = window.ActivePaneIdForSmokeTest;
                Assert.NotEqual(bottomLeftPane, topLeftPane);
                Assert.True(window.HandlePaneFocusShortcutForSmokeTest(System.Windows.Input.Key.Down));
                Assert.Equal(bottomLeftPane, window.ActivePaneIdForSmokeTest);

                Assert.True(window.HandlePaneActionShortcutForSmokeTest(System.Windows.Input.Key.Z));
                Assert.True(window.IsActivePaneZoomedForSmokeTest);
                Assert.Equal(3, window.PaneCountForSmokeTest);
                Assert.Equal(1, window.RenderedTerminalPaneCountForSmokeTest);

                Assert.True(window.HandlePaneFocusShortcutForSmokeTest(System.Windows.Input.Key.Right));
                Assert.False(window.IsActivePaneZoomedForSmokeTest);
                Assert.Equal(3, window.PaneCountForSmokeTest);
                Assert.Equal(3, window.RenderedTerminalPaneCountForSmokeTest);
                Assert.True(window.HandlePaneFocusShortcutForSmokeTest(System.Windows.Input.Key.Left));

                Assert.True(window.HandlePaneActionShortcutForSmokeTest(System.Windows.Input.Key.Z));
                Assert.True(window.IsActivePaneZoomedForSmokeTest);
                Assert.True(window.HandlePaneActionShortcutForSmokeTest(System.Windows.Input.Key.Z));
                Assert.Equal(3, window.RenderedTerminalPaneCountForSmokeTest);
                Assert.False(window.HandlePaneActionShortcutForSmokeTest(System.Windows.Input.Key.A));

                window.SetActivePaneTextForSmokeTest("AGENTMUX_UI_SMOKE");
                Assert.Equal(3, window.RenderedTerminalPaneCountForSmokeTest);
                Assert.True(window.RenderedTextContainsForSmokeTest("AGENTMUX_UI_SMOKE"));

                window.AppendActivePaneTextForSmokeTest("_STREAM_APPEND");
                Assert.True(window.RenderedTextContainsForSmokeTest("AGENTMUX_UI_SMOKE_STREAM_APPEND"));

                Assert.True(window.HandlePaneFocusShortcutForSmokeTest(System.Windows.Input.Key.Right));

                var browserUrl = window.OpenBrowserInActivePaneForSmokeTest("example.com");
                Assert.Equal("https://example.com/", browserUrl);
                Assert.Equal(1, window.RenderedBrowserPaneCountForSmokeTest);
                Assert.Equal(2, window.RenderedTerminalPaneCountForSmokeTest);
                Assert.True(window.RenderedTextContainsForSmokeTest(browserUrl));

                Assert.True(window.HandlePaneFocusShortcutForSmokeTest(System.Windows.Input.Key.Left));
                Assert.Equal(1, window.RenderedBrowserPaneCountForSmokeTest);
                Assert.Equal(2, window.RenderedTerminalPaneCountForSmokeTest);
                Assert.True(window.HandlePaneFocusShortcutForSmokeTest(System.Windows.Input.Key.Right));

                var safeUrl = window.OpenBrowserInActivePaneForSmokeTest("https://");
                Assert.Equal("about:blank", safeUrl);
                Assert.True(window.RenderedTextContainsForSmokeTest(safeUrl));

                Assert.True(window.HandlePaneActionShortcutForSmokeTest(System.Windows.Input.Key.Z));
                Assert.True(window.IsActivePaneZoomedForSmokeTest);
                Assert.Equal(1, window.RenderedBrowserPaneCountForSmokeTest);
                Assert.Equal(0, window.RenderedTerminalPaneCountForSmokeTest);

                Assert.True(window.HandlePaneActionShortcutForSmokeTest(System.Windows.Input.Key.Z));
                Assert.False(window.IsActivePaneZoomedForSmokeTest);
                Assert.Equal(1, window.RenderedBrowserPaneCountForSmokeTest);
                Assert.Equal(2, window.RenderedTerminalPaneCountForSmokeTest);

                Assert.Equal(1, window.CachedBrowserPaneViewCountForSmokeTest);
                Assert.True(window.HandlePaneActionShortcutForSmokeTest(System.Windows.Input.Key.X));
                Assert.Equal(2, window.PaneCountForSmokeTest);
                Assert.Equal(0, window.RenderedBrowserPaneCountForSmokeTest);
                Assert.Equal(2, window.RenderedTerminalPaneCountForSmokeTest);
                Assert.Equal(0, window.CachedBrowserPaneViewCountForSmokeTest);

                Assert.True(window.HandlePaneActionShortcutForSmokeTest(System.Windows.Input.Key.X));
                Assert.Equal(1, window.PaneCountForSmokeTest);
                Assert.Equal(1, window.RenderedTerminalPaneCountForSmokeTest);
                Assert.Equal(1, window.CachedTerminalPaneViewCountForSmokeTest);

                var lastPane = window.ActivePaneIdForSmokeTest;
                Assert.True(window.HandlePaneActionShortcutForSmokeTest(System.Windows.Input.Key.X));
                Assert.Equal(1, window.PaneCountForSmokeTest);
                Assert.Equal(lastPane, window.ActivePaneIdForSmokeTest);
            }
            finally
            {
                window.Close();
            }

            await RunHostedWebView2RuntimeSmokeAsync();
        });
    }

    private static async Task RunHostedWebView2RuntimeSmokeAsync()
    {
        var terminalWindow = CreateSmokeWindow();
        var terminal = new TerminalPaneView();
        terminalWindow.Content = terminal;
        try
        {
            terminalWindow.Show();

            const string terminalMarker = "AGENTMUX_WEBVIEW2_XTERM_SMOKE_APPEND";
            terminal.SetScreenText("AGENTMUX_WEBVIEW2_XTERM_SMOKE");
            terminal.AppendScreenText("_APPEND");

            var diagnosticsJson = await terminal.ExecuteRuntimeScriptForSmokeTestAsync("""
                (() => ({
                    hasXtermElement: !!document.querySelector(".xterm"),
                    hasSetText: typeof window.agentmuxSetText === "function",
                    hasAppendText: typeof window.agentmuxAppendText === "function",
                    hasSmokeProbe: typeof window.agentmuxGetTextForSmoke === "function"
                }))()
                """);
            using (var diagnostics = System.Text.Json.JsonDocument.Parse(diagnosticsJson))
            {
                var root = diagnostics.RootElement;
                Assert.True(root.GetProperty("hasXtermElement").GetBoolean());
                Assert.True(root.GetProperty("hasSetText").GetBoolean());
                Assert.True(root.GetProperty("hasAppendText").GetBoolean());
                Assert.True(root.GetProperty("hasSmokeProbe").GetBoolean());
            }

            var runtimeText = await terminal.WaitForRuntimeTextForSmokeTestAsync(terminalMarker);
            Assert.Contains(terminalMarker, runtimeText);

            var terminalScreenshot = await terminal.CapturePngForSmokeTestAsync(
                System.IO.Path.Combine(SmokeArtifactDirectory(), "terminal-webview2.png"));
            AssertPngFile(terminalScreenshot);
        }
        finally
        {
            terminalWindow.Close();
        }

        var browserWindow = CreateSmokeWindow();
        var browser = new BrowserPaneView();
        browserWindow.Content = browser;
        try
        {
            browserWindow.Show();

            var setupResult = await browser.EvaluateScriptAsync("""
                document.body.innerHTML = '<input id="name"><button id="go">go</button><output id="result"></output>';
                window.__agentMuxClicked = 0;
                document.querySelector("#go").addEventListener("click", () => {
                    window.__agentMuxClicked += 1;
                    document.querySelector("#result").textContent = document.querySelector("#name").value;
                });
                true;
                """);
            Assert.Equal("true", setupResult);

            AssertBrowserOk(await browser.FillAsync("#name", "agentmux-browser-smoke"));
            AssertBrowserOk(await browser.ClickAsync("#go"));

            var stateJson = await browser.EvaluateScriptAsync("""
                (() => ({
                    value: document.querySelector("#name").value,
                    result: document.querySelector("#result").textContent,
                    clicked: window.__agentMuxClicked
                }))()
                """);
            using (var state = System.Text.Json.JsonDocument.Parse(stateJson))
            {
                var root = state.RootElement;
                Assert.Equal("agentmux-browser-smoke", root.GetProperty("value").GetString());
                Assert.Equal("agentmux-browser-smoke", root.GetProperty("result").GetString());
                Assert.Equal(1, root.GetProperty("clicked").GetInt32());
            }

            var screenshotPath = System.IO.Path.Combine(SmokeArtifactDirectory(), "browser-webview2.png");
            var capturedPath = await browser.CapturePngAsync(screenshotPath);
            AssertPngFile(capturedPath);
        }
        finally
        {
            browserWindow.Close();
        }
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

    private static Window CreateSmokeWindow() => new()
    {
        Width = 900,
        Height = 560,
        ShowActivated = false,
        ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.Manual
    };

    private static void AssertBrowserOk(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
    }

    private static string SmokeArtifactDirectory()
    {
        return SmokeArtifactDirectoryPath;
    }

    private static string ResolveSmokeArtifactDirectory()
    {
        var configuredPath = Environment.GetEnvironmentVariable("AGENTMUX_SMOKE_ARTIFACT_DIR");
        return string.IsNullOrWhiteSpace(configuredPath)
            ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agentmux-app-smoke", $"{Guid.NewGuid():N}")
            : configuredPath;
    }

    private static void AssertPngFile(string path)
    {
        var screenshot = new System.IO.FileInfo(path);
        Assert.True(screenshot.Exists);
        Assert.True(screenshot.Length > 0);
        AssertPngSignature(path);
    }

    private static void AssertPngSignature(string path)
    {
        var signature = new byte[8];
        using var stream = System.IO.File.OpenRead(path);
        Assert.Equal(signature.Length, stream.Read(signature));
        Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], signature);

        var ihdr = new byte[17];
        Assert.Equal(ihdr.Length, stream.Read(ihdr));
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(ihdr, 4, 4));
        Assert.True(ReadBigEndianInt32(ihdr, 8) > 0);
        Assert.True(ReadBigEndianInt32(ihdr, 12) > 0);
    }

    private static int ReadBigEndianInt32(byte[] buffer, int offset) =>
        (buffer[offset] << 24)
        | (buffer[offset + 1] << 16)
        | (buffer[offset + 2] << 8)
        | buffer[offset + 3];

    private static class WpfTestHost
    {
        public static Task RunAsync(Action action) =>
            RunAsync(() =>
            {
                action();
                return Task.CompletedTask;
            });

        public static async Task RunAsync(Func<Task> action)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await action().ConfigureAwait(true);
                        completion.SetResult();
                    }
                    catch (Exception ex)
                    {
                        completion.SetException(ex);
                    }
                    finally
                    {
                        Application.Current?.Shutdown();
                        Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    }
                });
                Dispatcher.Run();
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            await completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }
}
