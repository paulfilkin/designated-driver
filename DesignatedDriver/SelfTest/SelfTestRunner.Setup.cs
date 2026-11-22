using System.Diagnostics;
using System.IO;
using DesignatedDriver.Core;
using DesignatedDriver.Models;
using DesignatedDriver.Services;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// Phase 2 checks (section 8 of the design): every convention file fills with no placeholder left, the plan has
/// the expected steps, the set-up runs against a recorded git and gh and resumes after a failure, and the hooks
/// accept and refuse the right cases under sh.
/// </summary>
public static partial class SelfTestRunner
{
    private static SetupAnswers DotNetAnswers(bool isPrivate = true, bool token = false) => new()
    {
        Source = SetupSource.NewRepository, Owner = "paulfilkin", Name = "example", Description = "An example", IsPrivate = isPrivate,
        ParentFolder = @"C:\Users\paul\Documents\GIT", Language = ProjectLanguage.DotNet, ProjectFile = @"app\Example.csproj", TestProject = @"tests\Example.Tests\Example.Tests.csproj",
        SelfTestCommand = "dotnet run --project app\\Example.csproj -- --self-test", HasBoardToken = token, GitProtocol = RemoteUrl.Https,
    };

    // Visibility: GitHub's when the repository already exists there, the user's choice otherwise

    private static void SetupVisibilityChecks(Action<bool, string> check)
    {
        static ScanFacts Found(bool isPrivate) => new() { Folder = @"C:\Users\paul\Documents\GIT\example", OwnerAndName = "paulfilkin/example", Details = new GhRepositoryDetails { Private = isPrivate } };
        var notFound = new ScanFacts { Folder = @"C:\Users\paul\Documents\GIT\example", RemoteLookedUp = true };

        check(SetupAnswers.PrivateOnGitHub(SetupSource.NewRepository, null) is null && SetupAnswers.PrivateOnGitHub(SetupSource.NewRepository, Found(false)) is null,
            "Set-up visibility: a new repository is the user's choice");
        check(SetupAnswers.PrivateOnGitHub(SetupSource.ExistingFolder, notFound) is null && SetupAnswers.PrivateOnGitHub(SetupSource.ExistingFolder, null) is null,
            "Set-up visibility: a folder with no repository on GitHub is the user's choice");
        check(SetupAnswers.PrivateOnGitHub(SetupSource.ExistingFolder, Found(false)) == false && SetupAnswers.PrivateOnGitHub(SetupSource.ExistingFolder, Found(true)) == true,
            "Set-up visibility: an existing folder takes GitHub's visibility, public or private");
        check(SetupAnswers.PrivateOnGitHub(SetupSource.ExistingRepository, Found(false)) == false,
            "Set-up visibility: an existing repository on GitHub takes GitHub's visibility");
    }

    // 16. Convention templates fill with nothing left over

