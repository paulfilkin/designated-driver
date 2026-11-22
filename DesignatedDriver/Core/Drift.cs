using DesignatedDriver.Models;
using DesignatedDriver.Resources;

namespace DesignatedDriver.Core;

/// <summary>How a repository has drifted from the convention in one respect.</summary>
public enum DriftKind { Missing, Different, Off, NotAvailable }

/// <summary>
/// One difference between the repository and the convention. Key names it for the apply; TickedByDefault is
/// false for files a project may legitimately tailor, so they are never overwritten without a deliberate tick.
/// </summary>
public sealed record DriftItem(string Key, SetupStep Step, DriftKind Kind, string Text, string Detail, bool TickedByDefault, string Path = "")
{
    public bool IsActionable => Kind != DriftKind.NotAvailable;
    public bool IsFile => Path.Length > 0;
}

public sealed class DriftReport(IReadOnlyList<DriftItem> items, Manifest manifest, bool manifestOlder)
{
    public IReadOnlyList<DriftItem> Items { get; } = items;
    public Manifest Manifest { get; } = manifest;
    /// <summary>True when the tool knows a newer convention than the manifest records.</summary>
    public bool ManifestOlder { get; } = manifestOlder;

    public IEnumerable<DriftItem> Actionable => Items.Where(i => i.IsActionable);
    public int Count => Actionable.Count();
    public bool IsClean => Count == 0 && !ManifestOlder;

    /// <summary>One line for Status and Repositories.</summary>
    public string Summary => IsClean
        ? string.Format(Strings.Drift_Summary_Clean, Manifest.ConventionVersion)
        : ManifestOlder ? string.Format(Strings.Drift_Summary_Update, Manifest.ConventionVersion, Manifest.CurrentConventionVersion, Count)
        : string.Format(Strings.Drift_Summary, Count);
}

/// <summary>Everything drift is judged from: the scan's facts, the manifest, and the convention files as expected and as found.</summary>
public sealed class DriftInput
{
    public required ScanFacts Facts { get; init; }
    public required Manifest Manifest { get; init; }
    /// <summary>Repository path to the convention's text, for the files the set-up answers allow rendering. Empty when the manifest has no answers.</summary>
    public IReadOnlyDictionary<string, string> Expected { get; init; } = new Dictionary<string, string>();
    /// <summary>Repository path to the clone's text, for the convention files that exist.</summary>
    public IReadOnlyDictionary<string, string> Actual { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<GhRuleset> Rulesets { get; init; } = [];
    public required SetupAnswers Answers { get; init; }
}

/// <summary>
/// Judges an already set-up repository against the convention (section 5 of the design, the Check screen).
/// Pure, so the self-test can feed it a changed label, a missing file, an edited hook and a changed setting.
/// </summary>
public static partial class DriftRules
{
    /// <summary>A file's text with the version after @ in every "uses:" line replaced, so a Dependabot bump reads the same as the template.</summary>
    public static string Comparable(string text) => ActionPin().Replace(text, "$1@*");

    [System.Text.RegularExpressions.GeneratedRegex(@"(uses:\s*[\w./-]+)@[\w.-]+")]
    private static partial System.Text.RegularExpressions.Regex ActionPin();

    /// <summary>Files a project may tailor; reported when they differ, but not ticked for overwriting by default.</summary>
    public static readonly IReadOnlyList<string> TailoredFiles = ["CONTRIBUTING.md", "Docs/Working with Git.md", ".github/pull_request_template.md", ".github/workflows/ci.yml", .. IssueForms.ConventionForms];

    public static bool IsTailored(string path) => TailoredFiles.Contains(path, StringComparer.OrdinalIgnoreCase);

    /// <summary>The Check item that renames a manifest from before the rename. A manifest: key, so applying it runs the files step.</summary>
    public const string ManifestRenameKey = "manifest:rename";

