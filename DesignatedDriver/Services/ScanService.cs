using System.IO;
using DesignatedDriver.Core;
using DesignatedDriver.Models;

namespace DesignatedDriver.Services;

/// <summary>
/// Gathers the facts a scan judges: from the clone through git and the file system, and from GitHub through gh.
/// Nothing here changes anything. Every failure to read a fact is recorded as "not known" rather than thrown,
/// so a scan always finishes with a report.
/// </summary>
public sealed class ScanService(Workspace workspace)
{
    private GitService Git => workspace.Git;
    private GitHubService GitHub => workspace.GitHub;

    /// <summary>Scans a clone, a folder that isn't one yet, or (with no folder) a repository by owner and name.</summary>
    public async Task<ScanFacts> ScanAsync(string? folder, string ownerAndName, CancellationToken ct)
    {
        var facts = new ScanFacts { Folder = folder?.Trim().TrimEnd('\\') ?? "", OwnerAndName = ownerAndName.Trim() };

        if (facts.HasClone) await ReadCloneAsync(facts, ct).ConfigureAwait(false);

        bool askGitHub = facts.OwnerAndName.Length > 0 && (!facts.HasClone || !facts.IsGitRepository || facts.OriginIsGitHub);
        if (askGitHub) await ReadGitHubAsync(facts, ct).ConfigureAwait(false);
        await ReadWorkflowTriggersAsync(facts, ct).ConfigureAwait(false);

        return facts;
    }

    /// <summary>Each workflow's triggers: from the clone when there is one, otherwise from GitHub.</summary>
    private async Task ReadWorkflowTriggersAsync(ScanFacts facts, CancellationToken ct)
    {
        var paths = facts.Files.Where(WorkflowTriggers.IsWorkflow).Concat(facts.Workflows.Select(w => w.Path).Where(WorkflowTriggers.IsWorkflow)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var path in paths)
        {
            string? text = null;
            var local = facts.HasClone ? Path.Combine(facts.Folder, path.Replace('/', '\\')) : "";
            if (local.Length > 0 && File.Exists(local))
            {
                try { text = await File.ReadAllTextAsync(local, ct).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* unreadable: triggers not known */ }
            }
            else if (facts.RemoteFound)
            {
                text = await GitHub.FileTextAsync(facts.OwnerAndName, path, ct).ConfigureAwait(false);
            }
            if (text is not null) facts.WorkflowTriggers[path] = WorkflowTriggers.Parse(text);
        }
    }

