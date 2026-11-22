using DesignatedDriver.Models;

namespace DesignatedDriver.Core;

/// <summary>
/// The open issues and pull requests that aren't on the board yet. Matched by URL, not number: an issue and a pull
/// request can share a number, and a board can hold items from other repositories. Pure, so the self-test covers it.
/// </summary>
public static class BoardMissing
{
    public static (List<string> Issues, List<string> PullRequests) Find(IEnumerable<GhIssue> issues, IEnumerable<GhPullRequest> pullRequests, IEnumerable<GhProjectItem> items)
    {
        var onBoard = items.Select(i => i.Content?.Url ?? "").Where(u => u.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (issues.Select(i => i.Url).Where(u => u.Length > 0 && !onBoard.Contains(u)).ToList(),
                pullRequests.Select(p => p.Url).Where(u => u.Length > 0 && !onBoard.Contains(u)).ToList());
    }
}
