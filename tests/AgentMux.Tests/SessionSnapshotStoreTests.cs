using AgentMux.Core.Models;
using AgentMux.Core.Persistence;

namespace AgentMux.Tests;

public sealed class SessionSnapshotStoreTests
{
    [Fact]
    public async Task SaveAndLoadRoundTripsSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agentmux-tests-{Guid.NewGuid():N}");
        var store = new SessionSnapshotStore(root);
        var surface = SurfaceState.CreateDefault();
        var paneId = surface.Root.Pane!.Id;
        surface.Root.Pane.Kind = PaneKind.Browser;
        surface.Root.Pane.Url = "https://example.com";
        surface.Root.Pane.Title = "example.com";
        surface.ActivePaneId = paneId;
        var snapshot = new SessionSnapshot
        {
            Workspaces =
            [
                new WorkspaceState
                {
                    Title = "API",
                    Surfaces = [surface]
                }
            ]
        };

        await store.SaveAsync(snapshot);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Single(loaded.Workspaces);
        Assert.Equal("API", loaded.Workspaces[0].Title);
        Assert.Equal(paneId, loaded.Workspaces[0].Surfaces[0].ActivePaneId);
        Assert.Equal(paneId, loaded.Workspaces[0].Surfaces[0].Root.Pane?.Id);
        Assert.Equal(PaneKind.Browser, loaded.Workspaces[0].Surfaces[0].Root.Pane?.Kind);
        Assert.Equal("https://example.com", loaded.Workspaces[0].Surfaces[0].Root.Pane?.Url);

        Directory.Delete(root, recursive: true);
    }
}
