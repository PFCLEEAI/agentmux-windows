namespace AgentMux.Core.Models;

public sealed class WorkspaceState
{
    public string Id { get; set; } = Ids.New("workspace");
    public string Title { get; set; } = "Workspace";
    public string WorkingDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string? GitBranch { get; set; }
    public bool IsGitDirty { get; set; }
    public int ActiveSurfaceIndex { get; set; }
    public int UnreadCount { get; set; }
    public string? LatestNotification { get; set; }
    public List<SurfaceState> Surfaces { get; set; } = [SurfaceState.CreateDefault()];
}

public sealed class SurfaceState
{
    public string Id { get; set; } = Ids.New("surface");
    public string Title { get; set; } = "Terminal";
    public string? ActivePaneId { get; set; }
    public string? ZoomedPaneId { get; set; }
    public SplitNodeState Root { get; set; } = SplitNodeState.CreateLeaf();

    public static SurfaceState CreateDefault() => new();
}

public sealed class SplitNodeState
{
    public string Id { get; set; } = Ids.New("node");
    public SplitDirection? Direction { get; set; }
    public double Ratio { get; set; } = 0.5;
    public PaneState? Pane { get; set; }
    public SplitNodeState? First { get; set; }
    public SplitNodeState? Second { get; set; }

    public bool IsLeaf => Pane is not null;

    public static SplitNodeState CreateLeaf() => new()
    {
        Pane = new PaneState()
    };

    public static SplitNodeState Split(SplitNodeState leaf, SplitDirection direction)
    {
        if (!leaf.IsLeaf)
        {
            throw new InvalidOperationException("Only leaf nodes can be split.");
        }

        return new SplitNodeState
        {
            Direction = direction,
            First = leaf,
            Second = CreateLeaf()
        };
    }
}

public sealed class PaneState
{
    public string Id { get; set; } = Ids.New("pane");
    public PaneKind Kind { get; set; } = PaneKind.Terminal;
    public string Title { get; set; } = "Shell";
    public string Shell { get; set; } = "pwsh";
    public string WorkingDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public int Cols { get; set; } = 120;
    public int Rows { get; set; } = 30;
    public bool HasUnreadNotification { get; set; }
    public string? LastScreenText { get; set; }
    public string? Url { get; set; }
}

public sealed class TerminalNotification
{
    public string Id { get; set; } = Ids.New("notification");
    public string PaneId { get; set; } = "";
    public string WorkspaceId { get; set; } = "";
    public string Title { get; set; } = "Terminal";
    public string? Subtitle { get; set; }
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsRead { get; set; }
}

public sealed class SessionSnapshot
{
    public int Version { get; set; } = 1;
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    public int ActiveWorkspaceIndex { get; set; }
    public List<WorkspaceState> Workspaces { get; set; } = [];
}

public enum PaneKind
{
    Terminal,
    Browser
}

public enum SplitDirection
{
    Right,
    Down
}

public static class Ids
{
    public static string New(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
