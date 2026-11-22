using System.IO;
using System.Text.RegularExpressions;
using DesignatedDriver.Core;
using DesignatedDriver.Models;

namespace DesignatedDriver.Services;

/// <summary>The gh commands the set-up runs. Each names the repository, because it is not current until the last step.</summary>
public sealed partial class GitHubService
{
    private Task<CommandResult> MustInFolderAsync(string? folder, CancellationToken ct, params string[] args)
        => MustInFolderAsync(folder, null, ct, args);

    private async Task<CommandResult> MustInFolderAsync(string? folder, string? standardInput, CancellationToken ct, params string[] args)
    {
        var r = await runner.RunAsync("gh", args, folder, null, ct, standardInput).ConfigureAwait(false);
        return r.Succeeded ? r : throw new CommandFailedException(r);
    }

    // Repository

    /// <summary>gh repo create for a new repository on GitHub. The clone is made by git afterwards, over the chosen protocol.</summary>
    public Task CreateAsync(string ownerAndName, bool isPrivate, string description, CancellationToken ct)
    {
        var args = new List<string> { "repo", "create", ownerAndName, isPrivate ? "--private" : "--public" };
        if (description.Length > 0) args.AddRange(["--description", description]);
        return MustInFolderAsync(null, ct, [.. args]);
    }

    /// <summary>gh's own git protocol for github.com (https or ssh), the default for the tool's setting; empty when unknown.</summary>
    public async Task<string> GitProtocolAsync(CancellationToken ct)
    {
        var r = await RunAsync(ct, "config", "get", "-h", "github.com", "git_protocol").ConfigureAwait(false);
        return r.Succeeded ? r.StandardOutput.Trim().ToLowerInvariant() : "";
    }

    /// <summary>gh repo create from an existing local repository, adding origin. The push is git's, over the chosen protocol.</summary>
    public Task CreateFromFolderAsync(string ownerAndName, bool isPrivate, string description, string folder, CancellationToken ct)
    {
        var args = new List<string> { "repo", "create", ownerAndName, isPrivate ? "--private" : "--public", "--source", ".", "--remote", "origin" };
        if (description.Length > 0) args.AddRange(["--description", description]);
        return MustInFolderAsync(folder, ct, [.. args]);
    }

    /// <summary>gh repo edit with the convention's settings (section 3.4 of the design, without the ruleset).</summary>
    public Task EditRepositoryAsync(string ownerAndName, string description, bool discussions, IReadOnlyList<string> topics, CancellationToken ct)
    {
        var args = new List<string>
        {
            "repo", "edit", ownerAndName,
            "--enable-squash-merge", "--enable-merge-commit=false", "--enable-rebase-merge=false",
            "--squash-merge-commit-message", "pr-title",
            "--delete-branch-on-merge", "--allow-update-branch",
            "--enable-issues", "--enable-projects", "--enable-wiki=false",
            discussions ? "--enable-discussions" : "--enable-discussions=false",
        };
        if (description.Length > 0) args.AddRange(["--description", description]);
        foreach (var t in topics) args.AddRange(["--add-topic", t]);
        return MustInFolderAsync(null, ct, [.. args]);
    }

