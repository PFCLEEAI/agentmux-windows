using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AgentMux.Core.Ipc;
using AgentMux.Core.Models;
using AgentMux.Core.Notifications;
using AgentMux.Core.Persistence;
using AgentMux.Win.App.Controls;
using AgentMux.Win.App.Input;
using AgentMux.Win.Pty;

namespace AgentMux.Win.App.Views;

public partial class MainWindow : Window
{
    private const int MaxNotifications = 200;

    private readonly ObservableCollection<WorkspaceState> _workspaces = [];
    private readonly List<TerminalNotification> _notifications = [];
    private readonly Dictionary<string, ConPtySession> _ptySessions = [];
    private readonly Dictionary<string, TerminalPaneView> _terminalViews = [];
    private readonly Dictionary<string, TerminalOutputProcessor> _terminalOutputProcessors = [];
    private readonly Dictionary<string, BrowserPaneView> _browserViews = [];
    private readonly HashSet<string> _ptyStartFailedPaneIds = [];
    private readonly SemaphoreSlim _browserSelectorWaitGate = new(1, 1);
    private readonly ShortcutSettings _shortcutSettings;
    private readonly SessionSnapshotStore? _sessionStore;
    private readonly bool _restoreSessionOnStartup;
    private readonly bool _persistSession;
    private NamedPipeRpcServer? _server;
    private CancellationTokenSource? _sessionSaveDebounce;
    private int _activeWorkspaceIndex;

    public MainWindow()
        : this(ShortcutSettings.Load(), new SessionSnapshotStore(), restoreSessionOnStartup: true, persistSession: true)
    {
    }

    internal MainWindow(ShortcutSettings shortcutSettings)
        : this(shortcutSettings, sessionStore: null, restoreSessionOnStartup: false, persistSession: false)
    {
    }

    internal MainWindow(
        ShortcutSettings shortcutSettings,
        SessionSnapshotStore? sessionStore,
        bool restoreSessionOnStartup,
        bool persistSession)
    {
        _shortcutSettings = shortcutSettings;
        _sessionStore = sessionStore;
        _restoreSessionOnStartup = restoreSessionOnStartup;
        _persistSession = persistSession;
        InitializeComponent();
        EnsureDefaultWorkspace();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_restoreSessionOnStartup)
        {
            await LoadSessionSnapshotAsync().ConfigureAwait(true);
        }

        InitializeWorkspaceListForCurrentSession();

        _server = new NamedPipeRpcServer(HandleRpcAsync);
        _server.Start();
        PipeStatus.Text = $"Pipe: {AgentMuxPipe.ForCurrentUser()}";

        await EnsurePanePtyAsync(ActivePane()).ConfigureAwait(false);
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        _sessionSaveDebounce?.Cancel();
        await SaveSessionSnapshotAsync().ConfigureAwait(true);

        var sessions = _ptySessions.Values.ToArray();
        _ptySessions.Clear();

