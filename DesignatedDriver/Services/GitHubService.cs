using System.IO;
using System.Text.RegularExpressions;
using DesignatedDriver.Core;
using DesignatedDriver.Models;

namespace DesignatedDriver.Services;

/// <summary>What a gh api call gave back, sorted by GhApi.Classify.</summary>
public sealed record ApiResult(ApiStatus Status, string Output, string Message)
{
    public bool Ok => Status == ApiStatus.Ok;
}

/// <summary>
/// The gh commands behind the workflow screens and the scan. Repository commands name the repository with -R,
/// so they work from the clone, from another folder, and for a repository that has no clone at all.
/// </summary>
public sealed partial class GitHubService(ICommandRunner runner, Func<RepositorySettings> repository, Func<BoardReference?> board)
{
    private RepositorySettings Repo => repository();
    private string? Folder => Repo.HasFolder ? Repo.Folder : null;

    public Task<CommandResult> RunAsync(CancellationToken ct, params string[] args)
        => runner.RunAsync("gh", args, Folder, null, ct);

    private async Task<CommandResult> MustAsync(CancellationToken ct, params string[] args)
    {
        var r = await RunAsync(ct, args).ConfigureAwait(false);
        return r.Succeeded ? r : throw new CommandFailedException(r);
    }

    /// <summary>The arguments with -R owner/name appended, when the repository is known.</summary>
    private static string[] InRepoOf(string ownerAndName, params string[] args)
        => ownerAndName.Length == 0 ? args : [.. args, "-R", ownerAndName];

    private string[] InRepo(params string[] args) => InRepoOf(Repo.OwnerAndName, args);

    private BoardReference Board => board() ?? throw new InvalidOperationException(Resources.Strings.Board_None);

    // Prerequisites

    public async Task<string> VersionAsync(CancellationToken ct)
    {
        var r = await runner.RunAsync("gh", ["--version"], null, null, ct).ConfigureAwait(false);
        return r.Succeeded ? r.StandardOutput.Split('\n')[0].Trim() : "";
    }

    public async Task<AuthStatus> AuthStatusAsync(CancellationToken ct)
    {
        // gh auth status writes to standard error in older versions and standard output in newer ones.
        var r = await runner.RunAsync("gh", ["auth", "status"], null, null, ct).ConfigureAwait(false);
        if (r.ExitCode == -1) return new AuthStatus();
        return AuthStatusParser.Parse(r.StandardOutput + "\n" + r.StandardError);
    }

    public async Task<string> LoginAsync(CancellationToken ct)
    {
        var r = await MustAsync(ct, "api", "user", "--jq", ".login").ConfigureAwait(false);
        return r.StandardOutput.Trim();
    }

    /// <summary>The account's plan name (free, pro, ...). Empty when the token lacks the user scope, which hides it.</summary>
    public async Task<string> PlanNameAsync(CancellationToken ct)
    {
        var r = await RunAsync(ct, "api", "user", "--jq", ".plan.name // \"\"").ConfigureAwait(false);
        return r.Succeeded ? r.StandardOutput.Trim() : "";
    }

    public Task<GhRepository> RepositoryAsync(CancellationToken ct) => RepositoryAsync(Repo.OwnerAndName, "viewerPermission,defaultBranchRef", ct);

    /// <summary>gh repo view --json with the given fields.</summary>
    public async Task<GhRepository> RepositoryAsync(string ownerAndName, string fields, CancellationToken ct)
    {
        var r = await MustAsync(ct, "repo", "view", ownerAndName, "--json", fields).ConfigureAwait(false);
        return GhJson.Parse<GhRepository>(r.StandardOutput);
    }

    // The REST API through gh api

    /// <summary>Runs gh api and sorts the outcome, without throwing: the scan wants to know about 403 and 404.</summary>
    public async Task<ApiResult> ApiAsync(CancellationToken ct, params string[] args)
    {
        var r = await RunAsync(ct, ["api", .. args]).ConfigureAwait(false);
        var status = r.ExitCode == -1 ? ApiStatus.Failed : GhApi.Classify(r.ExitCode, r.StandardError);
        return new ApiResult(status, r.StandardOutput, r.Message);
    }

    public async Task<GhRepositoryDetails?> RepositoryDetailsAsync(string ownerAndName, CancellationToken ct)
    {
        var r = await ApiAsync(ct, $"repos/{ownerAndName}").ConfigureAwait(false);
        return r.Ok ? GhJson.ParseRest<GhRepositoryDetails>(r.Output) : null;
    }

