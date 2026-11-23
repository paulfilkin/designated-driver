using DesignatedDriver.Core;
using DesignatedDriver.Resources;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// Scan recommends Keep it as it is for a convertible repository that already runs its own way (issue 75): one an
/// organisation owns, or one whose rules already cover the convention's ruleset. The screen and the text report agree.
/// </summary>
public static partial class SelfTestRunner
{
    private static void RecommendKeepChecks(Action<bool, string> check)
    {
        static ScanFinding Finding(ScanCode code) => new(ScanSeverity.Ok, code, SetupStep.None, code.ToString());
        static ScanReport Report(ScanVerdict verdict, params ScanCode[] codes) => new(verdict, codes.Select(Finding).ToList());

        var organisation = Report(ScanVerdict.Convertible, ScanCode.Organisation);
        var ownRules = Report(ScanVerdict.Convertible, ScanCode.RulesCover);
        var both = Report(ScanVerdict.Convertible, ScanCode.Organisation, ScanCode.RulesCover);
        var plain = Report(ScanVerdict.Convertible, ScanCode.LabelsOk);
        check(ScanRules.RecommendsKeep(organisation) && ScanRules.RecommendsKeep(ownRules) && ScanRules.RecommendsKeep(both) && !ScanRules.RecommendsKeep(plain),
            "Recommend keep: a convertible repository an organisation owns, or whose rules cover the ruleset, is recommended to keep as it is; a plain one isn't");
        check(!ScanRules.RecommendsKeep(Report(ScanVerdict.AlreadySetUp, ScanCode.Organisation)) && !ScanRules.RecommendsKeep(Report(ScanVerdict.NotConvertible, ScanCode.Organisation, ScanCode.RulesCover))
            && !ScanRules.RecommendsKeep(Report(ScanVerdict.KeepsOwnConventions, ScanCode.Organisation)),
            "Recommend keep: no recommendation for a repository that isn't convertible");

        check(ScanReportText.KeepReason(organisation) == Strings.Scan_KeepReason_Organisation && ScanReportText.KeepReason(ownRules) == Strings.Scan_KeepReason_OwnRules
            && ScanReportText.KeepReason(both) == Strings.Scan_KeepReason_Both && ScanReportText.KeepReason(plain) == "",
            "Recommend keep: the reason names the organisation, the rules, or both");

        string reason = ScanReportText.KeepReason(organisation);
        check(ScanReportText.NextStep(ScanVerdict.Convertible, false, canKeepAsIs: true, keepReason: reason) == string.Format(Strings.Scan_Next_RecommendKeep, reason)
            && ScanReportText.NextStep(ScanVerdict.Convertible, false, canKeepAsIs: true) == Strings.Scan_Next_ConvertibleOrKeep
            && ScanReportText.NextStep(ScanVerdict.Convertible, false, canKeepAsIs: false, keepReason: reason) == Strings.Scan_Next_Convertible,
            "Recommend keep: the next step recommends keeping it, with the reason, only when it can be kept as it is");

        var facts = new ScanFacts { OwnerAndName = "example-org/example-app" };
        var recommended = ScanReportText.Render(facts, organisation, new DateTime(2026, 11, 23, 9, 0, 0));
        var usual = ScanReportText.Render(facts, plain, new DateTime(2026, 11, 23, 9, 0, 0));
        check(recommended.Contains(string.Format(Strings.Scan_Next_RecommendKeep_Text, reason), StringComparison.Ordinal)
            && usual.Contains(Strings.Scan_Next_Convertible_Text, StringComparison.Ordinal) && !usual.Contains(reason, StringComparison.Ordinal),
            "Recommend keep: the copied report and --scan say the same as the screen");
    }
}
