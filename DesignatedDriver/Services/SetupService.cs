using System.IO;
using System.Text.Json;
using DesignatedDriver.Core;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;

namespace DesignatedDriver.Services;

/// <summary>
/// Builds a set-up plan from the wizard's answers and runs it (section 4.1 of the design): for each step, check,
/// skip if done, otherwise apply and check again. Every step is idempotent, so a plan that failed part way is
/// rerun and only does what is left. Steps that are already done stay done on a rerun.
/// </summary>
public sealed partial class SetupService(Workspace workspace)
{
    private GitService Git => workspace.Git;
    private GitHubService GitHub => workspace.GitHub;

    public event EventHandler<PlanStep>? StepChanged;

    /// <summary>The board token, given by the wizard and forgotten as soon as the secret is stored.</summary>
    public string? BoardToken { get; set; }

    // The run in hand
    private SetupAnswers _a = new();
    private ScanFacts? _facts;
    private Capabilities? _capabilities;
    private BoardReference? _board;
    private string _note = "";

    private string Folder => _a.Folder;
    private string Name => _a.OwnerAndName;

    public BoardReference? Board => _board;
    public string PullRequestUrl { get; private set; } = "";
    public string RepositoryUrl => $"https://github.com/{Name}";
    public IReadOnlyList<string> Written { get; private set; } = [];
    public IReadOnlyList<string> Kept { get; private set; } = [];
    /// <summary>Workflows the set-up removed because they built pull requests beside ci.yml.</summary>
    public IReadOnlyList<string> Removed { get; private set; } = [];

    public SetupPlan Build(SetupAnswers answers, ScanFacts? facts)
    {
        _a = answers;
        _facts = facts;
        _board = null;
        _update = false;
        _existingManifest = null;
        _selectedFiles.Clear();
        PullRequestUrl = "";
        Written = [];
        Kept = [];
        Removed = [];
        _capabilities = facts?.Capabilities ?? Capabilities.From(workspace.PlanName, !answers.IsPrivate, null);

        var steps = PlanOutline.For(answers, _capabilities).Select(kind => kind switch
        {
            SetupStep.PushProtection => Step(kind, Strings.Setup_Detail_PushProtection, CheckPushProtectionAsync, ApplyPushProtectionAsync),
            SetupStep.Ruleset => Step(kind, string.Format(Strings.Setup_Detail_Ruleset, answers.DefaultBranch, answers.RequiredCheck), CheckRulesetAsync, ApplyRulesetAsync),
            SetupStep.CreateRepository => Step(kind, CreateDetail(), CheckRepositoryAsync, CreateRepositoryAsync),
            SetupStep.Settings => Step(kind, Strings.Setup_Detail_Settings, CheckSettingsAsync, ApplySettingsAsync),
            SetupStep.Labels => Step(kind, string.Format(Strings.Setup_Detail_Labels, ConventionLabels.All.Count), CheckLabelsAsync, ApplyLabelsAsync),
            SetupStep.Board => Step(kind, Strings.Setup_Detail_Board, CheckBoardAsync, ApplyBoardAsync),
            SetupStep.BoardToken => Step(kind, Strings.Setup_Detail_BoardToken, CheckTokenAsync, ApplyTokenAsync),
            SetupStep.Files => Step(kind, FilesDetail(), CheckFilesAsync, ApplyFilesAsync),
            SetupStep.Hooks => Step(kind, Strings.Setup_Detail_Hooks, CheckHooksAsync, ApplyHooksAsync),
            SetupStep.Security => Step(kind, Strings.Setup_Detail_Security, CheckSecurityAsync, ApplySecurityAsync),
            SetupStep.Register => Step(kind, Strings.Setup_Detail_Register, CheckRegisteredAsync, RegisterAsync),
            _ => throw new InvalidOperationException(kind.ToString()),
        }).ToList();
        return new SetupPlan(answers, steps);
    }