    /// <summary>The rules in effect on a branch. NotAvailable means rulesets need a paid plan or a public repository.</summary>
    public async Task<(ApiStatus Status, List<GhBranchRule> Rules)> BranchRulesAsync(string ownerAndName, string branch, CancellationToken ct)
    {
        var r = await ApiAsync(ct, $"repos/{ownerAndName}/rules/branches/{branch}").ConfigureAwait(false);
        return (r.Status, r.Ok ? GhJson.ParseRestList<GhBranchRule>(r.Output) : []);
    }

    /// <summary>The checks classic branch protection requires on a branch; empty when it has none or it can't be read.</summary>
    public async Task<List<string>> ClassicRequiredChecksAsync(string ownerAndName, string branch, CancellationToken ct)
    {
        var r = await ApiAsync(ct, $"repos/{ownerAndName}/branches/{branch}/protection/required_status_checks").ConfigureAwait(false);
        if (!r.Ok || r.Output.Trim().Length == 0) return [];
        try { return GhJson.ParseRest<GhClassicRequiredChecks>(r.Output).Names.ToList(); }
        catch (System.Text.Json.JsonException) { return []; }
    }

    public async Task<List<GhRuleset>> RulesetsAsync(string ownerAndName, CancellationToken ct)
    {
        var r = await ApiAsync(ct, $"repos/{ownerAndName}/rulesets").ConfigureAwait(false);
        return r.Ok ? GhJson.ParseRestList<GhRuleset>(r.Output) : [];
    }

    /// <summary>True when Dependabot alerts are on, false when off, null when the call failed.</summary>
    public async Task<bool?> VulnerabilityAlertsEnabledAsync(string ownerAndName, CancellationToken ct)
    {
        var r = await ApiAsync(ct, $"repos/{ownerAndName}/vulnerability-alerts").ConfigureAwait(false);
        return r.Status switch { ApiStatus.Ok => true, ApiStatus.NotFound => false, _ => null };
    }

    public async Task<List<GhWorkflow>> WorkflowsAsync(string ownerAndName, CancellationToken ct)
    {
        var r = await ApiAsync(ct, $"repos/{ownerAndName}/actions/workflows").ConfigureAwait(false);
        return r.Ok ? GhJson.ParseRest<GhWorkflowList>(r.Output).Workflows : [];
    }

    /// <summary>The repository's most recent releases, newest first; empty when there are none or they can't be read.</summary>
    public async Task<List<GhRelease>> ReleasesAsync(string ownerAndName, CancellationToken ct)
    {
        var r = await ApiAsync(ct, $"repos/{ownerAndName}/releases?per_page=30").ConfigureAwait(false);
        return r.Ok ? GhJson.ParseRestList<GhRelease>(r.Output) : [];
    }