        foreach (var session in sessions)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        if (_server is not null)
        {
            await _server.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void NewWorkspace_Click(object sender, RoutedEventArgs e)
    {
        CreateWorkspace($"Workspace {_workspaces.Count + 1}", null);
    }

    private void TestNotification_Click(object sender, RoutedEventArgs e)
    {
        AddNotification("AgentMux", "Scaffold notification");
    }

    private async void SplitRight_Click(object sender, RoutedEventArgs e)
    {
        if (SplitActivePane(SplitDirection.Right))
        {
            await EnsurePanePtyAsync(ActivePane()).ConfigureAwait(true);
        }

        RefreshWorkspaceView();
    }

    private async void SplitDown_Click(object sender, RoutedEventArgs e)
    {
        if (SplitActivePane(SplitDirection.Down))
        {
            await EnsurePanePtyAsync(ActivePane()).ConfigureAwait(true);
        }

        RefreshWorkspaceView();
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        OpenBrowserInActivePane("about:blank");
        RefreshWorkspaceView();
    }

    private async void NewSurface_Click(object sender, RoutedEventArgs e)
    {
        CreateSurface(ActiveWorkspace(), null);
        await EnsurePanePtyAsync(ActivePane()).ConfigureAwait(true);
        RefreshWorkspaceView();
    }

    private async void SurfaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index })
        {
            return;
        }

        if (SelectSurface(index, null))
        {
            await EnsurePanePtyAsync(ActivePane()).ConfigureAwait(true);
        }

        RefreshWorkspaceView();
    }

    private void WorkspaceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorkspaceList.SelectedIndex >= 0)
        {
            _activeWorkspaceIndex = WorkspaceList.SelectedIndex;
            RefreshWorkspaceView();
            QueueSessionSave();
        }
    }

    private async void SendTerminalInput_Click(object sender, RoutedEventArgs e)
    {
        await SendTerminalInputAsync().ConfigureAwait(false);
    }

    private async void TerminalInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await SendTerminalInputAsync().ConfigureAwait(false);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = HandlePreviewKeyDown(e.Key, e.SystemKey, Keyboard.Modifiers);
    }

    private async Task LoadSessionSnapshotAsync()
    {
        if (_sessionStore is null)
        {
            return;
        }

        var snapshot = await _sessionStore.LoadAsync().ConfigureAwait(true);
        if (snapshot?.Workspaces is { Count: > 0 })
        {
            ApplySessionSnapshot(snapshot);
        }
        else
        {
            EnsureDefaultWorkspace();
        }
    }

    private void ApplySessionSnapshot(SessionSnapshot snapshot)
    {
        _workspaces.Clear();
        foreach (var workspace in snapshot.Workspaces)
        {
            if (workspace is null)
            {
                continue;
            }

            NormalizeWorkspace(workspace);
            _workspaces.Add(workspace);
        }

        EnsureDefaultWorkspace();
        _activeWorkspaceIndex = Math.Clamp(snapshot.ActiveWorkspaceIndex, 0, _workspaces.Count - 1);
    }

    private void EnsureDefaultWorkspace()
    {
        if (_workspaces.Count > 0)
        {
            return;
        }

        _workspaces.Add(new WorkspaceState
        {
            Title = "Default",
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        });
        _activeWorkspaceIndex = 0;
    }

    private void InitializeWorkspaceListForCurrentSession()
    {
        EnsureDefaultWorkspace();
        _activeWorkspaceIndex = Math.Clamp(_activeWorkspaceIndex, 0, _workspaces.Count - 1);
        WorkspaceList.ItemsSource = _workspaces;
        WorkspaceList.SelectedIndex = _activeWorkspaceIndex;
        RefreshWorkspaceView();
    }

    private static void NormalizeWorkspace(WorkspaceState workspace)
    {
        workspace.Surfaces ??= [];
        if (workspace.Surfaces.Count == 0)
        {
            workspace.Surfaces.Add(SurfaceState.CreateDefault());
        }

        workspace.ActiveSurfaceIndex = Math.Clamp(workspace.ActiveSurfaceIndex, 0, workspace.Surfaces.Count - 1);
        foreach (var surface in workspace.Surfaces)
        {
            NormalizeSurface(surface);
        }
    }

    private static void NormalizeSurface(SurfaceState surface)
    {
        surface.Root ??= SplitNodeState.CreateLeaf();
        if (!HasAnyPane(surface.Root))
        {
            surface.Root = SplitNodeState.CreateLeaf();
        }

        if (surface.ActivePaneId is null || FindPane(surface.Root, surface.ActivePaneId) is null)
        {
            surface.ActivePaneId = FindFirstPane(surface.Root)?.Id;
        }

        if (surface.ZoomedPaneId is not null && FindPane(surface.Root, surface.ZoomedPaneId) is null)
        {
            surface.ZoomedPaneId = null;
        }
    }

    private static bool HasAnyPane(SplitNodeState node)
    {
        return node.Pane is not null
            || (node.First is not null && HasAnyPane(node.First))
            || (node.Second is not null && HasAnyPane(node.Second));
    }

    private SessionSnapshot BuildSessionSnapshot()
    {
        EnsureDefaultWorkspace();
        _activeWorkspaceIndex = Math.Clamp(_activeWorkspaceIndex, 0, _workspaces.Count - 1);
        foreach (var workspace in _workspaces)
        {
            NormalizeWorkspace(workspace);
        }

        return new SessionSnapshot
        {
            ActiveWorkspaceIndex = _activeWorkspaceIndex,
            Workspaces = _workspaces.Select(CloneWorkspaceForSnapshot).ToList()
        };
    }

    private static WorkspaceState CloneWorkspaceForSnapshot(WorkspaceState workspace)
    {
        var json = JsonSerializer.Serialize(workspace, AgentMuxJson.Options);
        var clone = JsonSerializer.Deserialize<WorkspaceState>(json, AgentMuxJson.Options) ?? new WorkspaceState();
        clone.UnreadCount = 0;
        clone.LatestNotification = null;
        foreach (var surface in clone.Surfaces)
        {
            ClearPaneNotificationState(surface.Root);
        }

        return clone;
    }

    private static void ClearPaneNotificationState(SplitNodeState? node)
    {
        if (node is null)
        {
            return;
        }

        if (node.Pane is not null)
        {
            node.Pane.HasUnreadNotification = false;
        }

        ClearPaneNotificationState(node.First);
        ClearPaneNotificationState(node.Second);
    }

    private void QueueSessionSave()
    {
        if (!_persistSession || _sessionStore is null)
        {
            return;
        }

        _sessionSaveDebounce?.Cancel();
        _sessionSaveDebounce = new CancellationTokenSource();
        _ = SaveSessionSnapshotAfterDelayAsync(_sessionSaveDebounce);
    }

    private async Task SaveSessionSnapshotAfterDelayAsync(CancellationTokenSource debounce)
    {
        try
        {
            await Task.Delay(250, debounce.Token).ConfigureAwait(false);
            await SaveSessionSnapshotAsync(debounce.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_sessionSaveDebounce, debounce))
            {
                _sessionSaveDebounce = null;
            }

            debounce.Dispose();
        }
    }

    private async Task SaveSessionSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (!_persistSession || _sessionStore is null)
        {
            return;
        }

        try
        {
            var snapshot = Dispatcher.CheckAccess()
                ? BuildSessionSnapshot()
                : await Dispatcher.InvokeAsync(BuildSessionSnapshot).Task.ConfigureAwait(false);
            await _sessionStore.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
        }
    }

    private bool HandlePreviewKeyDown(Key key, Key systemKey, ModifierKeys modifiers)
    {
        var effectiveKey = EffectiveKey(key, systemKey);
        return _shortcutSettings.TryMatch(effectiveKey, modifiers, out var action)
            && HandleShortcutAction(action);
    }

    private bool HandleShortcutAction(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.ToggleZoom:
                ToggleActivePaneZoom();
                return true;
            case ShortcutAction.ClosePane:
                CloseActivePane();
                return true;
            case ShortcutAction.FocusLeft:
                FocusPane(PaneFocusDirection.Left);
                return true;
            case ShortcutAction.FocusRight:
                FocusPane(PaneFocusDirection.Right);
                return true;
            case ShortcutAction.FocusUp:
                FocusPane(PaneFocusDirection.Up);
                return true;
            case ShortcutAction.FocusDown:
                FocusPane(PaneFocusDirection.Down);
                return true;
            case ShortcutAction.FocusPrevious:
                return CycleActivePane(reverse: true);
            case ShortcutAction.FocusNext:
                return CycleActivePane(reverse: false);
            default:
                return false;
        }
    }

    private async Task<AgentMuxResponse> HandleRpcAsync(AgentMuxRequest request, CancellationToken cancellationToken)
    {
        if (Dispatcher.CheckAccess())
        {
            return await HandleRpcOnUiAsync(request, cancellationToken).ConfigureAwait(true);
        }

        var responseTask = await Dispatcher.InvokeAsync(() => HandleRpcOnUiAsync(request, cancellationToken)).Task.ConfigureAwait(false);
        return await responseTask.ConfigureAwait(false);
    }

    private async Task<AgentMuxResponse> HandleRpcOnUiAsync(AgentMuxRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AgentMuxResponse response = request.Method switch
        {
            AgentMuxMethods.Ping => AgentMuxResponse.Success(request.Id, new { pong = true }),
            AgentMuxMethods.Status => AgentMuxResponse.Success(request.Id, BuildStatus()),
            AgentMuxMethods.Tree => AgentMuxResponse.Success(request.Id, _workspaces),
            AgentMuxMethods.WorkspaceList => AgentMuxResponse.Success(request.Id, _workspaces),
            AgentMuxMethods.WorkspaceCreate => AgentMuxResponse.Success(request.Id, HandleWorkspaceCreate(request.Params)),
            AgentMuxMethods.WorkspaceSelect => AgentMuxResponse.Success(request.Id, HandleWorkspaceSelect(request.Params)),
            AgentMuxMethods.SurfaceList => AgentMuxResponse.Success(request.Id, BuildSurfaceList()),
            AgentMuxMethods.SurfaceCreate => AgentMuxResponse.Success(request.Id, HandleSurfaceCreate(request.Params)),
            AgentMuxMethods.SurfaceSelect => AgentMuxResponse.Success(request.Id, HandleSurfaceSelect(request.Params)),
            AgentMuxMethods.Notify => AgentMuxResponse.Success(request.Id, HandleNotify(request.Params)),
            AgentMuxMethods.NotificationsList => AgentMuxResponse.Success(request.Id, HandleNotificationsList(request.Params)),
            AgentMuxMethods.NotificationsClear => AgentMuxResponse.Success(request.Id, HandleNotificationsClear()),
            AgentMuxMethods.NotificationsJumpLatest => AgentMuxResponse.Success(request.Id, HandleNotificationsJumpLatest()),
            AgentMuxMethods.Split => AgentMuxResponse.Success(request.Id, HandleSplit(request.Params)),
            AgentMuxMethods.SendText => AgentMuxResponse.Success(request.Id, HandleSendText(request.Params)),
            AgentMuxMethods.SendKey => AgentMuxResponse.Success(request.Id, HandleSendKey(request.Params)),
            AgentMuxMethods.ResizeTerminal => AgentMuxResponse.Success(request.Id, HandleResizeTerminal(request.Params)),
            AgentMuxMethods.ReadScreen => AgentMuxResponse.Success(request.Id, new { text = ActivePane()?.LastScreenText ?? "" }),
            AgentMuxMethods.FocusPane => HandleFocusPane(request.Id, request.Params),
            AgentMuxMethods.ToggleZoom => AgentMuxResponse.Success(request.Id, HandleToggleZoom()),
            AgentMuxMethods.ClosePane => AgentMuxResponse.Success(request.Id, HandleClosePane()),
            AgentMuxMethods.OpenUrl => AgentMuxResponse.Success(request.Id, HandleOpenUrl(request.Params)),
            AgentMuxMethods.BrowserEval => AgentMuxResponse.Success(request.Id, await HandleBrowserEvalAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserClick => AgentMuxResponse.Success(request.Id, await HandleBrowserClickAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserFill => AgentMuxResponse.Success(request.Id, await HandleBrowserFillAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserType => AgentMuxResponse.Success(request.Id, await HandleBrowserTypeAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserPress => AgentMuxResponse.Success(request.Id, await HandleBrowserPressAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserScreenshot => AgentMuxResponse.Success(request.Id, await HandleBrowserScreenshotAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserFrameTree => AgentMuxResponse.Success(request.Id, await HandleBrowserFrameTreeAsync().ConfigureAwait(true)),
            AgentMuxMethods.BrowserWaitForSelector => AgentMuxResponse.Success(request.Id, await HandleBrowserWaitForSelectorAsync(request.Params, cancellationToken).ConfigureAwait(true)),
            AgentMuxMethods.BrowserConsoleLog => AgentMuxResponse.Success(request.Id, await HandleBrowserConsoleLogAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserConsoleClear => AgentMuxResponse.Success(request.Id, await HandleBrowserConsoleClearAsync().ConfigureAwait(true)),
            AgentMuxMethods.BrowserNetworkLog => AgentMuxResponse.Success(request.Id, await HandleBrowserNetworkLogAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserNetworkClear => AgentMuxResponse.Success(request.Id, await HandleBrowserNetworkClearAsync().ConfigureAwait(true)),
            AgentMuxMethods.BrowserResponseBody => AgentMuxResponse.Success(request.Id, await HandleBrowserResponseBodyAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserHarMetadata => AgentMuxResponse.Success(request.Id, await HandleBrowserHarMetadataAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserDownloads => AgentMuxResponse.Success(request.Id, await HandleBrowserDownloadsAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserDownloadsClear => AgentMuxResponse.Success(request.Id, await HandleBrowserDownloadsClearAsync().ConfigureAwait(true)),
            _ => AgentMuxResponse.Failure(request.Id, $"Unsupported method: {request.Method}")
        };

        if (!IsBrowserAutomationMethod(request.Method))
        {
            RefreshWorkspaceView();
        }

        return response;
    }

    private object BuildStatus()
    {
        var workspace = ActiveWorkspace();
        NormalizeWorkspace(workspace);
        var surface = ActiveSurface();
        return new
        {
            app = "AgentMux Windows",
            status = "pre-alpha scaffold",
            workspaceCount = _workspaces.Count,
            activeWorkspaceIndex = _activeWorkspaceIndex,
            surfaceCount = workspace.Surfaces.Count,
            activeSurfaceIndex = workspace.ActiveSurfaceIndex,
            activeSurfaceTitle = surface.Title,
            notificationCount = _notifications.Count,
            terminalSessionCount = _ptySessions.Count,
            browserPaneCount = CountPaneKind(surface.Root, PaneKind.Browser)
        };
    }

    private WorkspaceState HandleWorkspaceCreate(JsonElement? parameters)
    {
        var parsed = Deserialize<WorkspaceCreateParams>(parameters);
        return CreateWorkspace(parsed?.Title, parsed?.Cwd);
    }

    private object HandleWorkspaceSelect(JsonElement? parameters)
    {
        var parsed = Deserialize<WorkspaceSelectParams>(parameters);
        var index = parsed?.Index ?? 0;
        if (index < 0 || index >= _workspaces.Count)
        {
            return new { selected = false, reason = "index out of range" };
        }

        _activeWorkspaceIndex = index;
        Dispatcher.Invoke(() => WorkspaceList.SelectedIndex = index);
        QueueSessionSave();
        return new { selected = true, index };
    }

    private object BuildSurfaceList()
    {
        var workspace = ActiveWorkspace();
        NormalizeWorkspace(workspace);
        return new
        {
            workspaceId = workspace.Id,
            activeSurfaceIndex = workspace.ActiveSurfaceIndex,
            surfaces = workspace.Surfaces
                .Select((surface, index) => BuildSurfaceDto(workspace, surface, index))
                .ToList()
        };
    }

    private object HandleSurfaceCreate(JsonElement? parameters)
    {
        var parsed = Deserialize<SurfaceCreateParams>(parameters);
        var workspace = ActiveWorkspace();
        var surface = CreateSurface(workspace, parsed?.Title);
        _ = EnsurePanePtyAsync(ActivePane());
        RefreshWorkspaceView();
        var index = workspace.Surfaces.IndexOf(surface);
        return new
        {
            created = true,
            workspaceId = workspace.Id,
            surface = BuildSurfaceDto(workspace, surface, index)
        };
    }

    private object HandleSurfaceSelect(JsonElement? parameters)
    {
        if (!TryReadSurfaceTarget(parameters, out var index, out var id, out var error))
        {
            return new { selected = false, reason = error };
        }

        if (!SelectSurface(index, id))
        {
            return new
            {
                selected = false,
                reason = index.HasValue ? "index out of range" : "surface not found"
            };
        }

        _ = EnsurePanePtyAsync(ActivePane());
        RefreshWorkspaceView();
        var workspace = ActiveWorkspace();
        var surface = ActiveSurface();
        return new
        {
            selected = true,
            workspaceId = workspace.Id,
            surface = BuildSurfaceDto(workspace, surface, workspace.ActiveSurfaceIndex)
        };
    }

    private TerminalNotification HandleNotify(JsonElement? parameters)
    {
        var parsed = Deserialize<NotifyParams>(parameters);
        return AddNotification(parsed?.Title ?? "Terminal", parsed?.Body ?? "", parsed?.Subtitle);
    }

    private object HandleNotificationsList(JsonElement? parameters)
    {
        var parsed = Deserialize<NotificationListParams>(parameters);
        var limit = Math.Min(parsed?.Limit is > 0 ? parsed.Limit.Value : 50, MaxNotifications);
        return new
        {
            notifications = _notifications
                .OrderByDescending(notification => notification.CreatedAt)
                .Take(limit)
                .Select(notification => new
                {
                    notification.Id,
                    notification.WorkspaceId,
                    notification.PaneId,
                    notification.Title,
                    notification.Subtitle,
                    notification.Body,
                    notification.CreatedAt,
                    isRead = notification.IsRead
                })
                .ToList(),
            unreadCount = _notifications.Count(notification => !notification.IsRead)
        };
    }

    private object HandleNotificationsClear()
    {
        var cleared = 0;
        foreach (var notification in _notifications)
        {
            if (!notification.IsRead)
            {
                notification.IsRead = true;
                cleared++;
            }
        }

        RecalculateNotificationState();
        RefreshWorkspaceView();
        QueueSessionSave();
        return new { cleared };
    }

    private object HandleNotificationsJumpLatest()
    {
        var notification = _notifications
            .Where(candidate => !candidate.IsRead)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .FirstOrDefault();

        if (notification is null)
        {
            return new { jumped = false, reason = "no unread notifications" };
        }

        var target = FindWorkspaceSurfaceAndPane(notification.PaneId);
        if (target is null)
        {
            notification.IsRead = true;
            RecalculateNotificationState();
            RefreshWorkspaceView();
            QueueSessionSave();
            return new { jumped = false, reason = "notification pane not found", notificationId = notification.Id };
        }

        _activeWorkspaceIndex = _workspaces.IndexOf(target.Value.Workspace);
        target.Value.Workspace.ActiveSurfaceIndex = target.Value.Workspace.Surfaces.IndexOf(target.Value.Surface);
        target.Value.Surface.ActivePaneId = target.Value.Pane.Id;
        WorkspaceList.SelectedIndex = _activeWorkspaceIndex;
        notification.IsRead = true;
        RecalculateNotificationState();
        RefreshWorkspaceView();
        QueueSessionSave();
        return new
        {
            jumped = true,
            notificationId = notification.Id,
            workspaceId = target.Value.Workspace.Id,
            paneId = target.Value.Pane.Id
        };
    }

    private object HandleSplit(JsonElement? parameters)
    {
        var parsed = Deserialize<SplitParams>(parameters);
        var direction = string.Equals(parsed?.Direction, "down", StringComparison.OrdinalIgnoreCase)
            ? SplitDirection.Down
            : SplitDirection.Right;

        var changed = SplitActivePane(direction);
        if (changed)
        {
            _ = EnsurePanePtyAsync(ActivePane());
        }

        return new { split = changed, direction = direction.ToString().ToLowerInvariant() };
    }

    private object HandleSendText(JsonElement? parameters)
    {
        var parsed = Deserialize<SendTextParams>(parameters);
        var pane = ActivePane();
        if (pane?.Kind != PaneKind.Terminal)
        {
            return new { sent = false, reason = "active pane is not a terminal" };
        }

        var text = parsed?.Text ?? "";
        _ = SendTextToTerminalAsync(text);

        return new { sent = true, bytes = (parsed?.Text ?? "").Length };
    }

    private object HandleSendKey(JsonElement? parameters)
    {
        if (ActivePane()?.Kind != PaneKind.Terminal)
        {
            return new { sent = false, reason = "active pane is not a terminal" };
        }

        var parsed = Deserialize<Dictionary<string, string>>(parameters);
        string? key = null;
        parsed?.TryGetValue("key", out key);
        if (string.IsNullOrWhiteSpace(key))
        {
            parsed?.TryGetValue("_arg0", out key);
        }

        if (!TerminalKeyEncoder.TryEncode(key, out var sequence))
        {
            return new { sent = false, key = key ?? "", reason = "unsupported key" };
        }

        _ = SendTerminalSequenceAsync(sequence, $"[key: {key}]{Environment.NewLine}");
        return new { sent = true, key, bytes = Encoding.UTF8.GetByteCount(sequence) };
    }

    private object HandleResizeTerminal(JsonElement? parameters)
    {
        if (!TryReadPositiveIntParam(parameters, "cols", out var cols)
            || !TryReadPositiveIntParam(parameters, "rows", out var rows))
        {
            return new { resized = false, reason = "cols and rows must be positive integers" };
        }

        var pane = ActivePane();
        if (pane?.Kind != PaneKind.Terminal)
        {
            return new { resized = false, reason = "active pane is not a terminal" };
        }

        var running = _ptySessions.TryGetValue(pane.Id, out var session) && session.IsRunning;
        var changed = ResizeTerminalPane(pane, cols, rows);

        return new
        {
            resized = true,
            changed,
            paneId = pane.Id,
            pane.Cols,
            pane.Rows,
            running
        };
    }

    private AgentMuxResponse HandleFocusPane(string requestId, JsonElement? parameters)
    {
        var parsed = Deserialize<FocusPaneParams>(parameters);
        if (!PaneFocusNavigator.TryParseDirection(parsed?.Direction, out var direction))
        {
            return AgentMuxResponse.Failure(requestId, "direction must be next, previous, left, right, up, or down");
        }

        var previousPaneId = ActivePane()?.Id;
        if (!FocusPane(direction))
        {
            return AgentMuxResponse.Success(
                requestId,
                new
                {
                    focused = false,
                    direction = direction.ToString().ToLowerInvariant(),
                    activePaneId = previousPaneId,
                    reason = "no pane in that direction"
                });
        }

        return AgentMuxResponse.Success(
            requestId,
            new
            {
                focused = true,
                direction = direction.ToString().ToLowerInvariant(),
                previousPaneId,
                paneId = ActivePane()?.Id
            });
    }

    private object HandleToggleZoom()
    {
        var pane = ActivePane();
        if (pane is null)
        {
            return new { zoomed = false, reason = "no active pane" };
        }

        return ToggleActivePaneZoom();
    }

    private object HandleClosePane()
    {
        var pane = ActivePane();
        if (pane is null)
        {
            return new { closed = false, reason = "no active pane" };
        }

        return CloseActivePane();
    }

    private object HandleOpenUrl(JsonElement? parameters)
    {
        var parsed = Deserialize<OpenUrlParams>(parameters);
        var pane = ActivePane();
        if (pane is null)
        {
            return new { opened = false, reason = "no active pane" };
        }

        var url = OpenBrowserInPane(pane, parsed?.Url);
        return new { opened = true, paneId = pane.Id, url };
    }

    private async Task<object> HandleBrowserEvalAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserEvalParams>(parameters);
        if (string.IsNullOrWhiteSpace(parsed?.Script))
        {
            return new { ok = false, reason = "script is required" };
        }

        return await RunBrowserScriptAsync(view => view.EvaluateScriptAsync(parsed.Script)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserClickAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserSelectorParams>(parameters);
        if (string.IsNullOrWhiteSpace(parsed?.Selector))
        {
            return new { ok = false, reason = "selector is required" };
        }

        return await RunBrowserScriptAsync(view => view.ClickAsync(parsed.Selector, parsed.Frame)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserFillAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserFillParams>(parameters);
        if (string.IsNullOrWhiteSpace(parsed?.Selector))
        {
            return new { ok = false, reason = "selector is required" };
        }

        return await RunBrowserScriptAsync(view => view.FillAsync(parsed.Selector, parsed.Text ?? "", parsed.Frame)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserTypeAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserFillParams>(parameters);
        if (string.IsNullOrWhiteSpace(parsed?.Selector))
        {
            return new { ok = false, reason = "selector is required" };
        }

        return await RunBrowserScriptAsync(view => view.TypeAsync(parsed.Selector, parsed.Text ?? "", parsed.Frame)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserPressAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserPressParams>(parameters);
        if (string.IsNullOrWhiteSpace(parsed?.Key))
        {
            return new { ok = false, reason = "key is required" };
        }

        return await RunBrowserScriptAsync(view => view.PressAsync(parsed.Key, parsed.Selector, parsed.Frame)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserScreenshotAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserScreenshotParams>(parameters);
        if (string.IsNullOrWhiteSpace(parsed?.Path))
        {
            return new { ok = false, reason = "path is required" };
        }

        if (!TryGetActiveBrowserView(out var view, out var reason))
        {
            return new { ok = false, reason };
        }

        try
        {
            var path = await view.CapturePngAsync(parsed.Path).ConfigureAwait(true);
            return new { ok = true, path };
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return new { ok = false, reason = ex.Message };
        }
    }

    private async Task<object> HandleBrowserFrameTreeAsync()
    {
        return await RunBrowserScriptAsync(view => view.GetFrameTreeAsync()).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserWaitForSelectorAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        var parsed = Deserialize<BrowserWaitForSelectorParams>(parameters);
        if (string.IsNullOrWhiteSpace(parsed?.Selector))
        {
            return new { ok = false, reason = "selector is required" };
        }

        if (parsed.TimeoutMs is <= 0)
        {
            return new { ok = false, reason = "timeoutMs must be positive", timeoutMs = parsed.TimeoutMs };
        }

        if (!await _browserSelectorWaitGate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
        {
            return new { ok = false, reason = "selector wait already running" };
        }

        try
        {
            return await RunBrowserScriptAsync(view => view.WaitForSelectorAsync(
                parsed.Selector,
                parsed.State,
                parsed.TimeoutMs,
                parsed.Frame,
                cancellationToken)).ConfigureAwait(true);
        }
        finally
        {
            _browserSelectorWaitGate.Release();
        }
    }

    private async Task<object> HandleBrowserConsoleLogAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserConsoleLogParams>(parameters);
        return await RunBrowserScriptAsync(view => view.GetConsoleLogAsync(parsed?.Limit)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserConsoleClearAsync()
    {
        return await RunBrowserScriptAsync(view => view.ClearConsoleLogAsync()).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserNetworkLogAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserNetworkLogParams>(parameters);
        return await RunBrowserScriptAsync(view => view.GetNetworkLogAsync(parsed?.Limit)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserNetworkClearAsync()
    {
        return await RunBrowserScriptAsync(view => view.ClearNetworkLogAsync()).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserResponseBodyAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserResponseBodyParams>(parameters);
        if (string.IsNullOrWhiteSpace(parsed?.RequestId))
        {
            return new { ok = false, reason = "requestId is required" };
        }

        return await RunBrowserScriptAsync(view => view.GetResponseBodyAsync(parsed.RequestId)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserHarMetadataAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserHarMetadataParams>(parameters);
        if (string.IsNullOrWhiteSpace(parsed?.Path))
        {
            return new { ok = false, reason = "path is required" };
        }

        return await RunBrowserScriptAsync(view => view.ExportHarMetadataAsync(parsed.Path)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserDownloadsAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserDownloadLogParams>(parameters);
        return await RunBrowserScriptAsync(view => view.GetDownloadLogAsync(parsed?.Limit)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserDownloadsClearAsync()
    {
        return await RunBrowserScriptAsync(view => view.ClearDownloadLogAsync()).ConfigureAwait(true);
    }

    private async Task<ConPtySession?> EnsurePanePtyAsync(PaneState? pane)
    {
        Dispatcher.VerifyAccess();
        if (pane?.Kind != PaneKind.Terminal || _ptyStartFailedPaneIds.Contains(pane.Id))
        {
            return null;
        }

        if (_ptySessions.TryGetValue(pane.Id, out var existing))
        {
            if (existing.IsRunning)
            {
                return existing;
            }

            _ptySessions.Remove(pane.Id);
            await existing.DisposeAsync().ConfigureAwait(true);
        }

        var session = new ConPtySession();
        WirePanePtyEvents(pane.Id, session);
        _ptySessions[pane.Id] = session;

        try
        {
            await session.StartAsync(new PtyLaunchOptions
            {
                CommandLine = pane.Shell,
                WorkingDirectory = pane.WorkingDirectory,
                Cols = pane.Cols,
                Rows = pane.Rows
            }).ConfigureAwait(true);

            return session;
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _ptySessions.Remove(pane.Id);
            _ptyStartFailedPaneIds.Add(pane.Id);
            await session.DisposeAsync().ConfigureAwait(true);

            pane.LastScreenText = $"ConPTY did not start: {ex.Message}";
            UpdateTerminalView(pane);
            RefreshWorkspaceView();
            return null;
        }
    }

    private void WirePanePtyEvents(string paneId, ConPtySession session)
    {
        var decoder = Encoding.UTF8.GetDecoder();
        session.OutputReceived += bytes =>
        {
            var text = DecodePtyOutput(decoder, bytes.Span);
            if (text.Length == 0)
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (FindPaneById(paneId) is { } pane)
                {
                    ApplyTerminalOutput(pane, text);
                }
            });
        };
        session.Exited += exitCode =>
        {
            var tail = DecodePtyOutput(decoder, ReadOnlySpan<byte>.Empty, flush: true);
            Dispatcher.Invoke(() =>
            {
                if (FindPaneById(paneId) is { } pane)
                {
                    ApplyTerminalOutput(pane, tail);
                    AppendVisibleTerminalText(pane, string.Concat(Environment.NewLine, $"[process exited: {exitCode}]", Environment.NewLine));
                }

                RefreshWorkspaceView();
            });
        };
    }

    private static string DecodePtyOutput(Decoder decoder, ReadOnlySpan<byte> bytes, bool flush = false)
    {
        lock (decoder)
        {
            var charCount = decoder.GetCharCount(bytes, flush);
            if (charCount == 0)
            {
                return "";
            }

            var chars = new char[charCount];
            var charsWritten = decoder.GetChars(bytes, chars, flush);
            return new string(chars, 0, charsWritten);
        }
    }

    private async Task SendTerminalInputAsync()
    {
        string text;
        Dispatcher.VerifyAccess();
        text = TerminalInput.Text;
        TerminalInput.Clear();

        if (ActivePane() is { Kind: PaneKind.Browser } pane)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                OpenBrowserInPane(pane, text);
                RefreshWorkspaceView();
            }

            return;
        }

        await SendTextToTerminalAsync(text).ConfigureAwait(false);
    }

    private async Task SendTextToTerminalAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await SendTerminalSequenceAsync(text + Environment.NewLine, $"> {text}{Environment.NewLine}").ConfigureAwait(true);
    }

    private async Task SendTerminalSequenceAsync(string sequence, string? fallbackText = null)
    {
        await SendTerminalSequenceToPaneAsync(ActivePane(), sequence, fallbackText).ConfigureAwait(true);
    }

    private async Task SendTerminalSequenceToPaneAsync(PaneState? pane, string sequence, string? fallbackText = null)
    {
        if (string.IsNullOrEmpty(sequence) || pane?.Kind != PaneKind.Terminal)
        {
            return;
        }

        Dispatcher.VerifyAccess();
        var pty = await EnsurePanePtyAsync(pane).ConfigureAwait(true);
        if (pty is { IsRunning: true })
        {
            try
            {
                await pty.WriteAsync(Encoding.UTF8.GetBytes(sequence)).ConfigureAwait(true);
            }
            catch (InvalidOperationException ex)
            {
                if (pane is not null)
                {
                    var text = string.Concat(Environment.NewLine, $"[send failed: {ex.Message}]", Environment.NewLine);
                    AppendVisibleTerminalText(pane, text);
                }

                RefreshWorkspaceView();
            }
        }
        else
        {
            if (pane is not null)
            {
                var text = fallbackText ?? sequence;
                AppendVisibleTerminalText(pane, text);
            }

            RefreshWorkspaceView();
        }
    }

    private WorkspaceState CreateWorkspace(string? title, string? cwd)
    {
        var workspace = new WorkspaceState
        {
            Title = string.IsNullOrWhiteSpace(title) ? $"Workspace {_workspaces.Count + 1}" : title,
            WorkingDirectory = string.IsNullOrWhiteSpace(cwd)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : cwd
        };

        Dispatcher.Invoke(() =>
        {
            _workspaces.Add(workspace);
            _activeWorkspaceIndex = _workspaces.Count - 1;
            WorkspaceList.SelectedIndex = _activeWorkspaceIndex;
        });

        QueueSessionSave();
        return workspace;
    }

    private SurfaceState CreateSurface(WorkspaceState workspace, string? title)
    {
        NormalizeWorkspace(workspace);
        var surface = SurfaceState.CreateDefault();
        surface.Title = string.IsNullOrWhiteSpace(title)
            ? $"Surface {workspace.Surfaces.Count + 1}"
            : title.Trim();
        NormalizeSurface(surface);
        workspace.Surfaces.Add(surface);
        workspace.ActiveSurfaceIndex = workspace.Surfaces.Count - 1;
        QueueSessionSave();
        return surface;
    }

    private bool SelectSurface(int? index, string? id)
    {
        var workspace = ActiveWorkspace();
        NormalizeWorkspace(workspace);
        var selectedIndex = index ?? workspace.Surfaces.FindIndex(surface => string.Equals(surface.Id, id, StringComparison.Ordinal));
        if (selectedIndex < 0 || selectedIndex >= workspace.Surfaces.Count)
        {
            return false;
        }

        workspace.ActiveSurfaceIndex = selectedIndex;
        ActivePane();
        QueueSessionSave();
        return true;
    }

    private TerminalNotification AddNotification(string title, string body, string? subtitle = null, PaneState? sourcePane = null)
    {
        var target = sourcePane is null ? null : FindWorkspaceSurfaceAndPane(sourcePane.Id);
        var workspace = target.HasValue ? target.Value.Workspace : ActiveWorkspace();
        var pane = target.HasValue ? target.Value.Pane : sourcePane ?? ActivePane();
        var notification = new TerminalNotification
        {
            WorkspaceId = workspace.Id,
            PaneId = pane?.Id ?? "",
            Title = title,
            Subtitle = subtitle,
            Body = body
        };

        _notifications.Add(notification);
        TrimNotificationLog();
        RecalculateNotificationState();
        QueueSessionSave();
        return notification;
    }

    private void TrimNotificationLog()
    {
        while (_notifications.Count > MaxNotifications)
        {
            _notifications.RemoveAt(0);
        }
    }

    private void RecalculateNotificationState()
    {
        foreach (var workspace in _workspaces)
        {
            workspace.UnreadCount = 0;
            workspace.LatestNotification = null;
            foreach (var surface in workspace.Surfaces)
            {
                ClearPaneNotificationState(surface.Root);
            }
        }

        foreach (var notification in _notifications
            .Where(candidate => !candidate.IsRead)
            .OrderBy(candidate => candidate.CreatedAt))
        {
            var target = FindWorkspaceSurfaceAndPane(notification.PaneId);
            if (target is null)
            {
                continue;
            }

            target.Value.Workspace.UnreadCount++;
            target.Value.Workspace.LatestNotification = notification.Body;
            target.Value.Pane.HasUnreadNotification = true;
        }
    }

    private WorkspaceState ActiveWorkspace()
    {
        if (_activeWorkspaceIndex < 0 || _activeWorkspaceIndex >= _workspaces.Count)
        {
            _activeWorkspaceIndex = 0;
        }

        return _workspaces[_activeWorkspaceIndex];
    }

    private SurfaceState ActiveSurface()
    {
        var workspace = ActiveWorkspace();
        if (workspace.ActiveSurfaceIndex < 0 || workspace.ActiveSurfaceIndex >= workspace.Surfaces.Count)
        {
            workspace.ActiveSurfaceIndex = 0;
        }

        return workspace.Surfaces[workspace.ActiveSurfaceIndex];
    }

    private PaneState? ActivePane()
    {
        var surface = ActiveSurface();
        var pane = surface.ActivePaneId is null ? null : FindPane(surface.Root, surface.ActivePaneId);
        pane ??= FindFirstPane(surface.Root);
        surface.ActivePaneId = pane?.Id;
        return pane;
    }

    private static PaneState? FindFirstPane(SplitNodeState node)
    {
        if (node.Pane is not null)
        {
            return node.Pane;
        }

        return node.First is not null ? FindFirstPane(node.First) : node.Second is not null ? FindFirstPane(node.Second) : null;
    }

    private static PaneState? FindPane(SplitNodeState node, string paneId)
    {
        if (node.Pane?.Id == paneId)
        {
            return node.Pane;
        }

        return node.First is not null && FindPane(node.First, paneId) is { } first
            ? first
            : node.Second is not null
                ? FindPane(node.Second, paneId)
                : null;
    }

    private PaneState? FindPaneById(string paneId)
    {
        foreach (var workspace in _workspaces)
        {
            foreach (var surface in workspace.Surfaces)
            {
                if (FindPane(surface.Root, paneId) is { } pane)
                {
                    return pane;
                }
            }
        }

        return null;
    }

    private PaneTarget? FindWorkspaceSurfaceAndPane(string paneId)
    {
        if (string.IsNullOrWhiteSpace(paneId))
        {
            return null;
        }

        foreach (var workspace in _workspaces)
        {
            foreach (var surface in workspace.Surfaces)
            {
                if (FindPane(surface.Root, paneId) is { } pane)
                {
                    return new PaneTarget(workspace, surface, pane);
                }
            }
        }

        return null;
    }

    private bool SplitActivePane(SplitDirection direction)
    {
        var surface = ActiveSurface();
        var activePane = ActivePane();
        if (activePane is null)
        {
            return false;
        }

        var changed = SplitPane(surface.Root, activePane.Id, direction, out var newPane);
        if (changed)
        {
            surface.ZoomedPaneId = null;
            surface.ActivePaneId = newPane?.Id ?? activePane.Id;
            QueueSessionSave();
        }

        return changed;
    }

    private bool CycleActivePane(bool reverse)
    {
        return FocusPane(reverse ? PaneFocusDirection.Previous : PaneFocusDirection.Next);
    }

    private bool FocusPane(PaneFocusDirection direction)
    {
        var surface = ActiveSurface();
        var zoomedPane = PaneTreeEditor.FindPane(surface.Root, surface.ZoomedPaneId);
        if (zoomedPane is not null)
        {
            surface.ActivePaneId = zoomedPane.Id;
        }

        if (!PaneFocusNavigator.TryMoveFocus(surface, direction, out _))
        {
            return false;
        }

        if (zoomedPane is not null)
        {
            surface.ZoomedPaneId = null;
        }

        RefreshWorkspaceView();
        QueueSessionSave();
        return true;
    }

    private object ToggleActivePaneZoom()
    {
        var surface = ActiveSurface();
        var pane = ActivePane();
        if (pane is null || !PaneTreeEditor.TryToggleZoom(surface, pane.Id, out var zoomed))
        {
            return new { zoomed = false, reason = "no active pane" };
        }

        RefreshWorkspaceView();
        QueueSessionSave();
        return new { zoomed, paneId = pane.Id };
    }

    private object CloseActivePane()
    {
        var surface = ActiveSurface();
        var pane = ActivePane();
        if (pane is null)
        {
            return new { closed = false, reason = "no active pane" };
        }

        if (!PaneTreeEditor.TryClosePane(surface, pane.Id, out var closedPane, out var focusedPane))
        {
            return new { closed = false, paneId = pane.Id, reason = "cannot close the last pane" };
        }

        CleanupPaneResources(closedPane!);
        if (focusedPane?.Kind == PaneKind.Terminal)
        {
            _ = EnsurePanePtyAsync(focusedPane);
        }

        RefreshWorkspaceView();
        QueueSessionSave();
        return new { closed = true, paneId = closedPane!.Id, activePaneId = focusedPane?.Id };
    }

    private static bool SplitPane(SplitNodeState node, string paneId, SplitDirection direction, out PaneState? newPane)
    {
        if (node.Pane?.Id == paneId)
        {
            var existingPane = node.Pane;
            var second = SplitNodeState.CreateLeaf();
            node.Pane = null;
            node.Direction = direction;
            node.First = new SplitNodeState { Pane = existingPane };
            node.Second = second;
            newPane = second.Pane;
            return true;
        }

        if (node.First is not null && SplitPane(node.First, paneId, direction, out newPane))
        {
            return true;
        }

        if (node.Second is not null && SplitPane(node.Second, paneId, direction, out newPane))
        {
            return true;
        }

        newPane = null;
        return false;
    }

    private void RefreshWorkspaceView()
    {
        var workspace = ActiveWorkspace();
        var surface = ActiveSurface();
        var activePane = ActivePane();
        WorkspaceTitle.Text = workspace.Title;
        WorkspaceMeta.Text = $"{workspace.WorkingDirectory}  |  surfaces: {workspace.Surfaces.Count}  |  panes: {CountPanes(surface.Root)}  |  unread: {workspace.UnreadCount}";
        RefreshSurfaceTabs(workspace);
        var activeSessionRunning = activePane is not null
            && _ptySessions.TryGetValue(activePane.Id, out var activeSession)
            && activeSession.IsRunning;
        var activeKind = activePane?.Kind.ToString().ToLowerInvariant() ?? "none";
        TerminalStatus.Text = $"{(activeSessionRunning ? "running" : "stopped")}  |  active: {activePane?.Title ?? "none"}  |  {activeKind}";
        TerminalInput.IsEnabled = activePane is not null;
        PaneHost.Children.Clear();
        if (PaneTreeEditor.FindPane(surface.Root, surface.ZoomedPaneId) is { } zoomedPane)
        {
            PaneHost.Children.Add(BuildPaneElement(zoomedPane, zoomedPane.Id == activePane?.Id));
        }
        else
        {
            surface.ZoomedPaneId = null;
            PaneHost.Children.Add(BuildSplitElement(surface.Root, activePane?.Id));
        }

        WorkspaceList.Items.Refresh();
    }

    private void RefreshSurfaceTabs(WorkspaceState workspace)
    {
        NormalizeWorkspace(workspace);
        SurfaceTabs.Children.Clear();
        for (var index = 0; index < workspace.Surfaces.Count; index++)
        {
            var surface = workspace.Surfaces[index];
            var isActive = index == workspace.ActiveSurfaceIndex;
            var button = new Button
            {
                Tag = index,
                Height = 28,
                MinWidth = 78,
                MaxWidth = 150,
                Margin = new Thickness(index == 0 ? 0 : 6, 0, 0, 0),
                Padding = new Thickness(8, 0, 8, 0),
                Foreground = Brush("AgentMuxText"),
                Background = isActive ? Brush("AgentMuxAccent") : Brush("AgentMuxPanel"),
                BorderBrush = isActive ? Brush("AgentMuxAccent") : Brush("AgentMuxBorder"),
                BorderThickness = new Thickness(isActive ? 2 : 1),
                Content = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(surface.Title) ? $"Surface {index + 1}" : surface.Title,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 118
                }
            };
            button.Click += SurfaceTab_Click;
            SurfaceTabs.Children.Add(button);
        }
    }

    private FrameworkElement BuildSplitElement(SplitNodeState node, string? activePaneId)
    {
        if (node.Pane is not null)
        {
            return BuildPaneElement(node.Pane, node.Pane.Id == activePaneId);
        }

        var grid = new Grid();
        if (node.Direction == SplitDirection.Down)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(node.Ratio, GridUnitType.Star), MinHeight = 120 });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(0.1, 1 - node.Ratio), GridUnitType.Star), MinHeight = 120 });

            if (node.First is not null)
            {
                var first = BuildSplitElement(node.First, activePaneId);
                Grid.SetRow(first, 0);
                grid.Children.Add(first);
            }

            var splitter = new GridSplitter
            {
                Height = 6,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = Brush("AgentMuxBorder")
            };
            splitter.DragCompleted += (_, _) =>
            {
                node.Ratio = RatioFromActual(grid.RowDefinitions[0].ActualHeight, grid.RowDefinitions[2].ActualHeight);
                QueueSessionSave();
            };
            Grid.SetRow(splitter, 1);
            grid.Children.Add(splitter);

            if (node.Second is not null)
            {
                var second = BuildSplitElement(node.Second, activePaneId);
                Grid.SetRow(second, 2);
                grid.Children.Add(second);
            }
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(node.Ratio, GridUnitType.Star), MinWidth = 180 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.1, 1 - node.Ratio), GridUnitType.Star), MinWidth = 180 });

            if (node.First is not null)
            {
                var first = BuildSplitElement(node.First, activePaneId);
                Grid.SetColumn(first, 0);
                grid.Children.Add(first);
            }

            var splitter = new GridSplitter
            {
                Width = 6,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = Brush("AgentMuxBorder")
            };
            splitter.DragCompleted += (_, _) =>
            {
                node.Ratio = RatioFromActual(grid.ColumnDefinitions[0].ActualWidth, grid.ColumnDefinitions[2].ActualWidth);
                QueueSessionSave();
            };
            Grid.SetColumn(splitter, 1);
            grid.Children.Add(splitter);

            if (node.Second is not null)
            {
                var second = BuildSplitElement(node.Second, activePaneId);
                Grid.SetColumn(second, 2);
                grid.Children.Add(second);
            }
        }

        return grid;
    }

    private Border BuildPaneElement(PaneState pane, bool isActive)
    {
        var border = new Border
        {
            Background = Brush("AgentMuxPanel"),
            BorderBrush = isActive ? Brush("AgentMuxAccent") : Brush("AgentMuxBorder"),
            BorderThickness = new Thickness(isActive ? 2 : 1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(3),
            Padding = new Thickness(8),
            Tag = pane
        };
        border.PreviewMouseDown += async (_, _) =>
        {
            ActiveSurface().ActivePaneId = pane.Id;
            if (pane.Kind == PaneKind.Terminal)
            {
                await EnsurePanePtyAsync(pane).ConfigureAwait(true);
            }

            RefreshWorkspaceView();
            QueueSessionSave();
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 6) };
        var title = new TextBlock
        {
            Text = pane.HasUnreadNotification ? $"{pane.Title} *" : pane.Title,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("AgentMuxText")
        };
        DockPanel.SetDock(title, Dock.Left);
        header.Children.Add(title);

        var kind = new TextBlock
        {
            Text = pane.Kind.ToString().ToLowerInvariant(),
            Foreground = Brush("AgentMuxMutedText"),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(kind, Dock.Right);
        header.Children.Add(kind);

        layout.Children.Add(header);

        FrameworkElement paneContent = pane.Kind == PaneKind.Browser
            ? GetBrowserView(pane)
            : GetTerminalView(pane);
        Grid.SetRow(paneContent, 1);
        layout.Children.Add(paneContent);

        border.Child = layout;
        return border;
    }

    private TerminalPaneView GetTerminalView(PaneState pane)
    {
        if (!_terminalViews.TryGetValue(pane.Id, out var view))
        {
            view = new TerminalPaneView();
            view.InputReceived += (_, data) =>
            {
                ActiveSurface().ActivePaneId = pane.Id;
                _ = SendTerminalSequenceToPaneAsync(pane, data);
            };
            view.SizeChanged += (_, args) => ApplyTerminalPaneSize(pane.Id, args.NewSize.Width, args.NewSize.Height);
            _terminalViews[pane.Id] = view;
        }

        DetachFromParent(view);
        view.SetScreenText(pane.LastScreenText);
        ApplyTerminalPaneSize(pane.Id, view.ActualWidth, view.ActualHeight);
        return view;
    }

    private bool ApplyTerminalPaneSize(string paneId, double width, double height)
    {
        if (FindPaneById(paneId) is not { Kind: PaneKind.Terminal } pane
            || !TerminalPaneSizeCalculator.TryCalculate(width, height, out var cols, out var rows))
        {
            return false;
        }

        return ResizeTerminalPane(pane, cols, rows);
    }

    private bool ResizeTerminalPane(PaneState pane, int cols, int rows)
    {
        if (pane.Kind != PaneKind.Terminal
            || !TerminalPaneSizeCalculator.TryNormalize(cols, rows, out cols, out rows))
        {
            return false;
        }

        var changed = pane.Cols != cols || pane.Rows != rows;
        pane.Cols = cols;
        pane.Rows = rows;
        ResizeTerminalView(pane);

        if (!changed)
        {
            return false;
        }

        QueueSessionSave();
        if (_ptySessions.TryGetValue(pane.Id, out var session) && session.IsRunning)
        {
            _ = ResizePanePtyAsync(session, cols, rows);
        }

        return true;
    }

    private void ResizeTerminalView(PaneState pane)
    {
        if (_terminalViews.TryGetValue(pane.Id, out var view))
        {
            view.ResizeTerminal(pane.Cols, pane.Rows);
        }
    }

    private static async Task ResizePanePtyAsync(ConPtySession session, int cols, int rows)
    {
        try
        {
            await session.ResizeAsync(cols, rows).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or ObjectDisposedException
            or PlatformNotSupportedException
            or System.ComponentModel.Win32Exception)
        {
        }
    }

    private BrowserPaneView GetBrowserView(PaneState pane)
    {
        if (!_browserViews.TryGetValue(pane.Id, out var view))
        {
            view = new BrowserPaneView();
            view.NavigateRequested += (_, url) =>
            {
                OpenBrowserInPane(pane, url);
                RefreshWorkspaceView();
            };
            _browserViews[pane.Id] = view;
        }

        DetachFromParent(view);
        view.SetUrl(pane.Url);
        return view;
    }

    private void UpdateTerminalView(PaneState pane)
    {
        if (_terminalViews.TryGetValue(pane.Id, out var view))
        {
            view.SetScreenText(pane.LastScreenText);
        }
    }

    private void ApplyTerminalOutput(PaneState pane, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (!_terminalOutputProcessors.TryGetValue(pane.Id, out var processor))
        {
            processor = new TerminalOutputProcessor();
            _terminalOutputProcessors[pane.Id] = processor;
        }

        var chunk = processor.Process(text);
        var metadataChanged = false;
        foreach (var terminalEvent in chunk.Events)
        {
            metadataChanged |= ApplyTerminalEvent(pane, terminalEvent);
        }

        if (!string.IsNullOrEmpty(chunk.Text))
        {
            AppendVisibleTerminalText(pane, chunk.Text);
        }
        else if (metadataChanged)
        {
            QueueSessionSave();
        }

        if (metadataChanged)
        {
            RefreshWorkspaceView();
        }
    }

    private bool ApplyTerminalEvent(PaneState pane, OscEvent terminalEvent)
    {
        switch (terminalEvent.Kind)
        {
            case OscEventKind.Notification:
                AddNotification(
                    string.IsNullOrWhiteSpace(terminalEvent.Title) ? "Terminal" : terminalEvent.Title,
                    terminalEvent.Body ?? "",
                    terminalEvent.Subtitle,
                    pane);
                return true;
            case OscEventKind.Title when !string.IsNullOrWhiteSpace(terminalEvent.Value):
                pane.Title = terminalEvent.Value;
                return true;
            case OscEventKind.WorkingDirectory when !string.IsNullOrWhiteSpace(terminalEvent.Value):
                pane.WorkingDirectory = terminalEvent.Value;
                return true;
            default:
                return false;
        }
    }

    private void AppendVisibleTerminalText(PaneState pane, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        pane.LastScreenText = string.Concat(pane.LastScreenText, text);
        AppendTerminalView(pane, text);
        QueueSessionSave();
    }

    private void AppendTerminalView(PaneState pane, string text)
    {
        if (_terminalViews.TryGetValue(pane.Id, out var view))
        {
            view.AppendScreenText(text);
        }
    }

    private void UpdateBrowserView(PaneState pane)
    {
        if (_browserViews.TryGetValue(pane.Id, out var view))
        {
            view.SetUrl(pane.Url);
        }
    }

    private string OpenBrowserInActivePane(string? url)
    {
        var pane = ActivePane();
        return pane is null ? NormalizeBrowserUrl(url) : OpenBrowserInPane(pane, url);
    }

    private string OpenBrowserInPane(PaneState pane, string? url)
    {
        var normalizedUrl = NormalizeBrowserUrl(url);
        if (pane.Kind == PaneKind.Terminal)
        {
            StopPanePty(pane.Id);
            _terminalViews.Remove(pane.Id);
            _terminalOutputProcessors.Remove(pane.Id);
        }

        pane.Kind = PaneKind.Browser;
        pane.Title = BrowserTitle(normalizedUrl);
        pane.Url = normalizedUrl;
        pane.LastScreenText = null;
        ActiveSurface().ActivePaneId = pane.Id;
        UpdateBrowserView(pane);
        QueueSessionSave();
        return normalizedUrl;
    }

    private async Task<object> RunBrowserScriptAsync(Func<BrowserPaneView, Task<string>> action)
    {
        if (!TryGetActiveBrowserView(out var view, out var reason))
        {
            return new { ok = false, reason };
        }

        try
        {
            var resultJson = await action(view).ConfigureAwait(true);
            var result = ParseScriptJson(resultJson);
            if (result is JsonElement { ValueKind: JsonValueKind.Object } element
                && element.TryGetProperty("ok", out _))
            {
                return element.Clone();
            }

            return new { ok = true, result };
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return new { ok = false, reason = ex.Message };
        }
    }

    private bool TryGetActiveBrowserView(out BrowserPaneView view, out string reason)
    {
        view = null!;
        var pane = ActivePane();
        if (pane?.Kind != PaneKind.Browser)
        {
            reason = "active pane is not a browser";
            return false;
        }

        if (!_browserViews.TryGetValue(pane.Id, out view!))
        {
            reason = "browser view is not loaded";
            return false;
        }

        reason = "";
        return true;
    }

    private static object ParseScriptJson(string resultJson)
    {
        try
        {
            using var document = JsonDocument.Parse(resultJson);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return resultJson;
        }
    }

    private static bool IsBrowserAutomationMethod(string method)
    {
        return method is AgentMuxMethods.BrowserEval
            or AgentMuxMethods.BrowserClick
            or AgentMuxMethods.BrowserFill
            or AgentMuxMethods.BrowserType
            or AgentMuxMethods.BrowserPress
            or AgentMuxMethods.BrowserScreenshot
            or AgentMuxMethods.BrowserFrameTree
            or AgentMuxMethods.BrowserWaitForSelector
            or AgentMuxMethods.BrowserConsoleLog
            or AgentMuxMethods.BrowserConsoleClear
            or AgentMuxMethods.BrowserNetworkLog
            or AgentMuxMethods.BrowserNetworkClear
            or AgentMuxMethods.BrowserResponseBody
            or AgentMuxMethods.BrowserHarMetadata
            or AgentMuxMethods.BrowserDownloads
            or AgentMuxMethods.BrowserDownloadsClear;
    }

    private void StopPanePty(string paneId)
    {
        _ptyStartFailedPaneIds.Remove(paneId);
        if (_ptySessions.Remove(paneId, out var session))
        {
            _ = session.DisposeAsync().AsTask();
        }
    }

    private void CleanupPaneResources(PaneState pane)
    {
        StopPanePty(pane.Id);
        _terminalViews.Remove(pane.Id);
        _terminalOutputProcessors.Remove(pane.Id);
        if (_browserViews.Remove(pane.Id, out var browserView))
        {
            browserView.Dispose();
        }
    }

    private static void DetachFromParent(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, element):
                contentControl.Content = null;
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
        }
    }

    private static int CountPanes(SplitNodeState node)
    {
        if (node.Pane is not null)
        {
            return 1;
        }

        return (node.First is null ? 0 : CountPanes(node.First))
            + (node.Second is null ? 0 : CountPanes(node.Second));
    }

    private static int CountPaneKind(SplitNodeState node, PaneKind kind)
    {
        if (node.Pane is not null)
        {
            return node.Pane.Kind == kind ? 1 : 0;
        }

        return (node.First is null ? 0 : CountPaneKind(node.First, kind))
            + (node.Second is null ? 0 : CountPaneKind(node.Second, kind));
    }

    private static object BuildSurfaceDto(WorkspaceState workspace, SurfaceState surface, int index)
    {
        NormalizeSurface(surface);
        return new
        {
            surface.Id,
            surface.Title,
            index,
            isActive = index == workspace.ActiveSurfaceIndex,
            surface.ActivePaneId,
            paneCount = CountPanes(surface.Root),
            browserPaneCount = CountPaneKind(surface.Root, PaneKind.Browser),
            surface.ZoomedPaneId
        };
    }

    private static string NormalizeBrowserUrl(string? url)
    {
        var trimmed = string.IsNullOrWhiteSpace(url) ? "about:blank" : url.Trim();
        if (TryNormalizeAbsoluteBrowserUrl(trimmed, out var absoluteUrl))
        {
            return absoluteUrl;
        }

        if (trimmed.Contains("://", StringComparison.Ordinal)
            || trimmed.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return "about:blank";
        }

        if (trimmed.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("127.", StringComparison.Ordinal)
            || trimmed.StartsWith("[::1]", StringComparison.Ordinal))
        {
            return TryNormalizeAbsoluteBrowserUrl($"http://{trimmed}", out absoluteUrl)
                ? absoluteUrl
                : "about:blank";
        }

        return TryNormalizeAbsoluteBrowserUrl($"https://{trimmed}", out absoluteUrl)
            ? absoluteUrl
            : "about:blank";
    }

    private static bool TryNormalizeAbsoluteBrowserUrl(string value, out string normalizedUrl)
    {
        normalizedUrl = "";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https" or "about" or "file"))
        {
            return false;
        }

        normalizedUrl = uri.AbsoluteUri;
        return true;
    }

    private static string BrowserTitle(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host;
        }

        return "Browser";
    }

    private static double RatioFromActual(double first, double second)
    {
        var total = first + second;
        return total <= 0 ? 0.5 : Math.Clamp(first / total, 0.1, 0.9);
    }

    private static bool TryMapArrowKey(Key key, out PaneFocusDirection direction)
    {
        direction = key switch
        {
            Key.Left => PaneFocusDirection.Left,
            Key.Right => PaneFocusDirection.Right,
            Key.Up => PaneFocusDirection.Up,
            Key.Down => PaneFocusDirection.Down,
            _ => PaneFocusDirection.Next
        };

        return key is Key.Left or Key.Right or Key.Up or Key.Down;
    }

    private static Key EffectiveKey(KeyEventArgs e)
    {
        return EffectiveKey(e.Key, e.SystemKey);
    }

    private static Key EffectiveKey(Key key, Key systemKey) => key == Key.System ? systemKey : key;

    internal void InitializeForSmokeTest()
    {
        if (_restoreSessionOnStartup)
        {
            throw new InvalidOperationException("Use InitializeForSmokeTestAsync when session restore is enabled.");
        }

        InitializeWorkspaceListForCurrentSession();
    }

    internal async Task InitializeForSmokeTestAsync()
    {
        if (_restoreSessionOnStartup)
        {
            await LoadSessionSnapshotAsync().ConfigureAwait(true);
        }

        InitializeWorkspaceListForCurrentSession();
    }

    internal Task SaveSessionForSmokeTestAsync()
    {
        return SaveSessionSnapshotAsync();
    }

    internal async Task<AgentMuxResponse> HandleRpcForSmokeTestAsync(string method, object? parameters = null)
    {
        var request = new AgentMuxRequest
        {
            Id = "smoke",
            Method = method,
            Params = parameters is null
                ? null
                : JsonSerializer.SerializeToElement(parameters, AgentMuxJson.Options)
        };

        return await HandleRpcOnUiAsync(request, CancellationToken.None).ConfigureAwait(true);
    }

    internal int PaneCountForSmokeTest => CountPanes(ActiveSurface().Root);

    internal int WorkspaceCountForSmokeTest => _workspaces.Count;

    internal string ActiveWorkspaceTitleForSmokeTest => ActiveWorkspace().Title;

    internal int SurfaceCountForSmokeTest => ActiveWorkspace().Surfaces.Count;

    internal int ActiveSurfaceIndexForSmokeTest => ActiveWorkspace().ActiveSurfaceIndex;

    internal string ActiveSurfaceTitleForSmokeTest => ActiveSurface().Title;

    internal int RenderedSurfaceTabCountForSmokeTest => CountVisualDescendants<Button>(SurfaceTabs);

    internal int RenderedTerminalPaneCountForSmokeTest => CountVisualDescendants<TerminalPaneView>(PaneHost);

    internal int RenderedBrowserPaneCountForSmokeTest => CountVisualDescendants<BrowserPaneView>(PaneHost);

    internal int CachedTerminalPaneViewCountForSmokeTest => _terminalViews.Count;

    internal int CachedBrowserPaneViewCountForSmokeTest => _browserViews.Count;

    internal string? ActivePaneIdForSmokeTest => ActivePane()?.Id;

    internal PaneKind? ActivePaneKindForSmokeTest => ActivePane()?.Kind;

    internal string? ActivePaneUrlForSmokeTest => ActivePane()?.Url;

    internal int? ActivePaneColsForSmokeTest => ActivePane()?.Cols;

    internal int? ActivePaneRowsForSmokeTest => ActivePane()?.Rows;

    internal string? ActivePaneLastScreenTextForSmokeTest => ActivePane()?.LastScreenText;

    internal bool ActivePaneHasUnreadNotificationForSmokeTest => ActivePane()?.HasUnreadNotification ?? false;

    internal int ActiveWorkspaceUnreadCountForSmokeTest => ActiveWorkspace().UnreadCount;

    internal bool IsActivePaneZoomedForSmokeTest => ActiveSurface().ZoomedPaneId == ActivePane()?.Id;

    internal bool HasButtonForSmokeTest(string content) => VisualTreeContainsButton(this, content);

    internal bool RenderedTextContainsForSmokeTest(string marker) => VisualTreeTextContains(PaneHost, marker);

    internal bool SurfaceTabsContainTextForSmokeTest(string marker) => VisualTreeTextContains(SurfaceTabs, marker);

    internal bool SplitActivePaneForSmokeTest(SplitDirection direction)
    {
        var changed = SplitActivePane(direction);
        RefreshWorkspaceView();
        return changed;
    }

    internal bool HandlePreviewKeyDownForSmokeTest(Key key, ModifierKeys modifiers, Key systemKey = Key.None)
    {
        return HandlePreviewKeyDown(key, systemKey, modifiers);
    }

    internal void SetActivePaneTextForSmokeTest(string text)
    {
        if (ActivePane() is { } pane)
        {
            _terminalOutputProcessors.Remove(pane.Id);
            pane.LastScreenText = text;
        }

        RefreshWorkspaceView();
    }

    internal void AppendActivePaneTextForSmokeTest(string text)
    {
        if (ActivePane() is not { } pane)
        {
            return;
        }

        ApplyTerminalOutput(pane, text);
    }

    internal bool ResizeActiveTerminalPaneForSmokeTest(double width, double height)
    {
        return ActivePane() is { Kind: PaneKind.Terminal } pane
            && ApplyTerminalPaneSize(pane.Id, width, height);
    }

    internal string OpenBrowserInActivePaneForSmokeTest(string url)
    {
        var normalizedUrl = OpenBrowserInActivePane(url);
        RefreshWorkspaceView();
        return normalizedUrl;
    }

    private static int CountVisualDescendants<T>(DependencyObject root)
    {
        var count = 0;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T)
            {
                count++;
            }

            count += CountVisualDescendants<T>(child);
        }

        return count;
    }

    private static bool VisualTreeContainsButton(DependencyObject root, string content)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button button && string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal))
            {
                return true;
            }

            if (VisualTreeContainsButton(child, content))
            {
                return true;
            }
        }

        return false;
    }

    private static bool VisualTreeTextContains(DependencyObject root, string marker)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is TextBox textBox && textBox.Text.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }

            if (child is TextBlock textBlock && textBlock.Text.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }

            if (VisualTreeTextContains(child, marker))
            {
                return true;
            }
        }

        return false;
    }

    private Brush Brush(string resourceKey) => (Brush)FindResource(resourceKey);

    private static T? Deserialize<T>(JsonElement? parameters)
    {
        return parameters is { } element
            ? element.Deserialize<T>(AgentMuxJson.Options)
            : default;
    }

    private static bool TryReadPositiveIntParam(JsonElement? parameters, string name, out int value)
    {
        value = 0;
        if (parameters is not { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty(name, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out value) && value > 0,
            JsonValueKind.String => int.TryParse(property.GetString(), out value) && value > 0,
            _ => false
        };
    }

    private static bool TryReadSurfaceTarget(JsonElement? parameters, out int? index, out string? id, out string error)
    {
        index = null;
        id = null;
        error = "";
        if (parameters is not { ValueKind: JsonValueKind.Object } element)
        {
            error = "index or id is required";
            return false;
        }

        var parsedIndex = 0;
        var hasIndex = element.TryGetProperty("index", out var indexProperty);
        if (hasIndex && !TryReadNonNegativeInt(indexProperty, out parsedIndex))
        {
            error = "index must be a non-negative integer";
            return false;
        }

        var hasId = element.TryGetProperty("id", out var idProperty)
            && TryReadNonEmptyString(idProperty, out id);
        if (hasIndex && hasId)
        {
            error = "provide index or id, not both";
            return false;
        }

        if (!hasIndex && !hasId)
        {
            error = "index or id is required";
            return false;
        }

        index = hasIndex ? parsedIndex : null;
        return true;
    }

    private static bool TryReadNonNegativeInt(JsonElement property, out int value)
    {
        value = 0;
        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out value) && value >= 0,
            JsonValueKind.String => int.TryParse(property.GetString(), out value) && value >= 0,
            _ => false
        };
    }

    private static bool TryReadNonEmptyString(JsonElement property, out string? value)
    {
        value = property.ValueKind == JsonValueKind.String ? property.GetString() : null;
        return !string.IsNullOrWhiteSpace(value) && !value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NotifyParams
    {
        public string? Title { get; set; }
        public string? Subtitle { get; set; }
        public string? Body { get; set; }
    }

    private sealed class NotificationListParams
    {
        public int? Limit { get; set; }
    }

    private readonly record struct PaneTarget(WorkspaceState Workspace, SurfaceState Surface, PaneState Pane);

    private sealed class WorkspaceCreateParams
    {
        public string? Title { get; set; }
        public string? Cwd { get; set; }
    }

    private sealed class WorkspaceSelectParams
    {
        public int? Index { get; set; }
    }

    private sealed class SurfaceCreateParams
    {
        public string? Title { get; set; }
    }

    private sealed class SplitParams
    {
        public string? Direction { get; set; }
    }

    private sealed class SendTextParams
    {
        public string? Text { get; set; }
    }

    private sealed class OpenUrlParams
    {
        public string? Url { get; set; }
    }

    private sealed class FocusPaneParams
    {
        public string? Direction { get; set; }
    }

    private sealed class BrowserEvalParams
    {
        public string? Script { get; set; }
    }

    private sealed class BrowserSelectorParams
    {
        public string? Selector { get; set; }
        public string? Frame { get; set; }
    }

    private sealed class BrowserFillParams
    {
        public string? Selector { get; set; }
        public string? Text { get; set; }
        public string? Frame { get; set; }
    }

    private sealed class BrowserPressParams
    {
        public string? Key { get; set; }
        public string? Selector { get; set; }
        public string? Frame { get; set; }
    }

    private sealed class BrowserScreenshotParams
    {
        public string? Path { get; set; }
    }

    private sealed class BrowserWaitForSelectorParams
    {
        public string? Selector { get; set; }
        public string? State { get; set; }
        public int? TimeoutMs { get; set; }
        public string? Frame { get; set; }
    }

    private sealed class BrowserConsoleLogParams
    {
        public int? Limit { get; set; }
    }

    private sealed class BrowserNetworkLogParams
    {
        public int? Limit { get; set; }
    }

    private sealed class BrowserResponseBodyParams
    {
        public string? RequestId { get; set; }
    }

    private sealed class BrowserHarMetadataParams
    {
        public string? Path { get; set; }
    }

    private sealed class BrowserDownloadLogParams
    {
        public int? Limit { get; set; }
    }
}
