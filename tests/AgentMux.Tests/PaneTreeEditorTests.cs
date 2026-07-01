using System.Text.Json;
using AgentMux.Core.Ipc;
using AgentMux.Core.Models;

namespace AgentMux.Tests;

public sealed class PaneTreeEditorTests
{
    [Fact]
    public void ZoomedPaneIdRoundTripsThroughJson()
    {
        var surface = SurfaceState.CreateDefault();
        var pane = surface.Root.Pane!;
        surface.ActivePaneId = pane.Id;
        Assert.True(PaneTreeEditor.TryToggleZoom(surface, pane.Id, out var zoomed));
        Assert.True(zoomed);

        var json = JsonSerializer.Serialize(surface, AgentMuxJson.Options);
        var loaded = JsonSerializer.Deserialize<SurfaceState>(json, AgentMuxJson.Options);

        Assert.NotNull(loaded);
        Assert.Equal(pane.Id, loaded.ZoomedPaneId);
    }

    [Fact]
    public void ToggleZoomUnzoomsSamePane()
    {
        var surface = SurfaceState.CreateDefault();
        var pane = surface.Root.Pane!;

        Assert.True(PaneTreeEditor.TryToggleZoom(surface, pane.Id, out var zoomed));
        Assert.True(zoomed);
        Assert.True(PaneTreeEditor.TryToggleZoom(surface, pane.Id, out zoomed));
        Assert.False(zoomed);
        Assert.Null(surface.ZoomedPaneId);
    }

    [Fact]
    public void ToggleZoomRejectsMissingPaneWithoutMutation()
    {
        var surface = SurfaceState.CreateDefault();
        var pane = surface.Root.Pane!;
        surface.ZoomedPaneId = pane.Id;

        Assert.False(PaneTreeEditor.TryToggleZoom(surface, "missing-pane", out var zoomed));

        Assert.False(zoomed);
        Assert.Equal(pane.Id, surface.ZoomedPaneId);
    }

    [Fact]
    public void ClosePaneCollapsesRightSplitToSibling()
    {
        var surface = CreateRightSplit();
        var left = surface.Root.First!.Pane!;
        var right = surface.Root.Second!.Pane!;
        surface.ActivePaneId = left.Id;

        Assert.True(PaneTreeEditor.TryClosePane(surface, left.Id, out var closed, out var focused));

        Assert.Equal(left.Id, closed?.Id);
        Assert.Equal(right.Id, focused?.Id);
        Assert.Equal(right.Id, surface.ActivePaneId);
        Assert.True(surface.Root.IsLeaf);
        Assert.Equal(right.Id, surface.Root.Pane?.Id);
    }

    [Fact]
    public void ClosePaneCollapsesNestedDownSplitAndPreservesOuterSibling()
    {
        var surface = CreateNestedSplit();
        var topLeft = surface.Root.First!.First!.Pane!;
        var bottomLeft = surface.Root.First!.Second!.Pane!;
        var right = surface.Root.Second!.Pane!;
        surface.ActivePaneId = bottomLeft.Id;

        Assert.True(PaneTreeEditor.TryClosePane(surface, bottomLeft.Id, out var closed, out var focused));

        Assert.Equal(bottomLeft.Id, closed?.Id);
        Assert.Equal(topLeft.Id, focused?.Id);
        Assert.Equal(topLeft.Id, surface.ActivePaneId);
        Assert.Equal(SplitDirection.Right, surface.Root.Direction);
        Assert.Equal(topLeft.Id, surface.Root.First?.Pane?.Id);
        Assert.Equal(right.Id, surface.Root.Second?.Pane?.Id);
    }

    [Fact]
    public void ClosePaneInsideSecondBranchFocusesSurvivingSiblingBranch()
    {
        var surface = CreateRightBranchNestedSplit();
        var left = surface.Root.First!.Pane!;
        var topRight = surface.Root.Second!.First!.Pane!;
        var bottomRight = surface.Root.Second!.Second!.Pane!;
        surface.ActivePaneId = topRight.Id;

        Assert.True(PaneTreeEditor.TryClosePane(surface, topRight.Id, out var closed, out var focused));

        Assert.Equal(topRight.Id, closed?.Id);
        Assert.Equal(bottomRight.Id, focused?.Id);
        Assert.Equal(bottomRight.Id, surface.ActivePaneId);
        Assert.Equal(SplitDirection.Right, surface.Root.Direction);
        Assert.Equal(left.Id, surface.Root.First?.Pane?.Id);
        Assert.Equal(bottomRight.Id, surface.Root.Second?.Pane?.Id);
    }

    [Fact]
    public void ClosePaneClearsZoomWhenZoomedPaneIsClosed()
    {
        var surface = CreateRightSplit();
        var left = surface.Root.First!.Pane!;
        surface.ActivePaneId = left.Id;
        Assert.True(PaneTreeEditor.TryToggleZoom(surface, left.Id, out _));

        Assert.True(PaneTreeEditor.TryClosePane(surface, left.Id, out _, out _));

        Assert.Null(surface.ZoomedPaneId);
    }

    [Fact]
    public void ClosePaneClearsStaleZoomId()
    {
        var surface = CreateRightSplit();
        var left = surface.Root.First!.Pane!;
        surface.ZoomedPaneId = "missing-pane";

        Assert.True(PaneTreeEditor.TryClosePane(surface, left.Id, out _, out _));

        Assert.Null(surface.ZoomedPaneId);
    }

    [Fact]
    public void CloseSinglePaneFailsAndPreservesActivePane()
    {
        var surface = SurfaceState.CreateDefault();
        var pane = surface.Root.Pane!;
        surface.ActivePaneId = pane.Id;

        Assert.False(PaneTreeEditor.TryClosePane(surface, pane.Id, out var closed, out var focused));

        Assert.Null(closed);
        Assert.Null(focused);
        Assert.Equal(pane.Id, surface.ActivePaneId);
        Assert.Equal(pane.Id, surface.Root.Pane?.Id);
    }

    private static SurfaceState CreateRightSplit()
    {
        return new SurfaceState
        {
            Root = new SplitNodeState
            {
                Direction = SplitDirection.Right,
                Ratio = 0.5,
                First = SplitNodeState.CreateLeaf(),
                Second = SplitNodeState.CreateLeaf()
            }
        };
    }

    private static SurfaceState CreateNestedSplit()
    {
        return new SurfaceState
        {
            Root = new SplitNodeState
            {
                Direction = SplitDirection.Right,
                Ratio = 0.5,
                First = new SplitNodeState
                {
                    Direction = SplitDirection.Down,
                    Ratio = 0.5,
                    First = SplitNodeState.CreateLeaf(),
                    Second = SplitNodeState.CreateLeaf()
                },
                Second = SplitNodeState.CreateLeaf()
            }
        };
    }

    private static SurfaceState CreateRightBranchNestedSplit()
    {
        return new SurfaceState
        {
            Root = new SplitNodeState
            {
                Direction = SplitDirection.Right,
                Ratio = 0.5,
                First = SplitNodeState.CreateLeaf(),
                Second = new SplitNodeState
                {
                    Direction = SplitDirection.Down,
                    Ratio = 0.5,
                    First = SplitNodeState.CreateLeaf(),
                    Second = SplitNodeState.CreateLeaf()
                }
            }
        };
    }
}
