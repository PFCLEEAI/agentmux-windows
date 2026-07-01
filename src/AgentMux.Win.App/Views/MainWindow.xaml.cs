using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AgentMux.Core.Ipc;
using AgentMux.Core.Models;
using AgentMux.Win.Pty;

namespace AgentMux.Win.App.Views;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<WorkspaceState> _workspaces = [];
    private readonly List<TerminalNotification> _notifications = [];
    private NamedPipeRpcServer? _server;
    private ConPtySession? _pty;
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
        await StartPtyPreviewAsync().ConfigureAwait(false);
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        if (_pty is not null)
        {
            await _pty.DisposeAsync().ConfigureAwait(false);
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

    private Task<AgentMuxResponse> HandleRpcAsync(AgentMuxRequest request, CancellationToken cancellationToken)
    {
        var response = request.Method switch
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
            AgentMuxMethods.ReadScreen => AgentMuxResponse.Success(request.Id, new { text = FirstPane()?.LastScreenText ?? "" }),
            _ => AgentMuxResponse.Failure(request.Id, $"Unsupported method: {request.Method}")
        };

        Dispatcher.Invoke(RefreshWorkspaceView);
        return Task.FromResult(response);
    }

    private object BuildStatus() => new
    {
        app = "AgentMux Windows",
        status = "pre-alpha scaffold",
        workspaceCount = _workspaces.Count,
        activeWorkspaceIndex = _activeWorkspaceIndex,
        notificationCount = _notifications.Count
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

        var workspace = ActiveWorkspace();
        var surface = workspace.Surfaces.ElementAtOrDefault(workspace.ActiveSurfaceIndex) ?? workspace.Surfaces[0];
        var changed = SplitFirstLeaf(surface.Root, direction);
        return new { split = changed, direction = direction.ToString().ToLowerInvariant() };
    }

    private object HandleSendText(JsonElement? parameters)
    {
        var parsed = Deserialize<SendTextParams>(parameters);
        var pane = FirstPane();
        if (pane is null)
        {
            return new { sent = false };
        }

        var text = parsed?.Text ?? "";
        _ = SendTextToTerminalAsync(text);

        return new { sent = true, bytes = (parsed?.Text ?? "").Length };
    }

    private async Task StartPtyPreviewAsync()
    {
        _pty = new ConPtySession();
        _pty.OutputReceived += bytes =>
        {
            var text = Encoding.UTF8.GetString(bytes.Span);
            Dispatcher.Invoke(() =>
            {
                var pane = FirstPane();
                if (pane is not null)
                {
                    pane.LastScreenText = string.Concat(pane.LastScreenText, text);
                }

                RefreshWorkspaceView();
            });
        };
        _pty.Exited += exitCode =>
        {
            Dispatcher.Invoke(() =>
            {
                var pane = FirstPane();
                if (pane is not null)
                {
                    pane.LastScreenText = string.Concat(pane.LastScreenText, Environment.NewLine, $"[process exited: {exitCode}]", Environment.NewLine);
                }

                RefreshWorkspaceView();
            });
        };

        try
        {
            await _pty.StartAsync(new PtyLaunchOptions()).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Dispatcher.Invoke(() =>
            {
                var pane = FirstPane();
                if (pane is not null)
                {
                    pane.LastScreenText = $"ConPTY did not start: {ex.Message}";
                }

                RefreshWorkspaceView();
            });
        }
    }

    private async Task SendTerminalInputAsync()
    {
        string text;
        Dispatcher.VerifyAccess();
        text = TerminalInput.Text;
        TerminalInput.Clear();

        await SendTextToTerminalAsync(text).ConfigureAwait(false);
    }

    private async Task SendTextToTerminalAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var pane = FirstPane();
        if (_pty is { IsRunning: true })
        {
            try
            {
                await _pty.WriteAsync(Encoding.UTF8.GetBytes(text + Environment.NewLine)).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                Dispatcher.Invoke(() =>
                {
                    if (pane is not null)
                    {
                        pane.LastScreenText = string.Concat(pane.LastScreenText, Environment.NewLine, $"[send failed: {ex.Message}]", Environment.NewLine);
                    }

                    RefreshWorkspaceView();
                });
            }
        }
        else
        {
            Dispatcher.Invoke(() =>
            {
                if (pane is not null)
                {
                    pane.LastScreenText = string.Concat(pane.LastScreenText, "> ", text, Environment.NewLine);
                }

                RefreshWorkspaceView();
            });
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
        var pane = FirstPane();
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

    private PaneState? FirstPane()
    {
        var workspace = ActiveWorkspace();
        var surface = workspace.Surfaces.ElementAtOrDefault(workspace.ActiveSurfaceIndex) ?? workspace.Surfaces[0];
        return FindFirstPane(surface.Root);
    }

    private static PaneState? FindFirstPane(SplitNodeState node)
    {
        if (node.Pane is not null)
        {
            return node.Pane;
        }

        return node.First is not null ? FindFirstPane(node.First) : node.Second is not null ? FindFirstPane(node.Second) : null;
    }

    private static bool SplitFirstLeaf(SplitNodeState node, SplitDirection direction)
    {
        if (node.Pane is not null)
        {
            var existingPane = node.Pane;
            node.Pane = null;
            node.Direction = direction;
            node.First = new SplitNodeState { Pane = existingPane };
            node.Second = SplitNodeState.CreateLeaf();
            return true;
        }

        return (node.First is not null && SplitFirstLeaf(node.First, direction))
            || (node.Second is not null && SplitFirstLeaf(node.Second, direction));
    }

    private void RefreshWorkspaceView()
    {
        var workspace = ActiveWorkspace();
        WorkspaceTitle.Text = workspace.Title;
        WorkspaceMeta.Text = $"{workspace.WorkingDirectory}  |  unread: {workspace.UnreadCount}";
        ScreenPreview.Text = FirstPane()?.LastScreenText ?? "No terminal output yet.";
        TerminalStatus.Text = _pty is { IsRunning: true } ? "running" : "stopped";
        ScreenPreview.ScrollToEnd();
        WorkspaceList.Items.Refresh();
    }

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
}
