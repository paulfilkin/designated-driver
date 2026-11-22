using DesignatedDriver.Models;
using DesignatedDriver.Resources;

namespace DesignatedDriver.Core;

/// <summary>The set-up steps from section 4.1 of the design, so a scan finding can say which step it belongs to.</summary>
public enum SetupStep { None, CreateRepository, Settings, Labels, Board, BoardToken, Files, Hooks, Security, PushProtection, Ruleset, Manifest, Register }

/// <summary>How a finding bears on converting the repository. Blockers stop it; the rest describe it.</summary>
public enum ScanSeverity { Blocker, Warning, Change, NotAvailable, Ok }

public enum ScanCode
{
    // Blockers
    NotGitHubOrigin, RepositoryNotFound, RepositoryMoved, Archived, NotAdmin,
    // Warnings
    Fork, Organisation, DirtyTree, MergeInProgress, DefaultBranchNotMain, ExistingFilesKept, ExistingBoardOtherColumns,
    HooksPathElsewhere, RulesAlreadyApply, NoLanguage, LabelsBothPresent, FormAppliesRenamedLabel, ReleasesElsewhere, CompetingWorkflows,
    // Changes
    InitGit, CreateRepository, MergeSettings, Features, CreateLabels, RecolourLabels, RenameLabels, CreateBoard, WriteFiles, PointHooks,
    DependabotAlerts, DependabotUpdates, SecretScanning, CreateRuleset, UpdateConvention, AppendWorkflowSection,
    // Not available on this plan
    RulesetsUnavailable, CodeOwnersUnavailable, PushProtectionUnavailable, CodeScanningUnavailable,
    // Already in place, or information
    AlreadySetUp, Visibility, BoardUsable, LanguageDetected, EmptyRepository, HasHistory, MergeSettingsOk, LabelsOk,
    HooksOk, OtherWorkflows, OtherLabels, OwnIssueFormsKept,
}

public enum ScanVerdict { NotConvertible, Convertible, AlreadySetUp }

public sealed record ScanFinding(ScanSeverity Severity, ScanCode Code, SetupStep Step, string Text, string Detail = "");

/// <summary>A board linked to the repository, with its Status columns.</summary>
public sealed record ScanBoard(int Number, string Title, string Url, IReadOnlyList<string> StatusOptions);

/// <summary>
/// Everything the scan learnt about a repository, from the clone (when there is one) and from GitHub (when the
/// repository is known). ScanRules judges it; ScanService fills it; the self-test builds it by hand.
/// </summary>
public sealed class ScanFacts
{
    // Where
    /// <summary>The clone's folder, or empty when the scan is by owner and name alone.</summary>
    public string Folder { get; set; } = "";
    public string OwnerAndName { get; set; } = "";
    public bool HasClone => Folder.Length > 0;

    // From the clone
    public bool IsGitRepository { get; set; }
    public string OriginUrl { get; set; } = "";
    public bool HasCommits { get; set; }
    public bool WorkingTreeClean { get; set; } = true;
    public int ChangedFiles { get; set; }
    public bool MergeInProgress { get; set; }
    public string HooksPath { get; set; } = "";
    public string CurrentBranch { get; set; } = "";

    /// <summary>Repository paths with forward slashes: tracked and untracked files from the clone, or a listing from GitHub.</summary>
    public List<string> Files { get; set; } = [];

