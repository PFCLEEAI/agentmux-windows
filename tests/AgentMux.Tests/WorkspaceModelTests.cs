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

    [Fact]
    public void SplitConvertsLeafIntoContainer()
    {
        var leaf = SplitNodeState.CreateLeaf();
        var split = SplitNodeState.Split(leaf, SplitDirection.Right);

        Assert.False(split.IsLeaf);
        Assert.Equal(SplitDirection.Right, split.Direction);
        Assert.NotNull(split.First);
        Assert.NotNull(split.Second);
    }
}
