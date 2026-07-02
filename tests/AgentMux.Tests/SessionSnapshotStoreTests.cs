using AgentMux.Core.Models;
using AgentMux.Core.Persistence;

namespace AgentMux.Tests;

public sealed class SessionSnapshotStoreTests
{
    [Fact]
    public async Task LoadReturnsNullWhenSnapshotIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agentmux-tests-{Guid.NewGuid():N}");
        var store = new SessionSnapshotStore(root);

        var loaded = await store.LoadAsync();

        Assert.Null(loaded);
        Directory.Delete(root, recursive: true);
    }

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
                    Ports = [3000, 5173],
                    Surfaces = [surface]
                }
            ]
        };

        await store.SaveAsync(snapshot);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Single(loaded.Workspaces);
        Assert.Equal("API", loaded.Workspaces[0].Title);
        Assert.Equal(new[] { 3000, 5173 }, loaded.Workspaces[0].Ports);
        Assert.Equal(paneId, loaded.Workspaces[0].Surfaces[0].ActivePaneId);
        Assert.Equal(paneId, loaded.Workspaces[0].Surfaces[0].Root.Pane?.Id);
        Assert.Equal(PaneKind.Browser, loaded.Workspaces[0].Surfaces[0].Root.Pane?.Kind);
        Assert.Equal("https://example.com", loaded.Workspaces[0].Surfaces[0].Root.Pane?.Url);
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task SaveAndLoadRoundTripsMultipleSurfaces()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agentmux-tests-{Guid.NewGuid():N}");
        var store = new SessionSnapshotStore(root);
        var terminalSurface = SurfaceState.CreateDefault();
        var terminalPaneId = terminalSurface.Root.Pane!.Id;
        terminalSurface.Title = "Terminal A";
        terminalSurface.ActivePaneId = terminalPaneId;
        terminalSurface.Root.Pane.LastScreenText = "surface-one";

        var browserSurface = SurfaceState.CreateDefault();
        var browserPaneId = browserSurface.Root.Pane!.Id;
        browserSurface.Title = "Browser B";
        browserSurface.ActivePaneId = browserPaneId;
        browserSurface.Root.Pane.Kind = PaneKind.Browser;
        browserSurface.Root.Pane.Url = "https://example.test/surface-b";
        browserSurface.Root.Pane.Title = "surface-b";

        var snapshot = new SessionSnapshot
        {
            ActiveWorkspaceIndex = 0,
            Workspaces =
            [
                new WorkspaceState
                {
                    Title = "Multi",
                    ActiveSurfaceIndex = 1,
                    Surfaces = [terminalSurface, browserSurface]
                }
            ]
        };

        await store.SaveAsync(snapshot);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        var workspace = Assert.Single(loaded.Workspaces);
        Assert.Equal(1, workspace.ActiveSurfaceIndex);
        Assert.Equal(2, workspace.Surfaces.Count);
        Assert.Equal("Terminal A", workspace.Surfaces[0].Title);
        Assert.Equal(terminalPaneId, workspace.Surfaces[0].ActivePaneId);
        Assert.Equal("surface-one", workspace.Surfaces[0].Root.Pane?.LastScreenText);
        Assert.Equal("Browser B", workspace.Surfaces[1].Title);
        Assert.Equal(browserPaneId, workspace.Surfaces[1].ActivePaneId);
        Assert.Equal(PaneKind.Browser, workspace.Surfaces[1].Root.Pane?.Kind);
        Assert.Equal("https://example.test/surface-b", workspace.Surfaces[1].Root.Pane?.Url);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task LoadReturnsNullForCorruptSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agentmux-tests-{Guid.NewGuid():N}");
        var store = new SessionSnapshotStore(root);
        await File.WriteAllTextAsync(store.FilePath, "{ not-json");

        var loaded = await store.LoadAsync();

        Assert.Null(loaded);
        Directory.Delete(root, recursive: true);
    }
}
