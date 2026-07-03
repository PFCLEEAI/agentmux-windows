using System.Collections.ObjectModel;
using System.Globalization;
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
using AgentMux.Core.Terminals;
using AgentMux.Core.Workspaces;
using AgentMux.Win.App.Controls;
using AgentMux.Win.App.Input;
using AgentMux.Win.App.Notifications;
using AgentMux.Win.Pty;

namespace AgentMux.Win.App.Views;

public partial class MainWindow : Window
{
    private const int MaxNotifications = 200;
    private const int MaxWorkspaceLogs = 200;
    private const int MaxWorkspaceStatuses = 100;
    private const int MaxWorkspaceLogMessageLength = 1000;
    private const int MaxWorkspaceLogSourceLength = 80;
    private const int MaxWorkspaceStatusKeyLength = 80;
    private const int MaxWorkspaceStatusTextLength = 200;
    private const int MaxWorkspaceStatusMetaLength = 80;
    private const int MaxWorkspaceProgressLabelLength = 120;

    private readonly ObservableCollection<WorkspaceState> _workspaces = [];
    private readonly List<TerminalNotification> _notifications = [];
    private readonly List<WorkspaceLogEntry> _workspaceLogs = [];
    private readonly List<WorkspaceStatusEntry> _workspaceStatuses = [];
    private readonly Dictionary<string, WorkspaceProgressEntry> _workspaceProgress = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ConPtySession> _ptySessions = [];
    private readonly Dictionary<string, TerminalPaneView> _terminalViews = [];
    private readonly Dictionary<string, TerminalOutputProcessor> _terminalOutputProcessors = [];
    private readonly Dictionary<string, BrowserPaneView> _browserViews = [];
    private readonly HashSet<string> _ptyStartFailedPaneIds = [];
    private readonly SemaphoreSlim _browserSelectorWaitGate = new(1, 1);
    private readonly SemaphoreSlim _browserLoadWaitGate = new(1, 1);
    private readonly ShortcutSettings _shortcutSettings;
    private readonly SessionSnapshotStore? _sessionStore;
    private readonly INativeToastService _nativeToastService;
    private readonly bool _restoreSessionOnStartup;
    private readonly bool _persistSession;
    private NamedPipeRpcServer? _server;
    private CancellationTokenSource? _sessionSaveDebounce;
    private int _activeWorkspaceIndex;
    private bool _notificationPanelOpen;
    private bool _shortcutPanelOpen;

    public MainWindow()
        : this(
            ShortcutSettings.Load(),
            new SessionSnapshotStore(),
            restoreSessionOnStartup: true,
            persistSession: true,
            nativeToastService: new WindowsNativeToastService())
    {
    }

    internal MainWindow(ShortcutSettings shortcutSettings)
        : this(
            shortcutSettings,
            sessionStore: null,
            restoreSessionOnStartup: false,
            persistSession: false,
            nativeToastService: NullNativeToastService.Instance)
    {
    }