    private static void ConventionTemplateChecks(Action<bool, string> check)
    {
        check(ConventionResources.All.Count >= 30 && ConventionResources.Exists(".github/workflows/ci.yml") && ConventionResources.Exists(".gitattributes"), $"Convention resources: {ConventionResources.All.Count} files embedded, dot files stored underscored");
        check(ConventionResources.ToResourcePath(".gitignore") == "_gitignore" && ConventionResources.ToResourcePath(".github/CODEOWNERS") == ".github/CODEOWNERS" && ConventionResources.ToResourcePath(@"Docs\Working with Git.md") == "Docs/Working with Git.md", "Convention resources: only extensionless dot files are renamed");
        check(ConventionContent.HasUnfilled("{{name}}") && !ConventionContent.HasUnfilled("${{ secrets.PROJECT_TOKEN }}") && !ConventionContent.HasUnfilled("${{ env.X != '' }}"), "Placeholders: {{name}} is one; GitHub's ${{ expressions }} are not");

        var a = DotNetAnswers();
        var board = new BoardReference("paulfilkin", 7);
        var files = ConventionContent.FilesFor(a, isPublic: false);
        check(files.Count == 18 && files.Any(f => f.Path == ConventionContent.ManifestPath) && !files.Any(f => f.Path.EndsWith("codeql.yml", StringComparison.Ordinal)) && !files.Any(f => f.Path.EndsWith("release.yml", StringComparison.Ordinal)), $"Files for a private .NET repository: {files.Count}, with the manifest, without CodeQL or the release workflow");
        check(ConventionContent.FilesFor(a, isPublic: true).Count == 18, "Files for a public repository: CodeQL waits for its template in phase 3");

        var values = ConventionContent.ValuesFor(a, board, "<Project><PropertyGroup><TargetFramework>net10.0-windows</TargetFramework></PropertyGroup></Project>");
        var rendered = new Dictionary<string, string>();
        bool anyUnfilled = false;
        foreach (var file in files.Where(f => f.Path != ConventionContent.ManifestPath))
        {
            var text = ConventionContent.Render(file, a, values);
            rendered[file.Path] = text;
            if (ConventionContent.HasUnfilled(text)) { anyUnfilled = true; check(false, $"Template {file.Path} leaves a placeholder: {string.Join(", ", ConventionContent.PlaceholdersIn(text))}"); }
        }
        check(!anyUnfilled, $"Templates: all {rendered.Count} files fill with no placeholder left");
        check(rendered.Values.All(t => !t.Contains("\r\n", StringComparison.Ordinal) && t.EndsWith('\n')), "Templates: LF line endings and a final newline throughout");

        var ci = rendered[".github/workflows/ci.yml"];
        check(ci.Contains("name: Build and test", StringComparison.Ordinal) && ci.Contains("runs-on: windows-latest", StringComparison.Ordinal) && ci.Contains("dotnet-version: '10.0.x'", StringComparison.Ordinal), "ci.yml: the required check name, a Windows runner for .NET and the SDK version from the project file");
        check(ci.Contains("dotnet build \"app/Example.csproj\" -c Release -warnaserror", StringComparison.Ordinal) && ci.Contains("dotnet test \"tests/Example.Tests/Example.Tests.csproj\"", StringComparison.Ordinal) && ci.Contains("-- --self-test", StringComparison.Ordinal), "ci.yml: build, unit tests and self-test steps for .NET");
        check(ci.Contains("No data, keys or certificates found", StringComparison.Ordinal) && ci.Contains("${{", StringComparison.Ordinal) == false, "ci.yml: the committed-secrets check is in, and nothing GitHub would read as an expression");

        var guard = rendered[".github/workflows/guard.yml"];
        check(guard.Contains("behind_by > 10", StringComparison.Ordinal) && guard.Contains("branches: [main]", StringComparison.Ordinal) && guard.Contains("!github.event.created", StringComparison.Ordinal), "guard.yml: the behind limit, the default branch and no report on a branch's first push");
        var boardYml = rendered[".github/workflows/board.yml"];
        check(boardYml.Contains("https://github.com/users/paulfilkin/projects/7", StringComparison.Ordinal) && boardYml.Contains("${{ secrets.PROJECT_TOKEN }}", StringComparison.Ordinal) && boardYml.Contains("env.PROJECT_TOKEN != ''", StringComparison.Ordinal), "board.yml: the board URL, the secret, and steps that skip themselves without it");
        var dependabot = rendered[".github/dependabot.yml"];
        check(dependabot.Contains("package-ecosystem: github-actions", StringComparison.Ordinal) && dependabot.Contains("package-ecosystem: nuget", StringComparison.Ordinal), "dependabot.yml: github-actions and nuget for .NET");
        check(rendered[".github/CODEOWNERS"].Contains("* @paulfilkin", StringComparison.Ordinal), "CODEOWNERS: the owner");
        check(rendered[".github/ISSUE_TEMPLATE/config.yml"].Contains("https://github.com/paulfilkin/example/blob/main/CONTRIBUTING.md", StringComparison.Ordinal) && rendered[".github/ISSUE_TEMPLATE/bug.yml"].Contains("labels: [\"bug\"]", StringComparison.Ordinal), "Issue forms: blank issues off with a link to CONTRIBUTING, and the bug form applies its label");
        var prTemplate = rendered[".github/pull_request_template.md"];
        check(prTemplate.Contains("- [ ] The pull request closes one issue, named above.", StringComparison.Ordinal) && prTemplate.Contains("## AI assistance", StringComparison.Ordinal), "Pull request template: the checklist and the AI assistance section");
        check(rendered[".githooks/pre-push"].Contains("branch=\"main\"", StringComparison.Ordinal) && rendered[".githooks/commit-msg"].StartsWith("#!/bin/sh\n", StringComparison.Ordinal), "Hooks: the default branch is in pre-push and both start with a sh shebang");
        check(rendered[".gitignore"].Contains("bin/", StringComparison.Ordinal) && rendered["CONTRIBUTING.md"].Contains("# Working on example", StringComparison.Ordinal) && rendered["Docs/Working with Git.md"].Contains("gh repo clone paulfilkin/example", StringComparison.Ordinal), ".gitignore for .NET, and the two guides name the repository");
        check(rendered["CONTRIBUTING.md"].Contains("https://github.com/users/paulfilkin/projects/7", StringComparison.Ordinal) && rendered["CONTRIBUTING.md"].Contains("dotnet run --project app\\Example.csproj -- --self-test", StringComparison.Ordinal), "CONTRIBUTING.md: the board link and the check command");

        // Other languages and the choices
        var node = new SetupAnswers { Owner = "paulfilkin", Name = "web", Language = ProjectLanguage.Node, BuildCommand = "npm run build", TestCommand = "npm test", Checklist = ["one-issue", "no-secrets"] };
        var nodeValues = ConventionContent.ValuesFor(node, null);
        var nodeCi = ConventionContent.Render(files.First(f => f.Path == ".github/workflows/ci.yml"), node, nodeValues);
        check(nodeCi.Contains("runs-on: ubuntu-latest", StringComparison.Ordinal) && nodeCi.Contains("npm ci", StringComparison.Ordinal) && nodeCi.Contains("run: npm run build", StringComparison.Ordinal) && nodeCi.Contains("run: npm test", StringComparison.Ordinal) && !nodeCi.Contains("Self-test", StringComparison.Ordinal), "ci.yml for Node.js: Linux, npm ci, the build and test commands, no self-test step");
        var nodePr = ConventionContent.Render(files.First(f => f.Path == ".github/pull_request_template.md"), node, nodeValues);
        check(nodePr.Split('\n').Count(l => l.StartsWith("- [ ]", StringComparison.Ordinal)) == 2, "Pull request template: only the chosen checklist items");
        check(ConventionContent.Render(files.First(f => f.Path == ".gitignore"), node, nodeValues).Contains("node_modules/", StringComparison.Ordinal), ".gitignore for Node.js");
        var other = new SetupAnswers { Owner = "paulfilkin", Name = "misc", Language = "cobol", BuildCommand = "make" };
        check(ConventionContent.Render(files.First(f => f.Path == ".gitignore"), other, ConventionContent.ValuesFor(other, null)).Contains(".DS_Store", StringComparison.Ordinal), ".gitignore for an unknown language falls back to the plain one");
        check(ConventionContent.Ecosystems(ProjectLanguage.Python).Contains("package-ecosystem: pip", StringComparison.Ordinal) && !ConventionContent.Ecosystems("cobol").Contains("pip", StringComparison.Ordinal), "Dependabot ecosystems by language");
        check(ConventionContent.DotNetVersion("<TargetFramework>net8.0</TargetFramework>") == "8.0.x" && ConventionContent.DotNetVersion("") == "10.0.x", ".NET SDK version from the project file, 10.0.x by default");
        check(ConventionContent.DotNetBuildCommand("") == "dotnet build -c Release -warnaserror", "A new .NET repository with no project yet builds whatever the root holds");
        check(ConventionContent.CheckCommand(node) == "npm test" && ConventionContent.CheckCommand(a) == a.SelfTestCommand, "The documents' check command: self-test first, else the tests");

        // The manifest
        var free = Capabilities.From("", false, ApiStatus.NotAvailable);
        var manifest = ConventionContent.ManifestFor(a, board, free, new DateTime(2026, 11, 20));
        check(manifest.ConventionVersion == 1 && manifest.SetUp == "2026-11-20" && manifest.Board == board && manifest.Language == "dotnet" && manifest.SelfTestCommand == a.SelfTestCommand, "Manifest: version, date, board, language and self-test command");
        check(manifest.NotAvailable.SequenceEqual(["Ruleset", "CodeOwners", "PushProtection", "CodeScanning"]), "Manifest: the four features a private Free repository lacks are recorded");
        check(ConventionContent.ManifestFor(a, board, Capabilities.From("", true, null), DateTime.Now).NotAvailable.Count == 0, "Manifest: nothing unavailable on a public repository");
        var round = Manifest.Parse(manifest.ToJson());
        check(round is not null && round.Board == board && round.Options.ReviewRequired && round.NotAvailable.Count == 4, "Manifest: round-trips through JSON");

        // Texts
        var commit = SetupTexts.CommitMessage(12);
        var subject = commit.Split('\n')[0];
        check(CommitMessageChecks.IsAcceptable(subject, string.Join('\n', commit.Split('\n').Skip(2))) && subject.Length <= 70 && commit.Contains("Closes #12", StringComparison.Ordinal), "Set-up commit message passes the tool's own commit checks");
        var pr = SetupTexts.PullRequestBody(12, [".github/workflows/ci.yml"], ["README.md"]);
        check(pr.Contains("Closes #12", StringComparison.Ordinal) && pr.Contains("`README.md`", StringComparison.Ordinal) && pr.Contains("`.github/workflows/ci.yml`", StringComparison.Ordinal), "Set-up pull request body: Closes line, files added and files kept");
    }

