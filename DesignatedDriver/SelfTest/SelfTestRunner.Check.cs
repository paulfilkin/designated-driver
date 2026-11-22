using System.IO;
using System.Text.Json;
using DesignatedDriver.Core;
using DesignatedDriver.Models;
using DesignatedDriver.Services;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// Phase 3 checks (section 8 of the design): drift detection reports a changed label, a missing file, an edited
/// hook and a changed setting and nothing for an identical repository; the ruleset JSON matches a saved real
/// one; the CodeQL template fills per language; an apply runs only the ticked items.
/// </summary>
public static partial class SelfTestRunner
{
    private static Manifest TidyManifest(bool withAnswers = true)
    {
        var a = DotNetAnswers();
        var m = ConventionContent.ManifestFor(a, new BoardReference("paulfilkin", 7), Capabilities.From("", false, ApiStatus.NotAvailable), new DateTime(2026, 11, 20));
        if (!withAnswers) m.Answers = null;
        return m;
    }

    /// <summary>A private repository exactly as the set-up leaves it, with every convention file as rendered.</summary>
    private static (ScanFacts Facts, Dictionary<string, string> Expected, Dictionary<string, string> Actual) TidyRepository()
    {
        var a = DotNetAnswers();
        var board = new BoardReference("paulfilkin", 7);
        var values = ConventionContent.ValuesFor(a, board, "<TargetFramework>net10.0-windows</TargetFramework>");
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in ConventionContent.FilesFor(a, isPublic: false).Where(f => f.Path != ConventionContent.ManifestPath && !f.OnlyWhenMissing))
            expected[file.Path] = ConventionContent.Render(file, a, values);
        var actual = new Dictionary<string, string>(expected, StringComparer.OrdinalIgnoreCase) { ["README.md"] = "# example\n", [".gitignore"] = "bin/\n" };
        var facts = new ScanFacts
        {
            Folder = @"C:\Users\paul\Documents\GIT\example", OwnerAndName = "paulfilkin/example",
            IsGitRepository = true, OriginUrl = "git@github-paulfilkin:paulfilkin/example.git", HasCommits = true, HooksPath = ".githooks",
            Files = [.. actual.Keys, "app/Example.csproj", ConventionContent.ManifestPath],
            RemoteLookedUp = true, Details = GhJson.ParseRest<GhRepositoryDetails>(Sample("repo-details-private.json").Replace("paulfilkin/tidy", "paulfilkin/example")),
            Labels = ConventionLabels.All.Select(l => new GhLabel { Name = l.Name, Color = l.Color, Description = l.Description }).ToList(),
            LinkedBoards = [new ScanBoard(7, "example", "https://github.com/users/paulfilkin/projects/7", ["Todo", "In Progress", "Done"])],
            BranchRulesStatus = ApiStatus.NotAvailable, VulnerabilityAlerts = true, SecurityUpdates = true, Manifest = TidyManifest(),
        };
        return (facts, expected, actual);
    }

    private static DriftReport Drift(ScanFacts facts, Dictionary<string, string> expected, Dictionary<string, string> actual, Manifest? manifest = null, IReadOnlyList<GhRuleset>? rulesets = null)
        => DriftRules.Assess(new DriftInput { Facts = facts, Manifest = manifest ?? facts.Manifest!, Expected = expected, Actual = actual, Rulesets = rulesets ?? [], Answers = DotNetAnswers() });

    // 20. Drift detection

    private static void DriftChecks(Action<bool, string> check)
    {
        var (facts, expected, actual) = TidyRepository();
        var clean = Drift(facts, expected, actual);
        check(clean.IsClean && clean.Count == 0 && clean.Items.All(i => i.Kind == DriftKind.NotAvailable), $"Drift: an identical private repository reports nothing to do ({clean.Items.Count} not-available notes only)");
        check(clean.Items.Any(i => i.Key == "ruleset" && !i.IsActionable) && clean.Items.Any(i => i.Key == "pushprotection" && !i.IsActionable), "Drift: the ruleset and push protection are noted as not available on private Free, unticked");
        check(clean.Summary.Contains("v1", StringComparison.Ordinal), "Drift: the clean summary names the convention version");

        // A changed label colour
        var recoloured = Clone(facts);
        recoloured.Labels = facts.Labels.Select(l => l.Name == "bug" ? new GhLabel { Name = "bug", Color = "000000", Description = l.Description } : l).ToList();
        var r1 = Drift(recoloured, expected, actual);
        check(r1.Count == 1 && r1.Actionable.Single() is { Key: "label:bug", Kind: DriftKind.Different, Step: SetupStep.Labels, TickedByDefault: true }, "Drift: a changed label colour is one ticked item under Labels");

        // A missing label
        var missing = Clone(facts);
        missing.Labels = facts.Labels.Where(l => l.Name != "blocked").ToList();
        check(Drift(missing, expected, actual).Actionable.Single() is { Key: "label:blocked", Kind: DriftKind.Missing }, "Drift: a missing label is one item");

        // A missing file
        var withoutGuard = new Dictionary<string, string>(actual, StringComparer.OrdinalIgnoreCase);
        withoutGuard.Remove(".github/workflows/guard.yml");
        var noGuardFacts = Clone(facts);
        noGuardFacts.Files = facts.Files.Where(f => f != ".github/workflows/guard.yml").ToList();
        var r2 = Drift(noGuardFacts, expected, withoutGuard);
        check(r2.Count == 1 && r2.Actionable.Single() is { Key: "file:.github/workflows/guard.yml", Kind: DriftKind.Missing, Path: ".github/workflows/guard.yml", TickedByDefault: true }, "Drift: a missing workflow is one ticked file item");

        // An edited hook, and an edited tailored file
        var edited = new Dictionary<string, string>(actual, StringComparer.OrdinalIgnoreCase);
        edited[".githooks/pre-push"] = actual[".githooks/pre-push"].Replace("exit 1", "exit 0");
        edited["CONTRIBUTING.md"] = actual["CONTRIBUTING.md"] + "\n## 11. Our own rules\n\nNo tabs.\n";
        var r3 = Drift(facts, expected, edited);
        var hook = r3.Actionable.FirstOrDefault(i => i.Key == "file:.githooks/pre-push");
        var guide = r3.Actionable.FirstOrDefault(i => i.Key == "file:CONTRIBUTING.md");
        check(r3.Count == 2 && hook is { Kind: DriftKind.Different, TickedByDefault: true } && guide is { Kind: DriftKind.Different, TickedByDefault: false }, "Drift: an edited hook is ticked, an edited CONTRIBUTING.md is reported but unticked");
        check(hook!.Detail.Contains("- ", StringComparison.Ordinal) && hook.Detail.Contains("exit 1", StringComparison.Ordinal) && hook.Detail.Contains("+ ", StringComparison.Ordinal) && guide!.Text.Contains("4 added", StringComparison.Ordinal), "Drift: the detail is a line diff and the text counts the lines");

        // A CONTRIBUTING.md of the project's own, without the section on how a change gets in: its own unticked item
        var ownGuide = new Dictionary<string, string>(actual, StringComparer.OrdinalIgnoreCase) { ["CONTRIBUTING.md"] = "# Contributing\n\nBuild with make.\n" };
        var rGuide = Drift(facts, expected, ownGuide);
        check(rGuide.Actionable.FirstOrDefault(i => i.Key == WorkflowSection.DriftKey) is { Kind: DriftKind.Missing, TickedByDefault: false, Step: SetupStep.Files }
            && !r3.Actionable.Any(i => i.Key == WorkflowSection.DriftKey) && !clean.Items.Any(i => i.Key == WorkflowSection.DriftKey)
            && PlanOutline.ForUpdate([WorkflowSection.DriftKey], false).SequenceEqual([SetupStep.Files]),
            "Drift: a CONTRIBUTING.md without the section is an unticked item that applies through the files step; one with it isn't reported");
        // Issue forms are the project's to choose: an edited or missing one is reported, unticked
        var forms = new Dictionary<string, string>(actual, StringComparer.OrdinalIgnoreCase);
        forms[".github/ISSUE_TEMPLATE/bug.yml"] = actual[".github/ISSUE_TEMPLATE/bug.yml"] + "\n# Our own question\n";
        forms.Remove(".github/ISSUE_TEMPLATE/feature.yml");
        var formFacts = Clone(facts);
        formFacts.Files = facts.Files.Where(f => f != ".github/ISSUE_TEMPLATE/feature.yml").ToList();
        var rForms = Drift(formFacts, expected, forms);
        check(rForms.Count == 2 && rForms.Actionable.FirstOrDefault(i => i.Key == "file:.github/ISSUE_TEMPLATE/bug.yml") is { Kind: DriftKind.Different, TickedByDefault: false }
            && rForms.Actionable.FirstOrDefault(i => i.Key == "file:.github/ISSUE_TEMPLATE/feature.yml") is { Kind: DriftKind.Missing, TickedByDefault: false }, "Drift: an edited or missing issue form is reported but unticked, as a tailored file");

        // Line endings and trailing spaces are not drift
        var crlf = new Dictionary<string, string>(actual, StringComparer.OrdinalIgnoreCase);
        crlf[".github/workflows/ci.yml"] = actual[".github/workflows/ci.yml"].Replace("\n", "  \r\n");
        check(Drift(facts, expected, crlf).Count == 0, "Drift: CRLF endings and trailing spaces do not count");

        // Dependabot bumps the pinned action versions; the convention does not fight it
        var bumped = new Dictionary<string, string>(actual, StringComparer.OrdinalIgnoreCase);
        bumped[".github/workflows/guard.yml"] = actual[".github/workflows/guard.yml"].Replace("github-script@v9", "github-script@v11");
        bumped[".github/workflows/board.yml"] = actual[".github/workflows/board.yml"].Replace("add-to-project@v2.0.1", "add-to-project@v2.1.0");
        check(Drift(facts, expected, bumped).Count == 0 && DriftRules.Comparable("uses: actions/checkout@v7") == DriftRules.Comparable("uses: actions/checkout@v8"), "Drift: a bumped action version is not drift");

        // A changed merge setting
        var merge = Clone(facts);
        merge.Details = GhJson.ParseRest<GhRepositoryDetails>(Sample("repo-details-private.json").Replace("paulfilkin/tidy", "paulfilkin/example").Replace("\"delete_branch_on_merge\": true", "\"delete_branch_on_merge\": false"));
        var r4 = Drift(merge, expected, actual);
        check(r4.Count == 1 && r4.Actionable.Single() is { Key: "settings", Step: SetupStep.Settings } s && s.Detail.Contains("Delete branch", StringComparison.Ordinal), "Drift: a changed merge setting is one item naming the setting");

        // Hooks path, Dependabot, board
        var off = Clone(facts);
        off.HooksPath = ""; off.VulnerabilityAlerts = false; off.SecurityUpdates = false; off.LinkedBoards = [];
        var r5 = Drift(off, expected, actual);
        check(r5.Actionable.Select(i => i.Key).OrderBy(k => k).SequenceEqual(["board", "hooks", "security:alerts", "security:updates"]), "Drift: hooks path, both Dependabot switches and a missing board are each one item");

        // A manifest without answers, and an older manifest
        var r6 = Drift(facts, new Dictionary<string, string>(), actual, TidyManifest(withAnswers: false));
        check(r6.Count == 0 && r6.Items.Any(i => i.Key == "manifest:answers" && !i.IsActionable) && !r6.Items.Any(i => i.IsFile), "Drift: a manifest without answers is noted, not applied, and the file comparison is skipped");
        var old = TidyManifest(); old.ConventionVersion = 0;
        var r7 = Drift(facts, expected, actual, old);
        check(r7.ManifestOlder && !r7.IsClean && r7.Summary.Contains("v0", StringComparison.Ordinal), "Drift: an older manifest is an update even with nothing else to do");

        // Public: the ruleset and push protection count
        var pub = Clone(facts);
        pub.Details = GhJson.ParseRest<GhRepositoryDetails>(Sample("repo-details-private.json").Replace("paulfilkin/tidy", "paulfilkin/example").Replace("\"private\": true", "\"private\": false"));
        pub.BranchRulesStatus = ApiStatus.Ok;
        var r8 = Drift(pub, expected, actual);
        check(r8.Actionable.Any(i => i.Key == "ruleset" && i.Kind == DriftKind.Missing) && r8.Actionable.Any(i => i.Key == "pushprotection" && i.Kind == DriftKind.Off), "Drift: on a public repository a missing ruleset and push protection off are items");
        var r9 = Drift(pub, expected, actual, rulesets: [new GhRuleset { Id = 1, Name = "Protect main", Target = "branch", Enforcement = "active" }]);
        check(!r9.Actionable.Any(i => i.Key == "ruleset"), "Drift: an active ruleset named for the branch satisfies the check");
    }

    // 21. The ruleset, the line diff and CodeQL

    private static void RulesetChecks(Action<bool, string> check)
    {
        var def = RulesetDefinition.Build("main", "Build and test", reviewRequired: true);
        var json = def.ToJson();
        var real = JsonSerializer.Deserialize<GhRulesetDetail>(Sample("ruleset-sample.json"), RulesetDefinition.JsonOptions)!;
        var ours = JsonSerializer.Deserialize<GhRulesetDetail>(json, RulesetDefinition.JsonOptions)!;
        check(ours.Rules.Select(r => r.Type).OrderBy(t => t).SequenceEqual(real.Rules.Select(r => r.Type).OrderBy(t => t)), "Ruleset: the same five rule types as the sample ruleset");
        check(ours.Target == real.Target && ours.Enforcement == real.Enforcement && ours.Conditions.RefName.Include.SequenceEqual(real.Conditions.RefName.Include), "Ruleset: branch target, active, the default branch");
        check(ours.BypassActors.Count == 1 && ours.BypassActors[0].ActorId == real.BypassActors[0].ActorId && ours.BypassActors[0].BypassMode == "pull_request", "Ruleset: repository admins bypass through a pull request, as in the sample ruleset");
        check(json.Contains("\"context\": \"Build and test\"", StringComparison.Ordinal) && json.Contains("\"integration_id\": 15368", StringComparison.Ordinal) && json.Contains("\"required_approving_review_count\": 1", StringComparison.Ordinal) && json.Contains("\"allowed_merge_methods\"", StringComparison.Ordinal) && json.Contains("\"squash\"", StringComparison.Ordinal), "Ruleset: the check name, Actions as its source, one review and squash only");
        check(RulesetDefinition.Build("main", "Build and test", reviewRequired: false).ToJson().Contains("\"required_approving_review_count\": 0", StringComparison.Ordinal), "Ruleset: no review required means zero approvals");
        check(def.Name == "Protect main" && Convention.IsConventionRuleset("Protect main") && !Convention.IsConventionRuleset("Protect main branch".Replace("Protect ", "Guard ")), "Ruleset: named for the branch it protects");

        check(LineDiff.Same("a\nb\n", "a\r\nb") && !LineDiff.Same("a\nb", "a\nc"), "Line diff: endings and a final newline don't matter, content does");
        var rendered = LineDiff.Render("one\ntwo\nthree\nfour\nfive\nsix\nseven", "one\ntwo\nthree\nFOUR\nfive\nsix\nseven");
        check(rendered.Contains("- four", StringComparison.Ordinal) && rendered.Contains("+ FOUR", StringComparison.Ordinal) && rendered.Contains("  two", StringComparison.Ordinal) && !rendered.Contains("  one", StringComparison.Ordinal), "Line diff: removed and added lines with two lines of context");
        check(LineDiff.Render("same\n", "same\n") == "" && LineDiff.Counts("a\nb\nc", "a\nc\nd") == (1, 1), "Line diff: empty for equal texts, and the counts");

        var pub = DotNetAnswers(isPrivate: false);
        pub.CodeQl = true;
        var files = ConventionContent.FilesFor(pub, isPublic: true);
        check(files.Count == 19 && files.Any(f => f.Path == ".github/workflows/codeql.yml"), "CodeQL: a public .NET repository with the option on gets the workflow");
        check(ConventionContent.FilesFor(DotNetAnswers(isPrivate: false), isPublic: true).Count == 18, "CodeQL: not without the option");
        var codeql = ConventionContent.Render(files.First(f => f.Path == ".github/workflows/codeql.yml"), pub, ConventionContent.ValuesFor(pub, null));
        check(!ConventionContent.HasUnfilled(codeql) && codeql.Contains("languages: csharp", StringComparison.Ordinal) && codeql.Contains("build-mode: none", StringComparison.Ordinal), "CodeQL: fills for .NET with csharp and no build");
        var go = new SetupAnswers { Owner = "paulfilkin", Name = "svc", Language = ProjectLanguage.Go, CodeQl = true, BuildCommand = "go build ./..." };
        check(ConventionContent.ValuesFor(go, null)["codeqlBuildMode"] == "autobuild" && ConventionContent.CodeQlLanguage(ProjectLanguage.Java) == "java-kotlin" && ConventionContent.CodeQlLanguage("cobol") == "", "CodeQL: Go autobuilds, Java maps, an unknown language has no analyser");
        var cobol = new SetupAnswers { Owner = "paulfilkin", Name = "old", Language = "cobol", CodeQl = true, BuildCommand = "make" };
        check(!ConventionContent.FilesFor(cobol, isPublic: true).Any(f => f.Path.EndsWith("codeql.yml", StringComparison.Ordinal)), "CodeQL: no workflow for a language CodeQL cannot analyse");

        var manifest = TidyManifest();
        check(manifest.HasAnswers && manifest.Answers!.ProjectFile == @"app\Example.csproj" && manifest.Answers.Checklist.Count == ChecklistItems.All.Count, "Manifest: the set-up answers are recorded");
        var round = Manifest.Parse(manifest.ToJson())!;
        var back = SetupAnswers.FromManifest(round, new RepositorySettings { Owner = "paulfilkin", Name = "example", Folder = @"C:\x" }, new ScanFacts { Details = GhJson.ParseRest<GhRepositoryDetails>(Sample("repo-details-private.json").Replace("paulfilkin/tidy", "paulfilkin/example")) });
        check(back.ProjectFile == @"app\Example.csproj" && back.SelfTestCommand == DotNetAnswers().SelfTestCommand && back.IsPrivate && back.ReviewRequired && back.Language == ProjectLanguage.DotNet, "Manifest: the answers come back for rendering the files again");
        var kept = ConventionContent.ManifestFor(DotNetAnswers(), new BoardReference("paulfilkin", 9), null, new DateTime(2027, 1, 1), manifest);
        check(kept.SetUp == "2026-11-20" && kept.Board!.Number == 9, "Manifest: an update keeps the set-up date and takes the new board");
    }

    // 22. An apply from Check runs only the ticked items

    private static void CheckApplyChecks(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "designated-driver-check-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "example");
        Directory.CreateDirectory(Path.Combine(folder, ".github"));
        try
        {
            var host = new FakeHost(folder, hasHistory: true) { LabelsDone = false, HooksSet = false };
            var workspace = new Workspace(new Settings { SavePath = Path.Combine(root, "settings.json") }, host.Runner);
            var setup = new SetupService(workspace);
            var answers = DotNetAnswers();
            answers.Source = SetupSource.ExistingFolder; answers.ExistingFolder = folder;
            var (facts, expected, actual) = TidyRepository();
            facts.Folder = folder;
            File.WriteAllText(Manifest.PathIn(folder), facts.Manifest!.ToJson());
            var edited = new Dictionary<string, string>(actual, StringComparer.OrdinalIgnoreCase);
            edited[".githooks/pre-push"] = "#!/bin/sh\nexit 0\n";
            var tampered = Clone(facts);
            tampered.HooksPath = "";
            tampered.Labels = facts.Labels.Where(l => l.Name != "blocked").ToList();
            var report = Drift(tampered, expected, edited);
            check(report.Actionable.Select(i => i.Key).OrderBy(k => k).SequenceEqual(["file:.githooks/pre-push", "hooks", "label:blocked"]), "Check apply: three items to choose from");

            // Tick the label and the hook file, leave the hooks path alone.
            var plan = setup.BuildUpdate(answers, tampered, report, ["label:blocked", "file:.githooks/pre-push"]);
            check(plan.Steps.Select(s => s.Kind).SequenceEqual([SetupStep.Labels, SetupStep.Files]), "Check apply: a plan of the labels and files steps only");
            bool ok = setup.RunAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
            var lines = host.Runner.Calls.Select(c => c.Line).ToList();
            check(ok && plan.Succeeded, ok ? "Check apply: both steps applied" : $"Check apply failed at {plan.FailedStep?.Title}: {plan.FailedStep?.Message}");
            check(lines.Any(l => l.StartsWith("gh label create blocked", StringComparison.Ordinal)) && !lines.Any(l => l.StartsWith("git config core.hooksPath", StringComparison.Ordinal)), "Check apply: the label is created and the hooks path is left alone");
            check(lines.Any(l => l == "git switch -c chore/12-convention-update") && lines.Any(l => l.StartsWith("gh issue create -R paulfilkin/example --title \"Update the repository to the convention\"", StringComparison.Ordinal)), "Check apply: files go through an update issue and branch");
            var added = host.Runner.Calls.First(c => c.Is("add", "--"));
            check(added.Arguments.Contains(".githooks/pre-push") && added.Arguments.Contains(".github/repo-starter.json") && added.Arguments.Count == 4, "Check apply: only the ticked file and the manifest are staged");
            check(File.ReadAllText(Path.Combine(folder, ".githooks", "pre-push")).Contains("refusing to push", StringComparison.Ordinal), "Check apply: the ticked hook is overwritten with the convention's text");
            var manifest = Manifest.Load(folder)!;
            check(manifest.SetUp == "2026-11-20" && manifest.HasAnswers && manifest.Board!.Number == 7, "Check apply: the manifest keeps its date and board");
            check(host.Runner.Calls.First(c => c.Is("commit")).FileContent?.Contains("Update the repository to the convention", StringComparison.Ordinal) == true && host.Runner.Calls.First(c => c.Is("pr", "create")).FileContent?.Contains("Closes #12", StringComparison.Ordinal) == true, "Check apply: the update commit and pull request texts");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
