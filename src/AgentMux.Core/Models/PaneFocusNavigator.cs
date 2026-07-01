namespace AgentMux.Core.Models;

public static class PaneFocusNavigator
{
    private const double Epsilon = 0.000001;

    public static bool TryParseDirection(string? value, out PaneFocusDirection direction)
    {
        direction = PaneFocusDirection.Next;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "next":
                direction = PaneFocusDirection.Next;
                return true;
            case "previous":
            case "prev":
                direction = PaneFocusDirection.Previous;
                return true;
            case "left":
                direction = PaneFocusDirection.Left;
                return true;
            case "right":
                direction = PaneFocusDirection.Right;
                return true;
            case "up":
                direction = PaneFocusDirection.Up;
                return true;
            case "down":
                direction = PaneFocusDirection.Down;
                return true;
            default:
                return false;
        }
    }

    public static bool TryMoveFocus(SurfaceState surface, PaneFocusDirection direction, out PaneState? focusedPane)
    {
        focusedPane = FindTarget(surface, direction);
        if (focusedPane is null)
        {
            return false;
        }

        surface.ActivePaneId = focusedPane.Id;
        return true;
    }

    public static PaneState? FindTarget(SurfaceState surface, PaneFocusDirection direction)
    {
        var panes = new List<PaneBounds>();
        CollectBounds(surface.Root, left: 0, top: 0, width: 1, height: 1, panes);
        if (panes.Count == 0)
        {
            return null;
        }

        var activeIndex = surface.ActivePaneId is null
            ? -1
            : panes.FindIndex(bounds => bounds.Pane.Id == surface.ActivePaneId);
        if (activeIndex < 0)
        {
            return panes[0].Pane;
        }

        if (panes.Count < 2)
        {
            return null;
        }

        if (direction is PaneFocusDirection.Next or PaneFocusDirection.Previous)
        {
            var nextIndex = direction == PaneFocusDirection.Previous
                ? (activeIndex - 1 + panes.Count) % panes.Count
                : (activeIndex + 1) % panes.Count;
            return panes[nextIndex].Pane;
        }

        var active = panes[activeIndex];
        var best = panes
            .Where(bounds => bounds.Pane.Id != active.Pane.Id)
            .Select(bounds => ScoreDirectionalCandidate(active, bounds, direction))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!.Value)
            .OrderBy(candidate => candidate.OverlapPenalty)
            .ThenBy(candidate => candidate.Gap)
            .ThenByDescending(candidate => candidate.Overlap)
            .ThenBy(candidate => candidate.OrthogonalDistance)
            .ThenBy(candidate => candidate.PrimaryDistance)
            .ThenBy(candidate => candidate.Bounds.Pane.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        return best.Bounds.Pane;
    }

    private static CandidateScore? ScoreDirectionalCandidate(
        PaneBounds active,
        PaneBounds candidate,
        PaneFocusDirection direction)
    {
        return direction switch
        {
            PaneFocusDirection.Left when candidate.CenterX < active.CenterX - Epsilon => new CandidateScore(
                candidate,
                OverlapPenalty(active.Top, active.Bottom, candidate.Top, candidate.Bottom),
                Math.Max(0, active.Left - candidate.Right),
                Overlap(active.Top, active.Bottom, candidate.Top, candidate.Bottom),
                AxisDistance(active.Top, active.Bottom, candidate.Top, candidate.Bottom),
                active.CenterX - candidate.CenterX),
            PaneFocusDirection.Right when candidate.CenterX > active.CenterX + Epsilon => new CandidateScore(
                candidate,
                OverlapPenalty(active.Top, active.Bottom, candidate.Top, candidate.Bottom),
                Math.Max(0, candidate.Left - active.Right),
                Overlap(active.Top, active.Bottom, candidate.Top, candidate.Bottom),
                AxisDistance(active.Top, active.Bottom, candidate.Top, candidate.Bottom),
                candidate.CenterX - active.CenterX),
            PaneFocusDirection.Up when candidate.CenterY < active.CenterY - Epsilon => new CandidateScore(
                candidate,
                OverlapPenalty(active.Left, active.Right, candidate.Left, candidate.Right),
                Math.Max(0, active.Top - candidate.Bottom),
                Overlap(active.Left, active.Right, candidate.Left, candidate.Right),
                AxisDistance(active.Left, active.Right, candidate.Left, candidate.Right),
                active.CenterY - candidate.CenterY),
            PaneFocusDirection.Down when candidate.CenterY > active.CenterY + Epsilon => new CandidateScore(
                candidate,
                OverlapPenalty(active.Left, active.Right, candidate.Left, candidate.Right),
                Math.Max(0, candidate.Top - active.Bottom),
                Overlap(active.Left, active.Right, candidate.Left, candidate.Right),
                AxisDistance(active.Left, active.Right, candidate.Left, candidate.Right),
                candidate.CenterY - active.CenterY),
            _ => null
        };
    }

    private static int OverlapPenalty(double aStart, double aEnd, double bStart, double bEnd)
    {
        return Overlap(aStart, aEnd, bStart, bEnd) > Epsilon ? 0 : 1;
    }

    private static double Overlap(double aStart, double aEnd, double bStart, double bEnd)
    {
        return Math.Max(0, Math.Min(aEnd, bEnd) - Math.Max(aStart, bStart));
    }

    private static double AxisDistance(double aStart, double aEnd, double bStart, double bEnd)
    {
        if (Overlap(aStart, aEnd, bStart, bEnd) > Epsilon)
        {
            return 0;
        }

        return bEnd <= aStart ? aStart - bEnd : bStart - aEnd;
    }

    private static void CollectBounds(
        SplitNodeState node,
        double left,
        double top,
        double width,
        double height,
        List<PaneBounds> panes)
    {
        if (node.Pane is not null)
        {
            panes.Add(new PaneBounds(node.Pane, left, top, left + width, top + height));
            return;
        }

        var ratio = double.IsFinite(node.Ratio) ? Math.Clamp(node.Ratio, 0.1, 0.9) : 0.5;
        if (node.Direction == SplitDirection.Down)
        {
            var firstHeight = height * ratio;
            if (node.First is not null)
            {
                CollectBounds(node.First, left, top, width, firstHeight, panes);
            }

            if (node.Second is not null)
            {
                CollectBounds(node.Second, left, top + firstHeight, width, height - firstHeight, panes);
            }

            return;
        }

        var firstWidth = width * ratio;
        if (node.First is not null)
        {
            CollectBounds(node.First, left, top, firstWidth, height, panes);
        }

        if (node.Second is not null)
        {
            CollectBounds(node.Second, left + firstWidth, top, width - firstWidth, height, panes);
        }
    }

    private readonly record struct PaneBounds(PaneState Pane, double Left, double Top, double Right, double Bottom)
    {
        public double CenterX => (Left + Right) / 2;

        public double CenterY => (Top + Bottom) / 2;
    }

    private readonly record struct CandidateScore(
        PaneBounds Bounds,
        int OverlapPenalty,
        double Gap,
        double Overlap,
        double OrthogonalDistance,
        double PrimaryDistance);
}

public enum PaneFocusDirection
{
    Next,
    Previous,
    Left,
    Right,
    Up,
    Down
}