    // 17. The plan's steps and the board mutation

    private static void PlanChecks(Action<bool, string> check)
    {
        var steps = PlanOutline.For(DotNetAnswers(), Capabilities.From("", false, ApiStatus.NotAvailable));
        check(steps.SequenceEqual([SetupStep.CreateRepository, SetupStep.Settings, SetupStep.Labels, SetupStep.Board, SetupStep.Files, SetupStep.Hooks, SetupStep.Security, SetupStep.Register]), "Plan: eight steps in the design's order when there is no board token");
        var withToken = PlanOutline.For(DotNetAnswers(token: true), Capabilities.From("", false, ApiStatus.NotAvailable));
        check(withToken.Count == 9 && withToken[4] == SetupStep.BoardToken && withToken[5] == SetupStep.Files, "Plan: the secret step comes after the board and before the files");
        var publicSteps = PlanOutline.For(DotNetAnswers(isPrivate: false), Capabilities.From("", true, null));
        check(publicSteps.Count == 10 && publicSteps[7] == SetupStep.PushProtection && publicSteps[8] == SetupStep.Ruleset && publicSteps[9] == SetupStep.Register, "Plan: a public repository adds push protection and the ruleset before the last step");
        check(PlanOutline.For(DotNetAnswers(), Capabilities.From("pro", false, ApiStatus.Ok)).Contains(SetupStep.Ruleset) && !PlanOutline.For(DotNetAnswers(), Capabilities.From("pro", false, ApiStatus.Ok)).Contains(SetupStep.PushProtection), "Plan: a private repository on a paid plan gets the ruleset but not push protection");
        check(PlanOutline.ForUpdate(["label:bug", "file:.githooks/pre-push", "hooks"], false).SequenceEqual([SetupStep.Labels, SetupStep.Files, SetupStep.Hooks]) && PlanOutline.ForUpdate([], true).SequenceEqual([SetupStep.Files]) && PlanOutline.ForUpdate([], false).Count == 0, "Plan: an update has only the steps its ticked items need, and an older manifest needs the files step");

        check(ConventionBoard.IsReady("Todo") && ConventionBoard.IsReady("ready") && !ConventionBoard.IsReady("Backlog") && ConventionBoard.InProgress == "In Progress", "Board: Todo is the pick-up column, Ready still counts, and the names are GitHub's");

        // Clone URLs and the SSH aliases in ~/.ssh/config
        check(RemoteUrl.For(RemoteUrl.Https, "", "paulfilkin/example") == "https://github.com/paulfilkin/example.git" && RemoteUrl.For(RemoteUrl.Ssh, "github-paulfilkin", "paulfilkin/example") == "git@github-paulfilkin:paulfilkin/example.git" && RemoteUrl.For(RemoteUrl.Ssh, "", "a/b") == "git@github.com:a/b.git", "Clone URL: HTTPS, SSH with an alias, SSH with github.com by default");
        check(RemoteUrl.UsesProtocol("git@github-paulfilkin:a/b.git", RemoteUrl.Ssh) && !RemoteUrl.UsesProtocol("git@github.com:a/b.git", RemoteUrl.Https) && RemoteUrl.UsesProtocol("https://github.com/a/b.git", RemoteUrl.Https) && RemoteUrl.SshHostOf("git@github-paulfilkin:a/b.git") == "github-paulfilkin" && RemoteUrl.SshHostOf("https://github.com/a/b") == "", "Remote URL: protocol and SSH host are read back");
        var sshConfig = SshConfig.Parse("# two accounts\nHost github-editant\n  HostName github.com\n  User git\n  IdentityFile ~/.ssh/id_ed25519_editant\n\nHost github-paulfilkin\n  HostName github.com\n  IdentityFile ~/.ssh/id_ed25519_paulfilkin\n\nHost devbox *.internal\n  HostName 10.0.0.5\n");
        check(sshConfig.Count == 3 && sshConfig[1].Alias == "github-paulfilkin" && sshConfig[1].HostName == "github.com" && sshConfig[2].HostName == "10.0.0.5", "SSH config: Host blocks with HostName and IdentityFile are read");
        check(SshConfig.GitHubHosts(sshConfig).SequenceEqual(["github.com", "github-editant", "github-paulfilkin"]), "SSH config: the aliases that stand for github.com, github.com first");
        check(SshConfig.HostFor("paulfilkin", sshConfig) == "github-paulfilkin" && SshConfig.HostFor("editant", sshConfig) == "github-editant" && SshConfig.HostFor("someone", sshConfig) == "github.com" && SshConfig.HostFor("someone", sshConfig.Take(1)) == "github-editant", "SSH config: the alias for a login, the only alias when there is one, else github.com");
        check(Settings.Parse("{\"gitProtocol\":\"ssh\",\"sshHost\":\"github-paulfilkin\"}") is { GitProtocol: "ssh", SshHost: "github-paulfilkin" } && new Settings().GitProtocol.Length == 0, "Settings: protocol and SSH host round-trip, and are unset until chosen");
    }

