using DesignatedDriver.Core;
using DesignatedDriver.Resources;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// A repository marked as keeping its own conventions is tagged Own conventions on Repositories, and a scan of it
/// heads its changes with what the convention would change, since set-up refuses it (issue 80).
/// </summary>
public static partial class SelfTestRunner
{
    private static void OwnConventionsTagChecks(Action<bool, string> check)
    {
        var manifest = TidyManifest();
        var marked = new RepositorySettings { Owner = "example-org", Name = "example-app", KeepsOwnConventions = true };
        var plain = new RepositorySettings { Owner = "example-org", Name = "example-app" };
        check(OwnConventions.Tag(null, marked) == Strings.Repos_OwnConventions && OwnConventions.Tag(manifest, marked) == Strings.Repos_OwnConventions,
            "Own conventions tag: a marked repository is tagged Own conventions, with or without a manifest");
        check(OwnConventions.Tag(manifest, plain) == string.Format(Strings.Repos_Convention, manifest.ConventionVersion) && OwnConventions.Tag(null, plain) == Strings.Repos_NotSetUp,
            "Own conventions tag: an unmarked repository shows the convention's version with a manifest, else Not set up");

        check(ScanReportText.GroupTitle(ScanSeverity.Change, ScanVerdict.KeepsOwnConventions) == Strings.Scan_Group_ChangeKeepsOwn
            && ScanReportText.GroupTitle(ScanSeverity.Change, ScanVerdict.Convertible) == Strings.Scan_Group_Change
            && ScanReportText.GroupTitle(ScanSeverity.Change) == Strings.Scan_Group_Change
            && ScanReportText.GroupTitle(ScanSeverity.Ok, ScanVerdict.KeepsOwnConventions) == Strings.Scan_Group_Ok,
            "Own conventions tag: a marked repository's changes are what the convention would change; other groups and verdicts keep their headings");

        static ScanReport Report(ScanVerdict verdict) => new(verdict, [new ScanFinding(ScanSeverity.Change, ScanCode.CreateLabels, SetupStep.Labels, "2 label(s) would be created.")]);
        var facts = new ScanFacts { OwnerAndName = "example-org/example-app" };
        var when = new DateTime(2026, 11, 23, 9, 0, 0);
        var kept = ScanReportText.Render(facts, Report(ScanVerdict.KeepsOwnConventions), when);
        var convertible = ScanReportText.Render(facts, Report(ScanVerdict.Convertible), when);
        check(kept.Contains(Strings.Scan_Group_ChangeKeepsOwn, StringComparison.Ordinal) && !kept.Contains(Strings.Scan_Group_Change, StringComparison.Ordinal)
            && convertible.Contains(Strings.Scan_Group_Change, StringComparison.Ordinal),
            "Own conventions tag: the copied report and --scan use the same heading as the screen");
    }
}
