using AgentMux.Core.Workspaces;

namespace AgentMux.Tests;

public sealed class GitBranchDetectorTests
{
    [Fact]
    public void DetectCurrentBranchReadsNearestGitHead()
    {
        using var root = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(root.Path, ".git"));
        File.WriteAllText(Path.Combine(root.Path, ".git", "HEAD"), "ref: refs/heads/main\n");
        var nested = Path.Combine(root.Path, "src", "app");
        Directory.CreateDirectory(nested);

        Assert.Equal("main", GitBranchDetector.DetectCurrentBranch(nested));
    }

    [Fact]
    public void DetectCurrentBranchPreservesSlashBranchNames()
    {
        using var root = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(root.Path, ".git"));
        File.WriteAllText(Path.Combine(root.Path, ".git", "HEAD"), "ref: refs/heads/feature/workspace-sidebar\n");

        Assert.Equal("feature/workspace-sidebar", GitBranchDetector.DetectCurrentBranch(root.Path));
    }

    [Fact]
    public void DetectCurrentBranchFollowsGitdirFile()
    {
        using var root = TempDirectory.Create();
        var actualGitDirectory = Path.Combine(root.Path, "actual-git");
        Directory.CreateDirectory(actualGitDirectory);
        File.WriteAllText(Path.Combine(root.Path, ".git"), "gitdir: actual-git\n");
        File.WriteAllText(Path.Combine(actualGitDirectory, "HEAD"), "ref: refs/heads/worktree-branch\n");

        Assert.Equal("worktree-branch", GitBranchDetector.DetectCurrentBranch(root.Path));
    }

    [Fact]
    public void DetectCurrentBranchReturnsNullForDetachedOrMissingHead()
    {
        using var detached = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(detached.Path, ".git"));
        File.WriteAllText(Path.Combine(detached.Path, ".git", "HEAD"), "7d9c7d8c0c45f0cb2a4d9d5e2c4b3a1901020304\n");

        using var missing = TempDirectory.Create();

        Assert.Null(GitBranchDetector.DetectCurrentBranch(detached.Path));
        Assert.Null(GitBranchDetector.DetectCurrentBranch(missing.Path));
        Assert.Null(GitBranchDetector.DetectCurrentBranch(Path.Combine(missing.Path, "not-created")));
        Assert.Null(GitBranchDetector.DetectCurrentBranch(null));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        private TempDirectory(string path)
        {
            Path = path;
        }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agentmux-git-branch", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