    // 18. The set-up runner against a recorded git and gh

    /// <summary>Answers git and gh the way GitHub and a clone would, remembering what the set-up has done so far.</summary>
    private sealed class FakeHost
    {
        public FakeHost(string folder, bool hasHistory)
        {
            Folder = folder;
            HasHistory = hasHistory;
            Created = hasHistory;
            MainOnRemote = hasHistory;
            Runner.Respond = Respond;
        }

        public RecordingRunner Runner { get; } = new();
        public string Folder { get; }
        public bool HasHistory { get; }
        public bool Created, Edited, LabelsDone, ProjectCreated, Linked, Committed, Pushed, MainOnRemote, HooksSet, AlertsOn, FixesOn, IssueCreated, PrCreated, SecretSet, FailLinkOnce;
        /// <summary>When set, GitHub answers for the name with this repository instead, as it does after a rename.</summary>
        public string MovedTo = "";

        private const string Name = "paulfilkin/example";

        private CommandResult? Respond(string exe, IReadOnlyList<string> a)
        {
            CommandResult Ok(string output) => RecordingRunner.Ok(exe, a, output);
            CommandResult Fail(string error, int code = 1) => RecordingRunner.Fail(exe, a, error, code);

            if (exe == "git")
            {
                if (a[0] == "rev-parse" && a.Contains("--show-toplevel")) return Created ? Ok(Folder) : Fail("fatal: not a git repository", 128);
                if (a[0] == "remote" && a[1] == "get-url") return Created ? Ok($"https://github.com/{Name}.git") : Fail("error: No such remote 'origin'", 2);
                if (a[0] == "remote") return null;
                if (a[0] == "clone") { Created = true; Directory.CreateDirectory(a[2]); return null; }
                if (a[0] == "ls-remote") return MainOnRemote ? Ok("0123abc	refs/heads/main") : Fail("", 2);
                if (a[0] == "rev-parse" && a[^1] == "HEAD") return HasHistory || Committed ? Ok("0123abc") : Fail("", 1);
                if (a[0] == "rev-parse" && a[^1].StartsWith("refs/heads/", StringComparison.Ordinal)) return a[^1] == "refs/heads/main" ? Ok("0123abc") : Fail("", 1);
                if (a[0] == "branch") return Ok("main");
                if (a[0] == "status") return Ok(Sample("git-status-clean.txt"));
                if (a[0] == "config" && a[1] == "--get") return HooksSet ? Ok(".githooks") : Fail("", 1);
                if (a[0] == "config") { HooksSet = true; return null; }
                if (a[0] == "ls-files") return Ok(HasHistory ? "README.md\n.gitignore\nExample.csproj\n" : "");
                if (a[0] == "diff" && a[1] == "--cached") return Fail("", 1);
                if (a[0] == "diff")
                {
                    // Paths match a ref once pushed; a repository with history never has the new files on origin/main.
                    if (!Pushed) return Fail("", 1);
                    return a[2] == "origin/main" && HasHistory ? Fail("", 1) : null;
                }
                if (a[0] == "commit") { Committed = true; return null; }
                if (a[0] == "push") { Pushed = true; if (a[^1] == "main") MainOnRemote = true; return null; }
                if (a[0] == "for-each-ref")
                {
                    if (!Pushed || !HasHistory) return Ok("");
                    return Ok(a[^1].Contains("convention-update", StringComparison.Ordinal) ? "origin/chore/12-convention-update\n" : "origin/chore/12-repo-starter-v1\n");
                }
                return null;
            }

            if (a[0] == "repo" && a[1] == "create") { Created = true; return null; }
            if (a[0] == "repo" && a[1] == "edit") { Edited = true; return null; }
            if (a[0] == "repo" && a[1] == "view")
            {
                var nodes = ProjectCreated && Linked ? "{\"id\":\"PVT_7\",\"number\":7,\"title\":\"example\",\"url\":\"https://github.com/users/paulfilkin/projects/7\",\"closed\":false}" : "";
                return Ok("{\"viewerPermission\":\"ADMIN\",\"defaultBranchRef\":{\"name\":\"main\"},\"isEmpty\":" + (HasHistory ? "false" : "true") + ",\"projectsV2\":{\"Nodes\":[" + nodes + "]}}");
            }
            if (a[0] == "api" && a[1] == "-X") { if (a[3].EndsWith("vulnerability-alerts", StringComparison.Ordinal)) AlertsOn = true; else FixesOn = true; return null; }
            if (a[0] == "api" && a[1] == $"repos/{Name}" && MovedTo.Length > 0) return Ok(Sample("repo-details.json").Replace(Name, MovedTo));
            if (a[0] == "api" && a[1] == $"repos/{Name}") return Created ? Ok(Edited ? Sample("repo-details-private.json").Replace("A private repository already set to the convention's settings.", "An example").Replace("paulfilkin/tidy", Name) : Sample("repo-details.json")) : Fail("gh: Not Found (HTTP 404)");
            if (a[0] == "api" && a[1].EndsWith("/vulnerability-alerts", StringComparison.Ordinal)) return AlertsOn ? Ok("") : Fail("gh: Not Found (HTTP 404)");
            if (a[0] == "api" && a[1].EndsWith("/automated-security-fixes", StringComparison.Ordinal)) return Ok(FixesOn ? "true" : "false");
            if (a[0] == "api" && a[1] == "user") return Ok("paulfilkin");
            if (a[0] == "label" && a[1] == "list") return Ok(LabelsDone ? AllLabelsJson() : Sample("label-list.json"));
            if (a[0] == "label" && a[1] == "create") { LabelsDone = true; return null; }
            if (a[0] == "project" && a[1] == "list") return Ok(ProjectCreated ? "{\"projects\":[{\"number\":7,\"id\":\"PVT_7\",\"title\":\"example\",\"closed\":false,\"owner\":{\"login\":\"paulfilkin\"}}],\"totalCount\":1}" : "{\"projects\":[],\"totalCount\":0}");
            if (a[0] == "project" && a[1] == "create") { ProjectCreated = true; return Ok("{\"number\":7,\"id\":\"PVT_7\",\"title\":\"example\",\"url\":\"https://github.com/users/paulfilkin/projects/7\",\"owner\":{\"login\":\"paulfilkin\",\"type\":\"User\"}}"); }
            if (a[0] == "project" && a[1] == "link") { if (FailLinkOnce) { FailLinkOnce = false; return Fail("GraphQL: could not link the project"); } Linked = true; return null; }
            if (a[0] == "project" && a[1] == "field-list") return Ok("{\"fields\":[{\"id\":\"PVTSSF_1\",\"name\":\"Status\",\"type\":\"ProjectV2SingleSelectField\",\"options\":[{\"id\":\"a\",\"name\":\"Todo\"},{\"id\":\"b\",\"name\":\"In Progress\"},{\"id\":\"c\",\"name\":\"Done\"}]}]}");
            if (a[0] == "project" && a[1] == "view") return Ok("{\"id\":\"PVT_7\",\"number\":7,\"title\":\"example\",\"owner\":{\"login\":\"paulfilkin\"}}");
            if (a[0] == "project" && a[1] == "item-add") return Ok("{\"id\":\"PVTI_1\",\"title\":\"Set up\"}");
            if (a[0] == "secret" && a[1] == "list") return Ok(SecretSet ? "[{\"name\":\"PROJECT_TOKEN\"}]" : "[]");
            if (a[0] == "secret" && a[1] == "set") { SecretSet = true; return null; }
            if (a[0] == "issue" && a[1] == "list") return Ok(IssueCreated ? "[{\"number\":12,\"title\":\"Set up the repository to convention v1\",\"url\":\"https://github.com/paulfilkin/example/issues/12\"}]" : "[]");
            if (a[0] == "issue" && a[1] == "create") { IssueCreated = true; return Ok("https://github.com/paulfilkin/example/issues/12\n"); }
            if (a[0] == "pr" && a[1] == "view") return PrCreated ? Ok("{\"number\":13,\"title\":\"Set up\",\"url\":\"https://github.com/paulfilkin/example/pull/13\",\"state\":\"OPEN\",\"headRefName\":\"chore/12-repo-starter-v1\"}") : Fail("no pull requests found for branch");
            if (a[0] == "pr" && a[1] == "create") { PrCreated = true; return Ok("https://github.com/paulfilkin/example/pull/13\n"); }
            return null;
        }

