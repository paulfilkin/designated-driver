using DesignatedDriver.Core;
using DesignatedDriver.Resources;

namespace DesignatedDriver.SelfTest;

/// <summary>Scan next-step checks: every verdict says what to do next, on the screen and in the text report.</summary>
public static partial class SelfTestRunner
{
    private static void ScanNextStepChecks(Action<bool, string> check)
    {
        var verdicts = Enum.GetValues<ScanVerdict>();
        check(verdicts.All(v => ScanReportText.NextStep(v, true).StartsWith("Next step", StringComparison.Ordinal) && ScanReportText.NextStep(v, false).StartsWith("Next step", StringComparison.Ordinal)),
            "Scan next step: every verdict has a sentence for the screen");
        check(verdicts.All(v => ScanReportText.NextStepText(v).StartsWith("Next step", StringComparison.Ordinal)), "Scan next step: every verdict has a sentence for the text report");
        check(ScanReportText.NextStep(ScanVerdict.AlreadySetUp, true) != ScanReportText.NextStep(ScanVerdict.AlreadySetUp, false)
            && ScanReportText.NextStep(ScanVerdict.AlreadySetUp, true).Contains("Check", StringComparison.Ordinal)
            && ScanReportText.NextStep(ScanVerdict.AlreadySetUp, false).Contains("button below", StringComparison.Ordinal),
            "Scan next step: already set up points the current repository at Check and another at the button below");
        check(ScanReportText.NextStep(ScanVerdict.Convertible, true).Contains("nothing changes until you press " + Strings.Setup_Start, StringComparison.Ordinal),
            "Scan next step: convertible says nothing changes until Start in the wizard");
        check(ScanReportText.NextStep(ScanVerdict.Convertible, false, canKeepAsIs: true) is var choice
            && choice.Contains("choose", StringComparison.Ordinal) && choice.Contains("its own way of working", StringComparison.Ordinal)
            && choice.Contains("press " + Strings.Setup_Start, StringComparison.Ordinal)
            && ScanReportText.NextStep(ScanVerdict.AlreadySetUp, false, canKeepAsIs: true) == ScanReportText.NextStep(ScanVerdict.AlreadySetUp, false),
            "Scan next step: a convertible repository that can be kept as it is asks for a choice; other verdicts are unchanged");
        check(ScanReportText.NextStep(ScanVerdict.Convertible, false) == ScanReportText.NextStep(ScanVerdict.Convertible, true)
            && ScanReportText.NextStepText(ScanVerdict.Convertible).Contains("--setup", StringComparison.Ordinal)
            && ScanReportText.NextStepText(ScanVerdict.AlreadySetUp).Contains("--check", StringComparison.Ordinal),
            "Scan next step: the text report names the switches");

        var facts = new ScanFacts { Folder = @"C:\Users\paul\Documents\GIT\example", OwnerAndName = "paulfilkin/example", IsGitRepository = true, OriginUrl = "https://github.com/paulfilkin/example.git" };
        var report = new ScanReport(ScanVerdict.NotConvertible, [new ScanFinding(ScanSeverity.Blocker, ScanCode.Archived, SetupStep.None, "The repository is archived.")]);
        var lines = ScanReportText.Render(facts, report, new DateTime(2026, 11, 21, 12, 0, 0)).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        check(lines.Count > 2 && lines[2] == ScanReportText.NextStepText(ScanVerdict.NotConvertible), "Scan next step: the text report carries it on the line after the verdict");
    }
}
