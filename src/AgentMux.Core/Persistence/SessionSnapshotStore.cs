using System.Text.Json;
using AgentMux.Core.Ipc;
using AgentMux.Core.Models;

namespace AgentMux.Core.Persistence;

public sealed class SessionSnapshotStore
{
    private readonly string _filePath;

    public SessionSnapshotStore(string? rootDirectory = null)
    {
        var baseDirectory = rootDirectory;
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AgentMux");
        }

        Directory.CreateDirectory(baseDirectory);
        _filePath = Path.Combine(baseDirectory, "session.json");
    }

    public string FilePath => _filePath;

    public async Task SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        snapshot.SavedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(snapshot, AgentMuxJson.Options);
        await File.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SessionSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<SessionSnapshot>(json, AgentMuxJson.Options);
    }
}
