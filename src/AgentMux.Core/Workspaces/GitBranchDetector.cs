namespace AgentMux.Core.Workspaces;

public static class GitBranchDetector
{
    public static string? DetectCurrentBranch(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return null;
        }

        try
        {
            var current = new DirectoryInfo(workingDirectory);
            while (current is not null)
            {
                var marker = Path.Combine(current.FullName, ".git");
                var gitDirectory = ResolveGitDirectory(marker, current.FullName);
                if (gitDirectory is not null)
                {
                    return ReadBranchName(Path.Combine(gitDirectory, "HEAD"));
                }

                current = current.Parent;
            }
        }
        catch (Exception ex) when (ex is ArgumentException
            or IOException
            or NotSupportedException
            or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static string? ResolveGitDirectory(string markerPath, string repositoryDirectory)
    {
        if (Directory.Exists(markerPath))
        {
            return markerPath;
        }

        if (!File.Exists(markerPath))
        {
            return null;
        }

        var line = File.ReadLines(markerPath).FirstOrDefault();
        if (line is null || !line.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = line["gitdir:".Length..].Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, repositoryDirectory);
    }

    private static string? ReadBranchName(string headPath)
    {
        if (!File.Exists(headPath))
        {
            return null;
        }

        var head = File.ReadLines(headPath).FirstOrDefault()?.Trim();
        const string branchPrefix = "ref: refs/heads/";
        if (string.IsNullOrWhiteSpace(head) || !head.StartsWith(branchPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var branch = head[branchPrefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(branch) ? null : branch;
    }
}
