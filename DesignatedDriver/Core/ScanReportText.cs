using System.Text;
using DesignatedDriver.Resources;

namespace DesignatedDriver.Core;

/// <summary>The words for a scan report: group titles, step names, and the whole report as plain text.</summary>
public static class ScanReportText
{
    public static string GroupTitle(ScanSeverity severity) => severity switch
    {
        ScanSeverity.Blocker => Strings.Scan_Group_Blocker,
        ScanSeverity.Warning => Strings.Scan_Group_Warning,
        ScanSeverity.Change => Strings.Scan_Group_Change,
        ScanSeverity.NotAvailable => Strings.Scan_Group_NotAvailable,
        _ => Strings.Scan_Group_Ok,
    };

    public static string StepTitle(SetupStep step) => step switch
    {
        SetupStep.CreateRepository => Strings.Step_CreateRepository,
        SetupStep.Settings => Strings.Step_Settings,
        SetupStep.Labels => Strings.Step_Labels,
        SetupStep.Board => Strings.Step_Board,
        SetupStep.BoardToken => Strings.Step_BoardToken,
        SetupStep.Files => Strings.Step_Files,
        SetupStep.Hooks => Strings.Step_Hooks,
        SetupStep.Security => Strings.Step_Security,
        SetupStep.PushProtection => Strings.Step_PushProtection,
        SetupStep.Ruleset => Strings.Step_Ruleset,
        SetupStep.Manifest => Strings.Step_Manifest,
        SetupStep.Register => Strings.Step_Register,
        _ => Strings.Step_None,
    };

    public static string VerdictTitle(ScanVerdict verdict) => verdict switch
    {
        ScanVerdict.NotConvertible => Strings.Scan_Verdict_NotConvertible,
        ScanVerdict.AlreadySetUp => Strings.Scan_Verdict_AlreadySetUp,
        ScanVerdict.KeepsOwnConventions => Strings.Scan_Verdict_KeepsOwn,
        _ => Strings.Scan_Verdict_Convertible,
    };

    /// <summary>
    /// What to do after the scan, one sentence per verdict, as the screen says it. The already-set-up wording
    /// depends on whether the scanned repository is the one the tool is working on.
    /// </summary>
    public static string NextStep(ScanVerdict verdict, bool isCurrentRepository) => verdict switch
    {
        ScanVerdict.NotConvertible => Strings.Scan_Next_NotConvertible,
        ScanVerdict.AlreadySetUp => isCurrentRepository ? Strings.Scan_Next_AlreadySetUp_Current : Strings.Scan_Next_AlreadySetUp_Other,
        ScanVerdict.KeepsOwnConventions => Strings.Scan_Next_KeepsOwn,
        _ => Strings.Scan_Next_Convertible,
    };

    /// <summary>The next step as the text report says it, naming the command-line switches rather than the screens.</summary>
    public static string NextStepText(ScanVerdict verdict) => verdict switch
    {
        ScanVerdict.NotConvertible => Strings.Scan_Next_NotConvertible,
        ScanVerdict.AlreadySetUp => Strings.Scan_Next_AlreadySetUp_Text,
        ScanVerdict.KeepsOwnConventions => Strings.Scan_Next_KeepsOwn,
        _ => Strings.Scan_Next_Convertible_Text,
    };

    /// <summary>The report as plain text, for pasting into an issue or a note.</summary>
    public static string Render(ScanFacts facts, ScanReport report, DateTime when)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(Strings.Scan_ScannedAt, facts.OwnerAndName.Length > 0 ? facts.OwnerAndName : facts.Folder, when.ToString("g")));
        sb.AppendLine(VerdictTitle(report.Verdict) + ". " + report.Summary);
        sb.AppendLine(NextStepText(report.Verdict));
        foreach (var severity in Enum.GetValues<ScanSeverity>())
        {
            var group = report.Of(severity).ToList();
            if (group.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine(GroupTitle(severity));
            foreach (var f in group)
            {
                sb.AppendLine("- " + f.Text + (f.Step != SetupStep.None ? $"  [{StepTitle(f.Step)}]" : ""));
                if (f.Detail.Length > 0)
                    foreach (var line in f.Detail.Split('\n')) sb.AppendLine("    " + line.TrimEnd('\r'));
            }
        }
        return sb.ToString();
    }
}
