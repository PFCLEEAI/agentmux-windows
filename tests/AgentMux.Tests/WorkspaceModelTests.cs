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

    [Fact]
    public void WorkspaceGitBranchLabelIsDisplayOnly()
    {
        var workspace = new WorkspaceState
        {
            GitBranch = "feature/sidebar",
            IsGitDirty = true
        };

        var json = JsonSerializer.Serialize(workspace, AgentMuxJson.Options);

        Assert.Equal("branch: feature/sidebar", workspace.GitBranchLabel);
        Assert.DoesNotContain("gitBranch", json, StringComparison.Ordinal);
        Assert.DoesNotContain("gitBranchLabel", json, StringComparison.Ordinal);
        Assert.DoesNotContain("isGitDirty", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceLatestNotificationPreviewIsDisplayOnlyAndCapped()
    {
        var workspace = new WorkspaceState
        {
            LatestNotification = " Waiting\r\nfor\tinput \u0007" + new string('x', 140)
        };

        var json = JsonSerializer.Serialize(workspace, AgentMuxJson.Options);

        Assert.NotNull(workspace.LatestNotificationPreview);
        Assert.StartsWith("Waiting for input", workspace.LatestNotificationPreview, StringComparison.Ordinal);
        Assert.EndsWith("...", workspace.LatestNotificationPreview, StringComparison.Ordinal);
        Assert.Equal(120, workspace.LatestNotificationPreview!.Length);
        Assert.Equal($"notify: {workspace.LatestNotificationPreview}", workspace.LatestNotificationLabel);
        Assert.DoesNotContain("latestNotification", json, StringComparison.Ordinal);
        Assert.DoesNotContain("latestNotificationPreview", json, StringComparison.Ordinal);
        Assert.DoesNotContain("latestNotificationLabel", json, StringComparison.Ordinal);
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

    [Fact]
    public void BrowserPaneUrlRoundTripsThroughJson()
    {
        var surface = SurfaceState.CreateDefault();
        var pane = surface.Root.Pane!;
        pane.Kind = PaneKind.Browser;
        pane.Url = "https://example.com";
        pane.Title = "example.com";

        var json = JsonSerializer.Serialize(surface, AgentMuxJson.Options);
        var loaded = JsonSerializer.Deserialize<SurfaceState>(json, AgentMuxJson.Options);

        Assert.NotNull(loaded);
        Assert.Equal(PaneKind.Browser, loaded.Root.Pane?.Kind);
        Assert.Equal("https://example.com", loaded.Root.Pane?.Url);
        Assert.Equal("example.com", loaded.Root.Pane?.Title);
    }
}
