using System.Text.Json;
using AgentMux.Core.Ipc;
using AgentMux.Core.Models;

namespace AgentMux.Tests;

public sealed class WorkspaceModelTests
{
    [Fact]
    public void NewWorkspaceHasDefaultSurfaceAndPane()
    {
        var workspace = new WorkspaceState();

        Assert.Single(workspace.Surfaces);
        Assert.True(workspace.Surfaces[0].Root.IsLeaf);
        Assert.NotNull(workspace.Surfaces[0].Root.Pane);
    }

    [Theory]
    [InlineData(SplitDirection.Right)]
    [InlineData(SplitDirection.Down)]
    public void SplitConvertsLeafIntoContainer(SplitDirection direction)
    {
        var leaf = SplitNodeState.CreateLeaf();
        var originalPaneId = leaf.Pane!.Id;

        var split = SplitNodeState.Split(leaf, direction);

        Assert.False(split.IsLeaf);
        Assert.Null(split.Pane);
        Assert.Equal(direction, split.Direction);
        Assert.Equal(0.5, split.Ratio);
        Assert.NotNull(split.First);
        Assert.NotNull(split.Second);
        Assert.Equal(originalPaneId, split.First.Pane?.Id);
        Assert.NotEqual(originalPaneId, split.Second.Pane?.Id);
    }

    [Fact]
    public void SurfaceActivePaneIdRoundTripsThroughJson()
    {
        var surface = SurfaceState.CreateDefault();
        var paneId = surface.Root.Pane!.Id;
        surface.ActivePaneId = paneId;

        var json = JsonSerializer.Serialize(surface, AgentMuxJson.Options);
        var loaded = JsonSerializer.Deserialize<SurfaceState>(json, AgentMuxJson.Options);

        Assert.NotNull(loaded);
        Assert.Equal(paneId, loaded.ActivePaneId);
        Assert.Equal(paneId, loaded.Root.Pane?.Id);
    }
}