    public static DriftReport Assess(DriftInput input)
    {
        var f = input.Facts;
        var m = input.Manifest;
        var items = new List<DriftItem>();
        void Add(string key, SetupStep step, DriftKind kind, string text, string detail = "", bool ticked = true, string path = "")
            => items.Add(new DriftItem(key, step, kind, text, detail, ticked, path));

        // Settings and features (section 3.4)
        if (f.Details is { } d)
        {
            var merge = new List<string>();
            if (d.AllowMergeCommit) merge.Add(Strings.Scan_Merge_CommitsOff);
            if (d.AllowRebaseMerge) merge.Add(Strings.Scan_Merge_RebaseOff);
            if (!d.AllowSquashMerge) merge.Add(Strings.Scan_Merge_SquashOn);
            if (!d.SquashMergeCommitTitle.Equals("PR_TITLE", StringComparison.OrdinalIgnoreCase)) merge.Add(Strings.Scan_Merge_SquashTitle);
            if (!d.DeleteBranchOnMerge) merge.Add(Strings.Scan_Merge_DeleteBranch);
            if (!d.AllowUpdateBranch) merge.Add(Strings.Scan_Merge_UpdateBranch);
            if (!d.HasIssues) merge.Add(Strings.Scan_Feature_IssuesOn);
            if (!d.HasProjects) merge.Add(Strings.Scan_Feature_ProjectsOn);
            if (d.HasWiki) merge.Add(Strings.Scan_Feature_WikiOff);
            if (d.HasDiscussions != m.Options.Discussions) merge.Add(m.Options.Discussions ? Strings.Drift_DiscussionsOn : Strings.Drift_DiscussionsOff);
            if (merge.Count > 0) Add("settings", SetupStep.Settings, DriftKind.Different, string.Format(Strings.Drift_Settings, merge.Count), string.Join("\n", merge));
        }

        // Labels (section 3.2)
        foreach (var wanted in ConventionLabels.All)
        {
            var have = f.Labels.FirstOrDefault(l => l.Name.Equals(wanted.Name, StringComparison.OrdinalIgnoreCase));
            if (have is null)
            {
                // Applying it renames GitHub's default label when there is one, rather than adding a second label.
                var from = ConventionLabels.Renames.Where(r => r.To.Equals(wanted.Name, StringComparison.OrdinalIgnoreCase)).Select(r => r.From).FirstOrDefault(n => f.Labels.Any(l => l.Name.Equals(n, StringComparison.OrdinalIgnoreCase)));
                Add("label:" + wanted.Name, SetupStep.Labels, DriftKind.Missing, string.Format(Strings.Drift_LabelMissing, wanted.Name), from is null ? "" : string.Format(Strings.Drift_LabelRenamedFrom, from));
            }
            else if (!ConventionLabels.SameColour(have.Color, wanted.Color) || !ConventionLabels.SameDescription(have.Description, wanted.Description))
                Add("label:" + wanted.Name, SetupStep.Labels, DriftKind.Different, string.Format(Strings.Drift_LabelDiffers, wanted.Name), $"{have.Color} \"{have.Description}\" -> {wanted.Color} \"{wanted.Description}\"");
        }

        // Board (section 3.3)
        if (f.RemoteFound && !f.LinkedBoards.Any(b => ConventionBoard.HasRequiredColumns(b.StatusOptions)))
            Add("board", SetupStep.Board, DriftKind.Missing, Strings.Drift_Board);

        // Hooks (section 7)
        if (f.HasClone && f.IsGitRepository && !ScanRules.IsConventionHooksPath(f.HooksPath))
            Add("hooks", SetupStep.Hooks, DriftKind.Off, f.HooksPath.Length == 0 ? Strings.Drift_HooksOff : string.Format(Strings.Scan_HooksPathElsewhere, f.HooksPath));

        // Dependabot, push protection and the ruleset (section 3.4)
        if (f.RemoteFound)
        {
            if (f.VulnerabilityAlerts == false) Add("security:alerts", SetupStep.Security, DriftKind.Off, Strings.Drift_AlertsOff);
            if (f.SecurityUpdates == false) Add("security:updates", SetupStep.Security, DriftKind.Off, Strings.Drift_UpdatesOff);
            var caps = f.Capabilities!;
            if (caps.PushProtection)
            {
                if (f.Details is { } dd && (!dd.SecretScanningOn || !dd.PushProtectionOn)) Add("pushprotection", SetupStep.PushProtection, DriftKind.Off, Strings.Drift_PushProtectionOff);
            }
            else
            {
                // An organisation's private repository: its licence decides, not a personal plan (issue 58).
                Add("pushprotection", SetupStep.PushProtection, DriftKind.NotAvailable,
                    f.IsOrganisationPrivate ? Strings.Scan_OrganisationSecurity : Strings.Scan_PushProtectionUnavailable, ticked: false);
            }
            if (caps.Rulesets)
            {
                // Rules already in force that do what the convention's ruleset would need no ruleset of its own (issue 56).
                if (!input.Rulesets.Any(r => Convention.IsConventionRuleset(r.Name) && r.Enforcement.Equals("active", StringComparison.OrdinalIgnoreCase))
                    && !RuleCoverage.Covers(f.BranchRules))
                    Add("ruleset", SetupStep.Ruleset, DriftKind.Missing, string.Format(Strings.Drift_Ruleset, Convention.RulesetName(f.DefaultBranch)));
            }
            else
            {
                Add("ruleset", SetupStep.Ruleset, DriftKind.NotAvailable, Strings.Scan_RulesetsUnavailable, ticked: false);
            }
        }

        // Files (section 3.1): missing, or different from the convention's text
        if (f.ManifestAtOldPath)
        {
            // Set up before the app was renamed: applying writes the manifest under its new name and removes the old file.
            Add(ManifestRenameKey, SetupStep.Files, DriftKind.Different, string.Format(Strings.Drift_ManifestRename, Manifest.OldRelativePath.Replace('\\', '/'), ConventionContent.ManifestPath), path: ConventionContent.ManifestPath);
        }
        if (!m.HasAnswers)
        {
            // Only the set-up knows the answers (the wizard or --setup), so this is a note, not an item to apply.
            Add("manifest:answers", SetupStep.Manifest, DriftKind.NotAvailable, Strings.Drift_ManifestAnswers, ticked: false);
        }
        foreach (var file in ConventionContent.FilesFor(input.Answers, f.IsPublic))
        {
            if (file.Path == ConventionContent.ManifestPath) continue;
            bool present = input.Actual.ContainsKey(file.Path) || f.HasFile(file);
            if (!present)
            {
                // A project chooses its own issue forms, so a missing one is reported but not ticked.
                Add("file:" + file.Path, SetupStep.Files, DriftKind.Missing, string.Format(Strings.Drift_FileMissing, file.Path), ticked: !IssueForms.IsConventionForm(file.Path), path: file.Path);
                continue;
            }
            if (file.OnlyWhenMissing) continue;
            if (!input.Expected.TryGetValue(file.Path, out var rawExpected) || !input.Actual.TryGetValue(file.Path, out var rawActual)) continue;
            // Dependabot bumps the versions the templates pin, and that is not drift.
            var expected = Comparable(rawExpected);
            var actual = Comparable(rawActual);
            if (LineDiff.Same(expected, actual)) continue;
            var (removed, added) = LineDiff.Counts(expected, actual);
            Add("file:" + file.Path, SetupStep.Files, DriftKind.Different, string.Format(Strings.Drift_FileDiffers, file.Path, removed, added), LineDiff.Render(expected, actual), ticked: !IsTailored(file.Path), path: file.Path);
        }

        // A CONTRIBUTING.md of the project's own without the convention's section on how a change gets in. Applying it
        // adds the section at the end and leaves the rest alone, so it is offered on its own, unticked.
        if (input.Actual.TryGetValue(WorkflowSection.ContributingPath, out var contributing) && !WorkflowSection.IsIn(contributing))
            Add(WorkflowSection.DriftKey, SetupStep.Files, DriftKind.Missing, Strings.Drift_WorkflowSectionMissing, ticked: false, path: WorkflowSection.ContributingPath);

        bool older = m.ConventionVersion < Manifest.CurrentConventionVersion;
        return new DriftReport(items, m, older);
    }
}
