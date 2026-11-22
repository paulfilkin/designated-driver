namespace DesignatedDriver.Core;

/// <summary>
/// The Repositories screen's filter: text matched against owner, name and folder without regard to case, and a
/// starred-only switch judged against the starred set read from GitHub. Pure, so the self-test covers it.
/// </summary>
public static class RepositoryFilter
{
    /// <summary>True when the text is empty or every word of it appears in the owner, the name or the folder.</summary>
    public static bool MatchesText(RepositorySettings repo, string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return true;
        var haystack = repo.OwnerAndName + " " + repo.Folder;
        return words.All(w => haystack.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when the repository is in the starred set; the set holds owner/name as GitHub gives it.</summary>
    public static bool IsStarred(RepositorySettings repo, IReadOnlySet<string> starred)
        => repo.OwnerAndName.Length > 0 && starred.Contains(repo.OwnerAndName);

    public static bool Matches(RepositorySettings repo, string text, bool starredOnly, IReadOnlySet<string> starred)
        => MatchesText(repo, text) && (!starredOnly || IsStarred(repo, starred));

    /// <summary>A set for the starred names that ignores case, as GitHub does for owner and name.</summary>
    public static HashSet<string> StarredSet(IEnumerable<string> names) => new(names, StringComparer.OrdinalIgnoreCase);
}
