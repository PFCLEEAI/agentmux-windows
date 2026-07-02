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

    [Fact]
    public void WorkspaceLatestLogPreviewIsDisplayOnlyAndCapped()
    {
        var workspace = new WorkspaceState
        {
            LatestLog = " [warn]\r\nserver:\twaiting \u0007" + new string('x', 140)
        };

        var json = JsonSerializer.Serialize(workspace, AgentMuxJson.Options);

        Assert.NotNull(workspace.LatestLogPreview);
        Assert.StartsWith("[warn] server: waiting", workspace.LatestLogPreview, StringComparison.Ordinal);
        Assert.EndsWith("...", workspace.LatestLogPreview, StringComparison.Ordinal);
        Assert.Equal(120, workspace.LatestLogPreview!.Length);
        Assert.Equal($"log: {workspace.LatestLogPreview}", workspace.LatestLogLabel);
        Assert.DoesNotContain("latestLog", json, StringComparison.Ordinal);
        Assert.DoesNotContain("latestLogPreview", json, StringComparison.Ordinal);
        Assert.DoesNotContain("latestLogLabel", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceLatestStatusPreviewIsDisplayOnlyAndCapped()
    {
        var workspace = new WorkspaceState
        {
            LatestStatus = " build:\r\nrunning\tchecks \u0007" + new string('x', 140)
        };

        var json = JsonSerializer.Serialize(workspace, AgentMuxJson.Options);

        Assert.NotNull(workspace.LatestStatusPreview);
        Assert.StartsWith("build: running checks", workspace.LatestStatusPreview, StringComparison.Ordinal);
        Assert.EndsWith("...", workspace.LatestStatusPreview, StringComparison.Ordinal);
        Assert.Equal(120, workspace.LatestStatusPreview!.Length);
        Assert.Equal($"status: {workspace.LatestStatusPreview}", workspace.LatestStatusLabel);
        Assert.DoesNotContain("latestStatus", json, StringComparison.Ordinal);
        Assert.DoesNotContain("latestStatusPreview", json, StringComparison.Ordinal);
        Assert.DoesNotContain("latestStatusLabel", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspacePortsRoundTripAndLabelIsDisplayOnly()
    {
        var workspace = new WorkspaceState
        {
            Ports = [3000, 5173]
        };

        var json = JsonSerializer.Serialize(workspace, AgentMuxJson.Options);
        var loaded = JsonSerializer.Deserialize<WorkspaceState>(json, AgentMuxJson.Options);

        Assert.Equal("ports: 3000, 5173", workspace.PortsLabel);
        Assert.Contains("\"ports\":[3000,5173]", json, StringComparison.Ordinal);
        Assert.DoesNotContain("portsLabel", json, StringComparison.Ordinal);
        Assert.NotNull(loaded);
        Assert.Equal(new[] { 3000, 5173 }, loaded.Ports);
    }

    [Fact]
    public void WorkspacePullRequestRoundTripsAndLabelIsDisplayOnly()
    {
        var workspace = new WorkspaceState
        {
            PullRequest = new WorkspacePullRequest
            {
                Number = 123,
                Status = "open",
                Url = "https://github.com/example/repo/pull/123"
            }
        };

        var json = JsonSerializer.Serialize(workspace, AgentMuxJson.Options);
        var loaded = JsonSerializer.Deserialize<WorkspaceState>(json, AgentMuxJson.Options);

        Assert.Equal("pr: #123 open", workspace.PullRequestLabel);
        Assert.Contains("\"pullRequest\":{\"number\":123,\"status\":\"open\",\"url\":\"https://github.com/example/repo/pull/123\"}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("pullRequestLabel", json, StringComparison.Ordinal);
        Assert.NotNull(loaded?.PullRequest);
        Assert.Equal(123, loaded.PullRequest.Number);
        Assert.Equal("open", loaded.PullRequest.Status);
        Assert.Equal("https://github.com/example/repo/pull/123", loaded.PullRequest.Url);
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
