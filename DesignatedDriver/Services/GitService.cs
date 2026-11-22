using System.IO;
using DesignatedDriver.Models;

namespace DesignatedDriver.Services;

/// <summary>Thrown when a git or gh command fails; the message is what the command said.</summary>
public sealed class CommandFailedException(CommandResult result) : Exception(result.Message)
{
    public CommandResult Result { get; } = result;
}

/// <summary>The git commands behind the workflow screens and the scan, run in the repository folder.</summary>
public sealed partial class GitService(ICommandRunner runner, Func<string> repositoryFolder)
{
    private string Folder => repositoryFolder();

    public Task<CommandResult> RunAsync(CancellationToken ct, params string[] args)
        => runner.RunAsync("git", args, Folder, null, ct);

    /// <summary>The same command in another folder, for the scan of a clone that isn't the current repository.</summary>
    public Task<CommandResult> RunInAsync(string folder, CancellationToken ct, params string[] args)
        => runner.RunAsync("git", args, folder, null, ct);

    private async Task<CommandResult> MustAsync(CancellationToken ct, params string[] args)
    {
        var r = await RunAsync(ct, args).ConfigureAwait(false);
        return r.Succeeded ? r : throw new CommandFailedException(r);
    }

    public async Task<string> VersionAsync(CancellationToken ct)
    {
        var r = await runner.RunAsync("git", ["--version"], null, null, ct).ConfigureAwait(false);
        return r.Succeeded ? r.StandardOutput.Trim() : "";
    }

    /// <summary>The repository root, or null when the folder isn't inside a Git repository.</summary>
    public async Task<string?> RepositoryRootAsync(string folder, CancellationToken ct)
    {
        if (!Directory.Exists(folder)) return null;
        var r = await runner.RunAsync("git", ["rev-parse", "--show-toplevel"], folder, null, ct).ConfigureAwait(false);
        return r.Succeeded ? r.StandardOutput.Trim().Replace('/', '\\') : null;
    }

    public async Task<GitStatus> StatusAsync(CancellationToken ct)
    {
        var r = await MustAsync(ct, "status", "--porcelain=v2", "--branch").ConfigureAwait(false);
        return GitStatus.Parse(r.StandardOutput);
    }

    public Task FetchAsync(CancellationToken ct) => MustAsync(ct, "fetch");

    public Task PruneAsync(CancellationToken ct) => MustAsync(ct, "fetch", "--prune");

    /// <summary>Commits on HEAD that origin/main lacks, and the other way round. Call FetchAsync first.</summary>
    public async Task<AheadBehind> AheadBehindAsync(string defaultBranch, CancellationToken ct)
    {
        var r = await RunAsync(ct, "rev-list", "--left-right", "--count", $"HEAD...origin/{defaultBranch}").ConfigureAwait(false);
        return r.Succeeded ? AheadBehind.Parse(r.StandardOutput) : new AheadBehind(0, 0);
    }

    public Task SwitchAsync(string branch, CancellationToken ct) => MustAsync(ct, "switch", branch);

    public Task PullAsync(CancellationToken ct) => MustAsync(ct, "pull");

    public Task CreateBranchAsync(string branch, CancellationToken ct) => MustAsync(ct, "switch", "-c", branch);

