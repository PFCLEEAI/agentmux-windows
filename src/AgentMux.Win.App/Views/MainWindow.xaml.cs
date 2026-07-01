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
using AgentMux.Win.App.Controls;
using AgentMux.Win.Pty;

namespace AgentMux.Win.App.Views;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<WorkspaceState> _workspaces = [];
    private readonly List<TerminalNotification> _notifications = [];
    private readonly Dictionary<string, ConPtySession> _ptySessions = [];
    private readonly Dictionary<string, TerminalPaneView> _terminalViews = [];
    private readonly Dictionary<string, BrowserPaneView> _browserViews = [];
    private readonly HashSet<string> _ptyStartFailedPaneIds = [];
    private NamedPipeRpcServer? _server;
    private int _activeWorkspaceIndex;

    public MainWindow()
    {
        InitializeComponent();
        _workspaces.Add(new WorkspaceState
        {
            Title = "Default",
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        });
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        WorkspaceList.ItemsSource = _workspaces;
        WorkspaceList.SelectedIndex = 0;

        _server = new NamedPipeRpcServer(HandleRpcAsync);
        _server.Start();
        PipeStatus.Text = $"Pipe: {AgentMuxPipe.ForCurrentUser()}";

        RefreshWorkspaceView();
        await EnsurePanePtyAsync(ActivePane()).ConfigureAwait(false);
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
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

    private void WorkspaceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorkspaceList.SelectedIndex >= 0)
        {
            _activeWorkspaceIndex = WorkspaceList.SelectedIndex;
            RefreshWorkspaceView();
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
        if (e.Key != Key.Tab || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        e.Handled = CycleActivePane(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
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
            AgentMuxMethods.Notify => AgentMuxResponse.Success(request.Id, HandleNotify(request.Params)),
            AgentMuxMethods.Split => AgentMuxResponse.Success(request.Id, HandleSplit(request.Params)),
            AgentMuxMethods.SendText => AgentMuxResponse.Success(request.Id, HandleSendText(request.Params)),
            AgentMuxMethods.SendKey => AgentMuxResponse.Success(request.Id, HandleSendKey(request.Params)),
            AgentMuxMethods.ReadScreen => AgentMuxResponse.Success(request.Id, new { text = ActivePane()?.LastScreenText ?? "" }),
            AgentMuxMethods.OpenUrl => AgentMuxResponse.Success(request.Id, HandleOpenUrl(request.Params)),
            AgentMuxMethods.BrowserEval => AgentMuxResponse.Success(request.Id, await HandleBrowserEvalAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserClick => AgentMuxResponse.Success(request.Id, await HandleBrowserClickAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserFill => AgentMuxResponse.Success(request.Id, await HandleBrowserFillAsync(request.Params).ConfigureAwait(true)),
            AgentMuxMethods.BrowserScreenshot => AgentMuxResponse.Success(request.Id, await HandleBrowserScreenshotAsync(request.Params).ConfigureAwait(true)),
            _ => AgentMuxResponse.Failure(request.Id, $"Unsupported method: {request.Method}")
        };

        if (!IsBrowserAutomationMethod(request.Method))
        {
            RefreshWorkspaceView();
        }

        return response;
    }

    private object BuildStatus() => new
    {
        app = "AgentMux Windows",
        status = "pre-alpha scaffold",
        workspaceCount = _workspaces.Count,
        activeWorkspaceIndex = _activeWorkspaceIndex,
        notificationCount = _notifications.Count,
        terminalSessionCount = _ptySessions.Count,
        browserPaneCount = CountPaneKind(ActiveSurface().Root, PaneKind.Browser)
    };

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
        return new { selected = true, index };
    }

    private TerminalNotification HandleNotify(JsonElement? parameters)
    {
        var parsed = Deserialize<NotifyParams>(parameters);
        return AddNotification(parsed?.Title ?? "Terminal", parsed?.Body ?? "", parsed?.Subtitle);
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

        return await RunBrowserScriptAsync(view => view.ClickAsync(parsed.Selector)).ConfigureAwait(true);
    }

    private async Task<object> HandleBrowserFillAsync(JsonElement? parameters)
    {
        var parsed = Deserialize<BrowserFillParams>(parameters);
        if (string.IsNullOrWhiteSpace(parsed?.Selector))
        {
            return new { ok = false, reason = "selector is required" };
        }

        return await RunBrowserScriptAsync(view => view.FillAsync(parsed.Selector, parsed.Text ?? "")).ConfigureAwait(true);
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
                    pane.LastScreenText = string.Concat(pane.LastScreenText, text);
                    AppendTerminalView(pane, text);
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
                    var text = string.Concat(tail, Environment.NewLine, $"[process exited: {exitCode}]", Environment.NewLine);
                    pane.LastScreenText = string.Concat(pane.LastScreenText, text);
                    AppendTerminalView(pane, text);
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
                    pane.LastScreenText = string.Concat(pane.LastScreenText, text);
                    AppendTerminalView(pane, text);
                }

                RefreshWorkspaceView();
            }
        }
        else
        {
            if (pane is not null)
            {
                var text = fallbackText ?? sequence;
                pane.LastScreenText = string.Concat(pane.LastScreenText, text);
                AppendTerminalView(pane, text);
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

        return workspace;
    }

    private TerminalNotification AddNotification(string title, string body, string? subtitle = null)
    {
        var workspace = ActiveWorkspace();
        var pane = ActivePane();
        var notification = new TerminalNotification
        {
            WorkspaceId = workspace.Id,
            PaneId = pane?.Id ?? "",
            Title = title,
            Subtitle = subtitle,
            Body = body
        };

        _notifications.Add(notification);
        workspace.UnreadCount++;
        workspace.LatestNotification = body;
        if (pane is not null)
        {
            pane.HasUnreadNotification = true;
        }

        return notification;
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
            surface.ActivePaneId = newPane?.Id ?? activePane.Id;
        }

        return changed;
    }

    private bool CycleActivePane(bool reverse)
    {
        var surface = ActiveSurface();
        var panes = FlattenPanes(surface.Root);
        if (panes.Count < 2)
        {
            return false;
        }

        var activePane = ActivePane();
        var activeIndex = activePane is null ? -1 : panes.FindIndex(pane => pane.Id == activePane.Id);
        var nextIndex = activeIndex < 0
            ? 0
            : reverse
                ? (activeIndex - 1 + panes.Count) % panes.Count
                : (activeIndex + 1) % panes.Count;
        surface.ActivePaneId = panes[nextIndex].Id;
        RefreshWorkspaceView();
        return true;
    }

    private static List<PaneState> FlattenPanes(SplitNodeState root)
    {
        var panes = new List<PaneState>();
        CollectPanes(root, panes);
        return panes;
    }

    private static void CollectPanes(SplitNodeState node, List<PaneState> panes)
    {
        if (node.Pane is not null)
        {
            panes.Add(node.Pane);
            return;
        }

        if (node.First is not null)
        {
            CollectPanes(node.First, panes);
        }

        if (node.Second is not null)
        {
            CollectPanes(node.Second, panes);
        }
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
        WorkspaceMeta.Text = $"{workspace.WorkingDirectory}  |  panes: {CountPanes(surface.Root)}  |  unread: {workspace.UnreadCount}";
        var activeSessionRunning = activePane is not null
            && _ptySessions.TryGetValue(activePane.Id, out var activeSession)
            && activeSession.IsRunning;
        var activeKind = activePane?.Kind.ToString().ToLowerInvariant() ?? "none";
        TerminalStatus.Text = $"{(activeSessionRunning ? "running" : "stopped")}  |  active: {activePane?.Title ?? "none"}  |  {activeKind}";
        TerminalInput.IsEnabled = activePane is not null;
        PaneHost.Children.Clear();
        PaneHost.Children.Add(BuildSplitElement(surface.Root, activePane?.Id));
        WorkspaceList.Items.Refresh();
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
            _terminalViews[pane.Id] = view;
        }

        DetachFromParent(view);
        view.SetScreenText(pane.LastScreenText);
        return view;
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
        }

        pane.Kind = PaneKind.Browser;
        pane.Title = BrowserTitle(normalizedUrl);
        pane.Url = normalizedUrl;
        pane.LastScreenText = null;
        ActiveSurface().ActivePaneId = pane.Id;
        UpdateBrowserView(pane);
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
            or AgentMuxMethods.BrowserScreenshot;
    }

    private void StopPanePty(string paneId)
    {
        _ptyStartFailedPaneIds.Remove(paneId);
        if (_ptySessions.Remove(paneId, out var session))
        {
            _ = session.DisposeAsync().AsTask();
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

    internal void InitializeForSmokeTest()
    {
        WorkspaceList.ItemsSource = _workspaces;
        WorkspaceList.SelectedIndex = 0;
        RefreshWorkspaceView();
    }

    internal int PaneCountForSmokeTest => CountPanes(ActiveSurface().Root);

    internal int RenderedTerminalPaneCountForSmokeTest => CountVisualDescendants<TerminalPaneView>(PaneHost);

    internal int RenderedBrowserPaneCountForSmokeTest => CountVisualDescendants<BrowserPaneView>(PaneHost);

    internal string? ActivePaneIdForSmokeTest => ActivePane()?.Id;

    internal bool HasButtonForSmokeTest(string content) => VisualTreeContainsButton(this, content);

    internal bool RenderedTextContainsForSmokeTest(string marker) => VisualTreeTextContains(PaneHost, marker);

    internal bool SplitActivePaneForSmokeTest(SplitDirection direction)
    {
        var changed = SplitActivePane(direction);
        RefreshWorkspaceView();
        return changed;
    }

    internal bool CycleActivePaneForSmokeTest(bool reverse) => CycleActivePane(reverse);

    internal void SetActivePaneTextForSmokeTest(string text)
    {
        if (ActivePane() is { } pane)
        {
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

        pane.LastScreenText = string.Concat(pane.LastScreenText, text);
        AppendTerminalView(pane, text);
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

    private sealed class NotifyParams
    {
        public string? Title { get; set; }
        public string? Subtitle { get; set; }
        public string? Body { get; set; }
    }

    private sealed class WorkspaceCreateParams
    {
        public string? Title { get; set; }
        public string? Cwd { get; set; }
    }

    private sealed class WorkspaceSelectParams
    {
        public int? Index { get; set; }
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

    private sealed class BrowserEvalParams
    {
        public string? Script { get; set; }
    }

    private sealed class BrowserSelectorParams
    {
        public string? Selector { get; set; }
    }

    private sealed class BrowserFillParams
    {
        public string? Selector { get; set; }
        public string? Text { get; set; }
    }

    private sealed class BrowserScreenshotParams
    {
        public string? Path { get; set; }
    }
}
