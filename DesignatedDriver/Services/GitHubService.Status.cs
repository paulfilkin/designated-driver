using DesignatedDriver.Core;
using DesignatedDriver.Models;

namespace DesignatedDriver.Services;

/// <summary>The gh calls behind the Status screen's automation card: Dependabot alerts, Actions minutes and the board's missing issues.</summary>
public sealed partial class GitHubService
{
    /// <summary>Open Dependabot alerts, or null when they cannot be read (alerts off, no access).</summary>
    public async Task<List<GhDependabotAlert>?> DependabotOpenAlertsAsync(string ownerAndName, CancellationToken ct)
    {
        var r = await ApiAsync(ct, $"repos/{ownerAndName}/dependabot/alerts?state=open&per_page=100").ConfigureAwait(false);
        return r.Ok && r.Output.TrimStart().StartsWith('[') ? GhJson.ParseRestList<GhDependabotAlert>(r.Output) : null;
    }

    /// <summary>The account's Actions billing for the month, or null when the token lacks the user scope.</summary>
    public async Task<GhActionsBilling?> ActionsBillingAsync(string login, CancellationToken ct)
    {
        var r = await ApiAsync(ct, $"users/{login}/settings/billing/actions").ConfigureAwait(false);
        return r.Ok ? GhJson.ParseRest<GhActionsBilling>(r.Output) : null;
    }

    /// <summary>The repository's workflow runs created since a date, newest first, at most one page, with only their ids read.</summary>
    public async Task<List<GhWorkflowRun>> RunsSinceAsync(string ownerAndName, DateTime since, CancellationToken ct)
    {
        var r = await ApiAsync(ct, $"repos/{ownerAndName}/actions/runs?created={ActionsUsage.CreatedSince(since)}&per_page=100", "--jq", ActionsUsage.RunIdsOnly).ConfigureAwait(false);
        return r.Ok ? GhJson.ParseRest<GhWorkflowRunList>(r.Output).WorkflowRuns : [];
    }

    public async Task<GhRunTiming?> RunTimingAsync(string ownerAndName, long runId, CancellationToken ct)
    {
        var r = await ApiAsync(ct, $"repos/{ownerAndName}/actions/runs/{runId}/timing").ConfigureAwait(false);
        return r.Ok ? GhJson.ParseRest<GhRunTiming>(r.Output) : null;
    }

    /// <summary>
    /// Puts every open issue and open pull request that isn't on the board onto it, so a board with no token and no
    /// Auto-add still catches up, a bot's pull requests included (issue 68). GitHub's own automation then gives each
    /// Todo. Returns how many of each were added.
    /// </summary>
    public async Task<(int Issues, int PullRequests)> AddMissingToBoardAsync(BoardReference board, CancellationToken ct)
    {
        var issues = await OpenIssuesAsync(ct).ConfigureAwait(false);
        var pullRequests = await OpenPullRequestsAsync(ct).ConfigureAwait(false);
        var items = await ProjectItemsAsync(ct).ConfigureAwait(false);
        var (missingIssues, missingPullRequests) = BoardMissing.Find(issues, pullRequests, items.Items);
        foreach (var url in missingIssues.Concat(missingPullRequests))
            await AddToBoardAsync(board, url, ct).ConfigureAwait(false);
        return (missingIssues.Count, missingPullRequests.Count);
    }

    /// <summary>The repositories the signed-in user has starred, as owner/name, every page of them.</summary>
    public async Task<HashSet<string>> StarredAsync(CancellationToken ct)
    {
        var r = await MustAsync(ct, "api", "user/starred", "--paginate", "--jq", ".[].full_name").ConfigureAwait(false);
        return RepositoryFilter.StarredSet(r.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