    public async Task<bool> IsValidBranchNameAsync(string branch, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(branch)) return false;
        var r = await RunAsync(ct, "check-ref-format", "--branch", branch).ConfigureAwait(false);
        return r.Succeeded;
    }

    public async Task<bool> LocalBranchExistsAsync(string branch, CancellationToken ct)
    {
        var r = await RunAsync(ct, "rev-parse", "--verify", "--quiet", $"refs/heads/{branch}").ConfigureAwait(false);
        return r.Succeeded;
    }

    public Task AddAllAsync(CancellationToken ct) => MustAsync(ct, "add", "-A");

    /// <summary>Commits with the message passed through a file, never on the command line.</summary>
    public async Task CommitAsync(string message, CancellationToken ct)
    {
        var file = Path.Combine(Path.GetTempPath(), $"designated-driver-commit-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(file, message, new System.Text.UTF8Encoding(false), ct).ConfigureAwait(false);
            await MustAsync(ct, "commit", "-F", file).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    public Task PushAsync(string branch, bool setUpstream, CancellationToken ct)
        => setUpstream ? MustAsync(ct, "push", "-u", "origin", branch) : MustAsync(ct, "push");

    public async Task<List<string>> LocalBranchesAsync(CancellationToken ct)
    {
        var r = await MustAsync(ct, "branch", "--format=%(refname:short)").ConfigureAwait(false);
        return r.StandardOutput.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
    }

    /// <summary>git branch -D: squash merging makes Git think the branch wasn't merged, so -d would refuse.</summary>
    public Task DeleteBranchAsync(string branch, CancellationToken ct) => MustAsync(ct, "branch", "-D", branch);

    public async Task<string> DiffAsync(string? path, CancellationToken ct)
    {
        var args = new List<string> { "diff", "HEAD", "--no-color" };
        if (path is not null) { args.Add("--"); args.Add(path); }
        var r = await RunAsync(ct, args.ToArray()).ConfigureAwait(false);
        return r.StandardOutput;
    }

    public Task<string> OriginUrlAsync(CancellationToken ct) => OriginUrlInAsync(Folder, ct);

    public async Task<string> OriginUrlInAsync(string folder, CancellationToken ct)
    {
        var r = await RunInAsync(folder, ct, "remote", "get-url", "origin").ConfigureAwait(false);
        return r.Succeeded ? r.StandardOutput.Trim() : "";
    }

    /// <summary>Subjects of the commits on this branch that origin/main lacks, oldest first.</summary>
    public async Task<List<string>> CommitSubjectsSinceAsync(string defaultBranch, CancellationToken ct)
    {
        var r = await RunAsync(ct, "log", "--reverse", "--format=%s", $"origin/{defaultBranch}..HEAD").ConfigureAwait(false);
        return r.Succeeded ? r.StandardOutput.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList() : [];
    }

    // Scan

    /// <summary>False for a repository with no commits yet.</summary>
    public async Task<bool> HasCommitsInAsync(string folder, CancellationToken ct)
    {
        var r = await RunInAsync(folder, ct, "rev-parse", "--verify", "--quiet", "HEAD").ConfigureAwait(false);
        return r.Succeeded;
    }

    public async Task<GitStatus> StatusInAsync(string folder, CancellationToken ct)
    {
        var r = await RunInAsync(folder, ct, "status", "--porcelain=v2", "--branch").ConfigureAwait(false);
        return r.Succeeded ? GitStatus.Parse(r.StandardOutput) : new GitStatus();
    }

    /// <summary>core.hooksPath as configured for this clone, or an empty string.</summary>
    public async Task<string> HooksPathInAsync(string folder, CancellationToken ct)
    {
        var r = await RunInAsync(folder, ct, "config", "--get", "core.hooksPath").ConfigureAwait(false);
        return r.Succeeded ? r.StandardOutput.Trim() : "";
    }

    /// <summary>Tracked files plus untracked ones that aren't ignored, as forward-slash paths from the root.</summary>
    public async Task<List<string>> ListFilesInAsync(string folder, CancellationToken ct)
    {
        var r = await RunInAsync(folder, ct, "ls-files", "--cached", "--others", "--exclude-standard").ConfigureAwait(false);
        return r.Succeeded ? r.StandardOutput.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList() : [];
    }

    public async Task<bool> IsMergeInProgressInAsync(string folder, CancellationToken ct)
    {
        var r = await RunInAsync(folder, ct, "rev-parse", "--quiet", "--verify", "MERGE_HEAD").ConfigureAwait(false);
        return r.Succeeded;
    }

    // Fix a problem

    /// <summary>Subject of the last commit on this branch, or an empty string when there is none.</summary>
    public async Task<string> LastCommitSubjectAsync(CancellationToken ct)
    {
        var r = await RunAsync(ct, "log", "-1", "--format=%s").ConfigureAwait(false);
        return r.Succeeded ? r.StandardOutput.Trim() : "";
    }

    /// <summary>Throws the working-tree and staged changes to these tracked files away. Can't be undone.</summary>
    public async Task RestoreAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count == 0) return;
        var args = new List<string> { "restore", "--staged", "--worktree", "--" };
        args.AddRange(paths);
        await MustAsync(ct, args.ToArray()).ConfigureAwait(false);
    }

    /// <summary>Deletes untracked files, which git restore doesn't touch. Can't be undone.</summary>
    public async Task RemoveUntrackedAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count == 0) return;
        var args = new List<string> { "clean", "-f", "--" };
        args.AddRange(paths);
        await MustAsync(ct, args.ToArray()).ConfigureAwait(false);
    }

    /// <summary>Undoes the last commit and keeps its changes in the working tree.</summary>
    public Task UndoLastCommitAsync(CancellationToken ct) => MustAsync(ct, "reset", "--soft", "HEAD~1");

    /// <summary>Brings origin/main into the branch locally, for a branch that has no pull request yet.</summary>
    public Task MergeFromOriginAsync(string defaultBranch, CancellationToken ct) => MustAsync(ct, "merge", $"origin/{defaultBranch}");

    public Task<bool> IsMergeInProgressAsync(CancellationToken ct) => IsMergeInProgressInAsync(Folder, ct);

    public Task AbortMergeAsync(CancellationToken ct) => MustAsync(ct, "merge", "--abort");

    /// <summary>Finishes a merge whose conflicts are resolved, with the message Git prepared.</summary>
    public Task CommitMergeAsync(CancellationToken ct) => MustAsync(ct, "commit", "--no-edit");
}
