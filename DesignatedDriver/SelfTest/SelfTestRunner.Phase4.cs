using DesignatedDriver.Core;
using DesignatedDriver.Models;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// Phase 4 checks: the release workflow fills and names the project, Guard exempts Dependabot, Dependabot
/// ecosystems follow the manifests in the repository, and the Status screen's billing, runs, timing and
/// alerts JSON parse from samples with the month's minutes added up as GitHub bills them.
/// </summary>
public static partial class SelfTestRunner
{
    private static void Phase4Checks(Action<bool, string> check)
    {
        // The release workflow
        var a = DotNetAnswers();
        a.ReleaseWorkflow = true;
        var files = ConventionContent.FilesFor(a, isPublic: false);
        var release = files.FirstOrDefault(f => f.Path == ".github/workflows/release.yml");
        check(release is not null && files.Count == 19, "Release: a .NET repository with the option on gets the workflow");
        var text = ConventionContent.Render(release!, a, ConventionContent.ValuesFor(a, null, "<TargetFramework>net10.0-windows</TargetFramework>"));
        check(!ConventionContent.HasUnfilled(text) && text.Contains("dotnet publish \"app/Example.csproj\"", StringComparison.Ordinal) && text.Contains("-p:SelfContained=false", StringComparison.Ordinal) && text.Contains("tags: ['v*']", StringComparison.Ordinal) && text.Contains("gh release create", StringComparison.Ordinal), "Release: fills, publishes the project framework-dependent on a version tag and creates the release");
        check(text.Contains("example-${{ github.ref_name }}.zip", StringComparison.Ordinal) && text.Contains("dotnet-version: '10.0.x'", StringComparison.Ordinal), "Release: the zip is named for the repository and the tag, the SDK from the project file");
        var node = new SetupAnswers { Owner = "paulfilkin", Name = "web", Language = ProjectLanguage.Node, ReleaseWorkflow = true, BuildCommand = "npm run build" };
        check(!ConventionContent.FilesFor(node, isPublic: false).Any(f => f.Path.EndsWith("release.yml", StringComparison.Ordinal)), "Release: not written for a Node.js project even with the option on");
        var off = DotNetAnswers();
        check(!ConventionContent.FilesFor(off, isPublic: false).Any(f => f.Path.EndsWith("release.yml", StringComparison.Ordinal)), "Release: not written without the option");

        // Guard and Dependabot templates
        var guard = ConventionContent.Render(files.First(f => f.Path == ".github/workflows/guard.yml"), a, ConventionContent.ValuesFor(a, null));
        check(guard.Contains("github.actor != 'dependabot[bot]'", StringComparison.Ordinal), "Guard: Dependabot's pull requests are exempt from the hygiene checks");
        check(ConventionContent.EcosystemsFor(ProjectLanguage.DotNet, ["README.md", "app/Example.csproj"]).SequenceEqual(["github-actions", "nuget"]), "Ecosystems: a project file brings nuget");
        check(ConventionContent.EcosystemsFor(ProjectLanguage.Python, ["README.md", "main.py"]).SequenceEqual(["github-actions"]), "Ecosystems: no manifest, no pip, whatever the language");
        check(ConventionContent.EcosystemsFor(ProjectLanguage.Node, ["web/package.json", "api/requirements.txt", "go.mod"]).SequenceEqual(["github-actions", "npm", "pip", "gomod"]), "Ecosystems: every manifest found is listed, in a fixed order");
        check(ConventionContent.EcosystemsFor(ProjectLanguage.DotNet, null).SequenceEqual(["github-actions", "nuget"]), "Ecosystems: the language's own when no files are known");
        var dependabot = ConventionContent.Render(files.First(f => f.Path == ".github/dependabot.yml"), a, ConventionContent.ValuesFor(a, null, "", ["README.md"]));
        check(dependabot.Contains("package-ecosystem: github-actions", StringComparison.Ordinal) && !dependabot.Contains("nuget", StringComparison.Ordinal), "dependabot.yml: follows the files, not the language, when files are known");

        // Status: billing, runs, timing and alerts
        var billing = GhJson.ParseRest<GhActionsBilling>(Sample("actions-billing.json"));
        check(billing.TotalMinutesUsed == 305 && billing.IncludedMinutes == 2000, "gh api billing/actions: minutes used and included");
        var runs = GhJson.ParseRest<GhWorkflowRunList>(Sample("workflow-runs.json"));
        check(runs.TotalCount == 3 && runs.WorkflowRuns.Count == 3 && runs.WorkflowRuns[0].Id == 44697132639 && runs.WorkflowRuns[0].CreatedAt.Month == 11, "gh api actions/runs: ids and dates");
        var timing = GhJson.ParseRest<GhRunTiming>(Sample("run-timing.json"));
        check(timing.RunDurationMs == 82000 && timing.Billable["WINDOWS"].TotalMs == 77000 && timing.Billable["UBUNTU"].Jobs == 1, "gh api runs/{id}/timing: billable milliseconds per runner");
        check(ActionsUsage.BillableMinutes([timing, timing]) == 6, "Actions: each job rounds up to the minute, so 77 s and 5 s twice is six minutes");
        check(ActionsUsage.BillableMinutes([]) == 0 && ActionsUsage.MonthStart(new DateTime(2026, 11, 20, 10, 30, 0)) == new DateTime(2026, 11, 1) && ActionsUsage.CreatedSince(new DateTime(2026, 11, 20)) == ">=2026-11-01", "Actions: the month starts on the first and the runs filter says so");
        var reflected = TidyManifest();
        var changed = DotNetAnswers();
        changed.ReleaseWorkflow = true;
        check(ConventionContent.ManifestReflects(reflected, DotNetAnswers()) && !ConventionContent.ManifestReflects(reflected, changed) && !ConventionContent.ManifestReflects(TidyManifest(withAnswers: false), DotNetAnswers()), "Manifest: reflects the answers it was written from, not changed options or missing answers");

        var alerts = GhJson.ParseRestList<GhDependabotAlert>(Sample("dependabot-alerts.json"));
        check(alerts.Count == 2 && alerts.Count(x => x.SecurityAdvisory?.Severity is "high" or "critical") == 1 && alerts[0].Number == 2, "gh api dependabot/alerts: count and severities");
    }
}