        private static string AllLabelsJson()
            => "[" + string.Join(",", ConventionLabels.All.Select(l => $"{{\"name\":\"{l.Name}\",\"color\":\"{l.Color}\",\"description\":\"{l.Description.Replace("\"", "\\\"")}\"}}")) + "]";
    }

    private static void SetupRunChecks(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "designated-driver-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // A new private .NET repository, with a board token, from nothing.
            var folder = Path.Combine(root, "example");
            var host = new FakeHost(folder, hasHistory: false);
            var settings = new Settings { SavePath = Path.Combine(root, "settings.json") };
            var workspace = new Workspace(settings, host.Runner);
            var setup = new SetupService(workspace) { BoardToken = "ghp_selftest" };
            var answers = DotNetAnswers(token: true);
            answers.ParentFolder = root;
            var plan = setup.Build(answers, null);
            bool ok = setup.RunAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
            check(ok && plan.Succeeded && plan.Steps.All(s => s.State == StepState.Applied), ok ? "Set-up: nine steps applied on a new repository" : $"Set-up failed at {plan.FailedStep?.Title}: {plan.FailedStep?.Message}");

            var lines = host.Runner.Calls.Select(c => c.Line).ToList();
            int IndexOf(string start) => lines.FindIndex(l => l.StartsWith(start, StringComparison.Ordinal));
            var order = new[] { "gh repo create paulfilkin/example --private --description", "git clone https://github.com/paulfilkin/example.git", "gh repo edit paulfilkin/example --enable-squash-merge", "gh label create feature", "gh project create --owner @me --title example", "gh project link 7 --owner paulfilkin -R paulfilkin/example", "gh secret set PROJECT_TOKEN", "git add --", "git update-index --chmod=+x", "git commit -F", "git push -u origin main", "git config core.hooksPath .githooks", "gh api -X PUT repos/paulfilkin/example/vulnerability-alerts", "gh api -X PUT repos/paulfilkin/example/automated-security-fixes" };
            var positions = order.Select(IndexOf).ToList();
            check(positions.All(p => p >= 0) && positions.SequenceEqual(positions.OrderBy(p => p)), "Set-up: the commands run in the design's order: create, settings, labels, board, secret, files, hooks, Dependabot");
            check(lines.Count(l => l.StartsWith("gh label create", StringComparison.Ordinal)) == 4, "Set-up: only the four missing labels are created on a repository with GitHub's defaults");
            check(lines.Any(l => l.Contains("--squash-merge-commit-message pr-title", StringComparison.Ordinal) && l.Contains("--enable-wiki=false", StringComparison.Ordinal) && l.Contains("--description \"An example\"", StringComparison.Ordinal)), "Set-up: gh repo edit carries the convention's merge settings, wiki off and the description");
            var secret = host.Runner.Calls.First(c => c.Is("secret", "set"));
            check(secret.StandardInput == "ghp_selftest" && !lines.Any(l => l.Contains("ghp_", StringComparison.Ordinal)) && setup.BoardToken is null, "Set-up: the token goes in on standard input, never on a command line, and is forgotten afterwards");
            check(File.Exists(Path.Combine(folder, "CONTRIBUTING.md")) && File.Exists(Path.Combine(folder, ".github", "workflows", "ci.yml")) && File.Exists(Path.Combine(folder, ".githooks", "pre-push")) && File.Exists(Path.Combine(folder, ".gitignore")), "Set-up: the convention files are written into the clone");
            var manifest = Manifest.Load(folder);
            check(manifest is { ConventionVersion: 1, Board: { Owner: "paulfilkin", Number: 7 } } && manifest.NotAvailable.Contains("Ruleset"), "Set-up: the manifest names the new board and what a private Free repository lacks");
            check(setup.Written.Count == 18 && setup.Kept.Count == 0, $"Set-up: {setup.Written.Count} files written, none kept, on an empty repository");
            var added = host.Runner.Calls.First(c => c.Is("add", "--"));
            check(added.Arguments.Count == 2 + 18 && added.Arguments.Contains(".github/repo-starter.json"), "Set-up: exactly the written files are staged, the manifest among them");
            check(host.Runner.Calls.First(c => c.Is("commit")).FileContent?.StartsWith("Set up the repository to convention v1", StringComparison.Ordinal) == true, "Set-up: the commit message is the convention's");
            var saved = Settings.Parse(File.ReadAllText(settings.SavePath!));
            check(saved.Current == "paulfilkin/example" && saved.CurrentRepository is { BoardNumber: 7, BoardOwner: "paulfilkin" } r && r.Folder == folder && r.SelfTestCommand == answers.SelfTestCommand, "Set-up: the repository is in the tool's settings with its board and self-test command, and is current");

            // Run again: everything is already done and nothing is written twice.
            int before = host.Runner.Calls.Count;
            var again = setup.Build(answers, null);
            bool okAgain = setup.RunAsync(again, CancellationToken.None).GetAwaiter().GetResult();
            var newLines = host.Runner.Calls.Skip(before).Select(c => c.Line).ToList();
            check(okAgain && again.Steps.All(s => s.State == StepState.AlreadyDone), "Set-up rerun: every step is found done");
            check(!newLines.Any(l => l.StartsWith("gh repo edit", StringComparison.Ordinal) || l.StartsWith("gh label create", StringComparison.Ordinal) || l.StartsWith("git commit", StringComparison.Ordinal) || l.StartsWith("gh project create", StringComparison.Ordinal)), "Set-up rerun: no settings, labels, board or commit are written again");

            // A failure part way, then a resume that only does what is left.
            var folder2 = Path.Combine(root, "two", "example");
            var host2 = new FakeHost(folder2, hasHistory: false) { FailLinkOnce = true };
            var setup2 = new SetupService(new Workspace(new Settings { SavePath = Path.Combine(root, "settings2.json") }, host2.Runner));
            var answers2 = DotNetAnswers();
            answers2.ParentFolder = Path.Combine(root, "two");
            var plan2 = setup2.Build(answers2, null);
            bool first = setup2.RunAsync(plan2, CancellationToken.None).GetAwaiter().GetResult();
            check(!first && plan2.FailedStep?.Kind == SetupStep.Board && plan2.Steps.Take(3).All(s => s.IsDone) && plan2.Steps.Skip(4).All(s => s.State == StepState.Pending), "Set-up: a failed board step stops the plan with the earlier steps done and the later ones pending");
            int edits = host2.Runner.Calls.Count(c => c.Is("repo", "edit"));
            bool resumed = setup2.RunAsync(plan2, CancellationToken.None).GetAwaiter().GetResult();
            check(resumed && plan2.Succeeded && host2.Runner.Calls.Count(c => c.Is("repo", "edit")) == edits && host2.Runner.Calls.Count(c => c.Is("project", "create")) == 1, "Set-up: the rerun resumes at the board step; settings aren't edited again and the project isn't created twice");

            // An existing folder with history: issue, chore branch, pull request, existing files kept.
            var folder3 = Path.Combine(root, "history");
            Directory.CreateDirectory(folder3);
            File.WriteAllText(Path.Combine(folder3, "README.md"), "# Existing\n");
            var host3 = new FakeHost(folder3, hasHistory: true);
            var setup3 = new SetupService(new Workspace(new Settings { SavePath = Path.Combine(root, "settings3.json") }, host3.Runner));
            var answers3 = DotNetAnswers();
            answers3.Source = SetupSource.ExistingFolder; answers3.ExistingFolder = folder3; answers3.ProjectFile = "Example.csproj"; answers3.TestProject = "";
            var facts = new ScanFacts { Folder = folder3, OwnerAndName = "paulfilkin/example", IsGitRepository = true, HasCommits = true, OriginUrl = "https://github.com/paulfilkin/example.git" };
            var plan3 = setup3.Build(answers3, facts);
            bool ok3 = setup3.RunAsync(plan3, CancellationToken.None).GetAwaiter().GetResult();
            var lines3 = host3.Runner.Calls.Select(c => c.Line).ToList();
            check(ok3 && plan3.Succeeded, ok3 ? "Set-up on a repository with history: all steps done" : $"Set-up on history failed at {plan3.FailedStep?.Title}: {plan3.FailedStep?.Message}");
            check(!lines3.Any(l => l.StartsWith("gh repo create", StringComparison.Ordinal)) && lines3.Any(l => l.StartsWith("gh issue create -R paulfilkin/example --title \"Set up the repository to convention v1\" --label chore", StringComparison.Ordinal)), "History: no repository is created; the set-up issue is, labelled chore");
            check(lines3.Any(l => l == "git switch -c chore/12-repo-starter-v1") && lines3.Any(l => l == "git push -u origin chore/12-repo-starter-v1") && lines3.Any(l => l.StartsWith("gh pr create -R paulfilkin/example --base main --head chore/12-repo-starter-v1", StringComparison.Ordinal)), "History: a chore branch named for the issue, pushed, with a pull request into main");
            check(lines3.Any(l => l.StartsWith("gh project item-add 7 --owner paulfilkin --url https://github.com/paulfilkin/example/issues/12", StringComparison.Ordinal)), "History: the issue is put on the new board; GitHub's automation gives it Todo");
            check(setup3.Kept.SequenceEqual(["README.md", ".gitignore"]) && !setup3.Written.Contains("README.md") && setup3.PullRequestUrl == "https://github.com/paulfilkin/example/pull/13", "History: README and .gitignore are kept, and the pull request URL is reported");
            var prBody = host3.Runner.Calls.First(c => c.Is("pr", "create")).FileContent ?? "";
            check(prBody.Contains("Closes #12", StringComparison.Ordinal) && prBody.Contains("`README.md`", StringComparison.Ordinal), "History: the pull request body closes the issue and lists the kept files");
            check(host3.Runner.Calls.First(c => c.Is("commit")).FileContent?.Contains("Closes #12", StringComparison.Ordinal) == true, "History: the commit message closes the issue too");
            check(plan3.Steps.First(s => s.Kind == SetupStep.Files).Message.Contains("/pull/13", StringComparison.Ordinal), "History: the files step reports the pull request");

            // A name that GitHub answers with a renamed repository: the set-up stops before creating or cloning anything.
            var host4 = new FakeHost(Path.Combine(root, "moved", "example"), hasHistory: false) { MovedTo = "paulfilkin/example-archive" };
            var setup4 = new SetupService(new Workspace(new Settings { SavePath = Path.Combine(root, "settings4.json") }, host4.Runner));
            var answers4 = DotNetAnswers();
            answers4.ParentFolder = Path.Combine(root, "moved");
            var plan4 = setup4.Build(answers4, null);
            bool ok4 = setup4.RunAsync(plan4, CancellationToken.None).GetAwaiter().GetResult();
            check(!ok4 && plan4.FailedStep?.Kind == SetupStep.CreateRepository && plan4.FailedStep.Message.Contains("paulfilkin/example-archive", StringComparison.Ordinal)
                && !host4.Runner.Calls.Any(c => c.Is("repo", "create") || c.Is("clone")), "Set-up: a name that leads to a renamed repository stops the first step, naming where it leads, before anything is created or cloned");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // 19. The hooks under sh

    private static void HookChecks(Action<bool, string> check)
    {
        var sh = FindSh();
        if (sh is null)
        {
            check(true, "Hooks: sh was not found (Git for Windows not installed), so the hook checks were skipped");
            return;
        }
        var root = Path.Combine(Path.GetTempPath(), "designated-driver-hooks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var a = DotNetAnswers();
            var values = ConventionContent.ValuesFor(a, new BoardReference("paulfilkin", 7));
            var commitMsg = Path.Combine(root, "commit-msg");
            var prePush = Path.Combine(root, "pre-push");
            File.WriteAllText(commitMsg, ConventionContent.Render(ConventionFiles.All.First(f => f.Path == ".githooks/commit-msg"), a, values), new System.Text.UTF8Encoding(false));
            File.WriteAllText(prePush, ConventionContent.Render(ConventionFiles.All.First(f => f.Path == ".githooks/pre-push"), a, values), new System.Text.UTF8Encoding(false));

            int CommitMsg(string message)
            {
                var file = Path.Combine(root, "MSG-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(file, message, new System.Text.UTF8Encoding(false));
                return RunSh(sh, commitMsg, [file], "", null).ExitCode;
            }
            check(CommitMsg("Add feedback export\n") == 0, "commit-msg hook: a good summary passes");
            check(CommitMsg("Add feedback export\n\nWhy it changed.\n# Comment lines are ignored\n") == 0, "commit-msg hook: a body and comment lines are fine");
            check(CommitMsg("Added feedback export\n") != 0 && CommitMsg("Fixes the login page\n") != 0, "commit-msg hook: past tense and third person are refused");
            check(CommitMsg("Address the review comments\n") == 0 && CommitMsg("Bring the hook into line\n") == 0 && CommitMsg("Update the repository to the convention\n") == 0, "commit-msg hook: Address, Bring and Update are imperatives");
            check(CommitMsg("Tidied the colours\n") != 0 && CommitMsg("Enables the option\n") != 0 && CommitMsg("wip\n") != 0, "commit-msg hook: the tool's list of non-imperative words is refused");
            check(CommitMsg("Add export.\n") != 0 && CommitMsg("add export\n") != 0 && CommitMsg("\n") != 0, "commit-msg hook: trailing full stop, lower-case start and an empty line are refused");
            check(CommitMsg(new string('x', 71) + "\n") != 0 && CommitMsg("A" + new string('x', 69) + "\n") == 0, "commit-msg hook: 71 characters refused, 70 accepted");
            check(CommitMsg("Add export \u2014 fast\n") != 0 && CommitMsg("Add export\n\nDetail \u2014 with a dash\n") != 0, "commit-msg hook: an em-dash anywhere is refused");
            check(CommitMsg("Merge branch 'feature/1-x' into main\n") == 0 && CommitMsg("fixup! Add export\n") == 0, "commit-msg hook: merge and fixup messages pass");

            var pushMain = RunSh(sh, prePush, ["origin", "https://github.com/paulfilkin/example.git"], "refs/heads/main 1111111 refs/heads/main 2222222\n", null);
            check(pushMain.ExitCode != 0 && pushMain.Error.Contains("refusing to push to main", StringComparison.Ordinal), "pre-push hook: a push to main is refused with a reason");
            check(RunSh(sh, prePush, ["origin", "x"], "refs/heads/feature/1-x 1111111 refs/heads/feature/1-x 2222222\n", null).ExitCode == 0, "pre-push hook: a push to a feature branch passes");
            check(RunSh(sh, prePush, ["origin", "x"], "refs/heads/main 1111111 refs/heads/main 2222222\n", "1").ExitCode == 0, "pre-push hook: REPO_STARTER_ALLOW_MAIN lets a push to main through");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string? FindSh()
    {
        foreach (var candidate in new[] { @"C:\Program Files\Git\bin\sh.exe", @"C:\Program Files\Git\usr\bin\sh.exe", @"C:\Program Files (x86)\Git\bin\sh.exe" })
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    private static (int ExitCode, string Error) RunSh(string sh, string script, string[] args, string standardInput, string? allowMain)
    {
        var start = new ProcessStartInfo { FileName = sh, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(script.Replace('\\', '/'));
        foreach (var a in args) start.ArgumentList.Add(a);
        start.Environment.Remove("REPO_STARTER_ALLOW_MAIN");
        if (allowMain is not null) start.Environment["REPO_STARTER_ALLOW_MAIN"] = allowMain;
        using var process = Process.Start(start)!;
        process.StandardInput.Write(standardInput);
        process.StandardInput.Close();
        var error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, error);
    }
}
