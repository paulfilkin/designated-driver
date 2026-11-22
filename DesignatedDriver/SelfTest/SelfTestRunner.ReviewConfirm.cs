using DesignatedDriver.Core;
using DesignatedDriver.Resources;
using DesignatedDriver.Services;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// Review's confirmation names the repository on its button, and asks for the name typed before a merge unless the
/// repository's settings turn that off, which they don't by default, old entries included.
/// </summary>
public static partial class SelfTestRunner
{
    private static void ReviewConfirmChecks(Action<bool, string> check)
    {
        const string repo = "example-org/example-app";
        var actions = Enum.GetValues<ReviewAction>();
        check(actions.All(a => ReviewConfirm.ButtonLabel(a, repo).Contains(repo, StringComparison.Ordinal))
            && actions.Select(a => ReviewConfirm.ButtonLabel(a, repo)).Distinct().Count() == actions.Length,
            "Review confirmation: every action's button names the repository, and each says a different thing");

        check(ReviewConfirm.NeedsTypedName(ReviewAction.Merge, true) && ReviewConfirm.NeedsTypedName(ReviewAction.Bypass, true)
            && !ReviewConfirm.NeedsTypedName(ReviewAction.Approve, true) && !ReviewConfirm.NeedsTypedName(ReviewAction.RequestChanges, true)
            && !ReviewConfirm.NeedsTypedName(ReviewAction.Merge, false) && !ReviewConfirm.NeedsTypedName(ReviewAction.Bypass, false),
            "Review confirmation: merge and merge with bypass ask for the name typed, unless the setting is off; approve and request changes never do");

        check(ReviewConfirm.Matches(repo, repo) && ReviewConfirm.Matches("  Example-Org/Example-App ", repo)
            && !ReviewConfirm.Matches("example-org/example", repo) && !ReviewConfirm.Matches("example-app", repo) && !ReviewConfirm.Matches("", repo)
            && !ReviewConfirm.Matches("", ""),
            "Review confirmation: the typed name must be the whole owner and name, without regard to case or surrounding spaces");

        var fresh = new RepositorySettings();
        var old = Settings.Parse("{\"repositories\":[{\"owner\":\"example-org\",\"name\":\"example-app\"}]}").Repositories.Single();
        var off = Settings.Parse("{\"repositories\":[{\"owner\":\"example-org\",\"name\":\"example-app\",\"typeNameToMerge\":false}]}").Repositories.Single();
        check(fresh.TypeNameToMerge && old.TypeNameToMerge && !off.TypeNameToMerge,
            "Review confirmation: typing the name to merge is on for a new entry and for one saved before the setting existed, and off only when cleared");

        check(ReviewConfirm.Title(ReviewAction.Merge) == Strings.Review_Merge && ReviewConfirm.Title(ReviewAction.Bypass) == Strings.Review_Bypass,
            "Review confirmation: the window is titled with the action");
    }
}
