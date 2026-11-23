using DesignatedDriver.Core;
using DesignatedDriver.Models;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// Each repository's settings say whether Review offers merge with bypass (issue 74): off by default for a repository
/// that keeps its own conventions, old entries included, on for the rest, and a choice made by hand is kept.
/// </summary>
public static partial class SelfTestRunner
{
    private static void BypassSettingChecks(Action<bool, string> check)
    {
        static RepositorySettings Entry(string extra) => Settings.Parse("{\"repositories\":[{\"owner\":\"example-org\",\"name\":\"example-app\"" + extra + "}]}").Repositories.Single();

        check(new RepositorySettings().OffersBypass && Entry("").OffersBypass,
            "Bypass setting: on for a new entry and for one saved before the setting existed");
        check(!Entry(",\"keepsOwnConventions\":true").OffersBypass && !new RepositorySettings { KeepsOwnConventions = true }.OffersBypass,
            "Bypass setting: off for a repository that keeps its own conventions, an entry marked before the setting existed included");

        var chosenOn = Entry(",\"keepsOwnConventions\":true,\"offerBypass\":true");
        var chosenOff = Entry(",\"offerBypass\":false");
        check(chosenOn.OffersBypass && !chosenOff.OffersBypass,
            "Bypass setting: a choice made by hand wins over the Keeps its own conventions marker, either way");
        chosenOn.KeepsOwnConventions = false;
        chosenOff.KeepsOwnConventions = true;
        var unchosen = Entry("");
        unchosen.KeepsOwnConventions = true;
        check(chosenOn.OffersBypass && !chosenOff.OffersBypass && !unchosen.OffersBypass,
            "Bypass setting: a choice made by hand is kept when the marker changes; without one the setting follows the marker");

        var saved = new Settings();
        saved.Repositories.Add(new RepositorySettings { Owner = "example-org", Name = "example-app", KeepsOwnConventions = true, OfferBypass = true });
        saved.Repositories.Add(new RepositorySettings { Owner = "example-org", Name = "other-app" });
        var reloaded = Settings.Parse(saved.ToJson()).Repositories;
        check(reloaded[0].OfferBypass == true && reloaded[0].OffersBypass && reloaded[1].OfferBypass is null && reloaded[1].OffersBypass,
            "Bypass setting: a choice made by hand survives a save and reload, and an unchosen entry stays unchosen");

        // Review offers the bypass only when GitHub's rules allow it and the repository's settings offer it.
        var own = new GhPullRequest
        {
            Number = 40, Title = "Test", HeadRefName = "feature/40-test", ReviewDecision = "REVIEW_REQUIRED", MergeStateStatus = "BLOCKED",
            Author = new GhUser { Login = "paulfilkin" },
            StatusCheckRollup = [new GhStatusCheck { TypeName = "CheckRun", Name = "Build and test", Status = "COMPLETED", Conclusion = "SUCCESS" }],
        };
        var conflicted = new GhPullRequest
        {
            Number = 41, Title = "Test", HeadRefName = "feature/41-test", ReviewDecision = "REVIEW_REQUIRED", MergeStateStatus = "DIRTY",
            Author = new GhUser { Login = "paulfilkin" },
        };
        check(ReviewRules.OffersBypass(own, "paulfilkin", true) && !ReviewRules.OffersBypass(own, "paulfilkin", false)
            && !ReviewRules.OffersBypass(conflicted, "paulfilkin", true),
            "Bypass setting: Review offers the bypass only when the rules allow it and the repository's settings offer it");
        check(ReviewRules.BypassTurnedOff(own, "paulfilkin", false) && !ReviewRules.BypassTurnedOff(own, "paulfilkin", true)
            && !ReviewRules.BypassTurnedOff(conflicted, "paulfilkin", false),
            "Bypass setting: Review says the bypass is turned off only where it would otherwise have been offered");
    }
}