    // From GitHub
    /// <summary>True when the scan asked GitHub about the repository, whether or not it was found.</summary>
    public bool RemoteLookedUp { get; set; }
    public GhRepositoryDetails? Details { get; set; }
    public GhRepository? View { get; set; }
    public List<GhLabel> Labels { get; set; } = [];
    public List<ScanBoard> LinkedBoards { get; set; } = [];
    public ApiStatus? BranchRulesStatus { get; set; }
    public List<GhBranchRule> BranchRules { get; set; } = [];
    public bool? VulnerabilityAlerts { get; set; }
    /// <summary>Dependabot security updates, from the automated-security-fixes endpoint; null when not read, in which case the details decide.</summary>
    public bool? SecurityUpdates { get; set; }
    public List<GhWorkflow> Workflows { get; set; } = [];
    /// <summary>The repository's most recent releases, newest first.</summary>
    public List<GhRelease> Releases { get; set; } = [];
    /// <summary>Each workflow's triggers, by path, read from the clone or from GitHub.</summary>
    public Dictionary<string, WorkflowTriggers> WorkflowTriggers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The labels each of the clone's issue forms applies, by path; empty when there is no clone.</summary>
    public Dictionary<string, List<string>> IssueFormLabels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Manifest? Manifest { get; set; }
    /// <summary>True when the manifest was found only under its name before the rename, .github/repo-starter.json.</summary>
    public bool ManifestAtOldPath { get; set; }
    public string PlanName { get; set; } = "";
    /// <summary>Whether the clone's CONTRIBUTING.md has the convention's section on how a change gets in; null when there is no file or it wasn't read.</summary>
    public bool? ContributingHasWorkflowSection { get; set; }

    // Derived
    public bool OriginIsGitHub => IsGitHubUrl(OriginUrl);
    /// <summary>The full name GitHub answered with when it differs from the one asked for: the repository was renamed or transferred.</summary>
    public string MovedTo => Details is not null && OwnerAndName.Length > 0 && !Details.IsNamed(OwnerAndName) ? Details.FullName : "";
    public bool RemoteFound => Details is not null && MovedTo.Length == 0;
    public bool IsPublic => Details?.IsPublic ?? false;
    public string DefaultBranch => string.IsNullOrEmpty(Details?.DefaultBranch) ? RepositorySettings.DefaultDefaultBranch : Details!.DefaultBranch;
    public bool IsEmptyRepository => HasClone && IsGitRepository ? !HasCommits : View?.IsEmpty ?? false;
    public Capabilities? Capabilities => Details is null ? null : Capabilities.From(PlanName, IsPublic, BranchRulesStatus);
    public bool HasFile(ConventionFile file) => Files.Any(file.Matches);

