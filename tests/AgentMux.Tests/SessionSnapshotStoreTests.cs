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
        var snapshot = new SessionSnapshot
        {
            Workspaces =
            [
                new WorkspaceState
                {
                    Title = "API"
                }
            ]
        };

        await store.SaveAsync(snapshot);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Single(loaded.Workspaces);
        Assert.Equal("API", loaded.Workspaces[0].Title);

        Directory.Delete(root, recursive: true);
    }
}
