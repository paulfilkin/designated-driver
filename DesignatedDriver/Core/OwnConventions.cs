using DesignatedDriver.Resources;

namespace DesignatedDriver.Core;

/// <summary>
/// A repository whose entry says it keeps its own conventions isn't converted: Set up a repository and --setup
/// refuse it, and Check and --check say so instead of listing drift. Clearing the setting in its Settings undoes it.
/// </summary>
public static class OwnConventions
{
    /// <summary>Why set-up refuses this repository, or null when it may go ahead.</summary>
    public static string? SetupRefusal(bool keepsOwnConventions, string ownerAndName)
        => keepsOwnConventions ? string.Format(Strings.Setup_KeepsOwn, ownerAndName) : null;

    /// <summary>
    /// The board the workflow screens use: the manifest's, else the repository's own settings, else none. For a
    /// repository that keeps its own conventions its own settings win, and a manifest's board is ignored.
    /// </summary>
    public static BoardReference? BoardFor(Manifest? manifest, RepositorySettings repository)
    {
        var own = repository.HasBoardOverride ? new BoardReference(repository.BoardOwner, repository.BoardNumber) : null;
        if (repository.KeepsOwnConventions) return own;
        return manifest?.Board is { Number: > 0 } fromManifest ? fromManifest : own;
    }

    /// <summary>
    /// The tag Repositories shows beside an entry (issue 80): Own conventions when it is marked as keeping its own,
    /// which wins over a manifest as it does for the board; the convention's version when there is a manifest; else
    /// Not set up.
    /// </summary>
    public static string Tag(Manifest? manifest, RepositorySettings repository)
        => repository.KeepsOwnConventions ? Strings.Repos_OwnConventions
         : manifest is not null ? string.Format(Strings.Repos_Convention, manifest.ConventionVersion)
         : Strings.Repos_NotSetUp;

    /// <summary>What Check says instead of a drift report, or null when it checks as usual.</summary>
    public static string? CheckNotice(RepositorySettings? repository)
        => repository is { KeepsOwnConventions: true } ? string.Format(Strings.Check_KeepsOwn, repository.OwnerAndName) : null;
}