    public async Task<bool?> AutomatedSecurityFixesEnabledAsync(string ownerAndName, CancellationToken ct)
    {
        var r = await ApiAsync(ct, $"repos/{ownerAndName}/automated-security-fixes", "--jq", ".enabled").ConfigureAwait(false);
        if (!r.Ok) return r.Status == ApiStatus.NotFound ? false : null;
        return r.Output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>gh api -X PUT on a path with no body, for the switches that are turned on by PUT.</summary>
    public async Task PutAsync(string path, CancellationToken ct)
    {
        var r = await ApiAsync(ct, "-X", "PUT", path).ConfigureAwait(false);
        if (!r.Ok) throw new CommandFailedException(new CommandResult("gh", ["api", "-X", "PUT", path], 1, r.Output, r.Message, TimeSpan.Zero));
    }

    // Labels

    public Task CreateLabelAsync(string ownerAndName, ConventionLabel label, CancellationToken ct)
        => MustInFolderAsync(null, ct, "label", "create", label.Name, "--color", label.Color, "--description", label.Description, "--force", "-R", ownerAndName);

    /// <summary>Renames a label to the convention's, with its colour and description; issues keep it under the new name.</summary>
    public Task RenameLabelAsync(string ownerAndName, string from, ConventionLabel label, CancellationToken ct)
        => MustInFolderAsync(null, ct, "label", "edit", from, "--name", label.Name, "--color", label.Color, "--description", label.Description, "-R", ownerAndName);

    // Board

    /// <summary>Creates a user-owned project and returns it, owner login included.</summary>
    public async Task<GhProject> CreateProjectAsync(string title, CancellationToken ct)
    {
        var r = await MustInFolderAsync(null, ct, "project", "create", "--owner", "@me", "--title", title, "--format", "json").ConfigureAwait(false);
        return GhJson.Parse<GhProject>(r.StandardOutput);
    }

    public Task LinkProjectAsync(BoardReference board, string ownerAndName, CancellationToken ct)
        => MustInFolderAsync(null, ct, "project", "link", board.Number.ToString(), "--owner", board.Owner, "-R", ownerAndName);

    /// <summary>Adds an issue or pull request to a board by URL and returns the item id.</summary>
    public async Task<string> AddToBoardAsync(BoardReference board, string url, CancellationToken ct)
    {
        var r = await MustInFolderAsync(null, ct, "project", "item-add", board.Number.ToString(), "--owner", board.Owner, "--url", url, "--format", "json").ConfigureAwait(false);
        return GhJson.Parse<GhProjectItem>(r.StandardOutput).Id;
    }

    /// <summary>Sets an item's Status by column name, looking the ids up at run time. False when the board has no such column.</summary>
    public async Task<bool> SetItemStatusAsync(BoardReference board, string itemId, string columnName, CancellationToken ct)
    {
        var project = await ProjectAsync(board, ct).ConfigureAwait(false);
        var fields = await ProjectFieldsAsync(board, ct).ConfigureAwait(false);
        var status = fields.Fields.FirstOrDefault(f => f.Name.Equals("Status", StringComparison.OrdinalIgnoreCase));
        var option = status?.Options.FirstOrDefault(o => o.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
        if (status is null || option is null) return false;
        await MustInFolderAsync(null, ct, "project", "item-edit", "--project-id", project.Id, "--id", itemId,
            "--field-id", status.Id, "--single-select-option-id", option.Id).ConfigureAwait(false);
        return true;
    }

    // Secrets

    public async Task<List<string>> SecretNamesAsync(string ownerAndName, CancellationToken ct)
    {
        var r = await RunAsync(ct, "secret", "list", "-R", ownerAndName, "--json", "name").ConfigureAwait(false);
        return r.Succeeded ? GhJson.ParseList<GhSecret>(r.StandardOutput).Select(s => s.Name).ToList() : [];
    }

    /// <summary>Sets a repository secret with the value on standard input, so it is never on the command line or in the activity panel.</summary>
    public Task SetSecretAsync(string ownerAndName, string name, string value, CancellationToken ct)
        => MustInFolderAsync(null, value, ct, "secret", "set", name, "-R", ownerAndName);

    // Issues and pull requests by repository

    /// <summary>The open issue with exactly this title, or null.</summary>
    public async Task<GhIssue?> FindOpenIssueAsync(string ownerAndName, string title, CancellationToken ct)
    {
        var r = await MustInFolderAsync(null, ct, "issue", "list", "-R", ownerAndName, "--state", "open", "--search", $"\"{title}\" in:title", "--json", "number,title,url").ConfigureAwait(false);
        return GhJson.ParseList<GhIssue>(r.StandardOutput).FirstOrDefault(i => i.Title.Equals(title, StringComparison.Ordinal));
    }

    public async Task<(int Number, string Url)> CreateIssueAsync(string ownerAndName, string title, string body, string label, CancellationToken ct)
    {
        var file = Path.Combine(Path.GetTempPath(), $"designated-driver-issue-{Guid.NewGuid():N}.md");
        try
        {
            await File.WriteAllTextAsync(file, body, new System.Text.UTF8Encoding(false), ct).ConfigureAwait(false);
            var r = await MustInFolderAsync(null, ct, "issue", "create", "-R", ownerAndName, "--title", title, "--label", label, "--body-file", file).ConfigureAwait(false);
            var url = IssueUrl().Match(r.StandardOutput + "\n" + r.StandardError);
            return url.Success ? (int.Parse(url.Groups[1].Value), url.Value) : (0, r.StandardOutput.Trim());
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    public async Task<GhPullRequest?> PullRequestForBranchAsync(string ownerAndName, string branch, CancellationToken ct)
    {
        var r = await RunAsync(ct, "pr", "view", branch, "-R", ownerAndName, "--json", "number,title,url,state,headRefName").ConfigureAwait(false);
        if (!r.Succeeded) return null;
        var pr = GhJson.Parse<GhPullRequest>(r.StandardOutput);
        return pr.State.Equals("OPEN", StringComparison.OrdinalIgnoreCase) ? pr : null;
    }

    public async Task<(int Number, string Url)> CreatePullRequestAsync(string ownerAndName, string baseBranch, string headBranch, string title, string body, CancellationToken ct)
    {
        var file = Path.Combine(Path.GetTempPath(), $"designated-driver-pr-{Guid.NewGuid():N}.md");
        try
        {
            await File.WriteAllTextAsync(file, body, new System.Text.UTF8Encoding(false), ct).ConfigureAwait(false);
            var r = await MustInFolderAsync(null, ct, "pr", "create", "-R", ownerAndName, "--base", baseBranch, "--head", headBranch, "--title", title, "--body-file", file).ConfigureAwait(false);
            var url = PullRequestUrl().Match(r.StandardOutput + "\n" + r.StandardError);
            return url.Success ? (int.Parse(url.Groups[1].Value), url.Value) : (0, r.StandardOutput.Trim());
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    [GeneratedRegex(@"https://github\.com/[^/\s]+/[^/\s]+/issues/(\d+)")]
    private static partial Regex IssueUrl();
}

/// <summary>gh secret list --json name</summary>
public sealed class GhSecret
{
    public string Name { get; set; } = "";
}
