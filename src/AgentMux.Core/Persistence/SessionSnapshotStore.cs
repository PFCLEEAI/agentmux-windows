using System.Text.Json;
using AgentMux.Core.Ipc;
using AgentMux.Core.Models;

namespace AgentMux.Core.Persistence;

public sealed class SessionSnapshotStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

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
        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            snapshot.SavedAt = DateTimeOffset.UtcNow;
            var json = JsonSerializer.Serialize(snapshot, AgentMuxJson.Options);
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);

            if (File.Exists(_filePath))
            {
                File.Copy(tempPath, _filePath, overwrite: true);
                File.Delete(tempPath);
            }
            else
            {
                File.Move(tempPath, _filePath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            _saveLock.Release();
        }
    }

    public async Task<SessionSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<SessionSnapshot>(json, AgentMuxJson.Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