    internal MainWindow(
        ShortcutSettings shortcutSettings,
        SessionSnapshotStore? sessionStore,
        bool restoreSessionOnStartup,
        bool persistSession,
        INativeToastService? nativeToastService = null)
    {
        _shortcutSettings = shortcutSettings;
        _sessionStore = sessionStore;
        _nativeToastService = nativeToastService ?? NullNativeToastService.Instance;
        _restoreSessionOnStartup = restoreSessionOnStartup;
        _persistSession = persistSession;
        InitializeComponent();
        RefreshShortcutReference();
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
        RefreshWorkspaceView();
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

    private void NotificationToggle_Click(object sender, RoutedEventArgs e)
    {
        _notificationPanelOpen = !_notificationPanelOpen;
        RefreshNotificationCenter();
    }

    private void NotificationClose_Click(object sender, RoutedEventArgs e)
    {
        _notificationPanelOpen = false;
        RefreshNotificationCenter();
    }

    private async void NotificationJumpLatest_Click(object sender, RoutedEventArgs e)
    {
        HandleNotificationsJumpLatest();
        await EnsurePanePtyAsync(ActivePane()).ConfigureAwait(true);
        RefreshWorkspaceView();
    }

    private void NotificationClear_Click(object sender, RoutedEventArgs e)
    {
        HandleNotificationsClear();
        RefreshWorkspaceView();
    }

    private void ShortcutToggle_Click(object sender, RoutedEventArgs e)
    {
        _shortcutPanelOpen = !_shortcutPanelOpen;
        RefreshShortcutReference();
    }

    private void ShortcutClose_Click(object sender, RoutedEventArgs e)
    {
        _shortcutPanelOpen = false;
        RefreshShortcutReference();
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
            RefreshWorkspaceGitMetadata(ActiveWorkspace());
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
        RefreshAllWorkspaceGitMetadata();
        WorkspaceList.ItemsSource = _workspaces;
        WorkspaceList.SelectedIndex = _activeWorkspaceIndex;
        RefreshWorkspaceView();
    }

    private static void NormalizeWorkspace(WorkspaceState workspace)
    {
        workspace.PullRequest = NormalizePullRequest(workspace.PullRequest);
        workspace.Ports = NormalizePorts(workspace.Ports);
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

    private void RefreshAllWorkspaceGitMetadata()
    {
        foreach (var workspace in _workspaces)
        {
            RefreshWorkspaceGitMetadata(workspace);
        }
    }

    private static void RefreshWorkspaceGitMetadata(WorkspaceState workspace)
    {
        workspace.GitBranch = GitBranchDetector.DetectCurrentBranch(workspace.WorkingDirectory);
        workspace.IsGitDirty = false;
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
        clone.LatestLog = null;
        clone.LatestStatus = null;
        clone.LatestProgress = null;
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
            AgentMuxMethods.WorkspaceList => AgentMuxResponse.Success(request.Id, BuildWorkspaceList()),
            AgentMuxMethods.WorkspaceCreate => AgentMuxResponse.Success(request.Id, HandleWorkspaceCreate(request.Params)),
            AgentMuxMethods.WorkspaceSelect => AgentMuxResponse.Success(request.Id, HandleWorkspaceSelect(request.Params)),
            AgentMuxMethods.WorkspaceSetPorts => AgentMuxResponse.Success(request.Id, HandleWorkspaceSetPorts(request.Params)),
            AgentMuxMethods.WorkspaceSetPullRequest => AgentMuxResponse.Success(request.Id, HandleWorkspaceSetPullRequest(request.Params)),
            AgentMuxMethods.WorkspaceLog => AgentMuxResponse.Success(request.Id, HandleWorkspaceLog(request.Params)),
            AgentMuxMethods.WorkspaceListLog => AgentMuxResponse.Success(request.Id, HandleWorkspaceListLog(request.Params)),
            AgentMuxMethods.WorkspaceClearLog => AgentMuxResponse.Success(request.Id, HandleWorkspaceClearLog(request.Params)),
            AgentMuxMethods.WorkspaceSetStatus => AgentMuxResponse.Success(request.Id, HandleWorkspaceSetStatus(request.Params)),
            AgentMuxMethods.WorkspaceListStatus => AgentMuxResponse.Success(request.Id, HandleWorkspaceListStatus(request.Params)),
            AgentMuxMethods.WorkspaceClearStatus => AgentMuxResponse.Success(request.Id, HandleWorkspaceClearStatus(request.Params)),
            AgentMuxMethods.WorkspaceSetProgress => AgentMuxResponse.Success(request.Id, HandleWorkspaceSetProgress(request.Params)),
            AgentMuxMethods.WorkspaceClearProgress => AgentMuxResponse.Success(request.Id, HandleWorkspaceClearProgress(request.Params)),
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
            AgentMuxMethods.ReadScreen => AgentMuxResponse.Success(request.Id, HandleReadScreen(request.Params)),
            AgentMuxMethods.FocusPane => HandleFocusPane(request.Id, request.Params),
            AgentMuxMethods.ToggleZoom => AgentMuxResponse.Success(request.Id, HandleToggleZoom()),
            AgentMuxMethods.ClosePane => AgentMuxResponse.Success(request.Id, HandleClosePane()),
            AgentMuxMethods.OpenUrl => AgentMuxResponse.Success(request.Id, HandleOpenUrl(request.Params)),
            AgentMuxMethods.BrowserBack => AgentMuxResponse.Success(request.Id, await HandleBrowserBackAsync().ConfigureAwait(true)),
            AgentMuxMethods.BrowserForward => AgentMuxResponse.Success(request.Id, await HandleBrowserForwardAsync().ConfigureAwait(true)),
            AgentMuxMethods.BrowserReload => AgentMuxResponse.Success(request.Id, await HandleBrowserReloadAsync().ConfigureAwait(true)),
            AgentMuxMethods.BrowserGetUrl => AgentMuxResponse.Success(request.Id, await HandleBrowserGetUrlAsync().ConfigureAwait(true)),
            AgentMuxMethods.BrowserGetText => AgentMuxResponse.Success(request.Id, await HandleBrowserGetAsync(request.Params, "text").ConfigureAwait(true)),
            AgentMuxMethods.BrowserGetHtml => AgentMuxResponse.Success(request.Id, await HandleBrowserGetAsync(request.Params, "html").ConfigureAwait(true)),
            AgentMuxMethods.BrowserGetValue => AgentMuxResponse.Success(request.Id, await HandleBrowserGetAsync(request.Params, "value").ConfigureAwait(true)),
            AgentMuxMethods.BrowserGetAttribute => AgentMuxResponse.Success(request.Id, await HandleBrowserGetAsync(request.Params, "attr").ConfigureAwait(true)),
            AgentMuxMethods.BrowserGetCount => AgentMuxResponse.Success(request.Id, await HandleBrowserGetAsync(request.Params, "count").ConfigureAwait(true)),
            AgentMuxMethods.BrowserGetBox => AgentMuxResponse.Success(request.Id, await HandleBrowserGetAsync(request.Params, "box").ConfigureAwait(true)),
            AgentMuxMethods.BrowserGetStyle => AgentMuxResponse.Success(request.Id, await HandleBrowserGetAsync(request.Params, "styles").ConfigureAwait(true)),
            AgentMuxMethods.BrowserGetTitle => AgentMuxResponse.Success(request.Id, await HandleBrowserGetTitleAsync().ConfigureAwait(true)),
            AgentMuxMethods.BrowserEval => AgentMuxResponse.Success(request.Id, await HandleBrowserEvalAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserText => AgentMuxResponse.Success(request.Id, await HandleBrowserTextAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserClick => AgentMuxResponse.Success(request.Id, await HandleBrowserClickAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserHover => AgentMuxResponse.Success(request.Id, await HandleBrowserHoverAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserFocus => AgentMuxResponse.Success(request.Id, await HandleBrowserFocusAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserIsVisible => AgentMuxResponse.Success(request.Id, await HandleBrowserIsAsync(request.Params, "visible").ConfigureAwait(true)),
            AgentMuxMethods.BrowserIsEnabled => AgentMuxResponse.Success(request.Id, await HandleBrowserIsAsync(request.Params, "enabled").ConfigureAwait(true)),
            AgentMuxMethods.BrowserIsChecked => AgentMuxResponse.Success(request.Id, await HandleBrowserIsAsync(request.Params, "checked").ConfigureAwait(true)),
            AgentMuxMethods.BrowserFindRole => AgentMuxResponse.Success(request.Id, await HandleBrowserFindAsync(request.Params, "role").ConfigureAwait(true)),
            AgentMuxMethods.BrowserFindText => AgentMuxResponse.Success(request.Id, await HandleBrowserFindAsync(request.Params, "text").ConfigureAwait(true)),
            AgentMuxMethods.BrowserFindLabel => AgentMuxResponse.Success(request.Id, await HandleBrowserFindAsync(request.Params, "label").ConfigureAwait(true)),
            AgentMuxMethods.BrowserFindPlaceholder => AgentMuxResponse.Success(request.Id, await HandleBrowserFindAsync(request.Params, "placeholder").ConfigureAwait(true)),
            AgentMuxMethods.BrowserFindTestId => AgentMuxResponse.Success(request.Id, await HandleBrowserFindAsync(request.Params, "testid").ConfigureAwait(true)),
            AgentMuxMethods.BrowserFindFirst => AgentMuxResponse.Success(request.Id, await HandleBrowserFindAsync(request.Params, "first").ConfigureAwait(true)),
            AgentMuxMethods.BrowserFindLast => AgentMuxResponse.Success(request.Id, await HandleBrowserFindAsync(request.Params, "last").ConfigureAwait(true)),
            AgentMuxMethods.BrowserFindNth => AgentMuxResponse.Success(request.Id, await HandleBrowserFindAsync(request.Params, "nth").ConfigureAwait(true)),
            AgentMuxMethods.BrowserSnapshot => AgentMuxResponse.Success(request.Id, await HandleBrowserSnapshotAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserSelect => AgentMuxResponse.Success(request.Id, await HandleBrowserSelectAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserFill => AgentMuxResponse.Success(request.Id, await HandleBrowserFillAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserType => AgentMuxResponse.Success(request.Id, await HandleBrowserTypeAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserPress => AgentMuxResponse.Success(request.Id, await HandleBrowserPressAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserScreenshot => AgentMuxResponse.Success(request.Id, await HandleBrowserScreenshotAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserFrameTree => AgentMuxResponse.Success(request.Id, await HandleBrowserFrameTreeAsync().ConfigureAwait(true)),
            AgentMuxMethods.BrowserWaitForSelector => AgentMuxResponse.Success(request.Id, await HandleBrowserWaitForSelectorAsync(request.Params, cancellationToken).ConfigureAwait(true)),
            AgentMuxMethods.BrowserWaitForLoad => AgentMuxResponse.Success(request.Id, await HandleBrowserWaitForLoadAsync(request.Params, cancellationToken).ConfigureAwait(true)),
            AgentMuxMethods.BrowserConsoleLog => AgentMuxResponse.Success(request.Id, await HandleBrowserConsoleLogAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserConsoleClear => AgentMuxResponse.Success(request.Id, await HandleBrowserConsoleClearAsync().ConfigureAwait(true)),
            AgentMuxMethods.BrowserNetworkLog => AgentMuxResponse.Success(request.Id, await HandleBrowserNetworkLogAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserNetworkClear => AgentMuxResponse.Success(request.Id, await HandleBrowserNetworkClearAsync().ConfigureAwait(true)),
            AgentMuxMethods.BrowserResponseBody => AgentMuxResponse.Success(request.Id, await HandleBrowserResponseBodyAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserHarMetadata => AgentMuxResponse.Success(request.Id, await HandleBrowserHarMetadataAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserDownloads => AgentMuxResponse.Success(request.Id, await HandleBrowserDownloadsAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserDownloadsClear => AgentMuxResponse.Success(request.Id, await HandleBrowserDownloadsClearAsync().ConfigureAwait(true)),
            AgentMuxMethods.BrowserRouteList => AgentMuxResponse.Success(request.Id, await HandleBrowserRouteListAsync().ConfigureAwait(true)),
            AgentMuxMethods.BrowserRouteBlock => AgentMuxResponse.Success(request.Id, await HandleBrowserRouteBlockAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserRouteFulfill => AgentMuxResponse.Success(request.Id, await HandleBrowserRouteFulfillAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserRouteClear => AgentMuxResponse.Success(request.Id, await HandleBrowserRouteClearAsync().ConfigureAwait(true)),
            AgentMuxMethods.BrowserTrace => AgentMuxResponse.Success(request.Id, await HandleBrowserTraceAsync(request.Params).ConfigureAwait(true)),
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
            workspaceLogCount = _workspaceLogs.Count,
            workspaceStatusCount = _workspaceStatuses.Count,
            workspaceProgressCount = _workspaceProgress.Count,
            terminalSessionCount = _ptySessions.Count,
            browserPaneCount = CountPaneKind(surface.Root, PaneKind.Browser)
        };
    }

    private object BuildWorkspaceList()
    {
        EnsureDefaultWorkspace();
        _activeWorkspaceIndex = Math.Clamp(_activeWorkspaceIndex, 0, _workspaces.Count - 1);
        RefreshAllWorkspaceGitMetadata();
        return new
        {
            activeWorkspaceIndex = _activeWorkspaceIndex,
            workspaces = _workspaces
                .Select((workspace, index) => BuildWorkspaceDto(workspace, index))
                .ToList()
        };
    }

    private object HandleWorkspaceCreate(JsonElement? parameters)
    {
        var parsed = Deserialize<WorkspaceCreateParams>(parameters);
        var workspace = CreateWorkspace(parsed?.Title, parsed?.Cwd);
        var index = _workspaces.IndexOf(workspace);
        return new
        {
            created = true,
            activeWorkspaceIndex = _activeWorkspaceIndex,
            workspace = BuildWorkspaceDto(workspace, index)
        };
    }

    private object HandleWorkspaceSelect(JsonElement? parameters)
    {
        if (!TryReadWorkspaceTarget(parameters, out var index, out var id, out var error))
        {
            return new { selected = false, reason = error };
        }

        if (!SelectWorkspace(index, id))
        {
            return new
            {
                selected = false,
                reason = index.HasValue ? "index out of range" : "workspace not found"
            };
        }

        _ = EnsurePanePtyAsync(ActivePane());
        var workspace = ActiveWorkspace();
        return new
        {
            selected = true,
            workspace = BuildWorkspaceDto(workspace, _activeWorkspaceIndex)
        };
    }

    private object HandleWorkspaceSetPorts(JsonElement? parameters)
    {
        if (!TryReadOptionalWorkspaceTarget(parameters, out var index, out var id, out var targetError))
        {
            return new { updated = false, reason = targetError };
        }

        if (!TryReadWorkspacePorts(parameters, out var ports, out var portError))
        {
            return new { updated = false, reason = portError };
        }

        var selectedIndex = ResolveWorkspaceIndex(index, id);
        if (selectedIndex < 0 || selectedIndex >= _workspaces.Count)
        {
            return new
            {
                updated = false,
                reason = index.HasValue ? "index out of range" : "workspace not found"
            };
        }

        var workspace = _workspaces[selectedIndex];
        workspace.Ports = ports;
        QueueSessionSave();
        return new
        {
            updated = true,
            workspace = BuildWorkspaceDto(workspace, selectedIndex)
        };
    }

    private object HandleWorkspaceSetPullRequest(JsonElement? parameters)
    {
        if (!TryReadOptionalWorkspaceTarget(parameters, out var index, out var id, out var targetError))
        {
            return new { updated = false, reason = targetError };
        }

        if (!TryReadWorkspacePullRequest(parameters, out var pullRequest, out var pullRequestError))
        {
            return new { updated = false, reason = pullRequestError };
        }

        var selectedIndex = ResolveWorkspaceIndex(index, id);
        if (selectedIndex < 0 || selectedIndex >= _workspaces.Count)
        {
            return new
            {
                updated = false,
                reason = index.HasValue ? "index out of range" : "workspace not found"
            };
        }

        var workspace = _workspaces[selectedIndex];
        workspace.PullRequest = pullRequest;
        QueueSessionSave();
        return new
        {
            updated = true,
            workspace = BuildWorkspaceDto(workspace, selectedIndex)
        };
    }

    private object HandleWorkspaceLog(JsonElement? parameters)
    {
        if (!TryReadOptionalWorkspaceTarget(parameters, out var index, out var id, out var targetError))
        {
            return new { logged = false, reason = targetError };
        }

        if (!TryReadWorkspaceLogMessage(parameters, out var message, out var messageError))
        {
            return new { logged = false, reason = messageError };
        }

        if (!TryReadWorkspaceLogLevel(parameters, out var level, out var levelError))
        {
            return new { logged = false, reason = levelError };
        }

        if (!TryReadWorkspaceLogSource(parameters, out var source, out var sourceError))
        {
            return new { logged = false, reason = sourceError };
        }

        var selectedIndex = ResolveWorkspaceIndex(index, id);
        if (selectedIndex < 0 || selectedIndex >= _workspaces.Count)
        {
            return new
            {
                logged = false,
                reason = index.HasValue ? "index out of range" : "workspace not found"
            };
        }

        var workspace = _workspaces[selectedIndex];
        var entry = new WorkspaceLogEntry
        {
            WorkspaceId = workspace.Id,
            WorkspaceTitle = workspace.Title,
            Level = level,
            Source = source,
            Message = message
        };

        _workspaceLogs.Add(entry);
        TrimWorkspaceLogs();
        RecalculateWorkspaceLogState();
        return new
        {
            logged = true,
            log = BuildWorkspaceLogDto(entry),
            workspace = BuildWorkspaceDto(workspace, selectedIndex)
        };
    }

    private object HandleWorkspaceListLog(JsonElement? parameters)
    {
        if (!TryReadOptionalWorkspaceTarget(parameters, out var index, out var id, out var targetError))
        {
            return new { listed = false, reason = targetError };
        }

        if (!TryReadWorkspaceLogLimit(parameters, out var limit, out var limitError))
        {
            return new { listed = false, reason = limitError };
        }

        var selectedIndex = ResolveWorkspaceIndex(index, id);
        if (selectedIndex < 0 || selectedIndex >= _workspaces.Count)
        {
            return new
            {
                listed = false,
                reason = index.HasValue ? "index out of range" : "workspace not found"
            };
        }

        var workspace = _workspaces[selectedIndex];
        var logs = _workspaceLogs
            .Where(log => string.Equals(log.WorkspaceId, workspace.Id, StringComparison.Ordinal))
            .Reverse()
            .ToList();

        return new
        {
            listed = true,
            workspaceId = workspace.Id,
            count = logs.Count,
            logs = logs.Take(limit).Select(BuildWorkspaceLogDto).ToList()
        };
    }

    private object HandleWorkspaceClearLog(JsonElement? parameters)
    {
        if (!TryReadOptionalWorkspaceTarget(parameters, out var index, out var id, out var targetError))
        {
            return new { cleared = 0, reason = targetError };
        }

        var selectedIndex = ResolveWorkspaceIndex(index, id);
        if (selectedIndex < 0 || selectedIndex >= _workspaces.Count)
        {
            return new
            {
                cleared = 0,
                reason = index.HasValue ? "index out of range" : "workspace not found"
            };
        }

        var workspace = _workspaces[selectedIndex];
        var cleared = _workspaceLogs.RemoveAll(log => string.Equals(log.WorkspaceId, workspace.Id, StringComparison.Ordinal));
        RecalculateWorkspaceLogState();
        return new
        {
            cleared,
            workspace = BuildWorkspaceDto(workspace, selectedIndex)
        };
    }

    private object HandleWorkspaceSetStatus(JsonElement? parameters)
    {
        if (!TryReadOptionalWorkspaceTarget(parameters, out var index, out var id, out var targetError))
        {
            return new { updated = false, reason = targetError };
        }

        if (!TryReadWorkspaceStatusKey(parameters, out var key, out var keyError))
        {
            return new { updated = false, reason = keyError };
        }

        if (!TryReadWorkspaceStatusText(parameters, out var text, out var textError))
        {
            return new { updated = false, reason = textError };
        }

        if (!TryReadWorkspaceStatusOptionalText(parameters, "icon", MaxWorkspaceStatusMetaLength, out var icon, out var iconError))
        {
            return new { updated = false, reason = iconError };
        }

        if (!TryReadWorkspaceStatusOptionalText(parameters, "color", MaxWorkspaceStatusMetaLength, out var color, out var colorError))
        {
            return new { updated = false, reason = colorError };
        }

        var selectedIndex = ResolveWorkspaceIndex(index, id);
        if (selectedIndex < 0 || selectedIndex >= _workspaces.Count)
        {
            return new
            {
                updated = false,
                reason = index.HasValue ? "index out of range" : "workspace not found"
            };
        }

        var workspace = _workspaces[selectedIndex];
        var existingIndex = _workspaceStatuses.FindIndex(status =>
            string.Equals(status.WorkspaceId, workspace.Id, StringComparison.Ordinal)
            && string.Equals(status.Key, key, StringComparison.Ordinal));
        var entry = existingIndex >= 0
            ? _workspaceStatuses[existingIndex]
            : new WorkspaceStatusEntry();
        if (existingIndex >= 0)
        {
            _workspaceStatuses.RemoveAt(existingIndex);
        }

        entry.WorkspaceId = workspace.Id;
        entry.WorkspaceTitle = workspace.Title;
        entry.Key = key;
        entry.Text = text;
        entry.Icon = icon;
        entry.Color = color;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        _workspaceStatuses.Add(entry);
        TrimWorkspaceStatuses();
        RecalculateWorkspaceStatusState();

        return new
        {
            updated = true,
            status = BuildWorkspaceStatusDto(entry),
            workspace = BuildWorkspaceDto(workspace, selectedIndex)
        };
    }

    private object HandleWorkspaceListStatus(JsonElement? parameters)
    {
        if (!TryReadOptionalWorkspaceTarget(parameters, out var index, out var id, out var targetError))
        {
            return new { listed = false, reason = targetError };
        }

        var selectedIndex = ResolveWorkspaceIndex(index, id);
        if (selectedIndex < 0 || selectedIndex >= _workspaces.Count)
        {
            return new
            {
                listed = false,
                reason = index.HasValue ? "index out of range" : "workspace not found"
            };
        }

        var workspace = _workspaces[selectedIndex];
        var statuses = _workspaceStatuses
            .Where(status => string.Equals(status.WorkspaceId, workspace.Id, StringComparison.Ordinal))
            .ToList();

        return new
        {
            listed = true,
            workspaceId = workspace.Id,
            count = statuses.Count,
            statuses = statuses.Select(BuildWorkspaceStatusDto).ToList()
        };
    }

    private object HandleWorkspaceClearStatus(JsonElement? parameters)
    {
        if (!TryReadOptionalWorkspaceTarget(parameters, out var index, out var id, out var targetError))
        {
            return new { cleared = 0, reason = targetError };
        }

        if (!TryReadWorkspaceStatusClearKey(parameters, out var key, out var clearAll, out var keyError))
        {
            return new { cleared = 0, reason = keyError };
        }

        var selectedIndex = ResolveWorkspaceIndex(index, id);
        if (selectedIndex < 0 || selectedIndex >= _workspaces.Count)
        {
            return new
            {
                cleared = 0,
                reason = index.HasValue ? "index out of range" : "workspace not found"
            };
        }

        var workspace = _workspaces[selectedIndex];
        var cleared = _workspaceStatuses.RemoveAll(status =>
            string.Equals(status.WorkspaceId, workspace.Id, StringComparison.Ordinal)
            && (clearAll || string.Equals(status.Key, key, StringComparison.Ordinal)));
        RecalculateWorkspaceStatusState();
        return new
        {
            cleared,
            workspace = BuildWorkspaceDto(workspace, selectedIndex)
        };
    }

    private object HandleWorkspaceSetProgress(JsonElement? parameters)
    {
        if (!TryReadOptionalWorkspaceTarget(parameters, out var index, out var id, out var targetError))
        {
            return new { updated = false, reason = targetError };
        }

        if (!TryReadWorkspaceProgressValue(parameters, out var value, out var valueError))
        {
            return new { updated = false, reason = valueError };
        }

        if (!TryReadWorkspaceProgressLabel(parameters, out var label, out var labelError))
        {
            return new { updated = false, reason = labelError };
        }

        var selectedIndex = ResolveWorkspaceIndex(index, id);
        if (selectedIndex < 0 || selectedIndex >= _workspaces.Count)
        {
            return new
            {
                updated = false,
                reason = index.HasValue ? "index out of range" : "workspace not found"
            };
        }

        var workspace = _workspaces[selectedIndex];
        var entry = _workspaceProgress.TryGetValue(workspace.Id, out var existing)
            ? existing
            : new WorkspaceProgressEntry();

        entry.WorkspaceId = workspace.Id;
        entry.WorkspaceTitle = workspace.Title;
        entry.Value = value;
        entry.Label = label;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        _workspaceProgress[workspace.Id] = entry;
        RecalculateWorkspaceProgressState();

        return new
        {
            updated = true,
            progress = BuildWorkspaceProgressDto(entry),
            workspace = BuildWorkspaceDto(workspace, selectedIndex)
        };
    }

    private object HandleWorkspaceClearProgress(JsonElement? parameters)
    {
        if (!TryReadOptionalWorkspaceTarget(parameters, out var index, out var id, out var targetError))
        {
            return new { cleared = 0, reason = targetError };
        }

        var selectedIndex = ResolveWorkspaceIndex(index, id);
        if (selectedIndex < 0 || selectedIndex >= _workspaces.Count)
        {
            return new
            {
                cleared = 0,
                reason = index.HasValue ? "index out of range" : "workspace not found"
            };
        }

        var workspace = _workspaces[selectedIndex];
        var cleared = _workspaceProgress.Remove(workspace.Id) ? 1 : 0;
        RecalculateWorkspaceProgressState();
        return new
        {
            cleared,
            workspace = BuildWorkspaceDto(workspace, selectedIndex)
        };
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

    private object HandleReadScreen(JsonElement? parameters)
    {
        var pane = ActivePane();
        if (!TryReadOptionalPositiveIntParam(parameters, "lines", out var lines, out var error))
        {
            return new
            {
                text = "",
                lines = (int?)null,
                truncated = false,
                paneId = pane?.Id,
                paneKind = pane?.Kind.ToString().ToLowerInvariant(),
                ok = false,
                reason = error
            };
        }

        var sourceText = pane?.Kind == PaneKind.Terminal ? pane.LastScreenText : "";
        var result = TerminalScreenReader.Read(sourceText, lines);
        return new
        {
            result.Text,
            result.Lines,
            result.Truncated,
            paneId = pane?.Id,
            paneKind = pane?.Kind.ToString().ToLowerInvariant()
        };
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

    private async Task<object> HandleBrowserBackAsync()
    {
        return await RunBrowserNavigationAsync(view => view.GoBackAsync()).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserForwardAsync()
    {
        return await RunBrowserNavigationAsync(view => view.GoForwardAsync()).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserReloadAsync()
    {
        return await RunBrowserNavigationAsync(view => view.ReloadAsync()).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserGetUrlAsync()
    {
        return await RunBrowserNavigationAsync(view => view.GetCurrentUrlAsync()).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserGetTitleAsync()
    {
        var pane = ActivePane();
        return await RunBrowserScriptAsync(view => view.GetElementDataAsync("title", paneId: pane?.Id)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserGetAsync(JsonElement? parameters, string kind)
    {
        var parsed = Deserialize<BrowserGetParams>(parameters);
        var selector = parsed?.Selector;
        if (string.IsNullOrWhiteSpace(selector))
        {
            return new { ok = false, kind, reason = "selector is required" };
        }

        string? name = null;
        if (kind is "attr")
        {
            if (string.IsNullOrWhiteSpace(parsed?.Attr))
            {
                return new { ok = false, kind, selector, reason = "attr is required" };
            }

            name = parsed.Attr;
        }
        else if (kind is "styles")
        {
            if (string.IsNullOrWhiteSpace(parsed?.Property))
            {
                return new { ok = false, kind, selector, reason = "property is required" };
            }

            name = parsed.Property;
        }

        var pane = ActivePane();
        return await RunBrowserScriptAsync(view => view.GetElementDataAsync(
            kind,
            selector,
            parsed?.Frame,
            name,
            pane?.Id)).ConfigureAwait(true);
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

    private async Task<object> HandleBrowserTextAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserTextParams>(parameters);
        if (parsed?.MaxChars is <= 0)
        {
            return new { ok = false, reason = "maxChars must be positive", maxChars = parsed.MaxChars };
        }

        var pane = ActivePane();
        if (!TryGetActiveBrowserView(out var view, out var reason))
        {
            return new { ok = false, reason };
        }

        try
        {
            var resultJson = await view.ReadTextAsync(
                parsed?.Selector,
                parsed?.Frame,
                parsed?.MaxChars,
                pane?.Id).ConfigureAwait(true);
            var result = ParseScriptJson(resultJson);
            if (result is JsonElement { ValueKind: JsonValueKind.Object } element
                && element.TryGetProperty("ok", out _))
            {
                return element.Clone();
            }

            return new { ok = true, result, paneId = pane?.Id };
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return new { ok = false, reason = ex.Message, paneId = pane?.Id };
        }
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

    private async Task<object> HandleBrowserHoverAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserSelectorParams>(parameters);
        if (string.IsNullOrWhiteSpace(parsed?.Selector))
        {
            return new { ok = false, reason = "selector is required" };
        }

        return await RunBrowserScriptAsync(view => view.HoverAsync(parsed.Selector, parsed.Frame)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserFocusAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserSelectorParams>(parameters);
        if (string.IsNullOrWhiteSpace(parsed?.Selector))
        {
            return new { ok = false, reason = "selector is required" };
        }

        return await RunBrowserScriptAsync(view => view.FocusAsync(parsed.Selector, parsed.Frame)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserIsAsync(JsonElement? parameters, string state)
    {
        var parsed = Deserialize<BrowserSelectorParams>(parameters);
        if (string.IsNullOrWhiteSpace(parsed?.Selector))
        {
            return new { ok = false, reason = "selector is required" };
        }

        return await RunBrowserScriptAsync(view => view.CheckElementStateAsync(state, parsed.Selector, parsed.Frame)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserFindAsync(JsonElement? parameters, string kind)
    {
        var parsed = Deserialize<BrowserFindParams>(parameters);
        if (parsed is null)
        {
            return new { ok = false, kind, reason = "parameters are required" };
        }

        var value = kind switch
        {
            "role" => parsed.Role,
            "text" => parsed.Text,
            "label" => parsed.Label,
            "placeholder" => parsed.Placeholder,
            "testid" => parsed.TestId,
            _ => null
        };

        if (kind is "role" or "text" or "label" or "placeholder" or "testid"
            && string.IsNullOrWhiteSpace(value))
        {
            return new { ok = false, kind, reason = $"{kind} is required" };
        }

        if (kind is "first" or "last" or "nth" && string.IsNullOrWhiteSpace(parsed.Selector))
        {
            return new { ok = false, kind, reason = "selector is required" };
        }

        if (kind is "nth" && parsed.Index is null or < 0)
        {
            return new { ok = false, kind, reason = "index must be a non-negative integer" };
        }

        return await RunBrowserScriptAsync(view => view.FindElementsAsync(
            kind,
            value,
            parsed.Selector,
            parsed.Name,
            parsed.Index,
            parsed.Exact,
            parsed.Frame)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserSelectAsync(JsonElement? parameters)
    {
        if (!TryReadBrowserSelectParams(parameters, out var parsed, out var reason))
        {
            return new { ok = false, reason };
        }

        return await RunBrowserScriptAsync(view => view.SelectAsync(parsed.Selector!, parsed.Value!, parsed.Frame)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserSnapshotAsync(JsonElement? parameters)
    {
        if (!TryReadBrowserSnapshotParams(parameters, out var parsed, out var reason))
        {
            return new { ok = false, reason };
        }

        return await RunBrowserScriptAsync(view => view.SnapshotAsync(
            parsed.Selector,
            parsed.Interactive,
            parsed.Cursor,
            parsed.Compact,
            parsed.MaxDepth)).ConfigureAwait(true);
    }

    private static bool TryReadBrowserSnapshotParams(JsonElement? parameters, out BrowserSnapshotParams parsed, out string reason)
    {
        parsed = new BrowserSnapshotParams();
        reason = "";
        if (parameters is null or { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
        {
            return true;
        }

        if (parameters is not { ValueKind: JsonValueKind.Object } element)
        {
            reason = "parameters must be an object";
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals("interactive", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadBoolProperty(property.Value, out var interactive))
                {
                    reason = "interactive must be a boolean";
                    return false;
                }

                parsed.Interactive = interactive;
                continue;
            }

            if (property.Name.Equals("cursor", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadBoolProperty(property.Value, out var cursor))
                {
                    reason = "cursor must be a boolean";
                    return false;
                }

                parsed.Cursor = cursor;
                continue;
            }

            if (property.Name.Equals("compact", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadBoolProperty(property.Value, out var compact))
                {
                    reason = "compact must be a boolean";
                    return false;
                }

                parsed.Compact = compact;
                continue;
            }

            if (property.Name.Equals("selector", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    parsed.Selector = null;
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    reason = "selector must be a string";
                    return false;
                }

                parsed.Selector = property.Value.GetString();
                continue;
            }

            if (property.Name.Equals("maxDepth", StringComparison.OrdinalIgnoreCase)
                || property.Name.Equals("max_depth", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    parsed.MaxDepth = null;
                    continue;
                }

                if (!TryReadPositiveIntProperty(property.Value, out var maxDepth))
                {
                    reason = "maxDepth must be positive";
                    return false;
                }

                parsed.MaxDepth = maxDepth;
                continue;
            }

            reason = $"unsupported parameter: {property.Name}";
            return false;
        }

        return true;
    }

    private static bool TryReadBoolProperty(JsonElement property, out bool value)
    {
        value = false;
        if (property.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        return property.ValueKind == JsonValueKind.False;
    }

    private static bool TryReadPositiveIntProperty(JsonElement property, out int value)
    {
        value = 0;
        return property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value)
            && value > 0;
    }

    private static bool TryReadBrowserSelectParams(JsonElement? parameters, out BrowserSelectParams parsed, out string reason)
    {
        parsed = new BrowserSelectParams();
        reason = "";
        if (parameters is null or { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
        {
            reason = "selector is required";
            return false;
        }

        if (parameters is not { ValueKind: JsonValueKind.Object } element)
        {
            reason = "parameters must be an object";
            return false;
        }

        var hasValue = false;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals("selector", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    reason = "selector must be a string";
                    return false;
                }

                parsed.Selector = property.Value.GetString();
                continue;
            }

            if (property.Name.Equals("value", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    reason = property.Value.ValueKind == JsonValueKind.Null ? "value is required" : "value must be a string";
                    return false;
                }

                parsed.Value = property.Value.GetString();
                hasValue = true;
                continue;
            }

            if (property.Name.Equals("frame", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    parsed.Frame = null;
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    reason = "frame must be a string";
                    return false;
                }

                parsed.Frame = property.Value.GetString();
                continue;
            }

            reason = $"unsupported parameter: {property.Name}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(parsed.Selector))
        {
            reason = "selector is required";
            return false;
        }

        if (!hasValue)
        {
            reason = "value is required";
            return false;
        }

        return true;
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

    private async Task<object> HandleBrowserWaitForLoadAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        var parsed = Deserialize<BrowserWaitForLoadParams>(parameters);
        if (parsed?.TimeoutMs is <= 0)
        {
            return new { ok = false, reason = "timeoutMs must be positive", timeoutMs = parsed.TimeoutMs };
        }

        if (!await _browserLoadWaitGate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
        {
            return new { ok = false, reason = "load wait already running" };
        }

        try
        {
            return await RunBrowserScriptAsync(view => view.WaitForLoadAsync(
                parsed?.State,
                parsed?.TimeoutMs,
                cancellationToken)).ConfigureAwait(true);
        }
        finally
        {
            _browserLoadWaitGate.Release();
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

    private async Task<object> HandleBrowserRouteListAsync()
    {
        return await RunBrowserScriptAsync(view => view.GetRouteListAsync()).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserRouteBlockAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserRouteMatchParams>(parameters);
        if (string.IsNullOrWhiteSpace(parsed?.UrlContains))
        {
            return new { ok = false, reason = "urlContains is required" };
        }

        return await RunBrowserScriptAsync(view => view.AddBlockRouteAsync(parsed.UrlContains)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserRouteFulfillAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserRouteFulfillParams>(parameters);
        if (string.IsNullOrWhiteSpace(parsed?.UrlContains))
        {
            return new { ok = false, reason = "urlContains is required" };
        }

        if (parsed.Status is < 100 or > 599)
        {
            return new { ok = false, reason = "status must be between 100 and 599" };
        }

        return await RunBrowserScriptAsync(view => view.AddFulfillRouteAsync(
            parsed.UrlContains,
            parsed.Status,
            parsed.ContentType,
            parsed.Body)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserRouteClearAsync()
    {
        return await RunBrowserScriptAsync(view => view.ClearRoutesAsync()).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserTraceAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserTraceParams>(parameters);
        if (string.IsNullOrWhiteSpace(parsed?.Path))
        {
            return new { ok = false, reason = "path is required" };
        }

        if (parsed.DurationMs is <= 0)
        {
            return new { ok = false, reason = "durationMs must be positive" };
        }

        if (parsed.MaxBytes is <= 0)
        {
            return new { ok = false, reason = "maxBytes must be positive" };
        }

        return await RunBrowserScriptAsync(view => view.CaptureTraceAsync(
            parsed.Path,
            parsed.DurationMs,
            parsed.MaxBytes)).ConfigureAwait(true);
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
        NormalizeWorkspace(workspace);
        RefreshWorkspaceGitMetadata(workspace);

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

    private bool SelectWorkspace(int? index, string? id)
    {
        EnsureDefaultWorkspace();
        var selectedIndex = index ?? _workspaces.ToList().FindIndex(workspace => string.Equals(workspace.Id, id, StringComparison.Ordinal));
        if (selectedIndex < 0 || selectedIndex >= _workspaces.Count)
        {
            return false;
        }

        _activeWorkspaceIndex = selectedIndex;
        WorkspaceList.SelectedIndex = selectedIndex;
        var workspace = ActiveWorkspace();
        NormalizeWorkspace(workspace);
        RefreshWorkspaceGitMetadata(workspace);
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
        TryShowNativeToast(notification);
        return notification;
    }

    private void TryShowNativeToast(TerminalNotification notification)
    {
        try
        {
            _ = _nativeToastService.TryShow(NativeToastRequest.FromNotification(notification));
        }
        catch
        {
            // Native toasts are a best-effort mirror of local in-app state.
        }
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

    private void TrimWorkspaceLogs()
    {
        while (_workspaceLogs.Count > MaxWorkspaceLogs)
        {
            _workspaceLogs.RemoveAt(0);
        }
    }

    private void TrimWorkspaceStatuses()
    {
        while (_workspaceStatuses.Count > MaxWorkspaceStatuses)
        {
            _workspaceStatuses.RemoveAt(0);
        }
    }

    private void RecalculateWorkspaceLogState()
    {
        foreach (var workspace in _workspaces)
        {
            var latest = _workspaceLogs
                .LastOrDefault(log => string.Equals(log.WorkspaceId, workspace.Id, StringComparison.Ordinal));
            workspace.LatestLog = latest is null ? null : FormatWorkspaceLogText(latest);
        }
    }

    private void RecalculateWorkspaceStatusState()
    {
        foreach (var workspace in _workspaces)
        {
            var latest = _workspaceStatuses
                .LastOrDefault(status => string.Equals(status.WorkspaceId, workspace.Id, StringComparison.Ordinal));
            workspace.LatestStatus = latest is null ? null : FormatWorkspaceStatusText(latest);
        }
    }

    private void RecalculateWorkspaceProgressState()
    {
        foreach (var workspace in _workspaces)
        {
            workspace.LatestProgress = _workspaceProgress.TryGetValue(workspace.Id, out var progress)
                ? FormatWorkspaceProgressText(progress)
                : null;
        }
    }

    private static string FormatWorkspaceLogText(WorkspaceLogEntry entry)
    {
        var prefix = $"[{entry.Level}]";
        return string.IsNullOrWhiteSpace(entry.Source)
            ? $"{prefix} {entry.Message}"
            : $"{prefix} {entry.Source}: {entry.Message}";
    }

    private static string FormatWorkspaceStatusText(WorkspaceStatusEntry entry)
    {
        return string.IsNullOrWhiteSpace(entry.Icon)
            ? $"{entry.Key}: {entry.Text}"
            : $"{entry.Key}: {entry.Icon} {entry.Text}";
    }

    private static string FormatWorkspaceProgressText(WorkspaceProgressEntry entry)
    {
        var percent = (int)Math.Round(entry.Value * 100, MidpointRounding.AwayFromZero);
        var prefix = percent.ToString(CultureInfo.InvariantCulture) + "%";
        return string.IsNullOrWhiteSpace(entry.Label)
            ? prefix
            : $"{prefix} {entry.Label}";
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
        var branchMeta = string.IsNullOrWhiteSpace(workspace.GitBranchLabel) ? "" : $"  |  {workspace.GitBranchLabel}";
        var pullRequestMeta = string.IsNullOrWhiteSpace(workspace.PullRequestLabel) ? "" : $"  |  {workspace.PullRequestLabel}";
        var portsMeta = string.IsNullOrWhiteSpace(workspace.PortsLabel) ? "" : $"  |  {workspace.PortsLabel}";
        var statusMeta = string.IsNullOrWhiteSpace(workspace.LatestStatusLabel) ? "" : $"  |  {workspace.LatestStatusLabel}";
        var progressMeta = string.IsNullOrWhiteSpace(workspace.LatestProgressLabel) ? "" : $"  |  {workspace.LatestProgressLabel}";
        var notificationMeta = string.IsNullOrWhiteSpace(workspace.LatestNotificationLabel) ? "" : $"  |  {workspace.LatestNotificationLabel}";
        var logMeta = string.IsNullOrWhiteSpace(workspace.LatestLogLabel) ? "" : $"  |  {workspace.LatestLogLabel}";
        WorkspaceMeta.Text = $"{workspace.WorkingDirectory}{branchMeta}{pullRequestMeta}{portsMeta}{statusMeta}{progressMeta}{notificationMeta}{logMeta}  |  surfaces: {workspace.Surfaces.Count}  |  panes: {CountPanes(surface.Root)}  |  unread: {workspace.UnreadCount}";
        RefreshSurfaceTabs(workspace);
        var activeSessionRunning = activePane is not null
            && _ptySessions.TryGetValue(activePane.Id, out var activeSession)
            && activeSession.IsRunning;
        var activeKind = activePane?.Kind.ToString().ToLowerInvariant() ?? "none";
        TerminalStatus.Text = $"{(activeSessionRunning ? "running" : "stopped")}  |  active: {activePane?.Title ?? "none"}  |  {activeKind}";
        TerminalInput.IsEnabled = activePane is not null;
        RefreshNotificationCenter();
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

    private void RefreshNotificationCenter()
    {
        var unreadCount = _notifications.Count(notification => !notification.IsRead);
        NotificationToggle.Content = $"Notifications ({unreadCount})";
        NotificationPanel.Visibility = _notificationPanelOpen ? Visibility.Visible : Visibility.Collapsed;
        NotificationItems.Children.Clear();

        var recentNotifications = _notifications
            .OrderByDescending(notification => notification.CreatedAt)
            .Take(20)
            .ToList();
        if (recentNotifications.Count == 0)
        {
            NotificationItems.Children.Add(new TextBlock
            {
                Text = "No notifications",
                Foreground = Brush("AgentMuxMutedText"),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var notification in recentNotifications)
        {
            NotificationItems.Children.Add(BuildNotificationItem(notification));
        }
    }

    private void RefreshShortcutReference()
    {
        ShortcutPanel.Visibility = _shortcutPanelOpen ? Visibility.Visible : Visibility.Collapsed;
        ShortcutPathText.Text = $"Config: {_shortcutSettings.FilePath}";
        ShortcutItems.Children.Clear();

        foreach (var binding in _shortcutSettings.BindingsForDisplay())
        {
            var row = new DockPanel
            {
                LastChildFill = false,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var actionText = new TextBlock
            {
                Text = binding.Action,
                Foreground = Brush("AgentMuxMutedText"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(actionText, Dock.Left);

            var gestureText = new TextBlock
            {
                Text = binding.Gesture,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(gestureText, Dock.Right);

            row.Children.Add(actionText);
            row.Children.Add(gestureText);
            ShortcutItems.Children.Add(row);
        }
    }

    private Border BuildNotificationItem(TerminalNotification notification)
    {
        var item = new Border
        {
            Background = notification.IsRead ? Brush("AgentMuxPanel") : new SolidColorBrush(Color.FromRgb(20, 36, 50)),
            BorderBrush = notification.IsRead ? Brush("AgentMuxBorder") : Brush("AgentMuxAccent"),
            BorderThickness = new Thickness(notification.IsRead ? 1 : 2),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(8)
        };

        var layout = new StackPanel();
        layout.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(notification.Title) ? "Notification" : notification.Title,
            Foreground = Brush("AgentMuxText"),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        if (!string.IsNullOrWhiteSpace(notification.Subtitle))
        {
            layout.Children.Add(new TextBlock
            {
                Text = notification.Subtitle,
                Foreground = Brush("AgentMuxMutedText"),
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }

        layout.Children.Add(new TextBlock
        {
            Text = notification.Body,
            Foreground = Brush("AgentMuxText"),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        layout.Children.Add(new TextBlock
        {
            Text = $"{(notification.IsRead ? "read" : "unread")}  |  {NotificationContext(notification)}",
            Foreground = Brush("AgentMuxMutedText"),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        item.Child = layout;
        return item;
    }

    private string NotificationContext(TerminalNotification notification)
    {
        var workspace = _workspaces.FirstOrDefault(candidate => candidate.Id == notification.WorkspaceId);
        var pane = string.IsNullOrWhiteSpace(notification.PaneId) ? null : FindPaneById(notification.PaneId);
        var workspaceTitle = string.IsNullOrWhiteSpace(workspace?.Title) ? notification.WorkspaceId : workspace!.Title;
        var paneTitle = string.IsNullOrWhiteSpace(pane?.Title) ? notification.PaneId : pane!.Title;
        return string.IsNullOrWhiteSpace(paneTitle)
            ? workspaceTitle
            : $"{workspaceTitle} / {paneTitle}";
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

    private async Task<object> RunBrowserNavigationAsync(Func<BrowserPaneView, Task<string>> action)
    {
        var pane = ActivePane();
        if (!TryGetActiveBrowserView(out var view, out var reason))
        {
            return new { ok = false, reason };
        }

        try
        {
            var resultJson = await action(view).ConfigureAwait(true);
            var result = ParseScriptJson(resultJson);
            if (result is JsonElement { ValueKind: JsonValueKind.Object } element)
            {
                if (element.TryGetProperty("url", out var urlElement)
                    && urlElement.ValueKind == JsonValueKind.String)
                {
                    SyncActiveBrowserPaneUrl(pane, urlElement.GetString());
                }

                if (element.TryGetProperty("ok", out _))
                {
                    return element.Clone();
                }
            }

            return new { ok = true, result, paneId = pane?.Id };
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return new { ok = false, reason = ex.Message, paneId = pane?.Id };
        }
    }

    private void SyncActiveBrowserPaneUrl(PaneState? pane, string? url)
    {
        if (pane?.Kind != PaneKind.Browser || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (!TryNormalizeAbsoluteBrowserUrl(url.Trim(), out var normalizedUrl))
        {
            return;
        }

        var title = BrowserTitle(normalizedUrl);
        if (string.Equals(pane.Url, normalizedUrl, StringComparison.Ordinal)
            && string.Equals(pane.Title, title, StringComparison.Ordinal))
        {
            return;
        }

        pane.Url = normalizedUrl;
        pane.Title = title;
        UpdateBrowserView(pane);
        QueueSessionSave();
        RefreshWorkspaceView();
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
        return method is AgentMuxMethods.BrowserBack
            or AgentMuxMethods.BrowserForward
            or AgentMuxMethods.BrowserReload
            or AgentMuxMethods.BrowserGetUrl
            or AgentMuxMethods.BrowserGetText
            or AgentMuxMethods.BrowserGetHtml
            or AgentMuxMethods.BrowserGetValue
            or AgentMuxMethods.BrowserGetAttribute
            or AgentMuxMethods.BrowserGetCount
            or AgentMuxMethods.BrowserGetBox
            or AgentMuxMethods.BrowserGetStyle
            or AgentMuxMethods.BrowserGetTitle
            or AgentMuxMethods.BrowserEval
            or AgentMuxMethods.BrowserText
            or AgentMuxMethods.BrowserClick
            or AgentMuxMethods.BrowserHover
            or AgentMuxMethods.BrowserFocus
            or AgentMuxMethods.BrowserIsVisible
            or AgentMuxMethods.BrowserIsEnabled
            or AgentMuxMethods.BrowserIsChecked
            or AgentMuxMethods.BrowserFindRole
            or AgentMuxMethods.BrowserFindText
            or AgentMuxMethods.BrowserFindLabel
            or AgentMuxMethods.BrowserFindPlaceholder
            or AgentMuxMethods.BrowserFindTestId
            or AgentMuxMethods.BrowserFindFirst
            or AgentMuxMethods.BrowserFindLast
            or AgentMuxMethods.BrowserFindNth
            or AgentMuxMethods.BrowserSnapshot
            or AgentMuxMethods.BrowserSelect
            or AgentMuxMethods.BrowserFill
            or AgentMuxMethods.BrowserType
            or AgentMuxMethods.BrowserPress
            or AgentMuxMethods.BrowserScreenshot
            or AgentMuxMethods.BrowserFrameTree
            or AgentMuxMethods.BrowserWaitForSelector
            or AgentMuxMethods.BrowserWaitForLoad
            or AgentMuxMethods.BrowserConsoleLog
            or AgentMuxMethods.BrowserConsoleClear
            or AgentMuxMethods.BrowserNetworkLog
            or AgentMuxMethods.BrowserNetworkClear
            or AgentMuxMethods.BrowserResponseBody
            or AgentMuxMethods.BrowserHarMetadata
            or AgentMuxMethods.BrowserDownloads
            or AgentMuxMethods.BrowserDownloadsClear
            or AgentMuxMethods.BrowserRouteList
            or AgentMuxMethods.BrowserRouteBlock
            or AgentMuxMethods.BrowserRouteFulfill
            or AgentMuxMethods.BrowserRouteClear
            or AgentMuxMethods.BrowserTrace;
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

    private object BuildWorkspaceDto(WorkspaceState workspace, int index)
    {
        NormalizeWorkspace(workspace);
        var surface = workspace.Surfaces[workspace.ActiveSurfaceIndex];
        NormalizeSurface(surface);
        return new
        {
            workspace.Id,
            workspace.Title,
            index,
            isActive = index == _activeWorkspaceIndex,
            workspace.WorkingDirectory,
            gitBranch = workspace.GitBranch,
            pullRequest = BuildWorkspacePullRequestDto(workspace.PullRequest),
            ports = workspace.Ports.ToArray(),
            workspace.UnreadCount,
            latestNotification = workspace.LatestNotificationPreview,
            latestLog = workspace.LatestLogPreview,
            latestStatus = workspace.LatestStatusPreview,
            latestProgress = workspace.LatestProgressPreview,
            progress = BuildWorkspaceProgressDto(FindWorkspaceProgress(workspace.Id)),
            logCount = _workspaceLogs.Count(log => string.Equals(log.WorkspaceId, workspace.Id, StringComparison.Ordinal)),
            statusCount = _workspaceStatuses.Count(status => string.Equals(status.WorkspaceId, workspace.Id, StringComparison.Ordinal)),
            progressCount = _workspaceProgress.ContainsKey(workspace.Id) ? 1 : 0,
            surfaceCount = workspace.Surfaces.Count,
            workspace.ActiveSurfaceIndex,
            activeSurfaceTitle = surface.Title,
            surface.ActivePaneId,
            paneCount = CountPanes(surface.Root),
            browserPaneCount = CountPaneKind(surface.Root, PaneKind.Browser)
        };
    }

    private static object? BuildWorkspacePullRequestDto(WorkspacePullRequest? pullRequest)
    {
        return pullRequest is null
            ? null
            : new
            {
                pullRequest.Number,
                pullRequest.Status,
                pullRequest.Url
            };
    }

    private static object BuildWorkspaceLogDto(WorkspaceLogEntry log)
    {
        return new
        {
            log.Id,
            log.WorkspaceId,
            log.WorkspaceTitle,
            log.Level,
            log.Source,
            log.Message,
            log.CreatedAt
        };
    }

    private static object BuildWorkspaceStatusDto(WorkspaceStatusEntry status)
    {
        return new
        {
            status.Id,
            status.WorkspaceId,
            status.WorkspaceTitle,
            status.Key,
            status.Text,
            status.Icon,
            status.Color,
            status.UpdatedAt
        };
    }

    private static object? BuildWorkspaceProgressDto(WorkspaceProgressEntry? progress)
    {
        if (progress is null)
        {
            return null;
        }

        return new
        {
            progress.Id,
            progress.WorkspaceId,
            progress.WorkspaceTitle,
            progress.Value,
            percent = (int)Math.Round(progress.Value * 100, MidpointRounding.AwayFromZero),
            progress.Label,
            text = FormatWorkspaceProgressText(progress),
            progress.UpdatedAt
        };
    }

    private WorkspaceProgressEntry? FindWorkspaceProgress(string workspaceId)
    {
        return _workspaceProgress.TryGetValue(workspaceId, out var progress) ? progress : null;
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

    internal async Task StartActivePanePtyForSmokeTestAsync()
    {
        await EnsurePanePtyAsync(ActivePane()).ConfigureAwait(true);
    }

    internal async Task<bool> EmitActiveTerminalRendererInputForSmokeTestAsync(string input)
    {
        if (ActivePane() is not { Kind: PaneKind.Terminal } pane
            || !_terminalViews.TryGetValue(pane.Id, out var view))
        {
            return false;
        }

        return await view.EmitInputForSmokeTestAsync(input).ConfigureAwait(true);
    }

    internal async Task<bool> EmitActiveTerminalXtermInputForSmokeTestAsync(string input)
    {
        if (ActivePane() is not { Kind: PaneKind.Terminal } pane
            || !_terminalViews.TryGetValue(pane.Id, out var view))
        {
            return false;
        }

        return await view.EmitXtermInputForSmokeTestAsync(input).ConfigureAwait(true);
    }

    internal async Task<bool> EmitActiveTerminalSyntheticKeydownForSmokeTestAsync(string key)
    {
        if (ActivePane() is not { Kind: PaneKind.Terminal } pane
            || !_terminalViews.TryGetValue(pane.Id, out var view))
        {
            return false;
        }

        return await view.EmitSyntheticKeydownForSmokeTestAsync(key).ConfigureAwait(true);
    }

    internal async Task<string> CaptureActiveTerminalPngForSmokeTestAsync(string path)
    {
        if (ActivePane() is not { Kind: PaneKind.Terminal } pane
            || !_terminalViews.TryGetValue(pane.Id, out var view))
        {
            throw new InvalidOperationException("active pane is not a rendered terminal");
        }

        return await view.CapturePngForSmokeTestAsync(path).ConfigureAwait(true);
    }

    internal async Task<string> WaitForActiveTerminalRuntimeTextForSmokeTestAsync(string expectedText)
    {
        if (ActivePane() is not { Kind: PaneKind.Terminal } pane
            || !_terminalViews.TryGetValue(pane.Id, out var view))
        {
            throw new InvalidOperationException("active pane is not a rendered terminal");
        }

        return await view.WaitForRuntimeTextForSmokeTestAsync(expectedText).ConfigureAwait(true);
    }

    internal int PaneCountForSmokeTest => CountPanes(ActiveSurface().Root);

    internal int WorkspaceCountForSmokeTest => _workspaces.Count;

    internal int ActiveWorkspaceIndexForSmokeTest => _activeWorkspaceIndex;

    internal int WorkspaceListSelectedIndexForSmokeTest => WorkspaceList.SelectedIndex;

    internal string ActiveWorkspaceTitleForSmokeTest => ActiveWorkspace().Title;

    internal string ActiveWorkspaceMetaForSmokeTest => WorkspaceMeta.Text;

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

    internal bool IsNotificationPanelOpenForSmokeTest => NotificationPanel.Visibility == Visibility.Visible;

    internal int RenderedNotificationItemCountForSmokeTest => NotificationItems.Children.Count;

    internal string NotificationButtonContentForSmokeTest => NotificationToggle.Content?.ToString() ?? "";

    internal bool IsActivePaneZoomedForSmokeTest => ActiveSurface().ZoomedPaneId == ActivePane()?.Id;

    internal bool HasButtonForSmokeTest(string content) => VisualTreeContainsButton(this, content);

    internal bool IsShortcutPanelOpenForSmokeTest => ShortcutPanel.Visibility == Visibility.Visible;

    internal int RenderedShortcutItemCountForSmokeTest => ShortcutItems.Children.Count;

    internal bool ShortcutPanelContainsTextForSmokeTest(string marker) => VisualTreeTextContains(ShortcutPanel, marker);

    internal void OpenShortcutPanelForSmokeTest()
    {
        _shortcutPanelOpen = true;
        RefreshShortcutReference();
    }

    internal bool RenderedTextContainsForSmokeTest(string marker) => VisualTreeTextContains(PaneHost, marker);

    internal bool SurfaceTabsContainTextForSmokeTest(string marker) => VisualTreeTextContains(SurfaceTabs, marker);

    internal bool NotificationCenterContainsTextForSmokeTest(string marker) => VisualTreeTextContains(NotificationPanel, marker);

    internal void OpenNotificationCenterForSmokeTest()
    {
        _notificationPanelOpen = true;
        RefreshNotificationCenter();
    }

    internal void CloseNotificationCenterForSmokeTest()
    {
        _notificationPanelOpen = false;
        RefreshNotificationCenter();
    }

    internal async Task JumpLatestNotificationForSmokeTestAsync()
    {
        HandleNotificationsJumpLatest();
        await EnsurePanePtyAsync(ActivePane()).ConfigureAwait(true);
        RefreshWorkspaceView();
    }

    internal void ClearUnreadNotificationsForSmokeTest()
    {
        HandleNotificationsClear();
        RefreshWorkspaceView();
    }

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

    internal void SetActivePaneShellForSmokeTest(string shell, string? workingDirectory = null)
    {
        if (ActivePane() is { Kind: PaneKind.Terminal } pane)
        {
            pane.Shell = shell;
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                pane.WorkingDirectory = workingDirectory;
            }

            _ptyStartFailedPaneIds.Remove(pane.Id);
            _terminalOutputProcessors.Remove(pane.Id);
            pane.LastScreenText = "";
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

    private static bool TryReadOptionalPositiveIntParam(JsonElement? parameters, string name, out int? value, out string error)
    {
        value = null;
        error = "";
        if (parameters is not { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty(name, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (!TryReadPositiveInt(property, out var parsed))
        {
            error = $"{name} must be a positive integer";
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryReadPositiveInt(JsonElement property, out int value)
    {
        value = 0;
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

        var hasIdProperty = element.TryGetProperty("id", out var idProperty);
        var hasId = hasIdProperty && TryReadNonEmptyString(idProperty, out id);
        if (hasIdProperty && !hasId)
        {
            error = "id is required";
            return false;
        }

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

    private static bool TryReadWorkspaceTarget(JsonElement? parameters, out int? index, out string? id, out string error)
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

        var hasIdProperty = element.TryGetProperty("id", out var idProperty);
        var hasId = hasIdProperty && TryReadNonEmptyString(idProperty, out id);
        if (hasIdProperty && !hasId)
        {
            error = "id is required";
            return false;
        }

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

    private static bool TryReadOptionalWorkspaceTarget(JsonElement? parameters, out int? index, out string? id, out string error)
    {
        index = null;
        id = null;
        error = "";
        if (parameters is not { ValueKind: JsonValueKind.Object } element)
        {
            return true;
        }

        var parsedIndex = 0;
        var hasIndex = element.TryGetProperty("index", out var indexProperty);
        if (hasIndex && !TryReadNonNegativeInt(indexProperty, out parsedIndex))
        {
            error = "index must be a non-negative integer";
            return false;
        }

        var hasIdProperty = element.TryGetProperty("id", out var idProperty);
        var hasId = hasIdProperty && TryReadNonEmptyString(idProperty, out id);
        if (hasIdProperty && !hasId)
        {
            error = "id is required";
            return false;
        }

        if (hasIndex && hasId)
        {
            error = "provide index or id, not both";
            return false;
        }

        index = hasIndex ? parsedIndex : null;
        return true;
    }

    private static bool TryReadWorkspacePullRequest(JsonElement? parameters, out WorkspacePullRequest? pullRequest, out string error)
    {
        pullRequest = null;
        error = "";
        if (parameters is not { ValueKind: JsonValueKind.Object } element)
        {
            error = "pull request number is required";
            return false;
        }

        if (element.TryGetProperty("clear", out var clearProperty)
            && clearProperty.ValueKind == JsonValueKind.True)
        {
            if (element.TryGetProperty("number", out _)
                || element.TryGetProperty("status", out _)
                || element.TryGetProperty("url", out _))
            {
                error = "clear cannot include pull request metadata";
                return false;
            }

            return true;
        }

        if (!element.TryGetProperty("number", out var numberProperty)
            || numberProperty.ValueKind != JsonValueKind.Number
            || !numberProperty.TryGetInt32(out var number)
            || number is < 1 or > 9999999)
        {
            error = "pull request number must be an integer between 1 and 9999999";
            return false;
        }

        var status = "unknown";
        if (element.TryGetProperty("status", out var statusProperty))
        {
            if (statusProperty.ValueKind != JsonValueKind.String)
            {
                error = "pull request status must be one of unknown, open, draft, merged, closed";
                return false;
            }

            var parsedStatus = NormalizePullRequestStatus(statusProperty.GetString());
            if (parsedStatus is null)
            {
                error = "pull request status must be one of unknown, open, draft, merged, closed";
                return false;
            }

            status = parsedStatus;
        }

        string? url = null;
        if (element.TryGetProperty("url", out var urlProperty))
        {
            if (urlProperty.ValueKind != JsonValueKind.String
                || !TryNormalizePullRequestUrl(urlProperty.GetString(), out url, strict: true))
            {
                error = "pull request url must be an absolute http or https URL without credentials";
                return false;
            }
        }

        pullRequest = new WorkspacePullRequest
        {
            Number = number,
            Status = status,
            Url = url
        };
        return true;
    }

    private static bool TryReadWorkspacePorts(JsonElement? parameters, out List<int> ports, out string error)
    {
        ports = [];
        error = "";
        if (parameters is not { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty("ports", out var portsProperty)
            || portsProperty.ValueKind != JsonValueKind.Array)
        {
            error = "ports array is required";
            return false;
        }

        var values = new SortedSet<int>();
        foreach (var portProperty in portsProperty.EnumerateArray())
        {
            if (portProperty.ValueKind != JsonValueKind.Number
                || !portProperty.TryGetInt32(out var port)
                || port is < 1 or > 65535)
            {
                error = "ports must be integers between 1 and 65535";
                return false;
            }

            values.Add(port);
        }

        if (values.Count > 20)
        {
            error = "at most 20 ports are supported";
            return false;
        }

        ports = values.ToList();
        return true;
    }

    private static bool TryReadWorkspaceLogMessage(JsonElement? parameters, out string message, out string error)
    {
        message = "";
        error = "";
        if (parameters is not { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty("message", out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            error = "message is required";
            return false;
        }

        message = CompactWorkspaceLogValue(property.GetString(), MaxWorkspaceLogMessageLength);
        if (string.IsNullOrWhiteSpace(message))
        {
            error = "message is required";
            return false;
        }

        return true;
    }

    private static bool TryReadWorkspaceLogLevel(JsonElement? parameters, out string level, out string error)
    {
        level = "info";
        error = "";
        if (parameters is not { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty("level", out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error = "level must be one of info, warn, error, debug";
            return false;
        }

        var normalized = property.GetString()?.Trim().ToLowerInvariant();
        if (normalized is not ("info" or "warn" or "error" or "debug"))
        {
            error = "level must be one of info, warn, error, debug";
            return false;
        }

        level = normalized;
        return true;
    }

    private static bool TryReadWorkspaceLogSource(JsonElement? parameters, out string? source, out string error)
    {
        source = null;
        error = "";
        if (parameters is not { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty("source", out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error = "source must be a string";
            return false;
        }

        var compact = CompactWorkspaceLogValue(property.GetString(), MaxWorkspaceLogSourceLength);
        source = string.IsNullOrWhiteSpace(compact) ? null : compact;
        return true;
    }

    private static bool TryReadWorkspaceLogLimit(JsonElement? parameters, out int limit, out string error)
    {
        limit = 50;
        error = "";
        if (parameters is not { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty("limit", out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (!TryReadPositiveInt(property, out var parsed))
        {
            error = "limit must be a positive integer";
            return false;
        }

        limit = Math.Min(parsed, MaxWorkspaceLogs);
        return true;
    }

    private static bool TryReadWorkspaceStatusKey(JsonElement? parameters, out string key, out string error)
    {
        key = "";
        error = "";
        if (parameters is not { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty("key", out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            error = "key is required";
            return false;
        }

        key = CompactWorkspaceLogValue(property.GetString(), MaxWorkspaceStatusKeyLength);
        if (string.IsNullOrWhiteSpace(key))
        {
            error = "key is required";
            return false;
        }

        return true;
    }

    private static bool TryReadWorkspaceStatusText(JsonElement? parameters, out string text, out string error)
    {
        text = "";
        error = "";
        if (parameters is not { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty("text", out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            error = "text is required";
            return false;
        }

        text = CompactWorkspaceLogValue(property.GetString(), MaxWorkspaceStatusTextLength);
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "text is required";
            return false;
        }

        return true;
    }

    private static bool TryReadWorkspaceStatusOptionalText(
        JsonElement? parameters,
        string propertyName,
        int maxLength,
        out string? value,
        out string error)
    {
        value = null;
        error = "";
        if (parameters is not { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty(propertyName, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error = $"{propertyName} must be a string";
            return false;
        }

        var compact = CompactWorkspaceLogValue(property.GetString(), maxLength);
        value = string.IsNullOrWhiteSpace(compact) ? null : compact;
        return true;
    }

    private static bool TryReadWorkspaceStatusClearKey(JsonElement? parameters, out string? key, out bool clearAll, out string error)
    {
        key = null;
        clearAll = false;
        error = "";
        if (parameters is not { ValueKind: JsonValueKind.Object } element)
        {
            error = "key or all is required";
            return false;
        }

        if (element.TryGetProperty("all", out var allProperty))
        {
            if (allProperty.ValueKind != JsonValueKind.True)
            {
                error = "all must be true";
                return false;
            }

            clearAll = true;
        }

        var hasKey = element.TryGetProperty("key", out var keyProperty);
        if (hasKey)
        {
            if (keyProperty.ValueKind != JsonValueKind.String)
            {
                error = "key is required";
                return false;
            }

            key = CompactWorkspaceLogValue(keyProperty.GetString(), MaxWorkspaceStatusKeyLength);
            if (string.IsNullOrWhiteSpace(key))
            {
                error = "key is required";
                return false;
            }
        }

        if (clearAll == hasKey)
        {
            error = clearAll ? "provide key or all, not both" : "key or all is required";
            return false;
        }

        return true;
    }

    private static bool TryReadWorkspaceProgressValue(JsonElement? parameters, out double value, out string error)
    {
        value = 0;
        error = "";
        if (parameters is not { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty("value", out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            error = "value is required";
            return false;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetDouble(out value))
        {
            error = "value must be a number between 0 and 1";
            return false;
        }

        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            error = "value must be a number between 0 and 1";
            return false;
        }

        return true;
    }

    private static bool TryReadWorkspaceProgressLabel(JsonElement? parameters, out string? label, out string error)
    {
        label = null;
        error = "";
        if (parameters is not { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty("label", out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error = "label must be a string";
            return false;
        }

        var compact = CompactWorkspaceLogValue(property.GetString(), MaxWorkspaceProgressLabelLength);
        label = string.IsNullOrWhiteSpace(compact) ? null : compact;
        return true;
    }

    private static string CompactWorkspaceLogValue(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var cleaned = new string(value.Select(ch => char.IsControl(ch) && !char.IsWhiteSpace(ch) ? ' ' : ch).ToArray());
        var compact = string.Join(' ', cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= maxLength ? compact : compact[..maxLength];
    }

    private int ResolveWorkspaceIndex(int? index, string? id)
    {
        EnsureDefaultWorkspace();
        if (index.HasValue)
        {
            return index.Value;
        }

        if (!string.IsNullOrWhiteSpace(id))
        {
            return _workspaces.ToList().FindIndex(workspace => string.Equals(workspace.Id, id, StringComparison.Ordinal));
        }

        _activeWorkspaceIndex = Math.Clamp(_activeWorkspaceIndex, 0, _workspaces.Count - 1);
        return _activeWorkspaceIndex;
    }

    private static WorkspacePullRequest? NormalizePullRequest(WorkspacePullRequest? pullRequest)
    {
        if (pullRequest is null || pullRequest.Number is < 1 or > 9999999)
        {
            return null;
        }

        var status = NormalizePullRequestStatus(pullRequest.Status) ?? "unknown";
        TryNormalizePullRequestUrl(pullRequest.Url, out var url, strict: false);
        return new WorkspacePullRequest
        {
            Number = pullRequest.Number,
            Status = status,
            Url = url
        };
    }

    private static string? NormalizePullRequestStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "unknown";
        }

        var normalized = status.Trim().ToLowerInvariant();
        return normalized is "unknown" or "open" or "draft" or "merged" or "closed"
            ? normalized
            : null;
    }

    private static bool TryNormalizePullRequestUrl(string? value, out string? normalizedUrl, bool strict)
    {
        normalizedUrl = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return !strict;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 2048
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var absoluteUrl = uri.AbsoluteUri;
        if (absoluteUrl.Length > 2048)
        {
            return false;
        }

        normalizedUrl = absoluteUrl;
        return true;
    }

    private static List<int> NormalizePorts(IEnumerable<int>? ports)
    {
        if (ports is null)
        {
            return [];
        }

        return ports
            .Where(port => port is >= 1 and <= 65535)
            .Distinct()
            .OrderBy(port => port)
            .Take(20)
            .ToList();
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

    private sealed class BrowserTextParams
    {
        public string? Selector { get; set; }
        public string? Frame { get; set; }
        public int? MaxChars { get; set; }
    }

    private sealed class BrowserGetParams
    {
        public string? Selector { get; set; }
        public string? Frame { get; set; }
        public string? Attr { get; set; }
        public string? Property { get; set; }
    }

    private sealed class BrowserSelectorParams
    {
        public string? Selector { get; set; }
        public string? Frame { get; set; }
    }

    private sealed class BrowserFindParams
    {
        public string? Role { get; set; }
        public string? Name { get; set; }
        public string? Text { get; set; }
        public string? Label { get; set; }
        public string? Placeholder { get; set; }
        public string? TestId { get; set; }
        public string? Selector { get; set; }
        public int? Index { get; set; }
        public bool Exact { get; set; }
        public string? Frame { get; set; }
    }

    private sealed class BrowserSelectParams
    {
        public string? Selector { get; set; }
        public string? Value { get; set; }
        public string? Frame { get; set; }
    }

    private sealed class BrowserSnapshotParams
    {
        public string? Selector { get; set; }
        public bool Interactive { get; set; }
        public bool Cursor { get; set; }
        public bool Compact { get; set; }
        public int? MaxDepth { get; set; }
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

    private sealed class BrowserWaitForLoadParams
    {
        public string? State { get; set; }
        public int? TimeoutMs { get; set; }
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

    private sealed class BrowserRouteMatchParams
    {
        public string? UrlContains { get; set; }
    }

    private sealed class BrowserRouteFulfillParams
    {
        public string? UrlContains { get; set; }
        public int? Status { get; set; }
        public string? ContentType { get; set; }
        public string? Body { get; set; }
    }

    private sealed class BrowserTraceParams
    {
        public string? Path { get; set; }
        public int? DurationMs { get; set; }
        public int? MaxBytes { get; set; }
    }
}
