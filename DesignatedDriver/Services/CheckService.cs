using System.IO;
using DesignatedDriver.Core;
using DesignatedDriver.Models;

namespace DesignatedDriver.Services;

/// <summary>What a Check found: the facts, the manifest (null when the repository was never set up) and the drift.</summary>
public sealed record CheckResult(ScanFacts Facts, Manifest? Manifest, DriftReport? Report, SetupAnswers? Answers)
{
    public bool IsSetUp => Report is not null;
}

/// <summary>
/// Gathers what the Check screen judges: the scan's facts, the rulesets where the plan allows them, and every
/// convention file as the convention would write it and as the clone has it. Nothing here changes anything.
/// </summary>
public sealed class CheckService(Workspace workspace)
{
    public async Task<CheckResult> CheckAsync(RepositorySettings repo, CancellationToken ct)
    {
        var facts = await new ScanService(workspace).ScanAsync(repo.HasFolder ? repo.Folder : null, repo.OwnerAndName, ct).ConfigureAwait(false);
        var manifest = facts.Manifest;
        if (manifest is null) return new CheckResult(facts, null, null, null);

        var answers = SetupAnswers.FromManifest(manifest, repo, facts);
        var rulesets = facts.Capabilities?.Rulesets == true ? await workspace.GitHub.RulesetsAsync(repo.OwnerAndName, ct).ConfigureAwait(false) : [];

        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var actual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (facts.HasClone && facts.IsGitRepository)
        {
            Dictionary<string, string>? values = null;
            if (manifest.HasAnswers)
            {
                var projectText = "";
                var projectPath = answers.ProjectFile.Length > 0 ? Path.Combine(repo.Folder, answers.ProjectFile) : "";
                if (answers.UsesProjectFile && projectPath.Length > 0 && File.Exists(projectPath))
                    projectText = await File.ReadAllTextAsync(projectPath, ct).ConfigureAwait(false);
                values = ConventionContent.ValuesFor(answers, manifest.Board, projectText, facts.Files);
            }
            foreach (var file in ConventionContent.FilesFor(answers, facts.IsPublic))
            {
                if (file.Path == ConventionContent.ManifestPath) continue;
                var found = facts.Files.FirstOrDefault(file.Matches);
                if (found is not null)
                {
                    var path = Path.Combine(repo.Folder, found.Replace('/', '\\'));
                    if (File.Exists(path)) actual[file.Path] = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                }
                if (values is not null && !file.OnlyWhenMissing) expected[file.Path] = ConventionContent.Render(file, answers, values);
            }
        }

        var report = DriftRules.Assess(new DriftInput { Facts = facts, Manifest = manifest, Expected = expected, Actual = actual, Rulesets = rulesets, Answers = answers });
        workspace.LastChecks[repo.OwnerAndName] = (report, DateTime.Now);
        return new CheckResult(facts, manifest, report, answers);
    }
}
