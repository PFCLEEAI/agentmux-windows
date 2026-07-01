using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AgentMux.Core.Ipc;
using AgentMux.Core.Models;
using AgentMux.Core.Persistence;
using AgentMux.Win.App.Controls;
using AgentMux.Win.App.Input;
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

            var window = new MainWindow(ShortcutSettings.Default());
            try
            {
                window.InitializeForSmokeTest();

                Assert.True(TerminalPaneSizeCalculator.TryCalculate(640, 340, out var calculatedCols, out var calculatedRows));
                Assert.Equal(80, calculatedCols);
                Assert.Equal(20, calculatedRows);
                Assert.False(TerminalPaneSizeCalculator.TryCalculate(double.NaN, 340, out _, out _));

                Assert.True(window.HasButtonForSmokeTest("Split right"));
                Assert.True(window.HasButtonForSmokeTest("Split down"));
                Assert.True(window.HasButtonForSmokeTest("Browser"));
                Assert.Equal(1, window.PaneCountForSmokeTest);
                Assert.Equal(1, window.RenderedTerminalPaneCountForSmokeTest);
                Assert.Equal(120, window.ActivePaneColsForSmokeTest);
                Assert.Equal(30, window.ActivePaneRowsForSmokeTest);

                Assert.True(window.ResizeActiveTerminalPaneForSmokeTest(640, 340));
                Assert.Equal(80, window.ActivePaneColsForSmokeTest);
                Assert.Equal(20, window.ActivePaneRowsForSmokeTest);
                Assert.False(window.ResizeActiveTerminalPaneForSmokeTest(640, 340));
                Assert.False(window.ResizeActiveTerminalPaneForSmokeTest(double.NaN, 340));

                var resizeResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.ResizeTerminal, new
                {
                    cols = 100,
                    rows = 30
                });
                Assert.True(resizeResponse.Ok, resizeResponse.Error);
                var resizeResult = System.Text.Json.JsonSerializer.SerializeToElement(resizeResponse.Result, AgentMuxJson.Options);
                Assert.True(resizeResult.GetProperty("resized").GetBoolean());
                Assert.True(resizeResult.GetProperty("changed").GetBoolean());
                Assert.Equal(100, resizeResult.GetProperty("cols").GetInt32());
                Assert.Equal(30, resizeResult.GetProperty("rows").GetInt32());
                Assert.Equal(100, window.ActivePaneColsForSmokeTest);
                Assert.Equal(30, window.ActivePaneRowsForSmokeTest);

                Assert.True(window.SplitActivePaneForSmokeTest(SplitDirection.Right));
                Assert.Equal(2, window.PaneCountForSmokeTest);
                Assert.Equal(2, window.RenderedTerminalPaneCountForSmokeTest);
                var activeAfterSplit = window.ActivePaneIdForSmokeTest;

                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.Tab, ModifierKeys.Control));
                Assert.NotEqual(activeAfterSplit, window.ActivePaneIdForSmokeTest);
                var leftPane = window.ActivePaneIdForSmokeTest;

                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.System, ModifierKeys.Control | ModifierKeys.Alt, Key.Right));
                Assert.Equal(activeAfterSplit, window.ActivePaneIdForSmokeTest);

                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.System, ModifierKeys.Control | ModifierKeys.Alt, Key.Right));
                Assert.Equal(activeAfterSplit, window.ActivePaneIdForSmokeTest);

                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.System, ModifierKeys.Control | ModifierKeys.Alt, Key.Left));
                Assert.Equal(leftPane, window.ActivePaneIdForSmokeTest);
                Assert.False(window.HandlePreviewKeyDownForSmokeTest(Key.A, ModifierKeys.Control | ModifierKeys.Alt));
                Assert.Equal(leftPane, window.ActivePaneIdForSmokeTest);

                Assert.True(window.SplitActivePaneForSmokeTest(SplitDirection.Down));
                var bottomLeftPane = window.ActivePaneIdForSmokeTest;
                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.System, ModifierKeys.Control | ModifierKeys.Alt, Key.Up));
                var topLeftPane = window.ActivePaneIdForSmokeTest;
                Assert.NotEqual(bottomLeftPane, topLeftPane);
                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.System, ModifierKeys.Control | ModifierKeys.Alt, Key.Down));
                Assert.Equal(bottomLeftPane, window.ActivePaneIdForSmokeTest);

                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
                Assert.True(window.IsActivePaneZoomedForSmokeTest);
                Assert.Equal(3, window.PaneCountForSmokeTest);
                Assert.Equal(1, window.RenderedTerminalPaneCountForSmokeTest);

                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.System, ModifierKeys.Control | ModifierKeys.Alt, Key.Right));
                Assert.False(window.IsActivePaneZoomedForSmokeTest);
                Assert.Equal(3, window.PaneCountForSmokeTest);
                Assert.Equal(3, window.RenderedTerminalPaneCountForSmokeTest);
                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.System, ModifierKeys.Control | ModifierKeys.Alt, Key.Left));

                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
                Assert.True(window.IsActivePaneZoomedForSmokeTest);
                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
                Assert.Equal(3, window.RenderedTerminalPaneCountForSmokeTest);
                Assert.False(window.HandlePreviewKeyDownForSmokeTest(Key.A, ModifierKeys.Control | ModifierKeys.Shift));

                window.SetActivePaneTextForSmokeTest("AGENTMUX_UI_SMOKE");
                Assert.Equal(3, window.RenderedTerminalPaneCountForSmokeTest);
                Assert.True(window.RenderedTextContainsForSmokeTest("AGENTMUX_UI_SMOKE"));

                window.AppendActivePaneTextForSmokeTest("_STREAM_APPEND");
                Assert.True(window.RenderedTextContainsForSmokeTest("AGENTMUX_UI_SMOKE_STREAM_APPEND"));

                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.System, ModifierKeys.Control | ModifierKeys.Alt, Key.Right));

                var browserUrl = window.OpenBrowserInActivePaneForSmokeTest("example.com");
                Assert.Equal("https://example.com/", browserUrl);
                Assert.Equal(1, window.RenderedBrowserPaneCountForSmokeTest);
                Assert.Equal(2, window.RenderedTerminalPaneCountForSmokeTest);
                Assert.True(window.RenderedTextContainsForSmokeTest(browserUrl));

                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.System, ModifierKeys.Control | ModifierKeys.Alt, Key.Left));
                Assert.Equal(1, window.RenderedBrowserPaneCountForSmokeTest);
                Assert.Equal(2, window.RenderedTerminalPaneCountForSmokeTest);
                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.System, ModifierKeys.Control | ModifierKeys.Alt, Key.Right));

                var safeUrl = window.OpenBrowserInActivePaneForSmokeTest("https://");
                Assert.Equal("about:blank", safeUrl);
                Assert.True(window.RenderedTextContainsForSmokeTest(safeUrl));

                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
                Assert.True(window.IsActivePaneZoomedForSmokeTest);
                Assert.Equal(1, window.RenderedBrowserPaneCountForSmokeTest);
                Assert.Equal(0, window.RenderedTerminalPaneCountForSmokeTest);

                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
                Assert.False(window.IsActivePaneZoomedForSmokeTest);
                Assert.Equal(1, window.RenderedBrowserPaneCountForSmokeTest);
                Assert.Equal(2, window.RenderedTerminalPaneCountForSmokeTest);

                Assert.Equal(1, window.CachedBrowserPaneViewCountForSmokeTest);
                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.X, ModifierKeys.Control | ModifierKeys.Shift));
                Assert.Equal(2, window.PaneCountForSmokeTest);
                Assert.Equal(0, window.RenderedBrowserPaneCountForSmokeTest);
                Assert.Equal(2, window.RenderedTerminalPaneCountForSmokeTest);
                Assert.Equal(0, window.CachedBrowserPaneViewCountForSmokeTest);

                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.X, ModifierKeys.Control | ModifierKeys.Shift));
                Assert.Equal(1, window.PaneCountForSmokeTest);
                Assert.Equal(1, window.RenderedTerminalPaneCountForSmokeTest);
                Assert.Equal(1, window.CachedTerminalPaneViewCountForSmokeTest);

                var lastPane = window.ActivePaneIdForSmokeTest;
                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.X, ModifierKeys.Control | ModifierKeys.Shift));
                Assert.Equal(1, window.PaneCountForSmokeTest);
                Assert.Equal(lastPane, window.ActivePaneIdForSmokeTest);

                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
                Assert.True(window.IsActivePaneZoomedForSmokeTest);
                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
                Assert.False(window.IsActivePaneZoomedForSmokeTest);
            }
            finally
            {
                window.Close();
            }

            Assert.True(ShortcutSettings.TryParseGesture("Ctrl+Alt+ArrowRight", out var arrowGesture));
            Assert.Equal(Key.Right, arrowGesture.Key);
            Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, arrowGesture.Modifiers);

            var shortcutsPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agentmux-shortcuts", $"{Guid.NewGuid():N}.json");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(shortcutsPath)!);
            await System.IO.File.WriteAllTextAsync(shortcutsPath, """
                {
                  "toggleZoom": "Ctrl+Shift+F11",
                  "focusRight": "Ctrl+Alt+L",
                  "closePane": "not-a-shortcut"
                }
                """);

            var customWindow = new MainWindow(ShortcutSettings.LoadFromFile(shortcutsPath));
            try
            {
                customWindow.InitializeForSmokeTest();
                Assert.True(customWindow.HandlePreviewKeyDownForSmokeTest(Key.F11, ModifierKeys.Control | ModifierKeys.Shift));
                Assert.True(customWindow.IsActivePaneZoomedForSmokeTest);
                Assert.False(customWindow.HandlePreviewKeyDownForSmokeTest(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
                Assert.True(customWindow.SplitActivePaneForSmokeTest(SplitDirection.Right));
                Assert.True(customWindow.HandlePreviewKeyDownForSmokeTest(Key.Tab, ModifierKeys.Control));
                var leftCustomPane = customWindow.ActivePaneIdForSmokeTest;
                Assert.True(customWindow.HandlePreviewKeyDownForSmokeTest(Key.System, ModifierKeys.Control | ModifierKeys.Alt, Key.L));
                Assert.NotEqual(leftCustomPane, customWindow.ActivePaneIdForSmokeTest);
                Assert.True(customWindow.HandlePreviewKeyDownForSmokeTest(Key.F11, ModifierKeys.Control | ModifierKeys.Shift));
                Assert.True(customWindow.HandlePreviewKeyDownForSmokeTest(Key.X, ModifierKeys.Control | ModifierKeys.Shift));
                Assert.Equal(1, customWindow.PaneCountForSmokeTest);
            }
            finally
            {
                customWindow.Close();
            }

            Assert.False(ShortcutSettings.TryParseGesture("A", out _));
            Assert.False(ShortcutSettings.TryParseGesture("Shift+A", out _));

            var brokenShortcutsPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agentmux-shortcuts", $"{Guid.NewGuid():N}.json");
            await System.IO.File.WriteAllTextAsync(brokenShortcutsPath, "{ not-json");
            var fallbackWindow = new MainWindow(ShortcutSettings.LoadFromFile(brokenShortcutsPath));
            try
            {
                fallbackWindow.InitializeForSmokeTest();
                Assert.True(fallbackWindow.HandlePreviewKeyDownForSmokeTest(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
                Assert.True(fallbackWindow.IsActivePaneZoomedForSmokeTest);
            }
            finally
            {
                fallbackWindow.Close();
            }

            await RunSessionRestoreSmokeAsync();
            await RunHostedWebView2RuntimeSmokeAsync();
            await RunActiveBrowserRpcSmokeAsync();
        });
    }

    private static async Task RunSessionRestoreSmokeAsync()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agentmux-session-smoke", Guid.NewGuid().ToString("N"));
        var store = new SessionSnapshotStore(root);

        try
        {
            var source = new MainWindow(
                ShortcutSettings.Default(),
                store,
                restoreSessionOnStartup: false,
                persistSession: true);
            try
            {
                source.InitializeForSmokeTest();

                var createdWorkspace = await source.HandleRpcForSmokeTestAsync(AgentMuxMethods.WorkspaceCreate, new
                {
                    title = "Persisted workspace",
                    cwd = root
                });
                Assert.True(createdWorkspace.Ok, createdWorkspace.Error);

                source.SetActivePaneTextForSmokeTest("AGENTMUX_SESSION_RESTORE_TEXT");
                Assert.True(source.SplitActivePaneForSmokeTest(SplitDirection.Right));
                var browserUrl = source.OpenBrowserInActivePaneForSmokeTest("example.com/session-restore");
                Assert.Equal("https://example.com/session-restore", browserUrl);
                Assert.Equal(PaneKind.Browser, source.ActivePaneKindForSmokeTest);

                await source.SaveSessionForSmokeTestAsync();
            }
            finally
            {
                source.Close();
            }

            var restored = new MainWindow(
                ShortcutSettings.Default(),
                store,
                restoreSessionOnStartup: true,
                persistSession: false);
            try
            {
                await restored.InitializeForSmokeTestAsync();

                Assert.Equal(2, restored.WorkspaceCountForSmokeTest);
                Assert.Equal("Persisted workspace", restored.ActiveWorkspaceTitleForSmokeTest);
                Assert.Equal(2, restored.PaneCountForSmokeTest);
                Assert.Equal(PaneKind.Browser, restored.ActivePaneKindForSmokeTest);
                Assert.Equal("https://example.com/session-restore", restored.ActivePaneUrlForSmokeTest);
                Assert.Equal(1, restored.RenderedBrowserPaneCountForSmokeTest);
                Assert.Equal(1, restored.RenderedTerminalPaneCountForSmokeTest);
                Assert.True(restored.RenderedTextContainsForSmokeTest("AGENTMUX_SESSION_RESTORE_TEXT"));
            }
            finally
            {
                restored.Close();
            }

            var corruptRoot = System.IO.Path.Combine(root, "corrupt");
            var corruptStore = new SessionSnapshotStore(corruptRoot);
            await System.IO.File.WriteAllTextAsync(corruptStore.FilePath, "{ not-json");
            var fallback = new MainWindow(
                ShortcutSettings.Default(),
                corruptStore,
                restoreSessionOnStartup: true,
                persistSession: false);
            try
            {
                await fallback.InitializeForSmokeTestAsync();

                Assert.Equal(1, fallback.WorkspaceCountForSmokeTest);
                Assert.Equal("Default", fallback.ActiveWorkspaceTitleForSmokeTest);
                Assert.Equal(1, fallback.PaneCountForSmokeTest);
                Assert.Equal(PaneKind.Terminal, fallback.ActivePaneKindForSmokeTest);
            }
            finally
            {
                fallback.Close();
            }
        }
        finally
        {
            if (System.IO.Directory.Exists(root))
            {
                System.IO.Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task RunActiveBrowserRpcSmokeAsync()
    {
        var window = new MainWindow(ShortcutSettings.Default())
        {
            Width = 900,
            Height = 560,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        try
        {
            window.InitializeForSmokeTest();
            Assert.Equal("about:blank", window.OpenBrowserInActivePaneForSmokeTest("about:blank"));
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            var setup = await WaitForRpcOkAsync(window, AgentMuxMethods.BrowserEval, new
            {
                script = """
                    document.body.innerHTML = '<input id="rpc-name"><button id="rpc-go">go</button><output id="rpc-result"></output><input id="rpc-typed"><output id="rpc-typed-result"></output><iframe id="rpc-frame" name="agentmux-child-frame" srcdoc="<p>agentmux-frame-smoke</p>"></iframe>';
                    window.__agentMuxRpcClicked = 0;
                    window.__agentMuxRpcMouseDown = 0;
                    window.__agentMuxRpcTypedInput = 0;
                    window.__agentMuxRpcPressedEnter = 0;
                    document.querySelector("#rpc-go").addEventListener("mousedown", () => {
                        window.__agentMuxRpcMouseDown += 1;
                    });
                    document.querySelector("#rpc-go").addEventListener("click", () => {
                        window.__agentMuxRpcClicked += 1;
                        document.querySelector("#rpc-result").textContent = document.querySelector("#rpc-name").value;
                    });
                    document.querySelector("#rpc-typed").addEventListener("input", () => {
                        window.__agentMuxRpcTypedInput += 1;
                    });
                    document.querySelector("#rpc-typed").addEventListener("keydown", event => {
                        if (event.key === "Enter") {
                            window.__agentMuxRpcPressedEnter += 1;
                            document.querySelector("#rpc-typed-result").textContent = document.querySelector("#rpc-typed").value;
                        }
                    });
                    true;
                    """
            });
            Assert.True(setup.GetProperty("result").GetBoolean());

            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserFill, new
            {
                selector = "#rpc-name",
                text = "agentmux-rpc-browser-smoke"
            }));
            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserClick, new
            {
                selector = "#rpc-go"
            }));
            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserType, new
            {
                selector = "#rpc-typed",
                text = "agentmux-rpc-type-smoke"
            }));
            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserPress, new
            {
                selector = "#rpc-typed",
                key = "Enter"
            }));

            var stateRoot = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserEval, new
            {
                script = """
                    (() => ({
                        value: document.querySelector("#rpc-name").value,
                        result: document.querySelector("#rpc-result").textContent,
                        clicked: window.__agentMuxRpcClicked,
                        mouseDown: window.__agentMuxRpcMouseDown,
                        typedValue: document.querySelector("#rpc-typed").value,
                        typedInput: window.__agentMuxRpcTypedInput,
                        pressedEnter: window.__agentMuxRpcPressedEnter,
                        typedResult: document.querySelector("#rpc-typed-result").textContent
                    }))()
                    """
            }));
            var state = stateRoot.GetProperty("result");
            Assert.Equal("agentmux-rpc-browser-smoke", state.GetProperty("value").GetString());
            Assert.Equal("agentmux-rpc-browser-smoke", state.GetProperty("result").GetString());
            Assert.Equal(1, state.GetProperty("clicked").GetInt32());
            Assert.True(state.GetProperty("mouseDown").GetInt32() >= 1);
            Assert.Equal("agentmux-rpc-type-smoke", state.GetProperty("typedValue").GetString());
            Assert.True(state.GetProperty("typedInput").GetInt32() >= 1);
            Assert.Equal(1, state.GetProperty("pressedEnter").GetInt32());
            Assert.Equal("agentmux-rpc-type-smoke", state.GetProperty("typedResult").GetString());

            var frameTree = await WaitForFrameTreeWithChildAsync(window, "agentmux-child-frame").ConfigureAwait(true);
            Assert.True(frameTree.GetProperty("ok").GetBoolean(), frameTree.ToString());
            var rootFrameTree = frameTree.GetProperty("frameTree");
            Assert.True(rootFrameTree.TryGetProperty("frame", out var rootFrame), frameTree.ToString());
            Assert.False(string.IsNullOrWhiteSpace(rootFrame.GetProperty("id").GetString()));
            Assert.True(TryFindFrameByName(rootFrameTree, "agentmux-child-frame", out var childFrame), frameTree.ToString());
            Assert.Equal("agentmux-child-frame", childFrame.GetProperty("name").GetString());
            Assert.False(string.IsNullOrWhiteSpace(childFrame.GetProperty("parentId").GetString()));
        }
        finally
        {
            window.Close();
        }
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
            terminal.ResizeTerminal(84, 24);
            await terminal.WaitForRuntimeGeometryForSmokeTestAsync(84, 24);

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
                document.body.innerHTML = '<input id="name"><button id="go">go</button><output id="result"></output><input id="typed"><output id="typed-result"></output>';
                window.__agentMuxClicked = 0;
                window.__agentMuxPointerDown = 0;
                window.__agentMuxMouseDown = 0;
                window.__agentMuxMouseUp = 0;
                window.__agentMuxTypedInput = 0;
                window.__agentMuxPressedEnter = 0;
                window.__agentMuxKeyUpEnter = 0;
                document.querySelector("#go").addEventListener("pointerdown", () => {
                    window.__agentMuxPointerDown += 1;
                });
                document.querySelector("#go").addEventListener("mousedown", () => {
                    window.__agentMuxMouseDown += 1;
                });
                document.querySelector("#go").addEventListener("mouseup", () => {
                    window.__agentMuxMouseUp += 1;
                });
                document.querySelector("#go").addEventListener("click", () => {
                    window.__agentMuxClicked += 1;
                    document.querySelector("#result").textContent = document.querySelector("#name").value;
                });
                document.querySelector("#typed").addEventListener("input", () => {
                    window.__agentMuxTypedInput += 1;
                });
                document.querySelector("#typed").addEventListener("keydown", event => {
                    if (event.key === "Enter") {
                        window.__agentMuxPressedEnter += 1;
                        document.querySelector("#typed-result").textContent = document.querySelector("#typed").value;
                    }
                });
                document.querySelector("#typed").addEventListener("keyup", event => {
                    if (event.key === "Enter") {
                        window.__agentMuxKeyUpEnter += 1;
                    }
                });
                true;
                """);
            Assert.Equal("true", setupResult);

            AssertBrowserOk(await browser.FillAsync("#name", "agentmux-browser-smoke"));
            AssertBrowserOk(await browser.ClickAsync("#go"));
            AssertBrowserOk(await browser.TypeAsync("#typed", "agentmux-key-smoke"));
            AssertBrowserOk(await browser.PressAsync("Enter", "#typed"));

            var stateJson = await browser.EvaluateScriptAsync("""
                (() => ({
                    value: document.querySelector("#name").value,
                    result: document.querySelector("#result").textContent,
                    clicked: window.__agentMuxClicked,
                    pointerDown: window.__agentMuxPointerDown,
                    mouseDown: window.__agentMuxMouseDown,
                    mouseUp: window.__agentMuxMouseUp,
                    typedValue: document.querySelector("#typed").value,
                    typedInput: window.__agentMuxTypedInput,
                    pressedEnter: window.__agentMuxPressedEnter,
                    keyUpEnter: window.__agentMuxKeyUpEnter,
                    typedResult: document.querySelector("#typed-result").textContent
                }))()
                """);
            using (var state = System.Text.Json.JsonDocument.Parse(stateJson))
            {
                var root = state.RootElement;
                Assert.Equal("agentmux-browser-smoke", root.GetProperty("value").GetString());
                Assert.Equal("agentmux-browser-smoke", root.GetProperty("result").GetString());
                Assert.Equal(1, root.GetProperty("clicked").GetInt32());
                Assert.True(root.GetProperty("pointerDown").GetInt32() >= 1);
                Assert.True(root.GetProperty("mouseDown").GetInt32() >= 1);
                Assert.True(root.GetProperty("mouseUp").GetInt32() >= 1);
                Assert.Equal("agentmux-key-smoke", root.GetProperty("typedValue").GetString());
                Assert.True(root.GetProperty("typedInput").GetInt32() >= 1);
                Assert.Equal(1, root.GetProperty("pressedEnter").GetInt32());
                Assert.Equal(1, root.GetProperty("keyUpEnter").GetInt32());
                Assert.Equal("agentmux-key-smoke", root.GetProperty("typedResult").GetString());
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

    private static System.Text.Json.JsonElement AssertRpcOk(AgentMuxResponse response)
    {
        Assert.True(response.Ok, response.Error);
        var result = System.Text.Json.JsonSerializer.SerializeToElement(response.Result, AgentMuxJson.Options);
        Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
        return result.Clone();
    }

    private static async Task<System.Text.Json.JsonElement> WaitForRpcOkAsync(MainWindow window, string method, object parameters)
    {
        AgentMuxResponse? lastResponse = null;
        System.Text.Json.JsonElement? lastResult = null;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            lastResponse = await window.HandleRpcForSmokeTestAsync(method, parameters).ConfigureAwait(true);
            if (lastResponse.Ok)
            {
                var result = System.Text.Json.JsonSerializer.SerializeToElement(lastResponse.Result, AgentMuxJson.Options);
                lastResult = result.Clone();
                if (result.TryGetProperty("ok", out var okElement) && okElement.GetBoolean())
                {
                    return result.Clone();
                }
            }

            await Task.Delay(250).ConfigureAwait(true);
        }

        AssertRpcOk(lastResponse ?? AgentMuxResponse.Failure("smoke", "RPC did not complete"));
        return lastResult ?? default;
    }

    private static async Task<System.Text.Json.JsonElement> WaitForFrameTreeWithChildAsync(MainWindow window, string frameName)
    {
        System.Text.Json.JsonElement? lastResult = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var response = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserFrameTree).ConfigureAwait(true);
            if (response.Ok)
            {
                var result = System.Text.Json.JsonSerializer.SerializeToElement(response.Result, AgentMuxJson.Options);
                lastResult = result.Clone();
                if (result.TryGetProperty("ok", out var ok)
                    && ok.GetBoolean()
                    && result.TryGetProperty("frameTree", out var frameTree)
                    && TryFindFrameByName(frameTree, frameName, out _))
                {
                    return result.Clone();
                }
            }

            await Task.Delay(250).ConfigureAwait(true);
        }

        throw new InvalidOperationException($"Browser frame tree did not contain a child frame. Last result: {lastResult?.ToString() ?? "<none>"}");
    }

    private static bool TryFindFrameByName(System.Text.Json.JsonElement frameTree, string frameName, out System.Text.Json.JsonElement frame)
    {
        frame = default;
        if (frameTree.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return false;
        }

        if (frameTree.TryGetProperty("frame", out var candidate)
            && candidate.ValueKind == System.Text.Json.JsonValueKind.Object
            && candidate.TryGetProperty("name", out var name)
            && string.Equals(name.GetString(), frameName, StringComparison.Ordinal))
        {
            frame = candidate.Clone();
            return true;
        }

        if (!frameTree.TryGetProperty("childFrames", out var childFrames)
            || childFrames.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return false;
        }

        foreach (var child in childFrames.EnumerateArray())
        {
            if (TryFindFrameByName(child, frameName, out frame))
            {
                return true;
            }
        }

        return false;
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