    /// <summary>A file's text from the repository on GitHub's default branch, or null when it can't be read.</summary>
    public async Task<string?> FileTextAsync(string ownerAndName, string path, CancellationToken ct)
    {
        var r = await ApiAsync(ct, $"repos/{ownerAndName}/contents/{path}").ConfigureAwait(false);
        if (!r.Ok) return null;
        try { return GhJson.ParseRest<GhFileContent>(r.Output).Text; }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException) { return null; }
    }

    /// <summary>The entries of a folder in the repository on GitHub; empty when the folder doesn't exist.</summary>
    public async Task<List<GhContentEntry>> ContentsAsync(string ownerAndName, string path, CancellationToken ct)
    {
        var r = await ApiAsync(ct, path.Length == 0 ? $"repos/{ownerAndName}/contents" : $"repos/{ownerAndName}/contents/{path}").ConfigureAwait(false);
        if (!r.Ok || !r.Output.TrimStart().StartsWith('[')) return [];
        return GhJson.ParseRestList<GhContentEntry>(r.Output);
    }

    /// <summary>The text of one file in the repository on GitHub, or null when it doesn't exist.</summary>
    public async Task<string?> FileContentAsync(string ownerAndName, string path, CancellationToken ct)
    {
        var r = await ApiAsync(ct, $"repos/{ownerAndName}/contents/{path}", "-H", "Accept: application/vnd.github.raw+json").ConfigureAwait(false);
        return r.Ok ? r.Output : null;
    }

    /// <summary>
    /// The repository's issue forms on GitHub's default branch, where GitHub's own New issue page reads them, in
    /// file name order, each read as a New issue choice (issue 88). Empty when there are none; null when the
    /// folder or a form can't be read, so New issue falls back to its built-in choices and says why.
    /// </summary>
    public async Task<List<IssueChoice>?> IssueFormsAsync(string ownerAndName, CancellationToken ct)
    {
        var folder = await ApiAsync(ct, $"repos/{ownerAndName}/contents/.github/ISSUE_TEMPLATE").ConfigureAwait(false);
        if (folder.Status == ApiStatus.NotFound) return [];
        if (!folder.Ok || !folder.Output.TrimStart().StartsWith('[')) return null;
        var forms = new List<IssueChoice>();
        foreach (var entry in GhJson.ParseRestList<GhContentEntry>(folder.Output).Where(e => !e.IsDirectory && IssueForms.IsForm(e.Path)).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            var text = await FileContentAsync(ownerAndName, entry.Path, ct).ConfigureAwait(false);
            if (text is null) return null;
            if (IssueForms.Read(text, entry.Name) is { } form) forms.Add(form);
        }
        return forms;
    }

    /// <summary>User or Organization, for building the right project board URL.</summary>
    public async Task<string> OwnerTypeAsync(string owner, CancellationToken ct)
    {
        var r = await ApiAsync(ct, $"users/{owner}", "--jq", ".type").ConfigureAwait(false);
        return r.Ok ? r.Output.Trim() : "";
    }

    // Labels

    public async Task<List<GhLabel>> LabelsAsync(string ownerAndName, CancellationToken ct)
    {
        var r = await MustAsync(ct, InRepoOf(ownerAndName, "label", "list", "--json", "name,color,description", "--limit", "200")).ConfigureAwait(false);
        return GhJson.ParseList<GhLabel>(r.StandardOutput);
    }

    // Issues

    // Author and creation date for Start work's issue card (issue 89).
    private const string IssueFields = "number,title,labels,assignees,body,url,state,author,createdAt";

    public async Task<GhIssue> IssueAsync(int number, CancellationToken ct)
    {
        var r = await MustAsync(ct, InRepo("issue", "view", number.ToString(), "--json", IssueFields)).ConfigureAwait(false);
        return GhJson.Parse<GhIssue>(r.StandardOutput);
    }

    public async Task<List<GhIssue>> OpenIssuesAsync(CancellationToken ct)
    {
        var r = await MustAsync(ct, InRepo("issue", "list", "--state", "open", "--limit", "200", "--json", IssueFields)).ConfigureAwait(false);
        return GhJson.ParseList<GhIssue>(r.StandardOutput);
    }

    public async Task<List<GhIssue>> MyIssuesAsync(CancellationToken ct)
    {
        var r = await MustAsync(ct, InRepo("issue", "list", "--assignee", "@me", "--state", "open", "--limit", "100", "--json", IssueFields)).ConfigureAwait(false);
        return GhJson.ParseList<GhIssue>(r.StandardOutput);
    }

    public Task AssignToMeAsync(int number, CancellationToken ct) => MustAsync(ct, InRepo("issue", "edit", number.ToString(), "--add-assignee", "@me"));

    // Project board

    public bool HasBoard => board() is not null;

    public async Task<GhProjectItemList> ProjectItemsAsync(CancellationToken ct)
    {
        var b = Board;
        var r = await MustAsync(ct, "project", "item-list", b.Number.ToString(), "--owner", b.Owner, "--format", "json", "--limit", "200").ConfigureAwait(false);
        return GhJson.Parse<GhProjectItemList>(r.StandardOutput);
    }

    public Task<GhProject> ProjectAsync(CancellationToken ct) => ProjectAsync(Board, ct);

    public async Task<GhProject> ProjectAsync(BoardReference b, CancellationToken ct)
    {
        var r = await MustAsync(ct, "project", "view", b.Number.ToString(), "--owner", b.Owner, "--format", "json").ConfigureAwait(false);
        return GhJson.Parse<GhProject>(r.StandardOutput);
    }

    public Task<GhProjectFieldList> ProjectFieldsAsync(CancellationToken ct) => ProjectFieldsAsync(Board, ct);

    public async Task<GhProjectFieldList> ProjectFieldsAsync(BoardReference b, CancellationToken ct)
    {
        var r = await MustAsync(ct, "project", "field-list", b.Number.ToString(), "--owner", b.Owner, "--format", "json").ConfigureAwait(false);
        return GhJson.Parse<GhProjectFieldList>(r.StandardOutput);
    }

    /// <summary>The projects an owner has, for finding the board when the repository names none.</summary>
    public async Task<List<GhProject>> ProjectsAsync(string owner, CancellationToken ct)
    {
        var r = await RunAsync(ct, "project", "list", "--owner", owner, "--format", "json", "--limit", "100").ConfigureAwait(false);
        return r.Succeeded ? GhJson.Parse<GhProjectList>(r.StandardOutput).Projects : [];
    }

    /// <summary>
    /// Moves a card by looking the IDs up by name at run time: the project, its Status field and the option.
    /// Returns false when the board has no such column.
    /// </summary>
    public async Task<bool> MoveCardAsync(string itemId, string columnName, CancellationToken ct)
    {
        var project = await ProjectAsync(ct).ConfigureAwait(false);
        var fields = await ProjectFieldsAsync(ct).ConfigureAwait(false);
        var status = fields.Fields.FirstOrDefault(f => f.Name.Equals("Status", StringComparison.OrdinalIgnoreCase));
        var option = status?.Options.FirstOrDefault(o => o.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
        if (status is null || option is null) return false;

        await MustAsync(ct, "project", "item-edit", "--project-id", project.Id, "--id", itemId,
            "--field-id", status.Id, "--single-select-option-id", option.Id).ConfigureAwait(false);
        return true;
    }

    // Pull requests

    private const string PullRequestListFields = "number,title,headRefName,reviewDecision,statusCheckRollup,url,author,mergeStateStatus,isDraft,state";

    public async Task<List<GhPullRequest>> MyPullRequestsAsync(CancellationToken ct)
    {
        var r = await MustAsync(ct, InRepo("pr", "list", "--author", "@me", "--json", PullRequestListFields)).ConfigureAwait(false);
        return GhJson.ParseList<GhPullRequest>(r.StandardOutput);
    }

    public async Task<List<GhPullRequest>> AwaitingReviewAsync(CancellationToken ct)
    {
        var r = await MustAsync(ct, InRepo("pr", "list", "--search", "review:required", "--json", PullRequestListFields)).ConfigureAwait(false);
        return GhJson.ParseList<GhPullRequest>(r.StandardOutput);
    }

    /// <summary>Every open pull request, for the Review screen: the ones waiting for review and the ones ready to merge.</summary>
    public Task<List<GhPullRequest>> OpenPullRequestsAsync(CancellationToken ct) => OpenPullRequestsAsync(Repo.OwnerAndName, ct);

    public async Task<List<GhPullRequest>> OpenPullRequestsAsync(string ownerAndName, CancellationToken ct)
    {
        var r = await MustAsync(ct, InRepoOf(ownerAndName, "pr", "list", "--state", "open", "--limit", "100", "--json", PullRequestListFields)).ConfigureAwait(false);
        return GhJson.ParseList<GhPullRequest>(r.StandardOutput);
    }

    /// <summary>One pull request with the list's fields, read fresh.</summary>
    public async Task<GhPullRequest> PullRequestAsync(int number, CancellationToken ct)
    {
        var r = await MustAsync(ct, InRepo("pr", "view", number.ToString(), "--json", PullRequestListFields)).ConfigureAwait(false);
        return GhJson.Parse<GhPullRequest>(r.StandardOutput);
    }

    /// <summary>How long a merge waits for GitHub to work out a pull request's merge state before giving up on it.</summary>
    public static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(1);
    public const int SettleAttempts = 10;

    /// <summary>
    /// The pull request once GitHub has worked out its merge state. After main moves GitHub reports the state as
    /// unknown for a few seconds; this reads again, with a pause between reads, until it is known or the
    /// attempts run out, so a merge never acts on a state that was true before the last merge.
    /// </summary>
    public async Task<GhPullRequest> SettledPullRequestAsync(int number, TimeSpan delay, CancellationToken ct)
    {
        GhPullRequest pr = await PullRequestAsync(number, ct).ConfigureAwait(false);
        for (int attempt = 1; attempt < SettleAttempts && ReviewRules.IsComputing(pr); attempt++)
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct).ConfigureAwait(false);
            pr = await PullRequestAsync(number, ct).ConfigureAwait(false);
        }
        return pr;
    }

    /// <summary>Every open pull request, read again while any has an unknown merge state, so the list shows what is true now.</summary>
    public async Task<List<GhPullRequest>> OpenPullRequestsSettledAsync(TimeSpan delay, CancellationToken ct)
    {
        var list = await OpenPullRequestsAsync(ct).ConfigureAwait(false);
        for (int attempt = 1; attempt < SettleAttempts && list.Any(ReviewRules.IsComputing); attempt++)
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct).ConfigureAwait(false);
            list = await OpenPullRequestsAsync(ct).ConfigureAwait(false);
        }
        return list;
    }

    /// <summary>The open pull request for a branch, or null when there is none.</summary>
    public async Task<GhPullRequest?> PullRequestForBranchAsync(string branch, CancellationToken ct)
    {
        var r = await RunAsync(ct, InRepo("pr", "view", branch, "--json", "number,title,url,state,headRefName,body,statusCheckRollup,reviewDecision,mergeStateStatus")).ConfigureAwait(false);
        if (!r.Succeeded) return null;
        var pr = GhJson.Parse<GhPullRequest>(r.StandardOutput);
        return pr.State.Equals("OPEN", StringComparison.OrdinalIgnoreCase) ? pr : null;
    }

    public async Task<GhPullRequest> PullRequestFilesAsync(int number, CancellationToken ct)
    {
        var r = await MustAsync(ct, InRepo("pr", "view", number.ToString(), "--json", "number,title,url,files")).ConfigureAwait(false);
        return GhJson.Parse<GhPullRequest>(r.StandardOutput);
    }

    /// <summary>Opens the pull request with the body passed through a file. Returns its number and URL.</summary>
    public async Task<(int Number, string Url)> CreatePullRequestAsync(string title, string body, CancellationToken ct)
    {
        var file = Path.Combine(Path.GetTempPath(), $"designated-driver-pr-{Guid.NewGuid():N}.md");
        try
        {
            await File.WriteAllTextAsync(file, body, new System.Text.UTF8Encoding(false), ct).ConfigureAwait(false);
            var r = await MustAsync(ct, InRepo("pr", "create", "--base", Repo.DefaultBranch, "--title", title, "--body-file", file)).ConfigureAwait(false);
            var url = PullRequestUrl().Match(r.StandardOutput + "\n" + r.StandardError);
            return url.Success ? (int.Parse(url.Groups[1].Value), url.Value) : (0, r.StandardOutput.Trim());
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    /// <summary>gh pr checks exits non-zero while checks run or fail; the JSON is still read.</summary>
    public async Task<List<GhCheck>> ChecksAsync(int number, CancellationToken ct)
    {
        var r = await RunAsync(ct, InRepo("pr", "checks", number.ToString(), "--json", "name,state,bucket,link,workflow")).ConfigureAwait(false);
        var json = r.StandardOutput.Trim();
        if (json.StartsWith('[')) return GhJson.ParseList<GhCheck>(json);
        if (r.ExitCode == -1 || (!r.Succeeded && !r.StandardError.Contains("no checks", StringComparison.OrdinalIgnoreCase)))
            throw new CommandFailedException(r);
        return [];
    }

    public async Task<List<string>> MergedBranchesAsync(CancellationToken ct)
    {
        var r = await MustAsync(ct, InRepo("pr", "list", "--state", "merged", "--author", "@me", "--limit", "200", "--json", "headRefName")).ConfigureAwait(false);
        return GhJson.ParseList<GhPullRequest>(r.StandardOutput).Select(p => p.HeadRefName).Distinct().ToList();
    }

    // Review (admins)

    public Task ApproveAsync(int number, CancellationToken ct) => MustAsync(ct, InRepo("pr", "review", number.ToString(), "--approve"));

    public async Task RequestChangesAsync(int number, string comment, CancellationToken ct)
    {
        var file = Path.Combine(Path.GetTempPath(), $"designated-driver-review-{Guid.NewGuid():N}.md");
        try
        {
            await File.WriteAllTextAsync(file, comment, new System.Text.UTF8Encoding(false), ct).ConfigureAwait(false);
            await MustAsync(ct, InRepo("pr", "review", number.ToString(), "--request-changes", "--body-file", file)).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    public Task MergeAsync(int number, CancellationToken ct) => MustAsync(ct, InRepo("pr", "merge", number.ToString(), "--squash", "--delete-branch"));

    /// <summary>
    /// The admin bypass: the typed reason is posted as a comment on the pull request first, so the record exists
    /// before the merge, then the merge runs with --admin, squashing and deleting the branch.
    /// </summary>
    public async Task BypassMergeAsync(int number, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A bypass needs a reason.", nameof(reason));
        var file = Path.Combine(Path.GetTempPath(), $"designated-driver-bypass-{Guid.NewGuid():N}.md");
        try
        {
            await File.WriteAllTextAsync(file, reason.Trim() + "\n", new System.Text.UTF8Encoding(false), ct).ConfigureAwait(false);
            await MustAsync(ct, InRepo("pr", "comment", number.ToString(), "--body-file", file)).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { }
        }
        await MustAsync(ct, InRepo("pr", "merge", number.ToString(), "--squash", "--admin", "--delete-branch")).ConfigureAwait(false);
    }

    public Task UpdateBranchAsync(int number, CancellationToken ct) => MustAsync(ct, InRepo("pr", "update-branch", number.ToString()));

    [GeneratedRegex(@"https://github\.com/[^/\s]+/[^/\s]+/pull/(\d+)")]
    private static partial Regex PullRequestUrl();
}