    private async Task ReadCloneAsync(ScanFacts facts, CancellationToken ct)
    {
        var folder = facts.Folder;
        var root = await Git.RepositoryRootAsync(folder, ct).ConfigureAwait(false);
        facts.IsGitRepository = root is not null;
        facts.Manifest = Manifest.Load(folder);
        facts.ManifestAtOldPath = Manifest.OnlyAtOldPath(folder);

        if (!facts.IsGitRepository)
        {
            facts.Files = ListFolder(folder);
            return;
        }

        facts.OriginUrl = await Git.OriginUrlInAsync(folder, ct).ConfigureAwait(false);
        if (facts.OwnerAndName.Length == 0 && facts.OriginIsGitHub && RemoteUrl.ParseOwnerAndRepository(facts.OriginUrl) is { } parsed)
            facts.OwnerAndName = $"{parsed.Owner}/{parsed.Repository}";

        facts.HasCommits = await Git.HasCommitsInAsync(folder, ct).ConfigureAwait(false);
        var status = await Git.StatusInAsync(folder, ct).ConfigureAwait(false);
        facts.WorkingTreeClean = status.IsClean;
        facts.ChangedFiles = status.Changes.Count;
        facts.CurrentBranch = status.Branch;
        facts.MergeInProgress = await Git.IsMergeInProgressInAsync(folder, ct).ConfigureAwait(false);
        facts.HooksPath = await Git.HooksPathInAsync(folder, ct).ConfigureAwait(false);
        facts.Files = await Git.ListFilesInAsync(folder, ct).ConfigureAwait(false);

        // A kept CONTRIBUTING.md gets the convention's section on how a change gets in, unless it already has it.
        var contributing = Path.Combine(folder, WorkflowSection.ContributingPath);
        if (File.Exists(contributing))
        {
            try { facts.ContributingHasWorkflowSection = WorkflowSection.IsIn(await File.ReadAllTextAsync(contributing, ct).ConfigureAwait(false)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* unreadable: not known */ }
        }

        // The labels the repository's own issue forms apply, so the scan can name a form a label rename affects.
        foreach (var form in facts.Files.Where(IssueForms.IsForm))
        {
            try { facts.IssueFormLabels[form] = IssueForms.Labels(await File.ReadAllTextAsync(Path.Combine(folder, form.Replace('/', '\\')), ct).ConfigureAwait(false)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* unreadable: no labels known */ }
        }
    }

    private async Task ReadGitHubAsync(ScanFacts facts, CancellationToken ct)
    {
        facts.RemoteLookedUp = true;
        var name = facts.OwnerAndName;

        if (!workspace.PlanProbed)
        {
            workspace.PlanName = await GitHub.PlanNameAsync(ct).ConfigureAwait(false);
            workspace.PlanProbed = true;
        }
        facts.PlanName = workspace.PlanName;

        facts.Details = await GitHub.RepositoryDetailsAsync(name, ct).ConfigureAwait(false);
        // A renamed or transferred repository answers to its old name; nothing more is read from the one it leads to.
        if (facts.Details is null || facts.MovedTo.Length > 0) return;

        try { facts.View = await GitHub.RepositoryAsync(name, GhRepository.ScanFields, ct).ConfigureAwait(false); }
        catch (CommandFailedException) { /* the REST answer carries most of it */ }

        try { facts.Labels = await GitHub.LabelsAsync(name, ct).ConfigureAwait(false); }
        catch (CommandFailedException) { /* no labels known: every convention label is reported missing */ }

        facts.Workflows = await GitHub.WorkflowsAsync(name, ct).ConfigureAwait(false);
        facts.Releases = await GitHub.ReleasesAsync(name, ct).ConfigureAwait(false);

        var (status, rules) = await GitHub.BranchRulesAsync(name, facts.DefaultBranch, ct).ConfigureAwait(false);
        facts.BranchRulesStatus = status;
        facts.BranchRules = rules;

        facts.VulnerabilityAlerts = await GitHub.VulnerabilityAlertsEnabledAsync(name, ct).ConfigureAwait(false);
        facts.SecurityUpdates = await GitHub.AutomatedSecurityFixesEnabledAsync(name, ct).ConfigureAwait(false);

        // Linked boards and their Status columns. A board's owner is taken to be the repository's owner.
        var owner = facts.Details.Owner?.Login ?? name.Split('/')[0];
        foreach (var project in facts.View?.LinkedProjects ?? [])
        {
            if (project.Closed) continue;
            List<string> options = [];
            try { options = (await GitHub.ProjectFieldsAsync(new BoardReference(owner, project.Number), ct).ConfigureAwait(false)).StatusOptions; }
            catch (CommandFailedException) { /* without the project scope the columns can't be read */ }
            facts.LinkedBoards.Add(new ScanBoard(project.Number, project.Title, project.Url, options));
        }

        if (!facts.HasClone)
        {
            facts.Files = await ListRemoteAsync(name, ct).ConfigureAwait(false);
            var manifestJson = await GitHub.FileContentAsync(name, Manifest.RelativePath.Replace('\\', '/'), ct).ConfigureAwait(false);
            if (manifestJson is null)
            {
                // A repository set up before the rename has the manifest under its old name.
                manifestJson = await GitHub.FileContentAsync(name, Manifest.OldRelativePath.Replace('\\', '/'), ct).ConfigureAwait(false);
                facts.ManifestAtOldPath = manifestJson is not null;
            }
            if (manifestJson is not null) facts.Manifest = Manifest.Parse(manifestJson);
        }
    }

    /// <summary>The paths the scan cares about, read from GitHub when there is no clone: the root and the convention's folders.</summary>
    private async Task<List<string>> ListRemoteAsync(string name, CancellationToken ct)
    {
        var files = new List<string>();
        foreach (var folder in new[] { "", ".github", ".github/workflows", ".github/ISSUE_TEMPLATE", ".githooks", "Docs", "docs" })
        {
            foreach (var entry in await GitHub.ContentsAsync(name, folder, ct).ConfigureAwait(false))
                if (!entry.IsDirectory) files.Add(entry.Path);
        }
        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Files up to two folders deep, for a folder that isn't a Git repository yet.</summary>
    private static List<string> ListFolder(string folder)
    {
        var files = new List<string>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { MaxRecursionDepth = 2, RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                var relative = Path.GetRelativePath(folder, path).Replace('\\', '/');
                if (relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) || relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase) || relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase)) continue;
                files.Add(relative);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return files;
    }
}
