using System.IO;

namespace DesignatedDriver.Services;

/// <summary>
/// The git commands the set-up runs. They all take the folder, because the repository is not in the tool's
/// settings until the last step.
/// </summary>
public sealed partial class GitService
{
    private async Task<CommandResult> MustInAsync(string folder, CancellationToken ct, params string[] args)
    {
        var r = await RunInAsync(folder, ct, args).ConfigureAwait(false);
        return r.Succeeded ? r : throw new CommandFailedException(r);
    }

    public Task InitAsync(string folder, string defaultBranch, CancellationToken ct) => MustInAsync(folder, ct, "init", "-b", defaultBranch);

    /// <summary>git clone of a URL into a folder, run from the folder's parent.</summary>
    public Task CloneAsync(string url, string folder, CancellationToken ct) => MustInAsync(Path.GetDirectoryName(folder) ?? folder, ct, "clone", url, folder);

    public Task AddRemoteAsync(string folder, string url, CancellationToken ct) => MustInAsync(folder, ct, "remote", "add", "origin", url);

    public Task SetRemoteUrlAsync(string folder, string url, CancellationToken ct) => MustInAsync(folder, ct, "remote", "set-url", "origin", url);

    /// <summary>True when the remote has the branch. Asks the remote, so it needs the network.</summary>
    public async Task<bool> RemoteBranchExistsAsync(string folder, string branch, CancellationToken ct)
    {
        var r = await RunInAsync(folder, ct, "ls-remote", "--exit-code", "--heads", "origin", branch).ConfigureAwait(false);
        return r.Succeeded;
    }

    /// <summary>Makes an unborn HEAD point at the default branch, for a clone of an empty repository whose local default differs.</summary>
    public Task SetUnbornBranchAsync(string folder, string defaultBranch, CancellationToken ct) => MustInAsync(folder, ct, "symbolic-ref", "HEAD", $"refs/heads/{defaultBranch}");

    public async Task<string> CurrentBranchInAsync(string folder, CancellationToken ct)
    {
        var r = await RunInAsync(folder, ct, "branch", "--show-current").ConfigureAwait(false);
        return r.Succeeded ? r.StandardOutput.Trim() : "";
    }

    public async Task<bool> LocalBranchExistsInAsync(string folder, string branch, CancellationToken ct)
    {
        var r = await RunInAsync(folder, ct, "rev-parse", "--verify", "--quiet", $"refs/heads/{branch}").ConfigureAwait(false);
        return r.Succeeded;
    }

    public Task SwitchInAsync(string folder, string branch, bool create, CancellationToken ct)
        => create ? MustInAsync(folder, ct, "switch", "-c", branch) : MustInAsync(folder, ct, "switch", branch);

    public Task AddPathsInAsync(string folder, IReadOnlyList<string> paths, CancellationToken ct)
        => paths.Count == 0 ? Task.CompletedTask : MustInAsync(folder, ct, ["add", "--", .. paths]);

    /// <summary>Removes tracked files from the working tree and the index, for the next commit.</summary>
    public Task RemovePathsInAsync(string folder, IReadOnlyList<string> paths, CancellationToken ct)
        => paths.Count == 0 ? Task.CompletedTask : MustInAsync(folder, ct, ["rm", "-q", "--", .. paths]);

    /// <summary>Marks files executable in the index, so the hooks run on macOS and Linux clones too.</summary>
    public Task MarkExecutableInAsync(string folder, IReadOnlyList<string> paths, CancellationToken ct)
        => paths.Count == 0 ? Task.CompletedTask : MustInAsync(folder, ct, ["update-index", "--chmod=+x", "--", .. paths]);

    public async Task<bool> HasStagedChangesInAsync(string folder, CancellationToken ct)
    {
        // Exit 1 means there are staged changes; 0 means none.
        var r = await RunInAsync(folder, ct, "diff", "--cached", "--quiet").ConfigureAwait(false);
        return r.ExitCode == 1;
    }

    public async Task CommitInAsync(string folder, string message, CancellationToken ct)
    {
        var file = Path.Combine(Path.GetTempPath(), $"designated-driver-commit-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(file, message, new System.Text.UTF8Encoding(false), ct).ConfigureAwait(false);
            await MustInAsync(folder, ct, "commit", "-F", file).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    public Task PushInAsync(string folder, string branch, CancellationToken ct) => MustInAsync(folder, ct, "push", "-u", "origin", branch);

    public Task FetchInAsync(string folder, CancellationToken ct) => MustInAsync(folder, ct, "fetch", "--prune");

    public Task SetHooksPathInAsync(string folder, string hooksPath, CancellationToken ct) => MustInAsync(folder, ct, "config", "core.hooksPath", hooksPath);

    /// <summary>True when a path exists in a ref, for example origin/main.</summary>
    public async Task<bool> PathInRefAsync(string folder, string reference, string path, CancellationToken ct)
    {
        var r = await RunInAsync(folder, ct, "cat-file", "-e", $"{reference}:{path}").ConfigureAwait(false);
        return r.Succeeded;
    }

    /// <summary>True when the given paths are the same in the working tree and in a ref, for example origin/main.</summary>
    public async Task<bool> PathsMatchRefAsync(string folder, string reference, IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count == 0) return true;
        var r = await RunInAsync(folder, ct, ["diff", "--quiet", reference, "--", .. paths]).ConfigureAwait(false);
        return r.Succeeded;
    }

    /// <summary>Remote-tracking branches whose short name matches a glob, for example chore/*-designated-driver-v1.</summary>
    public async Task<List<string>> RemoteBranchesMatchingAsync(string folder, string pattern, CancellationToken ct)
    {
        var r = await RunInAsync(folder, ct, "for-each-ref", "--format=%(refname:short)", $"refs/remotes/origin/{pattern}").ConfigureAwait(false);
        if (!r.Succeeded) return [];
        return r.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.StartsWith("origin/", StringComparison.Ordinal)).Select(l => l["origin/".Length..]).ToList();
    }
}
