using DesignatedDriver.Core;
using DesignatedDriver.Resources;

namespace DesignatedDriver.Services;

/// <summary>
/// The Check screen's apply, and the two steps that exist only where the plan allows them. An update runs the
/// same steps as a set-up, limited to what was ticked, and takes its files through an issue and a pull request
/// on a branch named for the update.
/// </summary>
public sealed partial class SetupService
{
    private bool _update;
    private HashSet<string> _selectedFiles = new(StringComparer.OrdinalIgnoreCase);
    private Manifest? _existingManifest;
    private IReadOnlyList<string> _updateReasons = [];

    /// <summary>A plan for the ticked drift items of a repository that is already set up.</summary>
    public SetupPlan BuildUpdate(SetupAnswers answers, ScanFacts facts, DriftReport report, IEnumerable<string> selectedKeys)
    {
        var keys = selectedKeys.ToList();
        _a = answers;
        _facts = facts;
        _board = facts.LinkedBoards.FirstOrDefault(b => ConventionBoard.HasRequiredColumns(b.StatusOptions)) is { } usable
            ? new BoardReference(answers.Owner, usable.Number)
            : report.Manifest.Board;
        _capabilities = facts.Capabilities ?? Capabilities.From(workspace.PlanName, !answers.IsPrivate, null);
        _update = true;
        _existingManifest = report.Manifest;
        _selectedFiles = keys.Where(k => k.StartsWith("file:", StringComparison.Ordinal)).Select(k => k["file:".Length..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _updateReasons = report.Items.Where(i => keys.Contains(i.Key)).Select(i => i.Text).ToList();
        PullRequestUrl = "";
        Written = [];
        Kept = [];

        var steps = PlanOutline.ForUpdate(keys, report.ManifestOlder).Select(kind => kind switch
        {
            SetupStep.Settings => Step(kind, Strings.Setup_Detail_Settings, CheckSettingsAsync, ApplySettingsAsync),
            SetupStep.Labels => Step(kind, string.Format(Strings.Setup_Detail_Labels, ConventionLabels.All.Count), CheckLabelsAsync, ApplyLabelsAsync),
            SetupStep.Board => Step(kind, Strings.Setup_Detail_Board, CheckBoardAsync, ApplyBoardAsync),
            SetupStep.Files => Step(kind, string.Format(Strings.Setup_Detail_FilesUpdate, _selectedFiles.Count), CheckFilesAsync, ApplyFilesAsync),
            SetupStep.Hooks => Step(kind, Strings.Setup_Detail_Hooks, CheckHooksAsync, ApplyHooksAsync),
            SetupStep.Security => Step(kind, Strings.Setup_Detail_Security, CheckSecurityAsync, ApplySecurityAsync),
            SetupStep.PushProtection => Step(kind, Strings.Setup_Detail_PushProtection, CheckPushProtectionAsync, ApplyPushProtectionAsync),
            SetupStep.Ruleset => Step(kind, string.Format(Strings.Setup_Detail_Ruleset, _a.DefaultBranch, _a.RequiredCheck), CheckRulesetAsync, ApplyRulesetAsync),
            _ => throw new InvalidOperationException(kind.ToString()),
        }).ToList();
        return new SetupPlan(answers, steps);
    }

    // What the files step writes and says, in a set-up and in an update

    private string IssueTitle => _update ? ConventionContent.UpdateIssueTitle : ConventionContent.SetupIssueTitle;
    private string BranchSuffix => _update ? ConventionContent.UpdateBranchSuffix : ConventionContent.ConventionBranchSuffix;
    private string IssueBodyText() => _update ? SetupTexts.UpdateIssueBody(_updateReasons) : SetupTexts.IssueBody(_a);
    private string CommitMessageText(int issue) => _update ? SetupTexts.UpdateCommitMessage(issue, Written) : SetupTexts.CommitMessage(issue);
    private string PullRequestBodyText(int issue) => _update ? SetupTexts.UpdatePullRequestBody(issue, Written, _updateReasons) : SetupTexts.PullRequestBody(issue, Written, Kept);

    /// <summary>In an update only the ticked files and the manifest are written, and a ticked file is overwritten.</summary>
    private bool ShouldWrite(ConventionFile file, bool exists)
    {
        if (file.Path == ConventionContent.ManifestPath) return true;
        if (_update) return _selectedFiles.Contains(file.Path);
        return !exists;
    }

    // Secret scanning push protection (public repositories and paid plans)

    private async Task<bool> CheckPushProtectionAsync(CancellationToken ct)
    {
        var d = await GitHub.RepositoryDetailsAsync(Name, ct).ConfigureAwait(false);
        return d is not null && d.SecretScanningOn && d.PushProtectionOn;
    }

    private Task ApplyPushProtectionAsync(CancellationToken ct) => GitHub.EnablePushProtectionAsync(Name, ct);

    // The ruleset (public repositories and paid plans)

    private async Task<bool> CheckRulesetAsync(CancellationToken ct)
    {
        var rulesets = await GitHub.RulesetsAsync(Name, ct).ConfigureAwait(false);
        return rulesets.Any(r => Convention.IsConventionRuleset(r.Name) && r.Enforcement.Equals("active", StringComparison.OrdinalIgnoreCase));
    }

    private Task ApplyRulesetAsync(CancellationToken ct)
        => GitHub.CreateRulesetAsync(Name, RulesetDefinition.Build(_a.DefaultBranch, _a.RequiredCheck, _a.ReviewRequired), ct);
}
