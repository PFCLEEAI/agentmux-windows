using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AgentMux.Core.Ipc;
using AgentMux.Core.Models;
using AgentMux.Core.Persistence;
using AgentMux.Win.App.Controls;
using AgentMux.Win.App.Input;
using AgentMux.Win.App.Notifications;
using AgentMux.Win.App.Views;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AgentMux.App.Tests;

public sealed class MainWindowSmokeTests
{
    private static readonly string SmokeArtifactDirectoryPath = ResolveSmokeArtifactDirectory();

    [Fact]
    public void NativeToastXmlEscapesAndBoundsNotificationText()
    {
        var xml = WindowsNativeToastService.BuildToastXml(new NativeToastRequest(
            "notification-smoke",
            "workspace-smoke",
            "pane-smoke",
            "Title <&> \" '",
            "Sub\r\nTitle",
            new string('x', 700) + "<secret>"));

        Assert.Contains("Title &lt;&amp;&gt; &quot; &apos;", xml, StringComparison.Ordinal);
        Assert.Contains("<text>Sub  Title</text>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<secret>", xml, StringComparison.Ordinal);
        Assert.True(xml.Length < 1000);
    }

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

            await RunNotificationSmokeAsync();
            await RunReadScreenSmokeAsync();
            await RunSendKeySmokeAsync();
            await RunWorkspaceSwitcherSmokeAsync();
            await RunSurfaceTabsSmokeAsync();
            await RunSessionRestoreSmokeAsync();
            await RunHostedWebView2RuntimeSmokeAsync();
            await RunActiveBrowserRpcSmokeAsync();
        });
    }

    private static async Task RunNotificationSmokeAsync()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agentmux-notification-smoke", Guid.NewGuid().ToString("N"));
        var store = new SessionSnapshotStore(root);
        var firstToken = $"agentmux-osc-secret-{Guid.NewGuid():N}";
        var secondToken = $"agentmux-osc-clear-secret-{Guid.NewGuid():N}";
        var thirdToken = $"agentmux-osc-rpc-clear-secret-{Guid.NewGuid():N}";
        var rpcToken = $"agentmux-rpc-notify-secret-{Guid.NewGuid():N}";
        var nativeToasts = new RecordingNativeToastService();

        try
        {
            var window = new MainWindow(
                ShortcutSettings.Default(),
                store,
                restoreSessionOnStartup: false,
                persistSession: true,
                nativeToastService: nativeToasts);
            try
            {
                window.InitializeForSmokeTest();
                Assert.Equal("Notifications (0)", window.NotificationButtonContentForSmokeTest);

                var rightPaneCreated = window.SplitActivePaneForSmokeTest(SplitDirection.Right);
                Assert.True(rightPaneCreated);
                var rightPane = window.ActivePaneIdForSmokeTest;
                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.Tab, ModifierKeys.Control));
                var notifiedPane = window.ActivePaneIdForSmokeTest;
                Assert.NotEqual(rightPane, notifiedPane);

                window.AppendActivePaneTextForSmokeTest($"visible-before\u001b]99;t=Codex;s=Plan;b={firstToken}\u0007visible-after");
                Assert.Single(nativeToasts.Requests);
                var firstToast = nativeToasts.Requests.Single();
                Assert.Equal("Codex", firstToast.Title);
                Assert.Equal("Plan", firstToast.Subtitle);
                Assert.Equal(firstToken, firstToast.Body);
                Assert.Equal(notifiedPane, firstToast.PaneId);
                Assert.False(string.IsNullOrWhiteSpace(firstToast.WorkspaceId));
                Assert.Equal(1, window.ActiveWorkspaceUnreadCountForSmokeTest);
                Assert.True(window.ActivePaneHasUnreadNotificationForSmokeTest);
                Assert.Contains("visible-beforevisible-after", window.ActivePaneLastScreenTextForSmokeTest, StringComparison.Ordinal);
                Assert.DoesNotContain(firstToken, window.ActivePaneLastScreenTextForSmokeTest, StringComparison.Ordinal);
                Assert.DoesNotContain("\u001b]99", window.ActivePaneLastScreenTextForSmokeTest, StringComparison.Ordinal);
                Assert.Equal("Notifications (1)", window.NotificationButtonContentForSmokeTest);
                Assert.False(window.IsNotificationPanelOpenForSmokeTest);

                window.OpenNotificationCenterForSmokeTest();
                Assert.True(window.IsNotificationPanelOpenForSmokeTest);
                Assert.Equal(1, window.RenderedNotificationItemCountForSmokeTest);
                Assert.True(window.NotificationCenterContainsTextForSmokeTest("Codex"));
                Assert.True(window.NotificationCenterContainsTextForSmokeTest("Plan"));
                Assert.True(window.NotificationCenterContainsTextForSmokeTest(firstToken));
                Assert.True(window.NotificationCenterContainsTextForSmokeTest("unread"));
                Assert.True(window.NotificationCenterContainsTextForSmokeTest("Default"));
                Assert.Equal(1, window.ActiveWorkspaceUnreadCountForSmokeTest);
                window.CloseNotificationCenterForSmokeTest();
                Assert.False(window.IsNotificationPanelOpenForSmokeTest);
                Assert.Equal(1, window.ActiveWorkspaceUnreadCountForSmokeTest);
                window.OpenNotificationCenterForSmokeTest();
                Assert.True(window.IsNotificationPanelOpenForSmokeTest);

                var listResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.NotificationsList, new { limit = 10 });
                Assert.True(listResponse.Ok, listResponse.Error);
                var listRoot = System.Text.Json.JsonSerializer.SerializeToElement(listResponse.Result, AgentMuxJson.Options);
                Assert.Equal(1, listRoot.GetProperty("unreadCount").GetInt32());
                var notification = listRoot.GetProperty("notifications").EnumerateArray().Single();
                Assert.Equal(firstToken, notification.GetProperty("body").GetString());
                Assert.Equal("Codex", notification.GetProperty("title").GetString());
                Assert.Equal("Plan", notification.GetProperty("subtitle").GetString());
                Assert.Equal(notifiedPane, notification.GetProperty("paneId").GetString());
                Assert.False(notification.GetProperty("isRead").GetBoolean());

                Assert.True(window.HandlePreviewKeyDownForSmokeTest(Key.System, ModifierKeys.Control | ModifierKeys.Alt, Key.Right));
                Assert.Equal(rightPane, window.ActivePaneIdForSmokeTest);

                await window.JumpLatestNotificationForSmokeTestAsync();
                Assert.Single(nativeToasts.Requests);
                Assert.Equal(notifiedPane, window.ActivePaneIdForSmokeTest);
                Assert.Equal(0, window.ActiveWorkspaceUnreadCountForSmokeTest);
                Assert.False(window.ActivePaneHasUnreadNotificationForSmokeTest);
                Assert.Equal("Notifications (0)", window.NotificationButtonContentForSmokeTest);
                Assert.True(window.NotificationCenterContainsTextForSmokeTest("read"));

                window.AppendActivePaneTextForSmokeTest($"again\u001b]777;notify;Codex;{secondToken}\u001b\\done");
                Assert.Equal(2, nativeToasts.Requests.Count);
                Assert.Equal(secondToken, nativeToasts.Requests[1].Body);
                Assert.Equal(1, window.ActiveWorkspaceUnreadCountForSmokeTest);
                Assert.Equal("Notifications (1)", window.NotificationButtonContentForSmokeTest);
                Assert.True(window.NotificationCenterContainsTextForSmokeTest(secondToken));

                window.ClearUnreadNotificationsForSmokeTest();
                Assert.Equal(2, nativeToasts.Requests.Count);
                Assert.Equal(0, window.ActiveWorkspaceUnreadCountForSmokeTest);
                Assert.False(window.ActivePaneHasUnreadNotificationForSmokeTest);
                Assert.Equal("Notifications (0)", window.NotificationButtonContentForSmokeTest);
                Assert.True(window.NotificationCenterContainsTextForSmokeTest("read"));

                window.AppendActivePaneTextForSmokeTest($"third\u001b]99;t=Codex;b={thirdToken}\u0007done");
                Assert.Equal(3, nativeToasts.Requests.Count);
                Assert.Equal(thirdToken, nativeToasts.Requests[2].Body);
                Assert.Equal(1, window.ActiveWorkspaceUnreadCountForSmokeTest);
                var rpcClearResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.NotificationsClear);
                Assert.True(rpcClearResponse.Ok, rpcClearResponse.Error);
                var rpcClearRoot = System.Text.Json.JsonSerializer.SerializeToElement(rpcClearResponse.Result, AgentMuxJson.Options);
                Assert.Equal(1, rpcClearRoot.GetProperty("cleared").GetInt32());
                Assert.Equal(0, window.ActiveWorkspaceUnreadCountForSmokeTest);
                Assert.Equal(3, nativeToasts.Requests.Count);

                var rpcNotifyResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.Notify, new
                {
                    title = "RPC",
                    subtitle = "Toast",
                    body = rpcToken
                });
                Assert.True(rpcNotifyResponse.Ok, rpcNotifyResponse.Error);
                Assert.Equal(4, nativeToasts.Requests.Count);
                Assert.Equal("RPC", nativeToasts.Requests[3].Title);
                Assert.Equal("Toast", nativeToasts.Requests[3].Subtitle);
                Assert.Equal(rpcToken, nativeToasts.Requests[3].Body);

                var finalClearResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.NotificationsClear);
                Assert.True(finalClearResponse.Ok, finalClearResponse.Error);
                Assert.Equal(4, nativeToasts.Requests.Count);

                await window.SaveSessionForSmokeTestAsync();
                var snapshotText = await System.IO.File.ReadAllTextAsync(store.FilePath);
                Assert.DoesNotContain(firstToken, snapshotText, StringComparison.Ordinal);
                Assert.DoesNotContain(secondToken, snapshotText, StringComparison.Ordinal);
                Assert.DoesNotContain(thirdToken, snapshotText, StringComparison.Ordinal);
                Assert.DoesNotContain(rpcToken, snapshotText, StringComparison.Ordinal);
                Assert.DoesNotContain("hasUnreadNotification\":true", snapshotText, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            if (System.IO.Directory.Exists(root))
            {
                await DeleteDirectoryWithRetryAsync(root);
            }
        }

        var throwingWindow = new MainWindow(
            ShortcutSettings.Default(),
            sessionStore: null,
            restoreSessionOnStartup: false,
            persistSession: false,
            nativeToastService: new ThrowingNativeToastService());
        try
        {
            throwingWindow.InitializeForSmokeTest();
            throwingWindow.AppendActivePaneTextForSmokeTest("throwing\u001b]99;t=Throw;b=still-local\u0007done");
            Assert.Equal(1, throwingWindow.ActiveWorkspaceUnreadCountForSmokeTest);
            Assert.True(throwingWindow.ActivePaneHasUnreadNotificationForSmokeTest);
        }
        finally
        {
            throwingWindow.Close();
        }
    }

    private static async Task RunSurfaceTabsSmokeAsync()
    {
        var window = new MainWindow(ShortcutSettings.Default());
        try
        {
            window.InitializeForSmokeTest();

            Assert.True(window.HasButtonForSmokeTest("+ Surface"));
            Assert.Equal(1, window.SurfaceCountForSmokeTest);
            Assert.Equal(0, window.ActiveSurfaceIndexForSmokeTest);
            Assert.Equal("Terminal", window.ActiveSurfaceTitleForSmokeTest);
            Assert.Equal(1, window.RenderedSurfaceTabCountForSmokeTest);
            Assert.True(window.SurfaceTabsContainTextForSmokeTest("Terminal"));

            window.SetActivePaneTextForSmokeTest("AGENTMUX_SURFACE_ONE");
            var firstPaneId = window.ActivePaneIdForSmokeTest;

            var initialListResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.SurfaceList);
            Assert.True(initialListResponse.Ok, initialListResponse.Error);
            var initialList = System.Text.Json.JsonSerializer.SerializeToElement(initialListResponse.Result, AgentMuxJson.Options);
            Assert.Equal(0, initialList.GetProperty("activeSurfaceIndex").GetInt32());
            var initialSurface = initialList.GetProperty("surfaces").EnumerateArray().Single();
            Assert.True(initialSurface.GetProperty("isActive").GetBoolean());
            Assert.Equal(firstPaneId, initialSurface.GetProperty("activePaneId").GetString());
            Assert.Equal(1, initialSurface.GetProperty("paneCount").GetInt32());
            Assert.Equal(0, initialSurface.GetProperty("browserPaneCount").GetInt32());

            var createResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.SurfaceCreate, new
            {
                title = "Scratch"
            });
            Assert.True(createResponse.Ok, createResponse.Error);
            var createResult = System.Text.Json.JsonSerializer.SerializeToElement(createResponse.Result, AgentMuxJson.Options);
            Assert.True(createResult.GetProperty("created").GetBoolean());
            var createdSurface = createResult.GetProperty("surface");
            var createdSurfaceId = createdSurface.GetProperty("id").GetString();
            Assert.Equal("Scratch", createdSurface.GetProperty("title").GetString());
            Assert.Equal(1, createdSurface.GetProperty("index").GetInt32());
            Assert.True(createdSurface.GetProperty("isActive").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(createdSurfaceId));

            Assert.Equal(2, window.SurfaceCountForSmokeTest);
            Assert.Equal(1, window.ActiveSurfaceIndexForSmokeTest);
            Assert.Equal("Scratch", window.ActiveSurfaceTitleForSmokeTest);
            Assert.Equal(2, window.RenderedSurfaceTabCountForSmokeTest);
            Assert.True(window.SurfaceTabsContainTextForSmokeTest("Scratch"));
            Assert.Equal(1, window.PaneCountForSmokeTest);
            Assert.False(window.RenderedTextContainsForSmokeTest("AGENTMUX_SURFACE_ONE"));

            window.SetActivePaneTextForSmokeTest("AGENTMUX_SURFACE_TWO");
            var secondPaneId = window.ActivePaneIdForSmokeTest;
            Assert.NotEqual(firstPaneId, secondPaneId);
            Assert.True(window.RenderedTextContainsForSmokeTest("AGENTMUX_SURFACE_TWO"));

            var selectFirstResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.SurfaceSelect, new
            {
                index = 0
            });
            Assert.True(selectFirstResponse.Ok, selectFirstResponse.Error);
            var selectFirst = System.Text.Json.JsonSerializer.SerializeToElement(selectFirstResponse.Result, AgentMuxJson.Options);
            Assert.True(selectFirst.GetProperty("selected").GetBoolean());
            Assert.Equal(0, window.ActiveSurfaceIndexForSmokeTest);
            Assert.Equal("Terminal", window.ActiveSurfaceTitleForSmokeTest);
            Assert.Equal(firstPaneId, window.ActivePaneIdForSmokeTest);
            Assert.True(window.RenderedTextContainsForSmokeTest("AGENTMUX_SURFACE_ONE"));
            Assert.False(window.RenderedTextContainsForSmokeTest("AGENTMUX_SURFACE_TWO"));

            var invalidSelectResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.SurfaceSelect, new
            {
                index = 99
            });
            Assert.True(invalidSelectResponse.Ok, invalidSelectResponse.Error);
            var invalidSelect = System.Text.Json.JsonSerializer.SerializeToElement(invalidSelectResponse.Result, AgentMuxJson.Options);
            Assert.False(invalidSelect.GetProperty("selected").GetBoolean());
            Assert.Equal("index out of range", invalidSelect.GetProperty("reason").GetString());
            Assert.Equal(0, window.ActiveSurfaceIndexForSmokeTest);

            var ambiguousSelectResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.SurfaceSelect, new
            {
                index = 0,
                id = createdSurfaceId
            });
            Assert.True(ambiguousSelectResponse.Ok, ambiguousSelectResponse.Error);
            var ambiguousSelect = System.Text.Json.JsonSerializer.SerializeToElement(ambiguousSelectResponse.Result, AgentMuxJson.Options);
            Assert.False(ambiguousSelect.GetProperty("selected").GetBoolean());
            Assert.Equal("provide index or id, not both", ambiguousSelect.GetProperty("reason").GetString());
            Assert.Equal(0, window.ActiveSurfaceIndexForSmokeTest);

            var selectSecondResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.SurfaceSelect, new
            {
                id = createdSurfaceId
            });
            Assert.True(selectSecondResponse.Ok, selectSecondResponse.Error);
            var selectSecond = System.Text.Json.JsonSerializer.SerializeToElement(selectSecondResponse.Result, AgentMuxJson.Options);
            Assert.True(selectSecond.GetProperty("selected").GetBoolean());
            Assert.Equal(1, window.ActiveSurfaceIndexForSmokeTest);
            Assert.Equal("Scratch", window.ActiveSurfaceTitleForSmokeTest);
            Assert.Equal(secondPaneId, window.ActivePaneIdForSmokeTest);
            Assert.True(window.RenderedTextContainsForSmokeTest("AGENTMUX_SURFACE_TWO"));
            Assert.False(window.RenderedTextContainsForSmokeTest("AGENTMUX_SURFACE_ONE"));
            Assert.True(window.CachedTerminalPaneViewCountForSmokeTest >= 2);

            var finalListResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.SurfaceList);
            Assert.True(finalListResponse.Ok, finalListResponse.Error);
            var finalList = System.Text.Json.JsonSerializer.SerializeToElement(finalListResponse.Result, AgentMuxJson.Options);
            Assert.Equal(1, finalList.GetProperty("activeSurfaceIndex").GetInt32());
            Assert.Equal(2, finalList.GetProperty("surfaces").GetArrayLength());
            var finalSurfaces = finalList.GetProperty("surfaces").EnumerateArray().ToArray();
            Assert.Contains(finalSurfaces, surface =>
                surface.GetProperty("id").GetString() == createdSurfaceId
                && surface.GetProperty("isActive").GetBoolean());
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task RunWorkspaceSwitcherSmokeAsync()
    {
        var window = new MainWindow(ShortcutSettings.Default());
        try
        {
            window.InitializeForSmokeTest();

            Assert.Equal(1, window.WorkspaceCountForSmokeTest);
            Assert.Equal(0, window.ActiveWorkspaceIndexForSmokeTest);
            Assert.Equal(0, window.WorkspaceListSelectedIndexForSmokeTest);
            Assert.Equal("Default", window.ActiveWorkspaceTitleForSmokeTest);

            window.SetActivePaneTextForSmokeTest("AGENTMUX_WORKSPACE_ONE");
            var firstPaneId = window.ActivePaneIdForSmokeTest;

            var initialListResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.WorkspaceList);
            Assert.True(initialListResponse.Ok, initialListResponse.Error);
            var initialList = System.Text.Json.JsonSerializer.SerializeToElement(initialListResponse.Result, AgentMuxJson.Options);
            Assert.Equal(0, initialList.GetProperty("activeWorkspaceIndex").GetInt32());
            var initialWorkspace = initialList.GetProperty("workspaces").EnumerateArray().Single();
            Assert.True(initialWorkspace.GetProperty("isActive").GetBoolean());
            Assert.Equal("Default", initialWorkspace.GetProperty("title").GetString());
            Assert.Equal(0, initialWorkspace.GetProperty("index").GetInt32());
            Assert.Equal(firstPaneId, initialWorkspace.GetProperty("activePaneId").GetString());
            Assert.Equal(1, initialWorkspace.GetProperty("surfaceCount").GetInt32());
            Assert.Equal(0, initialWorkspace.GetProperty("activeSurfaceIndex").GetInt32());
            Assert.Equal("Terminal", initialWorkspace.GetProperty("activeSurfaceTitle").GetString());
            Assert.Equal(1, initialWorkspace.GetProperty("paneCount").GetInt32());
            Assert.Equal(0, initialWorkspace.GetProperty("browserPaneCount").GetInt32());
            Assert.False(initialWorkspace.TryGetProperty("surfaces", out _));
            Assert.False(initialWorkspace.TryGetProperty("root", out _));
            Assert.False(initialWorkspace.TryGetProperty("latestNotification", out _));
            Assert.False(initialWorkspace.TryGetProperty("gitBranch", out _));
            Assert.False(initialWorkspace.TryGetProperty("isGitDirty", out _));

            var createResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.WorkspaceCreate, new
            {
                title = "API",
                cwd = "C:\\src\\api"
            });
            Assert.True(createResponse.Ok, createResponse.Error);
            var createResult = System.Text.Json.JsonSerializer.SerializeToElement(createResponse.Result, AgentMuxJson.Options);
            Assert.True(createResult.GetProperty("created").GetBoolean());
            Assert.Equal(1, createResult.GetProperty("activeWorkspaceIndex").GetInt32());
            var createdWorkspace = createResult.GetProperty("workspace");
            var createdWorkspaceId = createdWorkspace.GetProperty("id").GetString();
            Assert.Equal("API", createdWorkspace.GetProperty("title").GetString());
            Assert.Equal(1, createdWorkspace.GetProperty("index").GetInt32());
            Assert.True(createdWorkspace.GetProperty("isActive").GetBoolean());
            Assert.Equal("C:\\src\\api", createdWorkspace.GetProperty("workingDirectory").GetString());
            Assert.False(string.IsNullOrWhiteSpace(createdWorkspaceId));

            Assert.Equal(2, window.WorkspaceCountForSmokeTest);
            Assert.Equal(1, window.ActiveWorkspaceIndexForSmokeTest);
            Assert.Equal(1, window.WorkspaceListSelectedIndexForSmokeTest);
            Assert.Equal("API", window.ActiveWorkspaceTitleForSmokeTest);
            Assert.False(window.RenderedTextContainsForSmokeTest("AGENTMUX_WORKSPACE_ONE"));

            window.SetActivePaneTextForSmokeTest("AGENTMUX_WORKSPACE_TWO");
            var secondPaneId = window.ActivePaneIdForSmokeTest;
            Assert.NotEqual(firstPaneId, secondPaneId);
            Assert.True(window.RenderedTextContainsForSmokeTest("AGENTMUX_WORKSPACE_TWO"));

            var selectFirstResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.WorkspaceSelect, new
            {
                index = 0
            });
            Assert.True(selectFirstResponse.Ok, selectFirstResponse.Error);
            var selectFirst = System.Text.Json.JsonSerializer.SerializeToElement(selectFirstResponse.Result, AgentMuxJson.Options);
            Assert.True(selectFirst.GetProperty("selected").GetBoolean());
            Assert.Equal(0, window.ActiveWorkspaceIndexForSmokeTest);
            Assert.Equal(0, window.WorkspaceListSelectedIndexForSmokeTest);
            Assert.Equal("Default", window.ActiveWorkspaceTitleForSmokeTest);
            Assert.Equal(firstPaneId, window.ActivePaneIdForSmokeTest);
            Assert.True(window.RenderedTextContainsForSmokeTest("AGENTMUX_WORKSPACE_ONE"));
            Assert.False(window.RenderedTextContainsForSmokeTest("AGENTMUX_WORKSPACE_TWO"));

            var invalidSelectResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.WorkspaceSelect, new
            {
                index = 99
            });
            Assert.True(invalidSelectResponse.Ok, invalidSelectResponse.Error);
            var invalidSelect = System.Text.Json.JsonSerializer.SerializeToElement(invalidSelectResponse.Result, AgentMuxJson.Options);
            Assert.False(invalidSelect.GetProperty("selected").GetBoolean());
            Assert.Equal("index out of range", invalidSelect.GetProperty("reason").GetString());
            Assert.Equal(0, window.ActiveWorkspaceIndexForSmokeTest);
            Assert.Equal(0, window.WorkspaceListSelectedIndexForSmokeTest);
            Assert.True(window.RenderedTextContainsForSmokeTest("AGENTMUX_WORKSPACE_ONE"));

            var missingTargetResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.WorkspaceSelect, new { });
            Assert.True(missingTargetResponse.Ok, missingTargetResponse.Error);
            var missingTarget = System.Text.Json.JsonSerializer.SerializeToElement(missingTargetResponse.Result, AgentMuxJson.Options);
            Assert.False(missingTarget.GetProperty("selected").GetBoolean());
            Assert.Equal("index or id is required", missingTarget.GetProperty("reason").GetString());
            Assert.Equal(0, window.ActiveWorkspaceIndexForSmokeTest);

            var missingWorkspaceResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.WorkspaceSelect, new
            {
                id = "missing-workspace"
            });
            Assert.True(missingWorkspaceResponse.Ok, missingWorkspaceResponse.Error);
            var missingWorkspace = System.Text.Json.JsonSerializer.SerializeToElement(missingWorkspaceResponse.Result, AgentMuxJson.Options);
            Assert.False(missingWorkspace.GetProperty("selected").GetBoolean());
            Assert.Equal("workspace not found", missingWorkspace.GetProperty("reason").GetString());
            Assert.Equal(0, window.ActiveWorkspaceIndexForSmokeTest);

            var ambiguousSelectResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.WorkspaceSelect, new
            {
                index = 0,
                id = createdWorkspaceId
            });
            Assert.True(ambiguousSelectResponse.Ok, ambiguousSelectResponse.Error);
            var ambiguousSelect = System.Text.Json.JsonSerializer.SerializeToElement(ambiguousSelectResponse.Result, AgentMuxJson.Options);
            Assert.False(ambiguousSelect.GetProperty("selected").GetBoolean());
            Assert.Equal("provide index or id, not both", ambiguousSelect.GetProperty("reason").GetString());
            Assert.Equal(0, window.ActiveWorkspaceIndexForSmokeTest);

            var selectSecondResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.WorkspaceSelect, new
            {
                id = createdWorkspaceId
            });
            Assert.True(selectSecondResponse.Ok, selectSecondResponse.Error);
            var selectSecond = System.Text.Json.JsonSerializer.SerializeToElement(selectSecondResponse.Result, AgentMuxJson.Options);
            Assert.True(selectSecond.GetProperty("selected").GetBoolean());
            Assert.Equal(1, window.ActiveWorkspaceIndexForSmokeTest);
            Assert.Equal(1, window.WorkspaceListSelectedIndexForSmokeTest);
            Assert.Equal("API", window.ActiveWorkspaceTitleForSmokeTest);
            Assert.Equal(secondPaneId, window.ActivePaneIdForSmokeTest);
            Assert.True(window.RenderedTextContainsForSmokeTest("AGENTMUX_WORKSPACE_TWO"));
            Assert.False(window.RenderedTextContainsForSmokeTest("AGENTMUX_WORKSPACE_ONE"));
            Assert.True(window.CachedTerminalPaneViewCountForSmokeTest >= 2);

            var finalListResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.WorkspaceList);
            Assert.True(finalListResponse.Ok, finalListResponse.Error);
            var finalList = System.Text.Json.JsonSerializer.SerializeToElement(finalListResponse.Result, AgentMuxJson.Options);
            Assert.Equal(1, finalList.GetProperty("activeWorkspaceIndex").GetInt32());
            Assert.Equal(2, finalList.GetProperty("workspaces").GetArrayLength());
            var finalWorkspaces = finalList.GetProperty("workspaces").EnumerateArray().ToArray();
            Assert.Equal(1, finalWorkspaces.Count(workspace => workspace.GetProperty("isActive").GetBoolean()));
            Assert.Contains(finalWorkspaces, workspace =>
                workspace.GetProperty("id").GetString() == createdWorkspaceId
                && workspace.GetProperty("isActive").GetBoolean());
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task RunReadScreenSmokeAsync()
    {
        var window = new MainWindow(ShortcutSettings.Default());
        try
        {
            window.InitializeForSmokeTest();
            window.SetActivePaneTextForSmokeTest("line-one\nline-two\nline-three\n");
            var terminalPaneId = window.ActivePaneIdForSmokeTest;

            var fullResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.ReadScreen);
            Assert.True(fullResponse.Ok, fullResponse.Error);
            var fullRead = System.Text.Json.JsonSerializer.SerializeToElement(fullResponse.Result, AgentMuxJson.Options);
            Assert.Equal("line-one\nline-two\nline-three\n", fullRead.GetProperty("text").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, fullRead.GetProperty("lines").ValueKind);
            Assert.False(fullRead.GetProperty("truncated").GetBoolean());
            Assert.Equal(terminalPaneId, fullRead.GetProperty("paneId").GetString());
            Assert.Equal("terminal", fullRead.GetProperty("paneKind").GetString());

            var tailResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.ReadScreen, new
            {
                lines = 2
            });
            Assert.True(tailResponse.Ok, tailResponse.Error);
            var tailRead = System.Text.Json.JsonSerializer.SerializeToElement(tailResponse.Result, AgentMuxJson.Options);
            Assert.Equal("line-two\nline-three", tailRead.GetProperty("text").GetString());
            Assert.Equal(2, tailRead.GetProperty("lines").GetInt32());
            Assert.True(tailRead.GetProperty("truncated").GetBoolean());
            Assert.Equal(terminalPaneId, tailRead.GetProperty("paneId").GetString());
            Assert.Equal("terminal", tailRead.GetProperty("paneKind").GetString());

            var invalidResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.ReadScreen, new
            {
                lines = 0
            });
            Assert.True(invalidResponse.Ok, invalidResponse.Error);
            var invalidRead = System.Text.Json.JsonSerializer.SerializeToElement(invalidResponse.Result, AgentMuxJson.Options);
            Assert.False(invalidRead.GetProperty("ok").GetBoolean());
            Assert.Equal("lines must be a positive integer", invalidRead.GetProperty("reason").GetString());
            Assert.Equal("", invalidRead.GetProperty("text").GetString());

            var traceOnTerminalResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserTrace, new
            {
                path = System.IO.Path.GetFullPath(System.IO.Path.Combine(SmokeArtifactDirectory(), "terminal-pane-trace.json"))
            });
            Assert.True(traceOnTerminalResponse.Ok, traceOnTerminalResponse.Error);
            var traceOnTerminal = System.Text.Json.JsonSerializer.SerializeToElement(traceOnTerminalResponse.Result, AgentMuxJson.Options);
            Assert.False(traceOnTerminal.GetProperty("ok").GetBoolean());
            Assert.Equal("active pane is not a browser", traceOnTerminal.GetProperty("reason").GetString());

            var textOnTerminalResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserText, new
            {
                selector = "body"
            });
            Assert.True(textOnTerminalResponse.Ok, textOnTerminalResponse.Error);
            var textOnTerminal = System.Text.Json.JsonSerializer.SerializeToElement(textOnTerminalResponse.Result, AgentMuxJson.Options);
            Assert.False(textOnTerminal.GetProperty("ok").GetBoolean());
            Assert.Equal("active pane is not a browser", textOnTerminal.GetProperty("reason").GetString());

            window.OpenBrowserInActivePaneForSmokeTest("about:blank");
            Assert.Equal(PaneKind.Browser, window.ActivePaneKindForSmokeTest);
            var browserPaneId = window.ActivePaneIdForSmokeTest;
            var browserResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.ReadScreen, new
            {
                lines = 5
            });
            Assert.True(browserResponse.Ok, browserResponse.Error);
            var browserRead = System.Text.Json.JsonSerializer.SerializeToElement(browserResponse.Result, AgentMuxJson.Options);
            Assert.Equal("", browserRead.GetProperty("text").GetString());
            Assert.Equal(5, browserRead.GetProperty("lines").GetInt32());
            Assert.False(browserRead.GetProperty("truncated").GetBoolean());
            Assert.Equal(browserPaneId, browserRead.GetProperty("paneId").GetString());
            Assert.Equal("browser", browserRead.GetProperty("paneKind").GetString());
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task RunSendKeySmokeAsync()
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
            var testHostPath = ResolvePtyTestHostPath();
            window.SetActivePaneShellForSmokeTest($"{QuoteCommand(testHostPath)} --raw-bytes", System.IO.Path.GetDirectoryName(testHostPath));
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            await window.StartActivePanePtyForSmokeTestAsync();
            await WaitForReadScreenContainsAsync(window, "AGENTMUX_RAW_READY");

            await AssertSendKeyDeliveredAsync(window, "PageDown", "\u001b[6~", "RAW:1B", "RAW:5B", "RAW:36", "RAW:7E");
            await AssertSendKeyDeliveredAsync(window, "Ctrl+A", "\u0001", "RAW:01");

            var rendererInputPane = window.ActivePaneIdForSmokeTest;
            Assert.True(await window.EmitActiveTerminalRendererInputForSmokeTestAsync("KM"));
            Assert.Equal(rendererInputPane, window.ActivePaneIdForSmokeTest);
            await WaitForReadScreenContainsAsync(window, "RAW:4B");
            await WaitForReadScreenContainsAsync(window, "RAW:4D");
            Assert.True(await window.EmitActiveTerminalXtermInputForSmokeTestAsync("XT"));
            Assert.Equal(rendererInputPane, window.ActivePaneIdForSmokeTest);
            await WaitForReadScreenContainsAsync(window, "RAW:58");
            await WaitForReadScreenContainsAsync(window, "RAW:54");
            Assert.True(await window.EmitActiveTerminalSyntheticKeydownForSmokeTestAsync("j"));
            Assert.Equal(rendererInputPane, window.ActivePaneIdForSmokeTest);
            await WaitForReadScreenContainsAsync(window, "RAW:6A");
            var terminalRuntimeText = await window.WaitForActiveTerminalRuntimeTextForSmokeTestAsync("RAW:6A");
            Assert.Contains("RAW:4B", terminalRuntimeText, StringComparison.Ordinal);
            Assert.Contains("RAW:4D", terminalRuntimeText, StringComparison.Ordinal);
            Assert.Contains("RAW:58", terminalRuntimeText, StringComparison.Ordinal);
            Assert.Contains("RAW:54", terminalRuntimeText, StringComparison.Ordinal);
            Assert.Contains("RAW:6A", terminalRuntimeText, StringComparison.Ordinal);
            var terminalKeyCapture = await window.CaptureActiveTerminalPngForSmokeTestAsync(
                System.IO.Path.Combine(SmokeArtifactDirectory(), "terminal-key-capture.png"));
            AssertPngFile(terminalKeyCapture);

            var unsupportedResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.SendKey, new
            {
                key = "LaunchRocket"
            });
            Assert.True(unsupportedResponse.Ok, unsupportedResponse.Error);
            var unsupported = System.Text.Json.JsonSerializer.SerializeToElement(unsupportedResponse.Result, AgentMuxJson.Options);
            Assert.False(unsupported.GetProperty("sent").GetBoolean());
            Assert.Equal("unsupported key", unsupported.GetProperty("reason").GetString());

            window.OpenBrowserInActivePaneForSmokeTest("about:blank");
            var browserResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.SendKey, new
            {
                key = "Enter"
            });
            Assert.True(browserResponse.Ok, browserResponse.Error);
            var browser = System.Text.Json.JsonSerializer.SerializeToElement(browserResponse.Result, AgentMuxJson.Options);
            Assert.False(browser.GetProperty("sent").GetBoolean());
            Assert.Equal("active pane is not a terminal", browser.GetProperty("reason").GetString());
        }
        finally
        {
            window.Close();
        }
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
                var createdWorkspaceResult = System.Text.Json.JsonSerializer.SerializeToElement(createdWorkspace.Result, AgentMuxJson.Options);
                var createdWorkspaceId = createdWorkspaceResult.GetProperty("workspace").GetProperty("id").GetString();
                Assert.False(string.IsNullOrWhiteSpace(createdWorkspaceId));

                var selectDefaultWorkspace = await source.HandleRpcForSmokeTestAsync(AgentMuxMethods.WorkspaceSelect, new
                {
                    index = 0
                });
                Assert.True(selectDefaultWorkspace.Ok, selectDefaultWorkspace.Error);
                Assert.Equal("Default", source.ActiveWorkspaceTitleForSmokeTest);

                var selectCreatedWorkspace = await source.HandleRpcForSmokeTestAsync(AgentMuxMethods.WorkspaceSelect, new
                {
                    id = createdWorkspaceId
                });
                Assert.True(selectCreatedWorkspace.Ok, selectCreatedWorkspace.Error);
                Assert.Equal("Persisted workspace", source.ActiveWorkspaceTitleForSmokeTest);

                source.SetActivePaneTextForSmokeTest("AGENTMUX_SESSION_RESTORE_TEXT");
                Assert.True(source.SplitActivePaneForSmokeTest(SplitDirection.Right));
                var browserUrl = source.OpenBrowserInActivePaneForSmokeTest("example.com/session-restore");
                Assert.Equal("https://example.com/session-restore", browserUrl);
                Assert.Equal(PaneKind.Browser, source.ActivePaneKindForSmokeTest);

                var createdSurface = await source.HandleRpcForSmokeTestAsync(AgentMuxMethods.SurfaceCreate, new
                {
                    title = "Persisted surface"
                });
                Assert.True(createdSurface.Ok, createdSurface.Error);
                Assert.Equal(2, source.SurfaceCountForSmokeTest);
                Assert.Equal(1, source.ActiveSurfaceIndexForSmokeTest);
                source.SetActivePaneTextForSmokeTest("AGENTMUX_SURFACE_SESSION_TEXT");

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
                Assert.Equal(2, restored.SurfaceCountForSmokeTest);
                Assert.Equal(1, restored.ActiveSurfaceIndexForSmokeTest);
                Assert.Equal("Persisted surface", restored.ActiveSurfaceTitleForSmokeTest);
                Assert.Equal(1, restored.PaneCountForSmokeTest);
                Assert.Equal(PaneKind.Terminal, restored.ActivePaneKindForSmokeTest);
                Assert.Equal(0, restored.RenderedBrowserPaneCountForSmokeTest);
                Assert.Equal(1, restored.RenderedTerminalPaneCountForSmokeTest);
                Assert.True(restored.RenderedTextContainsForSmokeTest("AGENTMUX_SURFACE_SESSION_TEXT"));

                var selectFirstSurface = await restored.HandleRpcForSmokeTestAsync(AgentMuxMethods.SurfaceSelect, new
                {
                    index = 0
                });
                Assert.True(selectFirstSurface.Ok, selectFirstSurface.Error);
                var selectFirstResult = System.Text.Json.JsonSerializer.SerializeToElement(selectFirstSurface.Result, AgentMuxJson.Options);
                Assert.True(selectFirstResult.GetProperty("selected").GetBoolean());
                Assert.Equal(0, restored.ActiveSurfaceIndexForSmokeTest);
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
                await DeleteDirectoryWithRetryAsync(root);
            }
        }
    }

    private static async Task RunActiveBrowserRpcSmokeAsync()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agentmux-browser-rpc-smoke", Guid.NewGuid().ToString("N"));
        var store = new SessionSnapshotStore(root);
        var window = new MainWindow(
            ShortcutSettings.Default(),
            store,
            restoreSessionOnStartup: false,
            persistSession: true)
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

            var waitLoadSecretToken = $"agentmux-wait-load-secret-{Guid.NewGuid():N}";
            await using (var loadServer = LoopbackHttpServer.Start(
                "/wait-load",
                $"<!doctype html><html><body><h1>{waitLoadSecretToken}</h1></body></html>",
                "text/html; charset=utf-8"))
            {
                var openLoadResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.OpenUrl, new
                {
                    url = loadServer.Url.ToString()
                }).ConfigureAwait(true);
                Assert.True(openLoadResponse.Ok, openLoadResponse.Error);
                var openLoadResult = System.Text.Json.JsonSerializer.SerializeToElement(openLoadResponse.Result, AgentMuxJson.Options);
                Assert.True(openLoadResult.GetProperty("opened").GetBoolean(), openLoadResult.ToString());

                var domContentLoaded = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserWaitForLoad, new
                {
                    state = "domcontentloaded",
                    timeoutMs = 5000
                }));
                Assert.Equal("domcontentloaded", domContentLoaded.GetProperty("state").GetString());
                var domReadyState = domContentLoaded.GetProperty("readyState").GetString();
                Assert.True(domReadyState is "interactive" or "complete", domContentLoaded.ToString());
                Assert.Equal(5000, domContentLoaded.GetProperty("timeoutMs").GetInt32());
                Assert.True(domContentLoaded.GetProperty("maxTimeoutMs").GetInt32() >= 5000);

                var load = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserWaitForLoad, new
                {
                    state = "load",
                    timeoutMs = 5000
                }));
                Assert.Equal("load", load.GetProperty("state").GetString());
                Assert.Equal("complete", load.GetProperty("readyState").GetString());

                await window.SaveSessionForSmokeTestAsync().ConfigureAwait(true);
                Assert.True(System.IO.File.Exists(store.FilePath));
                var waitLoadSnapshotText = await System.IO.File.ReadAllTextAsync(store.FilePath).ConfigureAwait(true);
                Assert.DoesNotContain(waitLoadSecretToken, waitLoadSnapshotText, StringComparison.Ordinal);
            }

            var setup = await WaitForRpcOkAsync(window, AgentMuxMethods.BrowserEval, new
            {
                script = """
                    document.body.innerHTML = '<main id="rpc-text">agentmux browser text smoke</main><input id="rpc-name"><button id="rpc-go">go</button><output id="rpc-result"></output><input id="rpc-typed"><output id="rpc-typed-result"></output><iframe id="rpc-frame" name="agentmux-child-frame" style="border:0;width:420px;height:160px;"></iframe>';
                    window.__agentMuxRpcClicked = 0;
                    window.__agentMuxRpcMouseDown = 0;
                    window.__agentMuxRpcTypedInput = 0;
                    window.__agentMuxRpcPressedEnter = 0;
                    window.__agentMuxFrameReady = false;
                    window.__agentMuxFrameClicked = 0;
                    window.__agentMuxFrameMouseDown = 0;
                    window.__agentMuxFrameTypedInput = 0;
                    window.__agentMuxFramePressedEnter = 0;
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
                    const frame = document.querySelector("#rpc-frame");
                    frame.addEventListener("load", () => {
                        const topWindow = window;
                        const doc = frame.contentDocument;
                        doc.querySelector("#frame-go").addEventListener("mousedown", () => {
                            topWindow.__agentMuxFrameMouseDown += 1;
                        });
                        doc.querySelector("#frame-go").addEventListener("click", () => {
                            topWindow.__agentMuxFrameClicked += 1;
                            doc.querySelector("#frame-result").textContent = doc.querySelector("#frame-name").value;
                        });
                        doc.querySelector("#frame-typed").addEventListener("input", () => {
                            topWindow.__agentMuxFrameTypedInput += 1;
                        });
                        doc.querySelector("#frame-typed").addEventListener("keydown", event => {
                            if (event.key === "Enter") {
                                topWindow.__agentMuxFramePressedEnter += 1;
                                doc.querySelector("#frame-typed-result").textContent = doc.querySelector("#frame-typed").value;
                            }
                        });
                        topWindow.__agentMuxFrameReady = true;
                    }, { once: true });
                    frame.srcdoc = `<!doctype html><html><body><main id="frame-text">agentmux frame text smoke</main><input id="frame-name"><button id="frame-go">go</button><output id="frame-result"></output><input id="frame-typed"><output id="frame-typed-result"></output></body></html>`;
                    true;
                    """
            });
            Assert.True(setup.GetProperty("result").GetBoolean());
            await WaitForBrowserEvalTrueAsync(window, "window.__agentMuxFrameReady === true").ConfigureAwait(true);

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

            const string frameName = "agentmux-child-frame";
            var browserTextSecretToken = $"agentmux-browser-text-{Guid.NewGuid():N}";
            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserEval, new
            {
                script = $$"""
                    (() => {
                        const marker = document.createElement("div");
                        marker.id = "rpc-secret-text";
                        marker.textContent = {{System.Text.Json.JsonSerializer.Serialize(browserTextSecretToken)}};
                        document.body.appendChild(marker);
                        return true;
                    })()
                    """
            }));

            var topDocumentText = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserText, new
            {
                maxChars = 100_000
            }));
            Assert.Contains("agentmux browser text smoke", topDocumentText.GetProperty("text").GetString(), StringComparison.Ordinal);
            Assert.Contains(browserTextSecretToken, topDocumentText.GetProperty("text").GetString(), StringComparison.Ordinal);
            Assert.False(topDocumentText.GetProperty("truncated").GetBoolean());
            Assert.Equal(100_000, topDocumentText.GetProperty("maxChars").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(topDocumentText.GetProperty("paneId").GetString()));

            var selectorText = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserText, new
            {
                selector = "#rpc-text",
                maxChars = 1000
            }));
            Assert.Equal("agentmux browser text smoke", selectorText.GetProperty("text").GetString());
            Assert.Equal("agentmux browser text smoke".Length, selectorText.GetProperty("length").GetInt32());
            Assert.False(selectorText.GetProperty("truncated").GetBoolean());
            Assert.Equal("#rpc-text", selectorText.GetProperty("selector").GetString());

            var invalidMaxCharsText = AssertRpcNotOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserText, new
            {
                selector = "#rpc-text",
                maxChars = 0
            }));
            Assert.Equal("maxChars must be positive", invalidMaxCharsText.GetProperty("reason").GetString());
            Assert.Equal(0, invalidMaxCharsText.GetProperty("maxChars").GetInt32());

            var truncatedText = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserText, new
            {
                selector = "#rpc-text",
                maxChars = 8
            }));
            Assert.Equal("agentmux", truncatedText.GetProperty("text").GetString());
            Assert.Equal("agentmux browser text smoke".Length, truncatedText.GetProperty("length").GetInt32());
            Assert.True(truncatedText.GetProperty("truncated").GetBoolean());
            Assert.Equal(8, truncatedText.GetProperty("maxChars").GetInt32());

            var cappedText = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserText, new
            {
                selector = "#rpc-text",
                maxChars = 200_000
            }));
            Assert.Equal("agentmux browser text smoke", cappedText.GetProperty("text").GetString());
            Assert.Equal(100_000, cappedText.GetProperty("maxChars").GetInt32());
            Assert.Equal(200_000, cappedText.GetProperty("requestedMaxChars").GetInt32());

            var frameText = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserText, new
            {
                selector = "#frame-text",
                frame = frameName
            }));
            Assert.Equal("agentmux frame text smoke", frameText.GetProperty("text").GetString());
            Assert.Equal(frameName, frameText.GetProperty("frame").GetString());
            Assert.Equal(10_000, frameText.GetProperty("maxChars").GetInt32());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, frameText.GetProperty("requestedMaxChars").ValueKind);

            var missingText = AssertRpcNotOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserText, new
            {
                selector = "#missing-browser-text"
            }));
            Assert.Equal("selector not found", missingText.GetProperty("reason").GetString());

            var invalidSelectorText = AssertRpcNotOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserText, new
            {
                selector = "["
            }));
            Assert.Equal("invalid selector", invalidSelectorText.GetProperty("reason").GetString());

            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserFill, new
            {
                selector = "#frame-name",
                text = "agentmux-frame-browser-smoke",
                frame = frameName
            }));
            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserClick, new
            {
                selector = "#frame-go",
                frame = frameName
            }));
            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserType, new
            {
                selector = "#frame-typed",
                text = "agentmux-frame-type-smoke",
                frame = frameName
            }));
            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserPress, new
            {
                selector = "#frame-typed",
                key = "Enter",
                frame = frameName
            }));

            var frameStateRoot = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserEval, new
            {
                script = """
                    (() => {
                        const doc = document.querySelector("#rpc-frame").contentDocument;
                        return {
                            frameValue: doc.querySelector("#frame-name").value,
                            frameResult: doc.querySelector("#frame-result").textContent,
                            frameClicked: window.__agentMuxFrameClicked,
                            frameMouseDown: window.__agentMuxFrameMouseDown,
                            frameTypedValue: doc.querySelector("#frame-typed").value,
                            frameTypedInput: window.__agentMuxFrameTypedInput,
                            framePressedEnter: window.__agentMuxFramePressedEnter,
                            frameTypedResult: doc.querySelector("#frame-typed-result").textContent
                        };
                    })()
                    """
            }));
            var frameState = frameStateRoot.GetProperty("result");
            Assert.Equal("agentmux-frame-browser-smoke", frameState.GetProperty("frameValue").GetString());
            Assert.Equal("agentmux-frame-browser-smoke", frameState.GetProperty("frameResult").GetString());
            Assert.Equal(1, frameState.GetProperty("frameClicked").GetInt32());
            Assert.True(frameState.GetProperty("frameMouseDown").GetInt32() >= 1);
            Assert.Equal("agentmux-frame-type-smoke", frameState.GetProperty("frameTypedValue").GetString());
            Assert.True(frameState.GetProperty("frameTypedInput").GetInt32() >= 1);
            Assert.Equal(1, frameState.GetProperty("framePressedEnter").GetInt32());
            Assert.Equal("agentmux-frame-type-smoke", frameState.GetProperty("frameTypedResult").GetString());

            var framePressWithoutSelector = AssertRpcNotOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserPress, new
            {
                key = "Enter",
                frame = frameName
            }));
            Assert.Equal("selector is required when frame is provided", framePressWithoutSelector.GetProperty("reason").GetString());
            Assert.Equal(frameName, framePressWithoutSelector.GetProperty("frame").GetString());

            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserEval, new
            {
                script = """
                    (() => {
                        window.__agentMuxSandboxReady = false;
                        const frame = document.createElement("iframe");
                        frame.id = "sandbox-frame";
                        frame.name = "agentmux-sandbox-frame";
                        frame.setAttribute("sandbox", "");
                        frame.addEventListener("load", () => {
                            window.__agentMuxSandboxReady = true;
                        }, { once: true });
                        frame.srcdoc = '<input id="blocked">';
                        document.body.appendChild(frame);
                        return true;
                    })()
                    """
            }));
            await WaitForBrowserEvalTrueAsync(window, "window.__agentMuxSandboxReady === true").ConfigureAwait(true);
            var sandboxFrame = AssertRpcNotOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserFill, new
            {
                selector = "#blocked",
                text = "blocked",
                frame = "agentmux-sandbox-frame"
            }));
            Assert.Equal("frame is not same-origin accessible", sandboxFrame.GetProperty("reason").GetString());
            Assert.Equal("agentmux-sandbox-frame", sandboxFrame.GetProperty("frame").GetString());

            var sandboxFrameText = AssertRpcNotOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserText, new
            {
                selector = "#blocked",
                frame = "agentmux-sandbox-frame"
            }));
            Assert.Equal("frame is not same-origin accessible", sandboxFrameText.GetProperty("reason").GetString());
            Assert.Equal("agentmux-sandbox-frame", sandboxFrameText.GetProperty("frame").GetString());

            var frameTree = await WaitForFrameTreeWithChildAsync(window, "agentmux-child-frame").ConfigureAwait(true);
            Assert.True(frameTree.GetProperty("ok").GetBoolean(), frameTree.ToString());
            var rootFrameTree = frameTree.GetProperty("frameTree");
            Assert.True(rootFrameTree.TryGetProperty("frame", out var rootFrame), frameTree.ToString());
            Assert.False(string.IsNullOrWhiteSpace(rootFrame.GetProperty("id").GetString()));
            Assert.True(TryFindFrameByName(rootFrameTree, "agentmux-child-frame", out var childFrame), frameTree.ToString());
            Assert.Equal("agentmux-child-frame", childFrame.GetProperty("name").GetString());
            Assert.False(string.IsNullOrWhiteSpace(childFrame.GetProperty("parentId").GetString()));

            var waitSecretToken = $"agentmux-wait-secret-{Guid.NewGuid():N}";
            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserEval, new
            {
                script = $$"""
                    (() => {
                        window.__agentMuxWaitReady = false;
                        setTimeout(() => {
                            const marker = document.createElement("div");
                            marker.id = "rpc-delayed-ready";
                            marker.textContent = {{System.Text.Json.JsonSerializer.Serialize(waitSecretToken)}};
                            marker.style.width = "24px";
                            marker.style.height = "24px";
                            document.body.appendChild(marker);
                            window.__agentMuxWaitReady = true;
                        }, 150);
                        return true;
                    })()
                    """
            }));
            var waitForSelector = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserWaitForSelector, new
            {
                selector = "#rpc-delayed-ready",
                state = "visible",
                timeoutMs = 3000
            }));
            Assert.Equal("#rpc-delayed-ready", waitForSelector.GetProperty("selector").GetString());
            Assert.Equal("visible", waitForSelector.GetProperty("state").GetString());
            Assert.True(waitForSelector.GetProperty("attached").GetBoolean());
            Assert.True(waitForSelector.GetProperty("visible").GetBoolean());
            Assert.True(waitForSelector.GetProperty("elapsedMs").GetInt32() >= 0);
            Assert.Equal(3000, waitForSelector.GetProperty("timeoutMs").GetInt32());
            Assert.True(waitForSelector.GetProperty("maxTimeoutMs").GetInt32() >= 3000);

            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserEval, new
            {
                script = """
                    (() => {
                        const frame = document.querySelector("#rpc-frame");
                        const doc = frame.contentDocument;
                        setTimeout(() => {
                            const marker = doc.createElement("div");
                            marker.id = "frame-delayed-ready";
                            marker.textContent = "ready";
                            marker.style.width = "24px";
                            marker.style.height = "24px";
                            doc.body.appendChild(marker);
                        }, 150);
                        return true;
                    })()
                    """
            }));
            var frameWaitForSelector = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserWaitForSelector, new
            {
                selector = "#frame-delayed-ready",
                state = "visible",
                timeoutMs = 3000,
                frame = frameName
            }));
            Assert.Equal(frameName, frameWaitForSelector.GetProperty("frame").GetString());
            Assert.True(frameWaitForSelector.GetProperty("attached").GetBoolean());
            Assert.True(frameWaitForSelector.GetProperty("visible").GetBoolean());

            var missingFrameWait = AssertRpcNotOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserWaitForSelector, new
            {
                selector = "#anything",
                frame = "agentmux-missing-frame",
                timeoutMs = 3000
            }));
            Assert.Equal("frame not found", missingFrameWait.GetProperty("reason").GetString());
            Assert.Equal("agentmux-missing-frame", missingFrameWait.GetProperty("frame").GetString());

            var sandboxFrameWait = AssertRpcNotOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserWaitForSelector, new
            {
                selector = "#blocked",
                frame = "agentmux-sandbox-frame",
                timeoutMs = 3000
            }));
            Assert.Equal("frame is not same-origin accessible", sandboxFrameWait.GetProperty("reason").GetString());
            Assert.Equal("agentmux-sandbox-frame", sandboxFrameWait.GetProperty("frame").GetString());

            await window.SaveSessionForSmokeTestAsync().ConfigureAwait(true);
            Assert.True(System.IO.File.Exists(store.FilePath));
            var waitSnapshotText = await System.IO.File.ReadAllTextAsync(store.FilePath).ConfigureAwait(true);
            Assert.DoesNotContain(waitSecretToken, waitSnapshotText, StringComparison.Ordinal);
            Assert.DoesNotContain(browserTextSecretToken, waitSnapshotText, StringComparison.Ordinal);

            var missingWait = AssertRpcNotOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserWaitForSelector, new
            {
                selector = "#rpc-never-ready",
                state = "visible",
                timeoutMs = 500
            }));
            Assert.Equal("timeout", missingWait.GetProperty("reason").GetString());
            Assert.Equal("#rpc-never-ready", missingWait.GetProperty("selector").GetString());
            Assert.Equal("visible", missingWait.GetProperty("state").GetString());

            var consoleLogToken = $"agentmux-console-log-{Guid.NewGuid():N}";
            var consoleErrorToken = $"agentmux-console-error-{Guid.NewGuid():N}";
            var consoleSecretToken = $"agentmux-console-secret-{Guid.NewGuid():N}";
            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserConsoleClear));
            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserEval, new
            {
                script = $$"""
                    console.log({{System.Text.Json.JsonSerializer.Serialize(consoleLogToken)}});
                    console.error({{System.Text.Json.JsonSerializer.Serialize(consoleErrorToken)}});
                    console.log({{System.Text.Json.JsonSerializer.Serialize(consoleSecretToken)}});
                    true;
                    """
            }));

            var consoleLog = await WaitForConsoleEventsAsync(
                window,
                (consoleLogToken, "log"),
                (consoleErrorToken, "error"),
                (consoleSecretToken, "log")).ConfigureAwait(true);
            Assert.True(consoleLog.GetProperty("maxMessageChars").GetInt32() >= consoleLogToken.Length);
            Assert.True(TryFindConsoleEvent(consoleLog, consoleLogToken, "log", out var logEvent), consoleLog.ToString());
            Assert.Equal("consoleAPICalled", logEvent.GetProperty("event").GetString());
            Assert.Equal("log", logEvent.GetProperty("type").GetString());
            Assert.Equal("log", logEvent.GetProperty("level").GetString());
            Assert.Equal(consoleLogToken, logEvent.GetProperty("message").GetString());
            Assert.Equal(consoleLogToken.Length, logEvent.GetProperty("messageLength").GetInt32());
            Assert.False(logEvent.GetProperty("truncated").GetBoolean());
            Assert.True(TryFindConsoleEvent(consoleLog, consoleErrorToken, "error", out var errorEvent), consoleLog.ToString());
            Assert.Equal("error", errorEvent.GetProperty("level").GetString());
            Assert.Equal(consoleErrorToken, errorEvent.GetProperty("message").GetString());

            await window.SaveSessionForSmokeTestAsync().ConfigureAwait(true);
            Assert.True(System.IO.File.Exists(store.FilePath));
            var snapshotText = await System.IO.File.ReadAllTextAsync(store.FilePath).ConfigureAwait(true);
            Assert.DoesNotContain(consoleSecretToken, snapshotText, StringComparison.Ordinal);

            var consoleClear = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserConsoleClear));
            Assert.True(consoleClear.GetProperty("cleared").GetInt32() >= 3);
            var emptyConsoleLog = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserConsoleLog, new
            {
                limit = 10
            }));
            Assert.Equal(0, emptyConsoleLog.GetProperty("count").GetInt32());

            var networkToken = $"agentmux-network-{Guid.NewGuid():N}";
            await using var networkServer = LoopbackHttpServer.Start($"/{networkToken}", networkToken);
            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserNetworkClear));
            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserEval, new
            {
                script = $$"""
                    window.__agentMuxNetworkText = "";
                    fetch({{System.Text.Json.JsonSerializer.Serialize(networkServer.Url.ToString())}})
                        .then(response => response.text())
                        .then(text => { window.__agentMuxNetworkText = text; })
                        .catch(error => { window.__agentMuxNetworkText = "ERROR:" + error.message; });
                    true;
                    """
            }));
            await WaitForBrowserEvalTrueAsync(
                window,
                $"window.__agentMuxNetworkText === {System.Text.Json.JsonSerializer.Serialize(networkToken)}").ConfigureAwait(true);

            var networkIdle = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserWaitForLoad, new
            {
                state = "network-idle",
                timeoutMs = 5000
            }));
            Assert.Equal("network-idle", networkIdle.GetProperty("state").GetString());
            Assert.Equal("complete", networkIdle.GetProperty("readyState").GetString());
            Assert.Equal(0, networkIdle.GetProperty("inFlightRequests").GetInt32());
            Assert.True(networkIdle.GetProperty("networkIdleMs").GetInt32() >= 500);

            var networkLog = await WaitForNetworkEventAsync(window, networkToken, "responseReceived").ConfigureAwait(true);
            Assert.True(TryFindNetworkEvent(networkLog, networkToken, "requestWillBeSent", out var requestEvent), networkLog.ToString());
            Assert.Equal("GET", requestEvent.GetProperty("method").GetString());
            Assert.True(TryFindNetworkEvent(networkLog, networkToken, "responseReceived", out var responseEvent), networkLog.ToString());
            Assert.Equal(200, responseEvent.GetProperty("status").GetInt32());
            Assert.Equal("text/plain", responseEvent.GetProperty("mimeType").GetString());
            var responseRequestId = responseEvent.GetProperty("requestId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(responseRequestId));

            var loadingFinishedLog = await WaitForNetworkRequestEventAsync(window, responseRequestId!, "loadingFinished").ConfigureAwait(true);
            Assert.True(TryFindNetworkRequestEvent(loadingFinishedLog, responseRequestId!, "loadingFinished", out var finishedEvent), loadingFinishedLog.ToString());
            Assert.True(finishedEvent.GetProperty("encodedDataLength").GetDouble() >= networkToken.Length);

            var responseBody = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserResponseBody, new
            {
                requestId = responseRequestId
            }));
            Assert.True(responseBody.GetProperty("ok").GetBoolean(), responseBody.ToString());
            Assert.Equal(responseRequestId, responseBody.GetProperty("requestId").GetString());
            Assert.Equal(networkToken, responseBody.GetProperty("body").GetString());
            Assert.False(responseBody.GetProperty("base64Encoded").GetBoolean());
            Assert.Equal(networkToken.Length, responseBody.GetProperty("bodyLength").GetInt32());
            Assert.False(responseBody.GetProperty("truncated").GetBoolean());
            Assert.True(responseBody.GetProperty("maxBodyChars").GetInt32() >= networkToken.Length);

            var harPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(SmokeArtifactDirectory(), "browser-network.har.json"));
            if (System.IO.File.Exists(harPath))
            {
                System.IO.File.Delete(harPath);
            }

            var harExport = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserHarMetadata, new
            {
                path = harPath
            }));
            Assert.Equal(harPath, harExport.GetProperty("path").GetString());
            Assert.True(harExport.GetProperty("entryCount").GetInt32() >= 1);
            Assert.True(harExport.GetProperty("eventCount").GetInt32() >= 2);
            Assert.True(harExport.GetProperty("metadataOnly").GetBoolean());
            Assert.True(System.IO.File.Exists(harPath));

            using (var harDocument = System.Text.Json.JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(harPath).ConfigureAwait(true)))
            {
                var log = harDocument.RootElement.GetProperty("log");
                Assert.Equal("1.2", log.GetProperty("version").GetString());
                Assert.Equal("AgentMux Windows", log.GetProperty("creator").GetProperty("name").GetString());
                Assert.Contains("Metadata-only", log.GetProperty("comment").GetString(), StringComparison.Ordinal);
                Assert.True(TryFindHarEntry(log, networkToken, out var harEntry), log.ToString());
                Assert.Equal("GET", harEntry.GetProperty("request").GetProperty("method").GetString());
                Assert.Equal(200, harEntry.GetProperty("response").GetProperty("status").GetInt32());
                Assert.Equal("text/plain", harEntry.GetProperty("response").GetProperty("content").GetProperty("mimeType").GetString());
                Assert.Empty(harEntry.GetProperty("request").GetProperty("headers").EnumerateArray());
                Assert.Empty(harEntry.GetProperty("request").GetProperty("cookies").EnumerateArray());
                Assert.Empty(harEntry.GetProperty("response").GetProperty("headers").EnumerateArray());
                Assert.Empty(harEntry.GetProperty("response").GetProperty("cookies").EnumerateArray());
                Assert.False(harEntry.GetProperty("request").TryGetProperty("postData", out _));
                Assert.False(harEntry.GetProperty("response").GetProperty("content").TryGetProperty("text", out _));
                var agentMuxHar = harEntry.GetProperty("_agentMux");
                Assert.True(agentMuxHar.GetProperty("metadataOnly").GetBoolean());
                Assert.True(agentMuxHar.GetProperty("capturedEventCount").GetInt32() >= 3);
                var harEvents = agentMuxHar.GetProperty("events").EnumerateArray().Select(candidate => candidate.GetString()).ToArray();
                Assert.Contains("requestWillBeSent", harEvents);
                Assert.Contains("responseReceived", harEvents);
                Assert.Contains("loadingFinished", harEvents);
            }

            var traceToken = $"agentmux-trace-{Guid.NewGuid():N}";
            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserEval, new
            {
                script = $$"""
                    performance.mark({{System.Text.Json.JsonSerializer.Serialize(traceToken)}});
                    document.body.dataset.agentmuxTraceToken = {{System.Text.Json.JsonSerializer.Serialize(traceToken)}};
                    true;
                    """
            }));
            var tracePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(SmokeArtifactDirectory(), "browser-trace.json"));
            if (System.IO.File.Exists(tracePath))
            {
                System.IO.File.Delete(tracePath);
            }

            var traceExport = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserTrace, new
            {
                path = tracePath,
                durationMs = 500,
                maxBytes = 5_000_000
            }));
            Assert.Equal(tracePath, traceExport.GetProperty("path").GetString());
            Assert.Equal(500, traceExport.GetProperty("durationMs").GetInt32());
            Assert.True(traceExport.GetProperty("maxDurationMs").GetInt32() >= 500);
            Assert.True(traceExport.GetProperty("maxBytes").GetInt32() >= 5_000_000);
            Assert.True(traceExport.GetProperty("bytesWritten").GetInt64() > 0, traceExport.ToString());
            Assert.True(traceExport.GetProperty("chunkCount").GetInt32() >= 1, traceExport.ToString());
            Assert.Equal("json", traceExport.GetProperty("traceFormat").GetString());
            Assert.Equal("none", traceExport.GetProperty("streamCompression").GetString());
            Assert.True(System.IO.File.Exists(tracePath));
            using (var traceDocument = System.Text.Json.JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(tracePath).ConfigureAwait(true)))
            {
                Assert.True(traceDocument.RootElement.TryGetProperty("traceEvents", out var traceEvents), traceDocument.RootElement.ToString());
                Assert.Equal(System.Text.Json.JsonValueKind.Array, traceEvents.ValueKind);
                Assert.True(traceEvents.GetArrayLength() > 0, traceDocument.RootElement.ToString());
            }

            await window.SaveSessionForSmokeTestAsync().ConfigureAwait(true);
            Assert.True(System.IO.File.Exists(store.FilePath));
            var traceSnapshotText = await System.IO.File.ReadAllTextAsync(store.FilePath).ConfigureAwait(true);
            Assert.DoesNotContain(traceToken, traceSnapshotText, StringComparison.Ordinal);
            Assert.DoesNotContain(tracePath, traceSnapshotText, StringComparison.Ordinal);

            await Task.Delay(250).ConfigureAwait(true);
            var networkClear = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserNetworkClear));
            Assert.True(networkClear.GetProperty("cleared").GetInt32() >= 1);
            var emptyNetworkLog = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserNetworkLog, new
            {
                limit = 10
            }));
            Assert.Equal(0, emptyNetworkLog.GetProperty("count").GetInt32());

            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserRouteClear));
            var emptyRoutes = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserRouteList));
            Assert.Equal(0, emptyRoutes.GetProperty("count").GetInt32());
            Assert.False(emptyRoutes.GetProperty("fetchEnabled").GetBoolean());

            var routeBlockToken = $"agentmux-route-block-{Guid.NewGuid():N}";
            var blockRoute = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserRouteBlock, new
            {
                urlContains = routeBlockToken
            }));
            Assert.Equal("block", blockRoute.GetProperty("rule").GetProperty("action").GetString());
            Assert.Equal(routeBlockToken, blockRoute.GetProperty("rule").GetProperty("urlContains").GetString());

            await using (var routeBlockServer = LoopbackHttpServer.Start($"/{routeBlockToken}", routeBlockToken))
            {
                AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserEval, new
                {
                    script = $$"""
                        window.__agentMuxRouteBlockText = "";
                        fetch({{System.Text.Json.JsonSerializer.Serialize(routeBlockServer.Url.ToString())}})
                            .then(response => response.text())
                            .then(text => { window.__agentMuxRouteBlockText = text; })
                            .catch(error => { window.__agentMuxRouteBlockText = "ERROR:" + error.message; });
                        true;
                        """
                }));
                await WaitForBrowserEvalTrueAsync(
                    window,
                    "window.__agentMuxRouteBlockText.startsWith('ERROR:')").ConfigureAwait(true);
            }

            var blockRoutes = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserRouteList));
            Assert.True(blockRoutes.GetProperty("fetchEnabled").GetBoolean());
            Assert.True(TryFindBrowserRoute(blockRoutes, routeBlockToken, "block", out var blockRouteSnapshot), blockRoutes.ToString());
            Assert.Equal(1, blockRouteSnapshot.GetProperty("hitCount").GetInt32());
            Assert.True(TryFindBrowserRouteHit(blockRoutes, routeBlockToken, "block", out var blockRouteHit), blockRoutes.ToString());
            Assert.Equal("GET", blockRouteHit.GetProperty("method").GetString());
            Assert.Equal("BlockedByClient", blockRouteHit.GetProperty("errorReason").GetString());

            var routeClearAfterBlock = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserRouteClear));
            Assert.True(routeClearAfterBlock.GetProperty("clearedRules").GetInt32() >= 1);
            Assert.True(routeClearAfterBlock.GetProperty("clearedHits").GetInt32() >= 1);
            Assert.False(routeClearAfterBlock.GetProperty("fetchEnabled").GetBoolean());

            var routeFulfillToken = $"agentmux-route-fulfill-{Guid.NewGuid():N}";
            var routeFulfillUrl = $"/{routeFulfillToken}";
            var routeFulfillBody = $"synthetic route body {Guid.NewGuid():N}";
            var fulfillRoute = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserRouteFulfill, new
            {
                urlContains = routeFulfillToken,
                status = 209,
                contentType = "text/plain; charset=utf-8",
                body = routeFulfillBody
            }));
            Assert.Equal("fulfill", fulfillRoute.GetProperty("rule").GetProperty("action").GetString());
            Assert.Equal(routeFulfillToken, fulfillRoute.GetProperty("rule").GetProperty("urlContains").GetString());
            Assert.Equal(209, fulfillRoute.GetProperty("rule").GetProperty("status").GetInt32());
            Assert.Equal(routeFulfillBody.Length, fulfillRoute.GetProperty("rule").GetProperty("bodyLength").GetInt32());
            Assert.DoesNotContain(routeFulfillBody, fulfillRoute.ToString(), StringComparison.Ordinal);

            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserEval, new
            {
                script = $$"""
                    window.__agentMuxRouteFulfill = {};
                    fetch({{System.Text.Json.JsonSerializer.Serialize(routeFulfillUrl)}})
                        .then(async response => {
                            window.__agentMuxRouteFulfill = {
                                ok: true,
                                status: response.status,
                                contentType: response.headers.get("content-type"),
                                text: await response.text()
                            };
                        })
                        .catch(error => {
                            window.__agentMuxRouteFulfill = {
                                ok: false,
                                error: error.message
                            };
                        });
                    true;
                    """
            }));
            await WaitForBrowserEvalTrueAsync(
                window,
                $"window.__agentMuxRouteFulfill.text === {System.Text.Json.JsonSerializer.Serialize(routeFulfillBody)}").ConfigureAwait(true);

            var fulfillState = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserEval, new
            {
                script = "window.__agentMuxRouteFulfill"
            }));
            Assert.True(fulfillState.GetProperty("ok").GetBoolean(), fulfillState.ToString());
            Assert.Equal(209, fulfillState.GetProperty("status").GetInt32());
            Assert.Contains("text/plain", fulfillState.GetProperty("contentType").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(routeFulfillBody, fulfillState.GetProperty("text").GetString());

            var fulfillRoutes = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserRouteList));
            Assert.True(TryFindBrowserRoute(fulfillRoutes, routeFulfillToken, "fulfill", out var fulfillRouteSnapshot), fulfillRoutes.ToString());
            Assert.Equal(1, fulfillRouteSnapshot.GetProperty("hitCount").GetInt32());
            Assert.Equal(routeFulfillBody.Length, fulfillRouteSnapshot.GetProperty("bodyLength").GetInt32());
            Assert.False(fulfillRouteSnapshot.GetProperty("bodyTruncated").GetBoolean());
            Assert.True(TryFindBrowserRouteHit(fulfillRoutes, routeFulfillToken, "fulfill", out var fulfillRouteHit), fulfillRoutes.ToString());
            Assert.Equal(209, fulfillRouteHit.GetProperty("status").GetInt32());
            Assert.DoesNotContain(routeFulfillBody, fulfillRoutes.ToString(), StringComparison.Ordinal);

            var routeFrameSrcDoc = "<!doctype html><html><body><script>fetch("
                + System.Text.Json.JsonSerializer.Serialize(routeFulfillUrl + "?frame=1")
                + ").catch(() => {});</script></body></html>";
            AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserEval, new
            {
                script = $$"""
                    (() => {
                        const frame = document.createElement("iframe");
                        frame.id = "agentmux-route-frame";
                        frame.srcdoc = {{System.Text.Json.JsonSerializer.Serialize(routeFrameSrcDoc)}};
                        document.body.appendChild(frame);
                        return true;
                    })()
                    """
            }));
            var frameFulfillRoutes = await WaitForBrowserRouteHitCountAsync(
                window,
                routeFulfillToken,
                "fulfill",
                2).ConfigureAwait(true);
            Assert.True(TryFindBrowserRouteHit(frameFulfillRoutes, routeFulfillToken, "fulfill", out var frameFulfillRouteHit), frameFulfillRoutes.ToString());
            Assert.Equal(209, frameFulfillRouteHit.GetProperty("status").GetInt32());

            await window.SaveSessionForSmokeTestAsync().ConfigureAwait(true);
            Assert.True(System.IO.File.Exists(store.FilePath));
            var routeSnapshotText = await System.IO.File.ReadAllTextAsync(store.FilePath).ConfigureAwait(true);
            Assert.DoesNotContain(routeFulfillBody, routeSnapshotText, StringComparison.Ordinal);
            Assert.DoesNotContain(routeFulfillToken, routeSnapshotText, StringComparison.Ordinal);

            var routeClearAfterFulfill = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserRouteClear));
            Assert.True(routeClearAfterFulfill.GetProperty("clearedRules").GetInt32() >= 1);
            Assert.False(routeClearAfterFulfill.GetProperty("fetchEnabled").GetBoolean());

            var routeClearToken = $"agentmux-route-clear-{Guid.NewGuid():N}";
            await using (var routeClearServer = LoopbackHttpServer.Start($"/{routeClearToken}", routeClearToken))
            {
                AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserEval, new
                {
                    script = $$"""
                        window.__agentMuxRouteClearText = "";
                        fetch({{System.Text.Json.JsonSerializer.Serialize(routeClearServer.Url.ToString())}})
                            .then(response => response.text())
                            .then(text => { window.__agentMuxRouteClearText = text; })
                            .catch(error => { window.__agentMuxRouteClearText = "ERROR:" + error.message; });
                        true;
                        """
                }));
                await WaitForBrowserEvalTrueAsync(
                    window,
                    $"window.__agentMuxRouteClearText === {System.Text.Json.JsonSerializer.Serialize(routeClearToken)}").ConfigureAwait(true);
            }

            var postClearRoutes = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserRouteList));
            Assert.Equal(0, postClearRoutes.GetProperty("count").GetInt32());
            Assert.False(postClearRoutes.GetProperty("fetchEnabled").GetBoolean());

            var slowNetworkToken = $"agentmux-wait-load-slow-{Guid.NewGuid():N}";
            await using (var slowServer = LoopbackHttpServer.Start(
                $"/{slowNetworkToken}",
                slowNetworkToken,
                "text/plain; charset=utf-8",
                responseDelay: TimeSpan.FromSeconds(2)))
            {
                AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserNetworkClear));
                AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserEval, new
                {
                    script = $$"""
                        window.__agentMuxSlowNetworkText = "";
                        fetch({{System.Text.Json.JsonSerializer.Serialize(slowServer.Url.ToString())}})
                            .then(response => response.text())
                            .then(text => { window.__agentMuxSlowNetworkText = text; })
                            .catch(error => { window.__agentMuxSlowNetworkText = "ERROR:" + error.message; });
                        true;
                        """
                }));
                await WaitForNetworkEventAsync(window, slowNetworkToken, "requestWillBeSent").ConfigureAwait(true);

                var slowNetworkIdle = AssertRpcNotOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserWaitForLoad, new
                {
                    state = "network-idle",
                    timeoutMs = 100
                }));
                Assert.Equal("timeout", slowNetworkIdle.GetProperty("reason").GetString());
                Assert.Equal("network-idle", slowNetworkIdle.GetProperty("state").GetString());
                Assert.True(slowNetworkIdle.GetProperty("inFlightRequests").GetInt32() >= 1);
            }

            var downloadToken = $"agentmux-download-{Guid.NewGuid():N}";
            var downloadFileName = $"{downloadToken}.txt";
            string? resultFilePath = null;
            try
            {
                await using var downloadServer = LoopbackHttpServer.StartAttachment($"/{downloadFileName}", downloadToken, downloadFileName);
                AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserDownloadsClear));
                var openDownloadResponse = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.OpenUrl, new
                {
                    url = downloadServer.Url.ToString()
                }).ConfigureAwait(true);
                Assert.True(openDownloadResponse.Ok, openDownloadResponse.Error);
                var openDownloadResult = System.Text.Json.JsonSerializer.SerializeToElement(openDownloadResponse.Result, AgentMuxJson.Options);
                Assert.True(openDownloadResult.GetProperty("opened").GetBoolean(), openDownloadResult.ToString());
                Assert.Equal(downloadServer.Url.ToString(), openDownloadResult.GetProperty("url").GetString());

                var downloadLog = await WaitForDownloadAsync(window, downloadToken, "Completed").ConfigureAwait(true);
                Assert.True(TryFindDownload(downloadLog, downloadToken, "Completed", out var downloadEvent), downloadLog.ToString());
                resultFilePath = downloadEvent.GetProperty("resultFilePath").GetString();
                Assert.False(string.IsNullOrWhiteSpace(resultFilePath));
                var expectedDownloadRoot = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AgentMux",
                    "Downloads");
                var fullResultPath = System.IO.Path.GetFullPath(resultFilePath!);
                var fullDownloadRoot = System.IO.Path.GetFullPath(expectedDownloadRoot).TrimEnd(
                    System.IO.Path.DirectorySeparatorChar,
                    System.IO.Path.AltDirectorySeparatorChar);
                Assert.StartsWith(
                    fullDownloadRoot + System.IO.Path.DirectorySeparatorChar,
                    fullResultPath,
                    StringComparison.OrdinalIgnoreCase);
                Assert.Contains(downloadFileName, System.IO.Path.GetFileName(resultFilePath));
                Assert.True(System.IO.File.Exists(resultFilePath), resultFilePath);
                Assert.Equal(downloadToken, await System.IO.File.ReadAllTextAsync(resultFilePath!).ConfigureAwait(true));
                Assert.True(downloadEvent.GetProperty("bytesReceived").GetInt64() >= downloadToken.Length);

                var downloadClear = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserDownloadsClear));
                Assert.True(downloadClear.GetProperty("cleared").GetInt32() >= 1);
                var emptyDownloadLog = AssertRpcOk(await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserDownloads, new
                {
                    limit = 10
                }));
                Assert.Equal(0, emptyDownloadLog.GetProperty("count").GetInt32());
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(resultFilePath) && System.IO.File.Exists(resultFilePath))
                {
                    System.IO.File.Delete(resultFilePath);
                }
            }
        }
        finally
        {
            window.Close();
            if (System.IO.Directory.Exists(root))
            {
                await DeleteDirectoryWithRetryAsync(root);
            }
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
                    hasSmokeProbe: typeof window.agentmuxGetTextForSmoke === "function",
                    hasInputProbe: typeof window.agentmuxEmitInputForSmoke === "function",
                    hasXtermInputProbe: typeof window.agentmuxEmitXtermInputForSmoke === "function",
                    hasSyntheticKeydownProbe: typeof window.agentmuxEmitSyntheticKeydownForSmoke === "function"
                }))()
                """);
            using (var diagnostics = System.Text.Json.JsonDocument.Parse(diagnosticsJson))
            {
                var root = diagnostics.RootElement;
                Assert.True(root.GetProperty("hasXtermElement").GetBoolean());
                Assert.True(root.GetProperty("hasSetText").GetBoolean());
                Assert.True(root.GetProperty("hasAppendText").GetBoolean());
                Assert.True(root.GetProperty("hasSmokeProbe").GetBoolean());
                Assert.True(root.GetProperty("hasInputProbe").GetBoolean());
                Assert.True(root.GetProperty("hasXtermInputProbe").GetBoolean());
                Assert.True(root.GetProperty("hasSyntheticKeydownProbe").GetBoolean());
            }

            var inputReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            terminal.InputReceived += (_, data) => inputReceived.TrySetResult(data);
            Assert.True(await terminal.EmitInputForSmokeTestAsync("AGENTMUX_RENDERER_INPUT"));
            Assert.Equal("AGENTMUX_RENDERER_INPUT", await inputReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            var xtermInputReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            terminal.InputReceived += (_, data) =>
            {
                if (string.Equals(data, "AGENTMUX_XTERM_INPUT", StringComparison.Ordinal))
                {
                    xtermInputReceived.TrySetResult(data);
                }
            };
            Assert.True(await terminal.EmitXtermInputForSmokeTestAsync("AGENTMUX_XTERM_INPUT"));
            Assert.Equal("AGENTMUX_XTERM_INPUT", await xtermInputReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            var syntheticKeydownReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            terminal.InputReceived += (_, data) =>
            {
                if (string.Equals(data, "j", StringComparison.Ordinal))
                {
                    syntheticKeydownReceived.TrySetResult(data);
                }
            };
            Assert.True(await terminal.EmitSyntheticKeydownForSmokeTestAsync("j"));
            Assert.Equal("j", await syntheticKeydownReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)));

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

    private static async Task DeleteDirectoryWithRetryAsync(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (System.IO.Directory.Exists(path))
                {
                    System.IO.Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (System.IO.IOException) when (attempt < 9)
            {
                await Task.Delay(100);
            }
            catch (System.UnauthorizedAccessException) when (attempt < 9)
            {
                await Task.Delay(100);
            }
        }
    }

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

    private static System.Text.Json.JsonElement AssertRpcNotOk(AgentMuxResponse response)
    {
        Assert.True(response.Ok, response.Error);
        var result = System.Text.Json.JsonSerializer.SerializeToElement(response.Result, AgentMuxJson.Options);
        Assert.False(result.GetProperty("ok").GetBoolean(), result.ToString());
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

    private static async Task AssertSendKeyDeliveredAsync(MainWindow window, string key, string expectedSequence, params string[] markers)
    {
        var response = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.SendKey, new { key }).ConfigureAwait(true);
        Assert.True(response.Ok, response.Error);
        var result = System.Text.Json.JsonSerializer.SerializeToElement(response.Result, AgentMuxJson.Options);
        Assert.True(result.GetProperty("sent").GetBoolean());
        Assert.Equal(key, result.GetProperty("key").GetString());
        Assert.Equal(Encoding.UTF8.GetByteCount(expectedSequence), result.GetProperty("bytes").GetInt32());

        foreach (var marker in markers)
        {
            await WaitForReadScreenContainsAsync(window, marker).ConfigureAwait(true);
        }
    }

    private static async Task WaitForReadScreenContainsAsync(MainWindow window, string marker)
    {
        var lastText = "";
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var response = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.ReadScreen, new
            {
                lines = 200
            }).ConfigureAwait(true);
            Assert.True(response.Ok, response.Error);

            var result = System.Text.Json.JsonSerializer.SerializeToElement(response.Result, AgentMuxJson.Options);
            lastText = result.GetProperty("text").GetString() ?? "";
            if (lastText.Contains(marker, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(250).ConfigureAwait(true);
        }

        throw new TimeoutException($"Timed out waiting for read-screen marker '{marker}'. Last text: {lastText}");
    }

    private static async Task<System.Text.Json.JsonElement> WaitForBrowserEvalTrueAsync(MainWindow window, string script)
    {
        System.Text.Json.JsonElement? lastResult = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var response = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserEval, new { script }).ConfigureAwait(true);
            if (response.Ok)
            {
                var result = System.Text.Json.JsonSerializer.SerializeToElement(response.Result, AgentMuxJson.Options);
                lastResult = result.Clone();
                if (result.TryGetProperty("ok", out var ok)
                    && ok.GetBoolean()
                    && result.TryGetProperty("result", out var evalResult)
                    && evalResult.ValueKind == System.Text.Json.JsonValueKind.True)
                {
                    return result.Clone();
                }
            }

            await Task.Delay(250).ConfigureAwait(true);
        }

        throw new InvalidOperationException($"Browser eval did not become true. Last result: {lastResult?.ToString() ?? "<none>"}");
    }

    private static async Task<System.Text.Json.JsonElement> WaitForConsoleEventsAsync(
        MainWindow window,
        params (string MessageFragment, string Level)[] expectedEvents)
    {
        System.Text.Json.JsonElement? lastResult = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserConsoleLog, new { limit = 50 }).ConfigureAwait(true);
            if (response.Ok)
            {
                var result = System.Text.Json.JsonSerializer.SerializeToElement(response.Result, AgentMuxJson.Options);
                lastResult = result.Clone();
                if (result.TryGetProperty("ok", out var ok)
                    && ok.GetBoolean()
                    && expectedEvents.All(expected => TryFindConsoleEvent(result, expected.MessageFragment, expected.Level, out _)))
                {
                    return result.Clone();
                }
            }

            await Task.Delay(250).ConfigureAwait(true);
        }

        throw new InvalidOperationException($"Browser console log did not contain expected events. Last result: {lastResult?.ToString() ?? "<none>"}");
    }

    private static bool TryFindConsoleEvent(
        System.Text.Json.JsonElement consoleLog,
        string messageFragment,
        string level,
        out System.Text.Json.JsonElement consoleEvent)
    {
        consoleEvent = default;
        if (!consoleLog.TryGetProperty("events", out var events)
            || events.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return false;
        }

        foreach (var candidate in events.EnumerateArray())
        {
            if (candidate.TryGetProperty("message", out var message)
                && message.ValueKind == System.Text.Json.JsonValueKind.String
                && message.GetString()?.Contains(messageFragment, StringComparison.Ordinal) == true
                && candidate.TryGetProperty("level", out var candidateLevel)
                && string.Equals(candidateLevel.GetString(), level, StringComparison.Ordinal))
            {
                consoleEvent = candidate.Clone();
                return true;
            }
        }

        return false;
    }

    private static async Task<System.Text.Json.JsonElement> WaitForNetworkEventAsync(MainWindow window, string urlFragment, string eventName)
    {
        System.Text.Json.JsonElement? lastResult = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserNetworkLog, new { limit = 50 }).ConfigureAwait(true);
            if (response.Ok)
            {
                var result = System.Text.Json.JsonSerializer.SerializeToElement(response.Result, AgentMuxJson.Options);
                lastResult = result.Clone();
                if (result.TryGetProperty("ok", out var ok)
                    && ok.GetBoolean()
                    && TryFindNetworkEvent(result, urlFragment, eventName, out _))
                {
                    return result.Clone();
                }
            }

            await Task.Delay(250).ConfigureAwait(true);
        }

        throw new InvalidOperationException($"Browser network log did not contain {eventName} for {urlFragment}. Last result: {lastResult?.ToString() ?? "<none>"}");
    }

    private static bool TryFindNetworkEvent(System.Text.Json.JsonElement networkLog, string urlFragment, string eventName, out System.Text.Json.JsonElement networkEvent)
    {
        networkEvent = default;
        if (!networkLog.TryGetProperty("events", out var events)
            || events.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return false;
        }

        foreach (var candidate in events.EnumerateArray())
        {
            if (candidate.TryGetProperty("event", out var eventProperty)
                && string.Equals(eventProperty.GetString(), eventName, StringComparison.Ordinal)
                && candidate.TryGetProperty("url", out var urlProperty)
                && urlProperty.ValueKind == System.Text.Json.JsonValueKind.String
                && (urlProperty.GetString()?.Contains(urlFragment, StringComparison.Ordinal) ?? false))
            {
                networkEvent = candidate.Clone();
                return true;
            }
        }

        return false;
    }

    private static async Task<System.Text.Json.JsonElement> WaitForNetworkRequestEventAsync(MainWindow window, string requestId, string eventName)
    {
        System.Text.Json.JsonElement? lastResult = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserNetworkLog, new { limit = 50 }).ConfigureAwait(true);
            if (response.Ok)
            {
                var result = System.Text.Json.JsonSerializer.SerializeToElement(response.Result, AgentMuxJson.Options);
                lastResult = result.Clone();
                if (result.TryGetProperty("ok", out var ok)
                    && ok.GetBoolean()
                    && TryFindNetworkRequestEvent(result, requestId, eventName, out _))
                {
                    return result.Clone();
                }
            }

            await Task.Delay(250).ConfigureAwait(true);
        }

        throw new InvalidOperationException($"Browser network log did not contain {eventName} for request id {requestId}. Last result: {lastResult?.ToString() ?? "<none>"}");
    }

    private static bool TryFindNetworkRequestEvent(System.Text.Json.JsonElement networkLog, string requestId, string eventName, out System.Text.Json.JsonElement networkEvent)
    {
        networkEvent = default;
        if (!networkLog.TryGetProperty("events", out var events)
            || events.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return false;
        }

        foreach (var candidate in events.EnumerateArray())
        {
            if (candidate.TryGetProperty("event", out var eventProperty)
                && string.Equals(eventProperty.GetString(), eventName, StringComparison.Ordinal)
                && candidate.TryGetProperty("requestId", out var requestIdProperty)
                && string.Equals(requestIdProperty.GetString(), requestId, StringComparison.Ordinal))
            {
                networkEvent = candidate.Clone();
                return true;
            }
        }

        return false;
    }

    private static async Task<System.Text.Json.JsonElement> WaitForBrowserRouteHitCountAsync(
        MainWindow window,
        string urlContains,
        string action,
        int minHitCount)
    {
        System.Text.Json.JsonElement? lastResult = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserRouteList).ConfigureAwait(true);
            if (response.Ok)
            {
                var result = System.Text.Json.JsonSerializer.SerializeToElement(response.Result, AgentMuxJson.Options);
                lastResult = result.Clone();
                if (result.TryGetProperty("ok", out var ok)
                    && ok.GetBoolean()
                    && TryFindBrowserRoute(result, urlContains, action, out var route)
                    && route.GetProperty("hitCount").GetInt32() >= minHitCount)
                {
                    return result.Clone();
                }
            }

            await Task.Delay(250).ConfigureAwait(true);
        }

        throw new InvalidOperationException($"Browser route {action} did not reach hit count {minHitCount} for {urlContains}. Last result: {lastResult?.ToString() ?? "<none>"}");
    }

    private static bool TryFindHarEntry(System.Text.Json.JsonElement harLog, string urlFragment, out System.Text.Json.JsonElement harEntry)
    {
        harEntry = default;
        if (!harLog.TryGetProperty("entries", out var entries)
            || entries.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return false;
        }

        foreach (var candidate in entries.EnumerateArray())
        {
            if (candidate.TryGetProperty("request", out var request)
                && request.TryGetProperty("url", out var url)
                && url.ValueKind == System.Text.Json.JsonValueKind.String
                && (url.GetString()?.Contains(urlFragment, StringComparison.Ordinal) ?? false))
            {
                harEntry = candidate.Clone();
                return true;
            }
        }

        return false;
    }

    private static bool TryFindBrowserRoute(
        System.Text.Json.JsonElement routeList,
        string urlContains,
        string action,
        out System.Text.Json.JsonElement route)
    {
        route = default;
        if (!routeList.TryGetProperty("routes", out var routes)
            || routes.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return false;
        }

        foreach (var candidate in routes.EnumerateArray())
        {
            if (candidate.TryGetProperty("urlContains", out var urlContainsProperty)
                && string.Equals(urlContainsProperty.GetString(), urlContains, StringComparison.Ordinal)
                && candidate.TryGetProperty("action", out var actionProperty)
                && string.Equals(actionProperty.GetString(), action, StringComparison.Ordinal))
            {
                route = candidate.Clone();
                return true;
            }
        }

        return false;
    }

    private static bool TryFindBrowserRouteHit(
        System.Text.Json.JsonElement routeList,
        string urlFragment,
        string action,
        out System.Text.Json.JsonElement hit)
    {
        hit = default;
        if (!routeList.TryGetProperty("recentHits", out var hits)
            || hits.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return false;
        }

        foreach (var candidate in hits.EnumerateArray())
        {
            if (candidate.TryGetProperty("url", out var urlProperty)
                && urlProperty.GetString()?.Contains(urlFragment, StringComparison.Ordinal) == true
                && candidate.TryGetProperty("action", out var actionProperty)
                && string.Equals(actionProperty.GetString(), action, StringComparison.Ordinal))
            {
                hit = candidate.Clone();
                return true;
            }
        }

        return false;
    }

    private static async Task<System.Text.Json.JsonElement> WaitForDownloadAsync(MainWindow window, string uriFragment, string state)
    {
        System.Text.Json.JsonElement? lastResult = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var response = await window.HandleRpcForSmokeTestAsync(AgentMuxMethods.BrowserDownloads, new { limit = 20 }).ConfigureAwait(true);
            if (response.Ok)
            {
                var result = System.Text.Json.JsonSerializer.SerializeToElement(response.Result, AgentMuxJson.Options);
                lastResult = result.Clone();
                if (result.TryGetProperty("ok", out var ok)
                    && ok.GetBoolean()
                    && TryFindDownload(result, uriFragment, state, out _))
                {
                    return result.Clone();
                }
            }

            await Task.Delay(250).ConfigureAwait(true);
        }

        throw new InvalidOperationException($"Browser download log did not contain {state} for {uriFragment}. Last result: {lastResult?.ToString() ?? "<none>"}");
    }

    private static bool TryFindDownload(System.Text.Json.JsonElement downloadLog, string uriFragment, string state, out System.Text.Json.JsonElement downloadEvent)
    {
        downloadEvent = default;
        if (!downloadLog.TryGetProperty("downloads", out var downloads)
            || downloads.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return false;
        }

        foreach (var candidate in downloads.EnumerateArray())
        {
            if (candidate.TryGetProperty("uri", out var uri)
                && uri.GetString()?.Contains(uriFragment, StringComparison.Ordinal) == true
                && candidate.TryGetProperty("state", out var candidateState)
                && string.Equals(candidateState.GetString(), state, StringComparison.Ordinal))
            {
                downloadEvent = candidate.Clone();
                return true;
            }
        }

        return false;
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

    private static string ResolvePtyTestHostPath()
    {
        var configuration = typeof(MainWindowSmokeTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Release";
        var path = System.IO.Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "AgentMux.Pty.TestHost",
            "bin",
            configuration,
            "net9.0-windows10.0.17763.0",
            "AgentMux.Pty.TestHost.exe");

        if (!System.IO.File.Exists(path))
        {
            throw new System.IO.FileNotFoundException("ConPTY test host was not built.", path);
        }

        return path;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "AgentMux.sln")))
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

    private sealed class RecordingNativeToastService : INativeToastService
    {
        public List<NativeToastRequest> Requests { get; } = [];

        public NativeToastResult TryShow(NativeToastRequest request)
        {
            Requests.Add(request);
            return NativeToastResult.Sent();
        }
    }

    private sealed class ThrowingNativeToastService : INativeToastService
    {
        public NativeToastResult TryShow(NativeToastRequest request) =>
            throw new InvalidOperationException("smoke-only native toast failure");
    }

    private sealed class LoopbackHttpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _serverTask;
        private readonly string _body;
        private readonly string _contentType;
        private readonly string? _contentDisposition;
        private readonly TimeSpan _responseDelay;

        private LoopbackHttpServer(
            string path,
            string body,
            string contentType,
            string? contentDisposition = null,
            TimeSpan? responseDelay = null)
        {
            _body = body;
            _contentType = contentType;
            _contentDisposition = contentDisposition;
            _responseDelay = responseDelay ?? TimeSpan.Zero;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Url = new Uri($"http://127.0.0.1:{port}{path}");
            _serverTask = ServeOneRequestAsync();
        }

        public Uri Url { get; }

        public static LoopbackHttpServer Start(string path, string body, string contentType = "text/plain; charset=utf-8", TimeSpan? responseDelay = null) =>
            new(path, body, contentType, responseDelay: responseDelay);

        public static LoopbackHttpServer StartAttachment(string path, string body, string fileName) =>
            new(path, body, "text/plain", $"attachment; filename=\"{fileName}\"");

        public async ValueTask DisposeAsync()
        {
            _cancellation.Cancel();
            _listener.Stop();
            try
            {
                await _serverTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException or TimeoutException)
            {
            }

            _cancellation.Dispose();
        }

        private async Task ServeOneRequestAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_cancellation.Token).ConfigureAwait(false);
                await using var stream = client.GetStream();
                await ReadRequestHeadersAsync(stream).ConfigureAwait(false);
                if (_responseDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_responseDelay, _cancellation.Token).ConfigureAwait(false);
                }

                var bodyBytes = Encoding.UTF8.GetBytes(_body);
                var contentDisposition = string.IsNullOrWhiteSpace(_contentDisposition)
                    ? ""
                    : $"Content-Disposition: {_contentDisposition}\r\n";
                var headers = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    $"Content-Type: {_contentType}\r\n" +
                    contentDisposition +
                    "Access-Control-Allow-Origin: *\r\n" +
                    "Cache-Control: no-store\r\n" +
                    $"Content-Length: {bodyBytes.Length}\r\n" +
                    "Connection: close\r\n\r\n");

                await stream.WriteAsync(headers, _cancellation.Token).ConfigureAwait(false);
                await stream.WriteAsync(bodyBytes, _cancellation.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
            }
            finally
            {
                _listener.Stop();
            }
        }

        private async Task ReadRequestHeadersAsync(NetworkStream stream)
        {
            var buffer = new byte[1024];
            var request = new StringBuilder();
            while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var bytesRead = await stream.ReadAsync(buffer, _cancellation.Token).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    return;
                }

                request.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
            }
        }
    }

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
