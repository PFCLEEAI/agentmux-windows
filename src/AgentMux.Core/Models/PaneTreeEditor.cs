namespace AgentMux.Core.Models;

public static class PaneTreeEditor
{
    public static bool TryToggleZoom(SurfaceState surface, string paneId, out bool zoomed)
    {
        zoomed = false;
        if (FindPane(surface.Root, paneId) is null)
        {
            return false;
        }

        if (surface.ZoomedPaneId == paneId)
        {
            surface.ZoomedPaneId = null;
            return true;
        }

        surface.ZoomedPaneId = paneId;
        zoomed = true;
        return true;
    }

    public static bool TryClosePane(
        SurfaceState surface,
        string paneId,
        out PaneState? closedPane,
        out PaneState? focusedPane)
    {
        closedPane = null;
        focusedPane = null;

        if (surface.Root.Pane is not null)
        {
            return false;
        }

        if (!TryClosePane(surface.Root, paneId, out closedPane, out focusedPane))
        {
            return false;
        }

        surface.ActivePaneId = focusedPane?.Id;
        if (surface.ZoomedPaneId == paneId || FindPane(surface.Root, surface.ZoomedPaneId) is null)
        {
            surface.ZoomedPaneId = null;
        }

        return closedPane is not null && focusedPane is not null;
    }

    public static PaneState? FindPane(SplitNodeState node, string? paneId)
    {
        if (string.IsNullOrWhiteSpace(paneId))
        {
            return null;
        }

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

    public static PaneState? FindFirstPane(SplitNodeState node)
    {
        if (node.Pane is not null)
        {
            return node.Pane;
        }

        return node.First is not null && FindFirstPane(node.First) is { } first
            ? first
            : node.Second is not null
                ? FindFirstPane(node.Second)
                : null;
    }

    private static bool TryClosePane(
        SplitNodeState node,
        string paneId,
        out PaneState? closedPane,
        out PaneState? focusedPane)
    {
        closedPane = null;
        focusedPane = null;
        if (node.Pane is not null)
        {
            return false;
        }

        if (node.First?.Pane?.Id == paneId && node.Second is not null)
        {
            closedPane = node.First.Pane;
            focusedPane = FindFirstPane(node.Second);
            CopyFrom(node, node.Second);
            return true;
        }

        if (node.Second?.Pane?.Id == paneId && node.First is not null)
        {
            closedPane = node.Second.Pane;
            focusedPane = FindFirstPane(node.First);
            CopyFrom(node, node.First);
            return true;
        }

        return node.First is not null && TryClosePane(node.First, paneId, out closedPane, out focusedPane)
            || node.Second is not null && TryClosePane(node.Second, paneId, out closedPane, out focusedPane);
    }

    private static void CopyFrom(SplitNodeState target, SplitNodeState source)
    {
        target.Id = source.Id;
        target.Direction = source.Direction;
        target.Ratio = source.Ratio;
        target.Pane = source.Pane;
        target.First = source.First;
        target.Second = source.Second;
    }
}