    public static bool IsGitHubUrl(string url)
    {
        var u = url.Trim();
        if (u.Length == 0) return false;
        // git@github-paulfilkin:owner/name.git names an alias from ~/.ssh/config that stands for github.com.
        var sshHost = RemoteUrl.SshHostOf(u);
        if (sshHost.Length > 0) return SshConfig.IsGitHubHost(sshHost);
        return Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ScanReport
{
    public ScanReport(ScanVerdict verdict, IReadOnlyList<ScanFinding> findings)
    {
        Verdict = verdict;
        Findings = findings;
    }

    public ScanVerdict Verdict { get; }
    public IReadOnlyList<ScanFinding> Findings { get; }

    public int Count(ScanSeverity severity) => Findings.Count(f => f.Severity == severity);
    public bool Has(ScanCode code) => Findings.Any(f => f.Code == code);
    public IEnumerable<ScanFinding> Of(ScanSeverity severity) => Findings.Where(f => f.Severity == severity);

    /// <summary>One line for the Repositories screen: the verdict and the counts that matter.</summary>
    public string Summary => Verdict switch
    {
        ScanVerdict.NotConvertible => string.Format(Strings.Scan_Summary_NotConvertible, Count(ScanSeverity.Blocker)),
        ScanVerdict.AlreadySetUp => Strings.Scan_Summary_AlreadySetUp,
        _ => string.Format(Strings.Scan_Summary_Convertible, Count(ScanSeverity.Change), Count(ScanSeverity.Warning)),
    };
}

/// <summary>
/// Judges a repository against the convention: what stops a conversion, what the set-up would change, what it
/// would keep, what this plan does not allow, and what is already in place. Pure, so the self-test can feed it
/// facts by hand.
/// </summary>
public static class ScanRules
{
    public static ScanReport Assess(ScanFacts f)
    {
        var findings = new List<ScanFinding>();
        void Add(ScanSeverity severity, ScanCode code, SetupStep step, string text, string detail = "") => findings.Add(new ScanFinding(severity, code, step, text, detail));

        // The clone
        if (f.HasClone)
        {
            if (!f.IsGitRepository)
            {
                Add(ScanSeverity.Change, ScanCode.InitGit, SetupStep.CreateRepository, Strings.Scan_InitGit);
            }
            else
            {
                if (f.OriginUrl.Length == 0)
                    Add(ScanSeverity.Change, ScanCode.CreateRepository, SetupStep.CreateRepository, Strings.Scan_CreateRepository);
                else if (!f.OriginIsGitHub)
                    Add(ScanSeverity.Blocker, ScanCode.NotGitHubOrigin, SetupStep.None, string.Format(Strings.Scan_NotGitHubOrigin, f.OriginUrl));

                if (f.MergeInProgress) Add(ScanSeverity.Warning, ScanCode.MergeInProgress, SetupStep.Files, Strings.Scan_MergeInProgress);
                else if (!f.WorkingTreeClean) Add(ScanSeverity.Warning, ScanCode.DirtyTree, SetupStep.Files, string.Format(Strings.Scan_DirtyTree, f.ChangedFiles));

                if (f.HooksPath.Length == 0)
                    Add(ScanSeverity.Change, ScanCode.PointHooks, SetupStep.Hooks, Strings.Scan_PointHooks);
                else if (IsConventionHooksPath(f.HooksPath))
                    Add(ScanSeverity.Ok, ScanCode.HooksOk, SetupStep.Hooks, Strings.Scan_HooksOk);
                else
                    Add(ScanSeverity.Warning, ScanCode.HooksPathElsewhere, SetupStep.Hooks, string.Format(Strings.Scan_HooksPathElsewhere, f.HooksPath));
            }
        }

        // GitHub
        if (f.RemoteLookedUp)
        {
            if (f.Details is null)
            {
                Add(ScanSeverity.Blocker, ScanCode.RepositoryNotFound, SetupStep.None, string.Format(Strings.Scan_RepositoryNotFound, f.OwnerAndName));
            }
            else if (f.MovedTo.Length > 0)
            {
                Add(ScanSeverity.Blocker, ScanCode.RepositoryMoved, SetupStep.None, string.Format(Strings.Scan_RepositoryMoved, f.OwnerAndName, f.MovedTo));
            }
            else
            {
                AssessRemote(f, f.Details, Add);
            }
        }

        // Files and language, from whichever listing there is
        if (f.Files.Count > 0 || (f.HasClone && f.IsGitRepository) || f.RemoteFound)
            AssessFiles(f, Add);

        // The manifest
        if (f.Manifest is { } m)
        {
            if (m.ConventionVersion < Manifest.CurrentConventionVersion)
                Add(ScanSeverity.Change, ScanCode.UpdateConvention, SetupStep.Manifest, string.Format(Strings.Scan_UpdateConvention, m.ConventionVersion, Manifest.CurrentConventionVersion));
            else
                Add(ScanSeverity.Ok, ScanCode.AlreadySetUp, SetupStep.Manifest, string.Format(Strings.Scan_AlreadySetUp, m.ConventionVersion, m.SetUp));
        }

        var ordered = findings.OrderBy(x => x.Severity).ToList();
        var verdict = ordered.Any(x => x.Severity == ScanSeverity.Blocker) ? ScanVerdict.NotConvertible
            : f.Manifest is { } current && current.ConventionVersion >= Manifest.CurrentConventionVersion && ordered.All(x => x.Severity != ScanSeverity.Change) ? ScanVerdict.AlreadySetUp
            : ScanVerdict.Convertible;
        return new ScanReport(verdict, ordered);
    }

    public static bool IsConventionHooksPath(string hooksPath)
    {
        var p = hooksPath.Replace('\\', '/').Trim().TrimEnd('/');
        return p.Equals(ConventionFiles.HooksFolder, StringComparison.OrdinalIgnoreCase) || p.EndsWith("/" + ConventionFiles.HooksFolder, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssessRemote(ScanFacts f, GhRepositoryDetails d, Action<ScanSeverity, ScanCode, SetupStep, string, string> add)
    {
        void Add(ScanSeverity s, ScanCode c, SetupStep st, string text, string detail = "") => add(s, c, st, text, detail);

        // Who and what
        if (d.Archived) Add(ScanSeverity.Blocker, ScanCode.Archived, SetupStep.None, Strings.Scan_Archived);
        bool admin = d.ViewerIsAdmin || (f.View?.IsAdmin ?? false);
        if (!admin) Add(ScanSeverity.Blocker, ScanCode.NotAdmin, SetupStep.None, string.Format(Strings.Scan_NotAdmin, f.OwnerAndName, f.View?.ViewerPermission ?? ""));
        if (d.Fork) Add(ScanSeverity.Warning, ScanCode.Fork, SetupStep.None, Strings.Scan_Fork);
        if (d.OwnerIsOrganization || (f.View?.IsInOrganization ?? false)) Add(ScanSeverity.Warning, ScanCode.Organisation, SetupStep.Board, string.Format(Strings.Scan_Organisation, d.Owner?.Login ?? ""));
        if (!f.DefaultBranch.Equals(RepositorySettings.DefaultDefaultBranch, StringComparison.Ordinal))
            Add(ScanSeverity.Warning, ScanCode.DefaultBranchNotMain, SetupStep.Files, string.Format(Strings.Scan_DefaultBranchNotMain, f.DefaultBranch));

        var caps = f.Capabilities!;
        Add(ScanSeverity.Ok, ScanCode.Visibility, SetupStep.None, f.IsPublic ? Strings.Scan_VisibilityPublic : string.Format(Strings.Scan_VisibilityPrivate, caps.PlanLabel));

        // Merge settings and features (section 3.4)
        var merge = new List<string>();
        if (d.AllowMergeCommit) merge.Add(Strings.Scan_Merge_CommitsOff);
        if (d.AllowRebaseMerge) merge.Add(Strings.Scan_Merge_RebaseOff);
        if (!d.AllowSquashMerge) merge.Add(Strings.Scan_Merge_SquashOn);
        if (!d.SquashMergeCommitTitle.Equals("PR_TITLE", StringComparison.OrdinalIgnoreCase)) merge.Add(Strings.Scan_Merge_SquashTitle);
        if (!d.DeleteBranchOnMerge) merge.Add(Strings.Scan_Merge_DeleteBranch);
        if (!d.AllowUpdateBranch) merge.Add(Strings.Scan_Merge_UpdateBranch);
        if (merge.Count > 0) Add(ScanSeverity.Change, ScanCode.MergeSettings, SetupStep.Settings, string.Format(Strings.Scan_MergeSettings, merge.Count), string.Join("\n", merge));
        else Add(ScanSeverity.Ok, ScanCode.MergeSettingsOk, SetupStep.Settings, Strings.Scan_MergeSettingsOk);

        var features = new List<string>();
        if (!d.HasIssues) features.Add(Strings.Scan_Feature_IssuesOn);
        if (!d.HasProjects) features.Add(Strings.Scan_Feature_ProjectsOn);
        if (d.HasWiki) features.Add(Strings.Scan_Feature_WikiOff);
        if (features.Count > 0) Add(ScanSeverity.Change, ScanCode.Features, SetupStep.Settings, string.Format(Strings.Scan_Features, features.Count), string.Join("\n", features));

        // Labels (section 3.2). GitHub's enhancement and documentation are renamed to feature and docs, not duplicated.
        var plan = ConventionLabels.Plan(f.Labels);
        var missing = plan.Where(p => p.Change == LabelChange.Create).Select(p => p.Label.Name).ToList();
        var differ = plan.Where(p => p.Change == LabelChange.Recolour).Select(p => p.Label.Name).ToList();
        var renames = plan.Where(p => p.Change == LabelChange.Rename).ToList();
        if (missing.Count > 0) Add(ScanSeverity.Change, ScanCode.CreateLabels, SetupStep.Labels, string.Format(Strings.Scan_CreateLabels, missing.Count), string.Join(", ", missing));
        if (differ.Count > 0) Add(ScanSeverity.Change, ScanCode.RecolourLabels, SetupStep.Labels, string.Format(Strings.Scan_RecolourLabels, differ.Count), string.Join(", ", differ));
        if (renames.Count > 0) Add(ScanSeverity.Change, ScanCode.RenameLabels, SetupStep.Labels, string.Format(Strings.Scan_RenameLabels, renames.Count), string.Join(", ", renames.Select(r => string.Format(Strings.Scan_LabelRename, r.From, r.Label.Name))));
        if (plan.Count == 0) Add(ScanSeverity.Ok, ScanCode.LabelsOk, SetupStep.Labels, string.Format(Strings.Scan_LabelsOk, ConventionLabels.All.Count));
        foreach (var (from, to) in ConventionLabels.BothPresent(f.Labels))
            Add(ScanSeverity.Warning, ScanCode.LabelsBothPresent, SetupStep.Labels, string.Format(Strings.Scan_LabelsBothPresent, from, to));
        foreach (var rename in renames)
            foreach (var (form, applied) in f.IssueFormLabels)
                if (applied.Contains(rename.From, StringComparer.OrdinalIgnoreCase))
                    Add(ScanSeverity.Warning, ScanCode.FormAppliesRenamedLabel, SetupStep.Labels, string.Format(Strings.Scan_FormAppliesRenamedLabel, form, rename.From, rename.Label.Name));
        int others = f.Labels.Count(l => !ConventionLabels.All.Any(w => w.Name.Equals(l.Name, StringComparison.OrdinalIgnoreCase)) && !renames.Any(r => r.From.Equals(l.Name, StringComparison.OrdinalIgnoreCase)));
        if (others > 0) Add(ScanSeverity.Ok, ScanCode.OtherLabels, SetupStep.Labels, string.Format(Strings.Scan_OtherLabels, others));

        // The board (section 3.3)
        var usable = f.LinkedBoards.FirstOrDefault(b => ConventionBoard.HasRequiredColumns(b.StatusOptions));
        if (usable is not null)
        {
            Add(ScanSeverity.Ok, ScanCode.BoardUsable, SetupStep.Board, string.Format(Strings.Scan_BoardUsable, usable.Number, usable.Title));
        }
        else
        {
            foreach (var b in f.LinkedBoards)
                Add(ScanSeverity.Warning, ScanCode.ExistingBoardOtherColumns, SetupStep.Board, string.Format(Strings.Scan_ExistingBoardOtherColumns, b.Number, b.Title), string.Join(", ", b.StatusOptions));
            Add(ScanSeverity.Change, ScanCode.CreateBoard, SetupStep.Board, Strings.Scan_CreateBoard);
        }

        // Dependabot and secret scanning (section 3.4)
        if (f.VulnerabilityAlerts != true) Add(ScanSeverity.Change, ScanCode.DependabotAlerts, SetupStep.Security, Strings.Scan_DependabotAlerts);
        if (!(f.SecurityUpdates ?? d.DependabotSecurityUpdatesOn)) Add(ScanSeverity.Change, ScanCode.DependabotUpdates, SetupStep.Security, Strings.Scan_DependabotUpdates);
        if (caps.PushProtection)
        {
            if (!d.SecretScanningOn || !d.PushProtectionOn) Add(ScanSeverity.Change, ScanCode.SecretScanning, SetupStep.Security, Strings.Scan_SecretScanning);
        }
        else
        {
            Add(ScanSeverity.NotAvailable, ScanCode.PushProtectionUnavailable, SetupStep.Security, Strings.Scan_PushProtectionUnavailable);
        }

        // Rules (sections 3.4 and 7)
        if (caps.Rulesets)
        {
            if (f.BranchRules.Count > 0)
                Add(ScanSeverity.Warning, ScanCode.RulesAlreadyApply, SetupStep.Ruleset, string.Format(Strings.Scan_RulesAlreadyApply, f.BranchRules.Count, f.DefaultBranch), string.Join(", ", f.BranchRules.Select(r => r.Type).Distinct()));
            else
                Add(ScanSeverity.Change, ScanCode.CreateRuleset, SetupStep.Ruleset, string.Format(Strings.Scan_CreateRuleset, f.DefaultBranch));
        }
        else
        {
            Add(ScanSeverity.NotAvailable, ScanCode.RulesetsUnavailable, SetupStep.Ruleset, Strings.Scan_RulesetsUnavailable);
        }
        if (!caps.CodeOwners) Add(ScanSeverity.NotAvailable, ScanCode.CodeOwnersUnavailable, SetupStep.Files, Strings.Scan_CodeOwnersUnavailable);
        if (!caps.CodeScanning) Add(ScanSeverity.NotAvailable, ScanCode.CodeScanningUnavailable, SetupStep.Files, Strings.Scan_CodeScanningUnavailable);

        // A repository that already publishes releases another way keeps the release workflow off (the wizard unticks it).
        var releaseReasons = ReleaseEvidence.Reasons(f);
        if (releaseReasons.Count > 0) Add(ScanSeverity.Warning, ScanCode.ReleasesElsewhere, SetupStep.Files, Strings.Scan_ReleasesElsewhere, string.Join("\n", releaseReasons));

        // Workflows that aren't the convention's. Those that build pull requests or the default branch would run beside
        // ci.yml, so each change would be built twice; the wizard offers to remove them in the set-up pull request.
        var competing = CompetingWorkflows.In(f);
        if (competing.Count > 0) Add(ScanSeverity.Warning, ScanCode.CompetingWorkflows, SetupStep.Files, string.Format(Strings.Scan_CompetingWorkflows, competing.Count, f.DefaultBranch), string.Join("\n", competing));
        var otherWorkflows = f.Workflows.Where(w => !ConventionFiles.All.Any(c => c.Matches(w.Path)) && !competing.Contains(w.Path, StringComparer.OrdinalIgnoreCase)).Select(w => w.Name).ToList();
        if (otherWorkflows.Count > 0) Add(ScanSeverity.Ok, ScanCode.OtherWorkflows, SetupStep.Files, string.Format(Strings.Scan_OtherWorkflows, otherWorkflows.Count), string.Join(", ", otherWorkflows));
    }

    private static void AssessFiles(ScanFacts f, Action<ScanSeverity, ScanCode, SetupStep, string, string> add)
    {
        void Add(ScanSeverity s, ScanCode c, SetupStep st, string text, string detail = "") => add(s, c, st, text, detail);

        var toWrite = new List<string>();
        var kept = new List<string>();
        var ownForms = IssueForms.OwnForms(f.Files);
        foreach (var file in ConventionFiles.All)
        {
            if (file.PublicOnly && !f.IsPublic) continue;
            if (file.Optional) continue;
            if (ownForms.Count > 0 && IssueForms.IsConventionForm(file.Path)) continue;
            bool present = f.HasFile(file);
            if (!present) toWrite.Add(file.Path);
            else if (!file.OnlyWhenMissing && !file.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) kept.Add(file.Path);
        }
        if (toWrite.Count > 0) Add(ScanSeverity.Change, ScanCode.WriteFiles, SetupStep.Files, string.Format(Strings.Scan_WriteFiles, toWrite.Count), string.Join("\n", toWrite));
        if (kept.Count > 0) Add(ScanSeverity.Warning, ScanCode.ExistingFilesKept, SetupStep.Files, string.Format(Strings.Scan_ExistingFilesKept, kept.Count), string.Join("\n", kept));
        if (f.ContributingHasWorkflowSection == false) Add(ScanSeverity.Change, ScanCode.AppendWorkflowSection, SetupStep.Files, Strings.Scan_AppendWorkflowSection);
        if (ownForms.Count > 0) Add(ScanSeverity.Ok, ScanCode.OwnIssueFormsKept, SetupStep.Files, string.Format(Strings.Scan_OwnIssueFormsKept, ownForms.Count), string.Join("\n", ownForms));

        if (f.IsEmptyRepository) Add(ScanSeverity.Ok, ScanCode.EmptyRepository, SetupStep.Files, string.Format(Strings.Scan_EmptyRepository, f.DefaultBranch));
        else if ((f.HasClone && f.IsGitRepository) || f.RemoteFound) Add(ScanSeverity.Ok, ScanCode.HasHistory, SetupStep.Files, Strings.Scan_HasHistory);

        var language = ProjectLanguage.Detect(f.Files);
        if (language is not null) Add(ScanSeverity.Ok, ScanCode.LanguageDetected, SetupStep.Files, string.Format(Strings.Scan_LanguageDetected, ProjectLanguage.DisplayName(language.Language), language.Evidence));
        else Add(ScanSeverity.Warning, ScanCode.NoLanguage, SetupStep.Files, Strings.Scan_NoLanguage);
    }
}
