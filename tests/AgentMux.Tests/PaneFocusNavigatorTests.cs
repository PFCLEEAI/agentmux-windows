using AgentMux.Core.Models;

namespace AgentMux.Tests;

public sealed class PaneFocusNavigatorTests
{
    [Fact]
    public void NextAndPreviousFollowFlattenedPaneOrder()
    {
        var surface = CreateRightSplit();
        var left = surface.Root.First!.Pane!;
        var right = surface.Root.Second!.Pane!;
        surface.ActivePaneId = left.Id;

        Assert.True(PaneFocusNavigator.TryMoveFocus(surface, PaneFocusDirection.Next, out var next));
        Assert.Equal(right.Id, next?.Id);
        Assert.Equal(right.Id, surface.ActivePaneId);

        Assert.True(PaneFocusNavigator.TryMoveFocus(surface, PaneFocusDirection.Previous, out var previous));
        Assert.Equal(left.Id, previous?.Id);
        Assert.Equal(left.Id, surface.ActivePaneId);
    }

    [Fact]
    public void DirectionalFocusUsesSplitGeometry()
    {
        var surface = CreateNestedSplit();
        var topLeft = surface.Root.First!.First!.Pane!;
        var bottomLeft = surface.Root.First!.Second!.Pane!;
        var right = surface.Root.Second!.Pane!;
        surface.ActivePaneId = topLeft.Id;

        Assert.True(PaneFocusNavigator.TryMoveFocus(surface, PaneFocusDirection.Down, out var down));
        Assert.Equal(bottomLeft.Id, down?.Id);

        surface.ActivePaneId = topLeft.Id;
        Assert.True(PaneFocusNavigator.TryMoveFocus(surface, PaneFocusDirection.Right, out var rightTarget));
        Assert.Equal(right.Id, rightTarget?.Id);

        surface.ActivePaneId = bottomLeft.Id;
        Assert.True(PaneFocusNavigator.TryMoveFocus(surface, PaneFocusDirection.Up, out var up));
        Assert.Equal(topLeft.Id, up?.Id);
    }

    [Fact]
    public void DirectionalFocusMovesLeftFromNestedRightBranch()
    {
        var surface = CreateRightBranchNestedSplit();
        var left = surface.Root.First!.Pane!;
        var topRight = surface.Root.Second!.First!.Pane!;
        var bottomRight = surface.Root.Second!.Second!.Pane!;
        surface.ActivePaneId = topRight.Id;

        Assert.True(PaneFocusNavigator.TryMoveFocus(surface, PaneFocusDirection.Left, out var leftTarget));
        Assert.Equal(left.Id, leftTarget?.Id);

        surface.ActivePaneId = topRight.Id;
        Assert.True(PaneFocusNavigator.TryMoveFocus(surface, PaneFocusDirection.Down, out var down));
        Assert.Equal(bottomRight.Id, down?.Id);
    }

    [Fact]
    public void DirectionalFocusReturnsFalseWhenNoPaneExistsInThatDirection()
    {
        var surface = CreateRightSplit();
        var right = surface.Root.Second!.Pane!;
        surface.ActivePaneId = right.Id;

        Assert.False(PaneFocusNavigator.TryMoveFocus(surface, PaneFocusDirection.Right, out var target));
        Assert.Null(target);
        Assert.Equal(right.Id, surface.ActivePaneId);
    }

    [Theory]
    [InlineData("next", PaneFocusDirection.Next)]
    [InlineData("prev", PaneFocusDirection.Previous)]
    [InlineData("previous", PaneFocusDirection.Previous)]
    [InlineData("left", PaneFocusDirection.Left)]
    [InlineData("right", PaneFocusDirection.Right)]
    [InlineData("up", PaneFocusDirection.Up)]
    [InlineData("down", PaneFocusDirection.Down)]
    public void DirectionParserAcceptsSupportedValues(string value, PaneFocusDirection expected)
    {
        Assert.True(PaneFocusNavigator.TryParseDirection(value, out var direction));
        Assert.Equal(expected, direction);
    }

    [Fact]
    public void StaleActivePaneIdFocusesFirstLeafDeterministically()
    {
        var surface = CreateRightSplit();
        var left = surface.Root.First!.Pane!;
        surface.ActivePaneId = "missing-pane";

        Assert.True(PaneFocusNavigator.TryMoveFocus(surface, PaneFocusDirection.Next, out var target));
        Assert.Equal(left.Id, target?.Id);
        Assert.Equal(left.Id, surface.ActivePaneId);
    }

    [Fact]
    public void NonFiniteRatioFallsBackToBalancedGeometry()
    {
        var surface = CreateRightSplit();
        surface.Root.Ratio = double.NaN;
        var left = surface.Root.First!.Pane!;
        var right = surface.Root.Second!.Pane!;
        surface.ActivePaneId = left.Id;

        Assert.True(PaneFocusNavigator.TryMoveFocus(surface, PaneFocusDirection.Right, out var target));
        Assert.Equal(right.Id, target?.Id);
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
                Ratio = 0.45,
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
                Ratio = 0.45,
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
