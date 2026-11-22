using DesignatedDriver.Core;
using DesignatedDriver.Models;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// The profile read from GitHub for a repository without a manifest: each value from sample gh output, a repository
/// with none of them, the column names, and what a read fills or asks about.
/// </summary>
public static partial class SelfTestRunner
{
    private static void ProfileChecks(Action<bool, string> check)
    {
        // The required check from a ruleset's rule, and from classic branch protection.
        var rules = GhJson.ParseRestList<GhBranchRule>(Sample("branch-rules-required-check.json"));
        check(rules.SelectMany(r => r.RequiredChecks).SequenceEqual(["Tests", "Lint"]), "Profile: a required_status_checks rule gives its checks; other rules give none");
        check(GhJson.ParseRestList<GhBranchRule>(Sample("branch-rules.json")).SelectMany(r => r.RequiredChecks).Any() == false, "Profile: rules without a required check give none");
        var classic = GhJson.ParseRest<GhClassicRequiredChecks>(Sample("branch-protection-checks.json")).Names.ToList();
        check(classic.SequenceEqual(["Unit tests"]), "Profile: classic branch protection gives its required check once");

        // The jobs in a workflow and the commands their steps run.
        var workflow = Sample("workflow-build.txt");
        var jobs = WorkflowJobs.Parse(workflow);
        check(jobs.Select(j => j.Id).SequenceEqual(["lint", "tests", "release"]) && jobs[1].Name == "Tests" && jobs[2].Name == "",
            "Profile: a workflow's jobs are read with their names, a comment after the name left off");
        check(jobs[1].RunLines.Count == 4 && jobs[1].RunLines[0] == "dotnet restore" && jobs[1].RunLines[3] == "dotnet test --no-build --configuration Release",
            "Profile: run lines are read from single-line steps and from run: | blocks");

        const string path = ".github/workflows/ci.yml";
        var texts = new Dictionary<string, string> { [path] = workflow };
        var triggers = new Dictionary<string, WorkflowTriggers> { [path] = WorkflowTriggers.Parse(workflow) };
        check(CiCommands.SelfTest(texts, triggers, "Tests", "develop") == "dotnet test --configuration Release",
            "Profile: the self-test is the required check's job's first test command, without workflow expressions or --no-build");
        check(CiCommands.SelfTest(texts, triggers, "Lint", "develop") == "dotnet test --configuration Release",
            "Profile: a required check's job with no test command falls back to the workflows that build pull requests");
        check(CiCommands.SelfTest(texts, new Dictionary<string, WorkflowTriggers> { [path] = WorkflowTriggers.None }, "", "develop") == "",
            "Profile: no self-test command when no job is named for the check and no workflow builds changes");

        // The whole profile from scan facts.
        var fields = GhJson.Parse<GhProjectFieldList>(Sample("project-field-list.json"));
        var facts = new ScanFacts
        {
            OwnerAndName = "example-org/example-app",
            Details = new GhRepositoryDetails { FullName = "example-org/example-app", DefaultBranch = "develop", Owner = new GhUser { Login = "example-org" } },
            BranchRules = rules,
            LinkedBoards = [new ScanBoard(2, "Old", "", ["Todo", "Done"]), new ScanBoard(5, "Delivery", "", fields.StatusOptions)],
            WorkflowTexts = new(texts, StringComparer.OrdinalIgnoreCase),
            WorkflowTriggers = new(triggers, StringComparer.OrdinalIgnoreCase),
        };
        var profile = RepositoryProfile.From(facts);
        check(profile.DefaultBranch == "develop" && profile.RequiredCheck == "Tests", "Profile: default branch and required check come from GitHub");
        check(profile.Board == new BoardReference("example-org", 5) && profile.ReadyColumn == "Ready" && profile.InProgressColumn == "In progress" && profile.InReviewColumn == "In review",
            "Profile: the first linked board with pick-up and in-progress columns is chosen, with its own column names");
        check(profile.SelfTestCommand == "dotnet test --configuration Release", "Profile: the self-test command comes from the CI workflow");

        facts.BranchRules = [];
        facts.ClassicRequiredChecks = classic;
        check(RepositoryProfile.From(facts).RequiredCheck == "Unit tests", "Profile: with no ruleset check, classic branch protection's is used");

        // A repository with none of them, and one GitHub doesn't know.
        var bare = RepositoryProfile.From(new ScanFacts { OwnerAndName = "example-org/bare", Details = new GhRepositoryDetails { FullName = "example-org/bare" } });
        check(bare.Values().Values.All(v => v.Length == 0) && bare.Board is null, "Profile: a repository with none of them gives an empty profile");
        check(RepositoryProfile.From(new ScanFacts { OwnerAndName = "example-org/missing" }).Values().Values.All(v => v.Length == 0), "Profile: a repository not found gives an empty profile");

        // Columns by name, without regard to case.
        check(BoardColumns.Match(["Todo", "IN PROGRESS", "Done"]) == ("Todo", "IN PROGRESS", ""), "Columns: matched without regard to case, keeping the board's spelling");
        check(BoardColumns.Match(["Backlog", "Todo", "Ready", "In progress", "In review"]).Ready == "Ready" && BoardColumns.Match(["To do", "In progress"]).Ready == "To do",
            "Columns: Ready is the pick-up column ahead of Todo, and To do is recognised");
        check(BoardColumns.IsPickUp("ready", "Ready") && !BoardColumns.IsPickUp("Todo", "Ready") && BoardColumns.IsPickUp("Todo", "") && BoardColumns.IsPickUp("Ready", ""),
            "Columns: Start work uses the profile's pick-up column, else the convention's Todo or Ready");
        check(BoardColumns.InProgressFor("") == ConventionBoard.InProgress && BoardColumns.InProgressFor("Doing") == "Doing", "Columns: work starts in the profile's in-progress column, else In Progress");

        // A read fills, and asks before replacing a value of the user's.
        var defaults = new Dictionary<ProfileField, string> { [ProfileField.DefaultBranch] = "main", [ProfileField.RequiredCheck] = RepositorySettings.DefaultRequiredCheck };
        var (firstFill, firstAsk) = ProfileComparison.Compare(defaults, profile, firstRead: true);
        check(firstFill.Count == 7 && firstAsk.Count == 0, "Profile read: the first read fills every value GitHub has over the app's defaults");

        var mine = new Dictionary<ProfileField, string>
        {
            [ProfileField.DefaultBranch] = "develop", [ProfileField.RequiredCheck] = "CI", [ProfileField.Board] = "",
            [ProfileField.ReadyColumn] = "Ready", [ProfileField.InProgressColumn] = "", [ProfileField.InReviewColumn] = "", [ProfileField.SelfTestCommand] = "my-tests.cmd",
        };
        var (fill, ask) = ProfileComparison.Compare(mine, profile, firstRead: false);
        check(fill.Select(f => f.Field).SequenceEqual([ProfileField.Board, ProfileField.InProgressColumn, ProfileField.InReviewColumn])
            && fill[0].FromGitHub == "example-org #5",
            "Profile read: a later read fills only the empty values");
        check(ask.Select(a => a.Field).SequenceEqual([ProfileField.RequiredCheck, ProfileField.SelfTestCommand]) && ask[0].Current == "CI" && ask[0].FromGitHub == "Tests",
            "Profile read: values the user has that differ from GitHub's are asked about, not replaced");

        // The columns are kept in the profile, and an older settings file reads as no columns.
        var parsed = Settings.Parse("{\"repositories\":[{\"owner\":\"example-org\",\"name\":\"example-app\",\"readyColumn\":\"Ready\",\"inProgressColumn\":\"In progress\",\"inReviewColumn\":\"In review\"},{\"owner\":\"example-org\",\"name\":\"old\"}]}");
        check(parsed.Repositories[0] is { ReadyColumn: "Ready", InProgressColumn: "In progress", InReviewColumn: "In review" }
            && parsed.Repositories[1] is { ReadyColumn: "", InProgressColumn: "", InReviewColumn: "" },
            "Settings: the board columns round-trip, and an entry without them has none");
    }
}
