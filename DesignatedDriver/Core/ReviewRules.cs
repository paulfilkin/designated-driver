using DesignatedDriver.Models;

namespace DesignatedDriver.Core;

/// <summary>Why a pull request can't be merged by the ordinary route, if it can't.</summary>
public enum MergeBlocker
{
    None,
    Draft,
    NotApproved,
    ChangesRequested,
    ChecksFailed,
    ChecksRunning,
    Conflicts,
    Behind,
    Blocked,
    /// <summary>GitHub has not yet worked out whether the branch merges cleanly, as happens for a few seconds after main moves.</summary>
    Computing,
}

/// <summary>
/// What the Review screen offers for a pull request. Pure logic so the self-test can exercise it:
/// approve needs someone else's pull request, squash and merge needs approval, green checks and a clean
/// merge state, and the bypass is offered when the rules block the merge or the pull request is the
/// user's own (which they can't approve).
/// </summary>
public static class ReviewRules
{
    public static bool IsOwn(GhPullRequest pr, string login)
        => login.Length > 0 && string.Equals(pr.Author?.Login, login, StringComparison.OrdinalIgnoreCase);

    public static bool CanApprove(GhPullRequest pr, string login) => !pr.IsDraft && !IsOwn(pr, login);

    public static bool CanRequestChanges(GhPullRequest pr, string login) => !pr.IsDraft && !IsOwn(pr, login);

    /// <summary>True while GitHub reports the merge state as unknown, or has not reported it at all.</summary>
    public static bool IsComputing(GhPullRequest pr)
        => pr.MergeStateStatus.Length == 0 || pr.MergeStateStatus.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase);

    public static MergeBlocker Blocker(GhPullRequest pr)
    {
        if (pr.IsDraft) return MergeBlocker.Draft;
        if (IsComputing(pr)) return MergeBlocker.Computing;
        var state = pr.MergeStateStatus.ToUpperInvariant();
        if (state == "DIRTY") return MergeBlocker.Conflicts;
        var checks = pr.ChecksBucket;
        if (checks == "fail") return MergeBlocker.ChecksFailed;
        var decision = pr.ReviewDecision.ToUpperInvariant();
        if (decision == "CHANGES_REQUESTED") return MergeBlocker.ChangesRequested;
        if (decision != "APPROVED") return MergeBlocker.NotApproved;
        if (checks == "pending") return MergeBlocker.ChecksRunning;
        if (state == "BEHIND") return MergeBlocker.Behind;
        if (state is "BLOCKED" or "UNSTABLE") return MergeBlocker.Blocked;
        return MergeBlocker.None;
    }

    public static bool CanMerge(GhPullRequest pr) => Blocker(pr) == MergeBlocker.None;

    /// <summary>
    /// Conflicts can't be bypassed: the branch has to be updated first. Nor can a state GitHub hasn't worked out
    /// yet, since it may turn out to be conflicts. Everything else can.
    /// </summary>
    public static bool OffersBypass(GhPullRequest pr, string login)
    {
        var blocker = Blocker(pr);
        if (blocker is MergeBlocker.Conflicts or MergeBlocker.Draft or MergeBlocker.Computing) return false;
        return blocker != MergeBlocker.None || IsOwn(pr, login);
    }

    /// <summary>
    /// The bypass as Review offers it: when GitHub's rules would allow it and the repository's settings offer it
    /// (issue 74). Off by default for a repository that keeps its own conventions.
    /// </summary>
    public static bool OffersBypass(GhPullRequest pr, string login, bool repositoryOffers) => repositoryOffers && OffersBypass(pr, login);

    /// <summary>True when the rules would allow a bypass but the repository's settings turn it off, so Review says where to change it.</summary>
    public static bool BypassTurnedOff(GhPullRequest pr, string login, bool repositoryOffers) => !repositoryOffers && OffersBypass(pr, login);

    /// <summary>
    /// Whether a merge may run on the pull request as just re-read from GitHub: the ordinary merge needs no
    /// blocker at all, and the bypass needs the state to be known and free of conflicts. The screen's buttons
    /// came from an earlier read, and main may have moved since.
    /// </summary>
    public static bool MayMerge(GhPullRequest fresh, bool bypass)
    {
        var blocker = Blocker(fresh);
        return bypass ? blocker is not (MergeBlocker.Conflicts or MergeBlocker.Draft or MergeBlocker.Computing) : blocker == MergeBlocker.None;
    }

    /// <summary>The pull requests that need updating from main before they can merge: behind it, or in conflict with it.</summary>
    public static IReadOnlyList<GhPullRequest> NeedingUpdate(IEnumerable<GhPullRequest> pullRequests)
        => pullRequests.Where(p => !p.IsDraft && p.MergeStateStatus.ToUpperInvariant() is "BEHIND" or "DIRTY").ToList();

    public static bool OffersUpdateBranch(GhPullRequest pr) => pr.MergeStateStatus.Equals("BEHIND", StringComparison.OrdinalIgnoreCase);
}
