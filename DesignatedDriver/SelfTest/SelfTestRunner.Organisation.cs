using DesignatedDriver.Core;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// An organisation's repository isn't judged by a personal plan: push protection and code scanning on its private
/// repositories depend on the organisation's licence, and the organisation warning names the board only when set-up
/// would create one.
/// </summary>
public static partial class SelfTestRunner
{
    private static void OrganisationWordingChecks(Action<bool, string> check)
    {
        ScanReport Scan(bool organisation, bool isPrivate, bool boardLinked) => ScanRules.Assess(new ScanFacts
        {
            OwnerAndName = "example-org/example-app",
            RemoteLookedUp = true,
            Details = new GhRepositoryDetails
            {
                FullName = "example-org/example-app",
                Private = isPrivate,
                Owner = new GhUser { Login = "example-org", Type = organisation ? "Organization" : "User" },
            },
            BranchRulesStatus = ApiStatus.Ok,
            LinkedBoards = boardLinked ? [new ScanBoard(3, "Delivery", "", ["Backlog", "Ready", "In progress", "In review", "Done"])] : [],
        });
        string? Text(ScanReport r, ScanCode code) => r.Findings.FirstOrDefault(f => f.Code == code)?.Text;

        var orgPrivate = Scan(organisation: true, isPrivate: true, boardLinked: true);
        check(orgPrivate.Findings.FirstOrDefault(f => f.Code == ScanCode.OrganisationSecurity) is { Severity: ScanSeverity.Warning }
            && !orgPrivate.Has(ScanCode.PushProtectionUnavailable) && !orgPrivate.Has(ScanCode.CodeScanningUnavailable),
            "Organisation: a private organisation repository's push protection and code scanning depend on its licence, and aren't called unavailable on this plan");
        var personalPrivate = Scan(organisation: false, isPrivate: true, boardLinked: true);
        check(personalPrivate.Has(ScanCode.PushProtectionUnavailable) && personalPrivate.Has(ScanCode.CodeScanningUnavailable) && !personalPrivate.Has(ScanCode.OrganisationSecurity)
            && !personalPrivate.Has(ScanCode.Organisation),
            "Organisation: a personal private repository still has them as not available on this plan, with no organisation warning");
        check(!Scan(organisation: true, isPrivate: false, boardLinked: true).Has(ScanCode.OrganisationSecurity),
            "Organisation: a public organisation repository has push protection and code scanning, so no licence note");

        check(Text(orgPrivate, ScanCode.Organisation) == string.Format(Strings.Scan_OrganisationBoardLinked, "example-org")
            && Text(Scan(organisation: true, isPrivate: true, boardLinked: false), ScanCode.Organisation) == string.Format(Strings.Scan_Organisation, "example-org"),
            "Organisation: the warning says the linked board is used as it is, and mentions an organisation-owned board only when set-up would create one");

        // Check says the same of push protection.
        var (facts, expected, actual) = TidyRepository();
        var org = Clone(facts);
        org.OwnerAndName = "example-org/example-app";
        org.Details =new GhRepositoryDetails { FullName = "example-org/example-app", Private = true, Owner = new GhUser { Login = "example-org", Type = "Organization" } };
        check(Drift(org, expected, actual).Items.FirstOrDefault(i => i.Key == "pushprotection")?.Text == Strings.Scan_OrganisationSecurity
            && Drift(facts, expected, actual).Items.FirstOrDefault(i => i.Key == "pushprotection")?.Text == Strings.Scan_PushProtectionUnavailable,
            "Organisation: Check's push protection note names the organisation's licence for its private repositories, and the plan for others");
    }
}