    private static PlanStep Step(SetupStep kind, string detail, Func<CancellationToken, Task<bool>> check, Func<CancellationToken, Task> apply)
        => new(kind, ScanReportText.StepTitle(kind), detail, check, apply);

    /// <summary>Runs the plan from the first step that isn't done. False when a step failed; the step says why.</summary>
    public async Task<bool> RunAsync(SetupPlan plan, CancellationToken ct)
    {
        foreach (var step in plan.Steps)
        {
            if (step.IsDone) continue;
            _note = "";
            step.Message = "";
            Set(step, StepState.Checking);
            try
            {
                if (await step.Check(ct).ConfigureAwait(false))
                {
                    if (_note.Length > 0) step.Message = _note;
                    Set(step, StepState.AlreadyDone);
                    continue;
                }
                Set(step, StepState.Running);
                await step.Apply(ct).ConfigureAwait(false);
                if (await step.Check(ct).ConfigureAwait(false))
                {
                    if (_note.Length > 0) step.Message = _note;
                    Set(step, StepState.Applied);
                }
                else
                {
                    step.Message = Strings.Setup_CheckStillFails;
                    Set(step, StepState.Failed);
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                step.Message = Strings.Activity_Cancelled;
                Set(step, StepState.Pending);
                throw;
            }
            catch (Exception ex) when (ex is CommandFailedException or IOException or InvalidOperationException or UnauthorizedAccessException or JsonException)
            {
                step.Message = ex.Message;
                Set(step, StepState.Failed);
                return false;
            }
        }
        return true;
    }

    private void Set(PlanStep step, StepState state)
    {
        step.State = state;
        StepChanged?.Invoke(this, step);
    }

    // 1. The repository

    private string TransportLabel => Protocol.Equals(RemoteUrl.Ssh, StringComparison.OrdinalIgnoreCase) ? $"SSH ({SshHost})" : "HTTPS";

    private string CreateDetail() => _a.Source switch
    {
        SetupSource.NewRepository => string.Format(Strings.Setup_Detail_CreateNew, Name, _a.IsPrivate ? Strings.Status_Visibility_Private.ToLowerInvariant() : Strings.Status_Visibility_Public.ToLowerInvariant(), Folder, TransportLabel),
        SetupSource.ExistingRepository => string.Format(Strings.Setup_Detail_CreateClone, Name, Folder, TransportLabel),
        _ => string.Format(Strings.Setup_Detail_CreateFromFolder, Folder, Name, TransportLabel),
    };

    private string Protocol => _a.GitProtocol.Length > 0 ? _a.GitProtocol : RemoteUrl.Https;
    private string SshHost => _a.SshHost.Length > 0 ? _a.SshHost : "github.com";
    private string CloneUrl => RemoteUrl.For(Protocol, SshHost, Name);

    /// <summary>Origin uses the chosen protocol, and for SSH the chosen host or alias.</summary>
    private bool OriginIsAsChosen(string origin)
        => RemoteUrl.UsesProtocol(origin, Protocol) && (!Protocol.Equals(RemoteUrl.Ssh, StringComparison.OrdinalIgnoreCase) || RemoteUrl.SshHostOf(origin).Equals(SshHost, StringComparison.OrdinalIgnoreCase));

    private async Task<bool> CheckRepositoryAsync(CancellationToken ct)
    {
        if (!Directory.Exists(Folder)) return false;
        if (await Git.RepositoryRootAsync(Folder, ct).ConfigureAwait(false) is null) return false;
        var origin = await Git.OriginUrlInAsync(Folder, ct).ConfigureAwait(false);
        if (!OriginIsThisRepository(origin) || !OriginIsAsChosen(origin)) return false;
        if (await ThisRepositoryAsync(ct).ConfigureAwait(false) is null) return false;
        // A repository with commits counts as created once its default branch is on the remote.
        if (!await Git.HasCommitsInAsync(Folder, ct).ConfigureAwait(false)) return true;
        return await Git.RemoteBranchExistsAsync(Folder, _a.DefaultBranch, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The repository's details, or null when there is none. GitHub answers a renamed or transferred repository's old
    /// name with the repository it now is; setting that up would change the wrong repository, so the step stops.
    /// </summary>
    private async Task<GhRepositoryDetails?> ThisRepositoryAsync(CancellationToken ct)
    {
        var d = await GitHub.RepositoryDetailsAsync(Name, ct).ConfigureAwait(false);
        if (d is not null && !d.IsNamed(Name)) throw new InvalidOperationException(string.Format(Strings.Scan_RepositoryMoved, Name, d.FullName));
        return d;
    }

    private bool OriginIsThisRepository(string origin)
    {
        if (!ScanFacts.IsGitHubUrl(origin)) return false;
        var parsed = RemoteUrl.ParseOwnerAndRepository(origin);
        return parsed is not null && $"{parsed.Value.Owner}/{parsed.Value.Repository}".Equals(Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Clones and pushes with git over the protocol the user chose, rather than letting gh pick from its own
    /// configuration: a machine signed in over HTTPS with no SSH key would otherwise fail on the first push.
    /// </summary>
    private async Task CreateRepositoryAsync(CancellationToken ct)
    {
        switch (_a.Source)
        {
            case SetupSource.NewRepository:
                Directory.CreateDirectory(_a.ParentFolder);
                if (Directory.Exists(Folder) && Directory.EnumerateFileSystemEntries(Folder).Any()) throw new InvalidOperationException(string.Format(Strings.Setup_FolderNotEmpty, Folder));
                if (await ThisRepositoryAsync(ct).ConfigureAwait(false) is null)
                    await GitHub.CreateAsync(Name, _a.IsPrivate, _a.Description, ct).ConfigureAwait(false);
                await Git.CloneAsync(CloneUrl, Folder, ct).ConfigureAwait(false);
                if (!await Git.HasCommitsInAsync(Folder, ct).ConfigureAwait(false))
                    await Git.SetUnbornBranchAsync(Folder, _a.DefaultBranch, ct).ConfigureAwait(false);
                break;

            case SetupSource.ExistingRepository:
                Directory.CreateDirectory(_a.ParentFolder);
                if (Directory.Exists(Folder))
                {
                    var existingOrigin = await Git.OriginUrlInAsync(Folder, ct).ConfigureAwait(false);
                    if (OriginIsThisRepository(existingOrigin)) { await EnsureOriginAsync(ct).ConfigureAwait(false); return; }
                    if (Directory.EnumerateFileSystemEntries(Folder).Any()) throw new InvalidOperationException(string.Format(Strings.Setup_FolderNotEmpty, Folder));
                }
                await Git.CloneAsync(CloneUrl, Folder, ct).ConfigureAwait(false);
                break;

            default:
                if (!Directory.Exists(Folder)) throw new InvalidOperationException(Strings.Settings_FolderMissing);
                if (await Git.RepositoryRootAsync(Folder, ct).ConfigureAwait(false) is null)
                    await Git.InitAsync(Folder, _a.DefaultBranch, ct).ConfigureAwait(false);
                var origin = await Git.OriginUrlInAsync(Folder, ct).ConfigureAwait(false);
                if (origin.Length == 0)
                {
                    // gh adds origin on its own protocol; EnsureOriginAsync puts it right. An earlier run may have
                    // created the repository and stopped before the remote was added.
                    if (await ThisRepositoryAsync(ct).ConfigureAwait(false) is null)
                        await GitHub.CreateFromFolderAsync(Name, _a.IsPrivate, _a.Description, Folder, ct).ConfigureAwait(false);
                    else
                        await Git.AddRemoteAsync(Folder, CloneUrl, ct).ConfigureAwait(false);
                }
                else if (!OriginIsThisRepository(origin))
                {
                    throw new InvalidOperationException(string.Format(Strings.Setup_OriginElsewhere, origin, Name));
                }
                else if (await ThisRepositoryAsync(ct).ConfigureAwait(false) is null)
                {
                    throw new InvalidOperationException(string.Format(Strings.Setup_RepositoryMissing, Name));
                }
                await EnsureOriginAsync(ct).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>Origin on the chosen protocol, and the default branch on the remote when there are commits to push.</summary>
    private async Task EnsureOriginAsync(CancellationToken ct)
    {
        var origin = await Git.OriginUrlInAsync(Folder, ct).ConfigureAwait(false);
        if (!OriginIsAsChosen(origin)) await Git.SetRemoteUrlAsync(Folder, CloneUrl, ct).ConfigureAwait(false);
        if (await Git.HasCommitsInAsync(Folder, ct).ConfigureAwait(false) && !await Git.RemoteBranchExistsAsync(Folder, _a.DefaultBranch, ct).ConfigureAwait(false))
            await Git.PushInAsync(Folder, _a.DefaultBranch, ct).ConfigureAwait(false);
    }

    // 2. Settings

    private async Task<bool> CheckSettingsAsync(CancellationToken ct)
    {
        var d = await GitHub.RepositoryDetailsAsync(Name, ct).ConfigureAwait(false);
        if (d is null) return false;
        bool merge = !d.AllowMergeCommit && !d.AllowRebaseMerge && d.AllowSquashMerge && d.SquashMergeCommitTitle.Equals("PR_TITLE", StringComparison.OrdinalIgnoreCase)
                  && d.DeleteBranchOnMerge && d.AllowUpdateBranch;
        bool features = d.HasIssues && d.HasProjects && !d.HasWiki && d.HasDiscussions == _a.Discussions;
        bool description = _a.Description.Length == 0 || d.Description.Trim() == _a.Description.Trim();
        bool topics = _a.Topics.All(t => d.Topics.Contains(t, StringComparer.OrdinalIgnoreCase));
        return merge && features && description && topics;
    }

    private Task ApplySettingsAsync(CancellationToken ct) => GitHub.EditRepositoryAsync(Name, _a.Description, _a.Discussions, _a.Topics, ct);

    // 3. Labels

    // Only the labels that are missing or differ are written; a missing one GitHub has a default for is renamed from it.
    private async Task<bool> CheckLabelsAsync(CancellationToken ct)
        => ConventionLabels.Plan(await GitHub.LabelsAsync(Name, ct).ConfigureAwait(false)).Count == 0;

    private async Task ApplyLabelsAsync(CancellationToken ct)
    {
        foreach (var action in ConventionLabels.Plan(await GitHub.LabelsAsync(Name, ct).ConfigureAwait(false)))
        {
            if (action.Change == LabelChange.Rename) await GitHub.RenameLabelAsync(Name, action.From, action.Label, ct).ConfigureAwait(false);
            else await GitHub.CreateLabelAsync(Name, action.Label, ct).ConfigureAwait(false);
        }
    }

    // 4. Board

    private async Task<bool> CheckBoardAsync(CancellationToken ct)
    {
        GhRepository view;
        try { view = await GitHub.RepositoryAsync(Name, GhRepository.ScanFields, ct).ConfigureAwait(false); }
        catch (CommandFailedException) { return false; }
        foreach (var project in view.LinkedProjects.Where(p => !p.Closed))
        {
            var board = new BoardReference(_a.Owner, project.Number);
            try
            {
                var fields = await GitHub.ProjectFieldsAsync(board, ct).ConfigureAwait(false);
                if (ConventionBoard.HasRequiredColumns(fields.StatusOptions))
                {
                    _board = board;
                    _note = string.Format(Strings.Setup_BoardIs, project.Number, project.Title);
                    return true;
                }
            }
            catch (CommandFailedException) { /* not readable: try the next */ }
        }
        return false;
    }

    private async Task ApplyBoardAsync(CancellationToken ct)
    {
        // A project with the repository's name that an earlier run created but could not link is reused, not duplicated.
        var project = (await GitHub.ProjectsAsync(_a.Owner, ct).ConfigureAwait(false)).FirstOrDefault(p => !p.Closed && p.Title.Equals(_a.Name, StringComparison.OrdinalIgnoreCase))
            ?? await GitHub.CreateProjectAsync(_a.Name, ct).ConfigureAwait(false);
        var board = new BoardReference(string.IsNullOrEmpty(project.Owner?.Login) ? _a.Owner : project.Owner!.Login, project.Number);
        // The Status field is left as GitHub made it, so the built-in automations keep working.
        await GitHub.LinkProjectAsync(board, Name, ct).ConfigureAwait(false);
        _board = board;
    }

    // 5. Board token

    private async Task<bool> CheckTokenAsync(CancellationToken ct)
        => (await GitHub.SecretNamesAsync(Name, ct).ConfigureAwait(false)).Contains("PROJECT_TOKEN", StringComparer.OrdinalIgnoreCase);

    private async Task ApplyTokenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(BoardToken)) throw new InvalidOperationException(Strings.Setup_NoToken);
        await GitHub.SetSecretAsync(Name, "PROJECT_TOKEN", BoardToken.Trim(), ct).ConfigureAwait(false);
        BoardToken = null;
    }

    // 6. Files, committed straight to the default branch when the repository is empty, otherwise through a pull request

    private string FilesDetail()
    {
        int count = ConventionContent.FilesFor(_a, !_a.IsPrivate).Count;
        bool empty = _facts?.IsEmptyRepository ?? _a.Source == SetupSource.NewRepository;
        return empty ? string.Format(Strings.Setup_Detail_FilesDirect, count, _a.DefaultBranch) : string.Format(Strings.Setup_Detail_FilesPullRequest, count);
    }

    private async Task<bool> CheckFilesAsync(CancellationToken ct)
    {
        var manifest = Manifest.Load(Folder);
        if (manifest is null || manifest.ConventionVersion < Manifest.CurrentConventionVersion) return false;
        if (_board is not null && (manifest.Board is null || manifest.Board != _board)) return false;
        // A manifest from before the answers were recorded, or one whose answers or options no longer match what
        // was given, is brought up to date by running the set-up again.
        if (!manifest.HasAnswers) return false;
        if (!_update && !ConventionContent.ManifestReflects(manifest, _a)) return false;

        // In an update every ticked file must read as the convention writes it.
        var paths = new List<string> { ConventionContent.ManifestPath };
        if (_update)
        {
            var values = ConventionContent.ValuesFor(_a, _board, await ProjectFileTextAsync(ct).ConfigureAwait(false), await Git.ListFilesInAsync(Folder, ct).ConfigureAwait(false));
            foreach (var file in ConventionContent.FilesFor(_a, !_a.IsPrivate).Where(f => _selectedFiles.Contains(f.Path)))
            {
                var target = Path.Combine(Folder, file.Path.Replace('/', '\\'));
                if (!File.Exists(target)) return false;
                if (!LineDiff.Same(ConventionContent.Render(file, _a, values), await File.ReadAllTextAsync(target, ct).ConfigureAwait(false))) return false;
                paths.Add(file.Path);
            }
        }

        // Done once the working tree's versions are on the remote: on the default branch, or on a branch with its pull request.
        if (await Git.PathsMatchRefAsync(Folder, $"origin/{_a.DefaultBranch}", paths, ct).ConfigureAwait(false)) return true;
        foreach (var branch in await Git.RemoteBranchesMatchingAsync(Folder, $"chore/*-{BranchSuffix}", ct).ConfigureAwait(false))
        {
            if (!await Git.PathsMatchRefAsync(Folder, $"origin/{branch}", paths, ct).ConfigureAwait(false)) continue;
            var pr = await GitHub.PullRequestForBranchAsync(Name, branch, ct).ConfigureAwait(false);
            if (pr is not null) { PullRequestUrl = pr.Url; _note = string.Format(Strings.Setup_PullRequestOpen, pr.Url); }
            return true;
        }
        return false;
    }

    private async Task<string> ProjectFileTextAsync(CancellationToken ct)
    {
        if (!_a.UsesProjectFile || _a.ProjectFile.Length == 0) return "";
        var path = Path.Combine(Folder, _a.ProjectFile);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, ct).ConfigureAwait(false) : "";
    }

    private async Task ApplyFilesAsync(CancellationToken ct)
    {
        bool hasCommits = await Git.HasCommitsInAsync(Folder, ct).ConfigureAwait(false);
        int issue = 0;
        var branch = _a.DefaultBranch;

        if (hasCommits)
        {
            string issueUrl;
            var existing = await GitHub.FindOpenIssueAsync(Name, IssueTitle, ct).ConfigureAwait(false);
            if (existing is not null) (issue, issueUrl) = (existing.Number, existing.Url);
            else (issue, issueUrl) = await GitHub.CreateIssueAsync(Name, IssueTitle, IssueBodyText(), "chore", ct).ConfigureAwait(false);
            if (issue == 0) throw new InvalidOperationException(Strings.Setup_IssueNotCreated);

            if (_board is not null && issueUrl.Length > 0)
            {
                // GitHub's own automation puts it in Todo. The board is a convenience here; the files still go in if it can't be reached.
                try { await GitHub.AddToBoardAsync(_board, issueUrl, ct).ConfigureAwait(false); }
                catch (CommandFailedException) { }
            }

            branch = $"chore/{issue}-{BranchSuffix}";
            var current = await Git.CurrentBranchInAsync(Folder, ct).ConfigureAwait(false);
            if (current != branch)
            {
                if (await Git.LocalBranchExistsInAsync(Folder, branch, ct).ConfigureAwait(false))
                {
                    await Git.SwitchInAsync(Folder, branch, false, ct).ConfigureAwait(false);
                }
                else
                {
                    // Branch from the default branch when that is possible without disturbing anything.
                    if (current != _a.DefaultBranch
                        && (await Git.StatusInAsync(Folder, ct).ConfigureAwait(false)).IsClean
                        && await Git.LocalBranchExistsInAsync(Folder, _a.DefaultBranch, ct).ConfigureAwait(false))
                        await Git.SwitchInAsync(Folder, _a.DefaultBranch, false, ct).ConfigureAwait(false);
                    await Git.SwitchInAsync(Folder, branch, true, ct).ConfigureAwait(false);
                }
            }
        }

        var present = await Git.ListFilesInAsync(Folder, ct).ConfigureAwait(false);
        var values = ConventionContent.ValuesFor(_a, _board, await ProjectFileTextAsync(ct).ConfigureAwait(false), present);
        var written = new List<string>();
        var kept = new List<string>();
        foreach (var file in ConventionContent.FilesFor(_a, !_a.IsPrivate))
        {
            bool isManifest = file.Path == ConventionContent.ManifestPath;
            var target = Path.Combine(Folder, file.Path.Replace('/', '\\'));
            bool exists = File.Exists(target) || present.Any(file.Matches);
            if (!ShouldWrite(file, exists))
            {
                // A set-up keeps what exists; an update leaves alone what wasn't ticked.
                if (!_update) kept.Add(file.Path);
                continue;
            }
            var text = isManifest
                ? ConventionContent.ManifestFor(_a, _board, _capabilities, DateTime.Now, _existingManifest).ToJson() + "\n"
                : ConventionContent.Render(file, _a, values);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, text, new System.Text.UTF8Encoding(false), ct).ConfigureAwait(false);
            written.Add(file.Path);
        }
        Written = written;
        Kept = kept;

        await Git.AddPathsInAsync(Folder, written, ct).ConfigureAwait(false);
        // Workflows the user chose to remove go out in the same commit, so the pull request shows them going.
        var removed = _update ? [] : _a.RemoveWorkflows.Select(p => p.Replace('\\', '/'))
            .Where(p => CompetingWorkflows.MayRemove(p) && File.Exists(Path.Combine(Folder, p.Replace('/', '\\')))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        await Git.RemovePathsInAsync(Folder, removed, ct).ConfigureAwait(false);
        Removed = removed;
        await Git.MarkExecutableInAsync(Folder, written.Where(p => p.StartsWith(ConventionFiles.HooksFolder + "/", StringComparison.Ordinal)).ToList(), ct).ConfigureAwait(false);
        if (await Git.HasStagedChangesInAsync(Folder, ct).ConfigureAwait(false))
            await Git.CommitInAsync(Folder, CommitMessageText(issue), ct).ConfigureAwait(false);
        await Git.PushInAsync(Folder, branch, ct).ConfigureAwait(false);

        if (hasCommits)
        {
            var pr = await GitHub.PullRequestForBranchAsync(Name, branch, ct).ConfigureAwait(false);
            if (pr is null)
            {
                var created = await GitHub.CreatePullRequestAsync(Name, _a.DefaultBranch, branch, IssueTitle, PullRequestBodyText(issue), ct).ConfigureAwait(false);
                PullRequestUrl = created.Url;
            }
            else
            {
                PullRequestUrl = pr.Url;
            }
            _note = string.Format(Strings.Setup_FilesPullRequest, written.Count, kept.Count, PullRequestUrl);
        }
        else
        {
            _note = string.Format(Strings.Setup_FilesCommitted, written.Count, kept.Count, _a.DefaultBranch);
        }
    }

    // 7. Hooks

    private async Task<bool> CheckHooksAsync(CancellationToken ct)
        => ScanRules.IsConventionHooksPath(await Git.HooksPathInAsync(Folder, ct).ConfigureAwait(false));

    private Task ApplyHooksAsync(CancellationToken ct) => Git.SetHooksPathInAsync(Folder, ConventionFiles.HooksFolder, ct);

    // 8. Dependabot

    private async Task<bool> CheckSecurityAsync(CancellationToken ct)
    {
        var alerts = await GitHub.VulnerabilityAlertsEnabledAsync(Name, ct).ConfigureAwait(false);
        if (alerts != true) return false;
        return await GitHub.AutomatedSecurityFixesEnabledAsync(Name, ct).ConfigureAwait(false) == true;
    }

    private async Task ApplySecurityAsync(CancellationToken ct)
    {
        await GitHub.PutAsync($"repos/{Name}/vulnerability-alerts", ct).ConfigureAwait(false);
        await GitHub.PutAsync($"repos/{Name}/automated-security-fixes", ct).ConfigureAwait(false);
    }

    // 9. The tool's own settings

    private Task<bool> CheckRegisteredAsync(CancellationToken ct)
    {
        var known = workspace.Settings.Find(Name);
        bool ok = known is not null
            && known.Folder.TrimEnd('\\').Equals(Folder, StringComparison.OrdinalIgnoreCase)
            && (_board is null || (known.BoardOwner.Equals(_board.Owner, StringComparison.OrdinalIgnoreCase) && known.BoardNumber == _board.Number))
            && workspace.Settings.Current.Equals(Name, StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(ok);
    }

    private Task RegisterAsync(CancellationToken ct)
    {
        workspace.SaveRepository(new RepositorySettings
        {
            Folder = Folder,
            Owner = _a.Owner,
            Name = _a.Name,
            SelfTestCommand = _a.SelfTestCommand,
            RequiredCheck = _a.RequiredCheck,
            DefaultBranch = _a.DefaultBranch,
            BoardOwner = _board?.Owner ?? "",
            BoardNumber = _board?.Number ?? 0,
        });
        return Task.CompletedTask;
    }
}
