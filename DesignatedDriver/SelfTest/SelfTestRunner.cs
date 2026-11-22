using System.IO;
using System.Reflection;
using DesignatedDriver.Core;
using DesignatedDriver.Models;
using DesignatedDriver.Services;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// --self-test: branch naming, commit-message checks, reading sample gh output for every command the tool
/// runs, which screens an admin and a non-admin see, the bypass flow posting its comment before merging, the
/// review rules, the commands behind Fix a problem, the multi-repository settings, the capability probe and
/// the scan rules. Prints PASS or FAIL per check and returns the number of failures.
/// </summary>
public static partial class SelfTestRunner
{
    public static int Run(TextWriter output)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            output.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}");
            if (!ok) failures++;
        }

        try
        {
            BranchNameChecks(Check);
            CommitMessageCheckChecks(Check);
            SampleOutputChecks(Check);
            ScreenChecks(Check);
            BypassFlowChecks(Check);
            BoardMoveChecks(Check);
            CommandLineChecks(Check);
            ReviewRuleChecks(Check);
            MergeRereadChecks(Check);
            FixProblemChecks(Check);
            SettingsChecks(Check);
            ManifestChecks(Check);
            CapabilityChecks(Check);
            ConventionChecks(Check);
            ScanSampleChecks(Check);
            ScanRuleChecks(Check);
            ConventionTemplateChecks(Check);
            SetupVisibilityChecks(Check);
            PlanChecks(Check);
            SetupRunChecks(Check);
            HookChecks(Check);
            ScreenXamlChecks(Check);
            DriftChecks(Check);
            RulesetChecks(Check);
            CheckApplyChecks(Check);
            Phase4Checks(Check);
            ActivityBufferChecks(Check);
            HeldReloadChecks(Check);
            IssueTextChecks(Check);
            RepositoryFilterChecks(Check);
            WindowPlacementChecks(Check);
            ScanNextStepChecks(Check);
        }
        catch (Exception ex)
        {
            Check(false, $"Self-test stopped: {ex.GetType().Name}: {ex.Message}");
        }

        output.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
        return failures;
    }

    private static RepositorySettings SampleRepository(string owner = "paulfilkin", string name = "example")
        => new() { Folder = Path.GetTempPath(), Owner = owner, Name = name };

    private static GitHubService GitHubFor(ICommandRunner runner, RepositorySettings repo, BoardReference? board = null)
        => new(runner, () => repo, () => board);

    // 1. Branch names from type, number and title

    private static void BranchNameChecks(Action<bool, string> check)
    {
        check(BranchNames.Build(["feature", "priority: must"], 42, "Feedback export") == "feature/42-feedback-export", "Branch name: feature label gives feature/42-feedback-export");
        check(BranchNames.Build(["bug", "component: portal"], 7, "Login fails on Monday") == "fix/7-login-fails-on-monday", "Branch name: bug label gives fix/");
        check(BranchNames.Build(["docs"], 12, "Update the hosting guide") == "docs/12-update-the-hosting-guide", "Branch name: docs label gives docs/");
        check(BranchNames.Build(["chore"], 5, "Bump dependencies") == "chore/5-bump-dependencies", "Branch name: chore label gives chore/");
        check(BranchNames.Build(["question"], 5, "Demo package") == "feature/5-demo-package", "Branch name: any other label gives feature/");
        check(BranchNames.Build([], 9, "No labels at all") == "feature/9-no-labels-at-all", "Branch name: no labels gives feature/");
        check(BranchNames.Build(["bug", "chore"], 3, "Both") == "fix/3-both", "Branch name: bug wins over chore when both are present");
        check(BranchNames.Slug("Café: réseau / API!  v2") == "cafe-reseau-api-v2", "Slug drops accents and punctuation");

        var longSlug = BranchNames.Slug("Add an xUnit project for the pure logic covering scoring and csv import and more");
        check(longSlug.Length <= BranchNames.MaxSlugLength && !longSlug.EndsWith('-') && !longSlug.EndsWith("-cov", StringComparison.Ordinal), $"Slug is cut at a word boundary within {BranchNames.MaxSlugLength} characters ({longSlug})");
        check(BranchNames.Build(["bug"], 3, "!!!") == "fix/3", "Branch name without a usable title is just the number");

        check(BranchNames.IssueNumber("fix/7-login-fails") == 7 && BranchNames.IssueNumber("chore/11-tidy") == 11, "Issue number read from fix/ and chore/ branch names");
        check(BranchNames.IssueNumber("main") is null && BranchNames.IssueNumber("feature/no-number") is null, "No issue number from main or an unconventional branch");
        check(BranchNames.FollowsConvention("docs/32-updated-documentation") && BranchNames.FollowsConvention("chore/1") && !BranchNames.FollowsConvention("paul/test"), "Branch convention recognised, chore/ included");
    }

    // 2. Commit message checks

    private static void CommitMessageCheckChecks(Action<bool, string> check)
    {
        static bool Has(IReadOnlyList<CommitMessageIssue> issues, CommitMessageRule rule) => issues.Any(i => i.Rule == rule);

        check(CommitMessageChecks.Check("Add feedback export", "").Count == 0, "Commit message: a good summary passes every check");
        check(Has(CommitMessageChecks.Check("Added feedback export", ""), CommitMessageRule.SummaryNotImperative), "Commit message: past tense first word is flagged");
        check(Has(CommitMessageChecks.Check("Fixes the login page", ""), CommitMessageRule.SummaryNotImperative), "Commit message: third person first word is flagged");
        check(!Has(CommitMessageChecks.Check("Address the review comments", ""), CommitMessageRule.SummaryNotImperative), "Commit message: an imperative ending in s is not flagged");
        check(Has(CommitMessageChecks.Check(new string('x', 71), ""), CommitMessageRule.SummaryTooLong) && !Has(CommitMessageChecks.Check(new string('x', 70), ""), CommitMessageRule.SummaryTooLong), "Commit message: summary over 70 characters is flagged");
        check(Has(CommitMessageChecks.Check("Add export \u2014 fast", ""), CommitMessageRule.EmDash), "Commit message: em-dash in the summary is flagged");
        check(Has(CommitMessageChecks.Check("Add export", "Detail \u2014 with a dash"), CommitMessageRule.EmDash), "Commit message: em-dash in the detail is flagged");
        check(!CommitMessageChecks.IsAcceptable("Add export \u2014 fast", ""), "Commit message: em-dash blocks the commit");
        check(Has(CommitMessageChecks.Check("Add export.", ""), CommitMessageRule.SummaryTrailingFullStop), "Commit message: trailing full stop is flagged");
        check(Has(CommitMessageChecks.Check("add export", ""), CommitMessageRule.SummaryLowerCase), "Commit message: lower-case start is flagged");
        check(Has(CommitMessageChecks.Check("", ""), CommitMessageRule.SummaryEmpty) && !CommitMessageChecks.IsAcceptable("", ""), "Commit message: empty summary blocks the commit");
        check(CommitMessageChecks.IsAcceptable("Added export.", ""), "Commit message: warnings alone don't block the commit");
        check(Has(CommitMessageChecks.Check("Add export", new string('y', 73)), CommitMessageRule.DetailLineTooLong), "Commit message: detail line over 72 characters is flagged");
        check(!Has(CommitMessageChecks.Check("Add export", "See https://github.com/paulfilkin/example/blob/main/Docs/Working%20with%20Git.md#section-8-common-problems"), CommitMessageRule.DetailLineTooLong), "Commit message: a long URL line is not flagged");
        check(CommitMessageChecks.Compose("Add export ", "Why it\r\nchanged\r\n") == "Add export\n\nWhy it\nchanged\n", "Commit message: summary, blank line, detail");
        check(CommitMessageChecks.Compose("Add export", "") == "Add export\n", "Commit message: summary alone ends with one newline");
    }

    // 3. Sample gh and git output for the workflow screens

    private static void SampleOutputChecks(Action<bool, string> check)
    {
        var repo = GhJson.Parse<GhRepository>(Sample("repo-view.json"));
        check(repo.IsAdmin && repo.DefaultBranchRef?.Name == "main", "gh repo view: viewerPermission and default branch");
        check(!GhJson.Parse<GhRepository>("{\"viewerPermission\":\"WRITE\"}").IsAdmin, "gh repo view: WRITE is not admin");

        var auth = AuthStatusParser.Parse(Sample("auth-status.txt"));
        check(auth.SignedIn && auth.Account == "paulfilkin" && auth.HasRequiredScopes, "gh auth status: signed in with repo, workflow and project scopes");
        var missing = AuthStatusParser.Parse(Sample("auth-status-missing-project.txt"));
        check(missing.SignedIn && missing.MissingScopes.SequenceEqual(["project"]), "gh auth status: missing project scope is reported");
        check(AuthStatus.FixCommandForScope("project") == "gh auth refresh -h github.com -s project", "Missing scope shows the gh auth refresh command");
        check(!AuthStatusParser.Parse(Sample("auth-status-not-logged-in.txt")).SignedIn, "gh auth status: not signed in");
        var two = AuthStatusParser.Parse(Sample("auth-status-two-accounts.txt"));
        check(two.SignedIn && two.Account == "paulfilkin" && two.HasRequiredScopes, "gh auth status: the active account is used when there are two");

        var status = GitStatus.Parse(Sample("git-status.txt"));
        check(status.Branch == "feature/42-feedback-export" && status.Upstream == "origin/feature/42-feedback-export", "git status: branch and upstream");
        check(status.AheadOfUpstream == 2 && status.BehindUpstream == 1, "git status: ahead and behind the upstream");
        check(status.Changes.Count == 4 && status.Changes.Any(c => c.IsUntracked && c.Path == "Docs/New file.md"), "git status: changed, renamed and untracked files");
        check(status.Changes.Any(c => c.OriginalPath == "old.txt" && c.Path == "new.txt"), "git status: a rename keeps both paths");
        check(status.HasConflicts && status.Changes.Any(c => c.IsConflict && c.Path == "app/Program.cs"), "git status: a conflicted file is recognised");
        var clean = GitStatus.Parse(Sample("git-status-clean.txt"));
        check(clean.IsClean && clean.Branch == "main" && clean.AheadOfUpstream == 0, "git status: clean main");

        var ab = AheadBehind.Parse("3\t1\n");
        check(ab.Ahead == 3 && ab.Behind == 1, "git rev-list --left-right --count: ahead and behind origin/main");

        var items = GhJson.Parse<GhProjectItemList>(Sample("project-item-list.json"));
        check(items.Items.Count == 3 && items.TotalCount == 25, "gh project item-list: items and total");
        var ready = items.Items.Where(i => i.IsIssue && i.Status == "Ready").ToList();
        check(ready.Count == 1 && ready[0].Number == 6 && ready[0].Id == "PVTI_lADOAAAAAA4AAAAAzgAAAAY", "gh project item-list: filtered on status Ready with the item id");
        check(items.Items[0].Labels.SequenceEqual(["chore", "component: data"]), "gh project item-list: labels");
        check(items.Items.Count(i => i.IsIssue) == 2, "gh project item-list: a draft item is not an issue");

        var issue = GhJson.Parse<GhIssue>(Sample("issue-view.json"));
        check(issue.Number == 42 && issue.Title == "Feedback export" && issue.Assignees.Count == 1 && issue.Assignees[0].Login == "paulfilkin", "gh issue view: number, title and assignees");
        check(issue.LabelNames.SequenceEqual(["feature", "area: reports", "priority: must"]) && issue.Labels[0].Color == "1D76DB", "gh issue view: labels with colours");
        check(issue.Body.Contains("Acceptance criteria", StringComparison.Ordinal), "gh issue view: body");
        check(BranchNames.Build(issue.LabelNames, issue.Number, issue.Title) == "feature/42-feedback-export", "Branch name built from the sample issue");

        var list = GhJson.ParseList<GhIssue>(Sample("issue-list.json"));
        check(list.Count == 3 && list.Select(i => i.Number).SequenceEqual([28, 27, 26]), "gh issue list: three issues");

        var fields = GhJson.Parse<GhProjectFieldList>(Sample("project-field-list.json"));
        var statusField = fields.Fields.FirstOrDefault(f => f.Name == "Status");
        check(statusField is not null && statusField.Id == "PVTSSF_lADOAAAAAA4AAAAAzgAAAAM" && statusField.Options.Count == 5, "gh project field-list: Status field and its five options");
        check(statusField?.Options.First(o => o.Name == "In progress").Id == "c3d4e5f6", "gh project field-list: In progress option id");
        check(fields.StatusOptions.SequenceEqual(["Backlog", "Ready", "In progress", "In review", "Done"]) && ConventionBoard.HasRequiredColumns(fields.StatusOptions), "gh project field-list: Status options in order, and they include the convention's columns");

        var project = GhJson.Parse<GhProject>(Sample("project-view.json"));
        check(project.Id == "PVT_kwDOAAAAAA4AAAAA" && project.Number == 3, "gh project view: project id");

        var checks = GhJson.ParseList<GhCheck>(Sample("pr-checks.json"));
        check(checks.Count == 1 && checks[0].Name == "Build and self-test" && checks[0].Bucket == "pass" && checks[0].Link.StartsWith("https://github.com/", StringComparison.Ordinal), "gh pr checks: name, bucket and link");
        check(checks[0].IsFinished, "gh pr checks: a passed check is finished");
        var pending = GhJson.ParseList<GhCheck>(Sample("pr-checks-pending.json"));
        check(pending.Count == 1 && !pending[0].IsFinished && pending[0].Bucket == "pending", "gh pr checks: a running check is not finished");

        var mine = GhJson.ParseList<GhPullRequest>(Sample("pr-list-mine.json"));
        check(mine.Count == 2 && mine[0].Number == 33 && mine[0].HeadRefName == "docs/32-updated-documentation", "gh pr list --author @me: number and branch");
        check(mine[0].ReviewDecision == "REVIEW_REQUIRED" && mine[0].ChecksBucket == "pass" && mine[0].StatusCheckRollup[0].Name == "Build and self-test", "gh pr list --author @me: review state and check status");
        check(mine[1].ChecksBucket == "fail", "gh pr list: a failed check run gives fail");

        var review = GhJson.ParseList<GhPullRequest>(Sample("pr-list-review.json"));
        check(review.Count == 1 && review[0].Author?.Login == "contributor" && review[0].MergeStateStatus == "BLOCKED", "gh pr list --search review:required: author and mergeable state");

        var files = GhJson.Parse<GhPullRequest>(Sample("pr-view-files.json"));
        check(files.Files.Count == 3 && files.Files[0].Path == "Docs/User guide.md" && files.Files[0].Additions == 69, "gh pr view --json files: changed files");

        var merged = GhJson.ParseList<GhPullRequest>(Sample("pr-list-merged.json")).Select(p => p.HeadRefName).ToList();
        check(merged.SequenceEqual(["docs/32-updated-documentation", "docs/31-board-notes", "docs/30-setup-guide"]), "gh pr list --state merged: branch names");

        check(RemoteUrl.ParseOwnerAndRepository("https://github.com/paulfilkin/example.git") == ("paulfilkin", "example"), "Origin URL (HTTPS) gives owner and repository");
        check(RemoteUrl.ParseOwnerAndRepository("git@github.com:paulfilkin/example.git") == ("paulfilkin", "example"), "Origin URL (SSH) gives owner and repository");
        check(RemoteUrl.ParseOwnerAndRepository("") is null, "Empty origin URL gives nothing");
        check(ScanFacts.IsGitHubUrl("https://github.com/paulfilkin/example.git") && ScanFacts.IsGitHubUrl("git@github.com:paulfilkin/example.git") && ScanFacts.IsGitHubUrl("ssh://git@github.com/paulfilkin/example.git"), "GitHub origin URLs are recognised in HTTPS, SCP and SSH forms");
        check(!ScanFacts.IsGitHubUrl("https://gitlab.com/paul/example.git") && !ScanFacts.IsGitHubUrl("") && !ScanFacts.IsGitHubUrl("git@bitbucket.org:paul/example.git"), "Other hosts are not GitHub");

        var body = PullRequestBody.Fill(Sample("pull_request_template.md"), 42);
        check(body.Contains("Closes #42\n", StringComparison.Ordinal) && !body.Contains("the issue number, for example", StringComparison.Ordinal), "Pull request template: Closes #42 replaces the placeholder");
        check(body.Contains("- [ ] The pull request closes one issue", StringComparison.Ordinal), "Pull request template: the checklist is left for the user");
        check(PullRequestBody.IssueNumber(body) == 42, "Pull request body: issue number read back");
        check(PullRequestBody.Fill("No issue section here\n", 7).StartsWith("## Issue\n\nCloses #7", StringComparison.Ordinal), "Pull request template without an Issue section gets one");
        check(PullRequestBody.Fill(PullRequestBody.Fill(PullRequestBody.FallbackTemplate, 7), 8).Contains("Closes #8", StringComparison.Ordinal), "Pull request body: changing the issue number replaces the old one");

        // A template whose only "Closes #" is inside a comment, as Mycelia's is, gets an Issue section of its own.
        const string commented = "## What this changes\n\n<!-- One change per pull request.  Link the issue it closes, if there is one: \"Closes #12\". -->\n";
        var filled = PullRequestBody.Fill(commented, 42);
        check(filled.StartsWith("## Issue\n\nCloses #42\n\n", StringComparison.Ordinal) && filled.Contains("\"Closes #12\". -->", StringComparison.Ordinal) && PullRequestBody.IssueNumber(filled) == 42,
            "Pull request template: a Closes inside a comment is left alone and an Issue section is added");
        var spanning = PullRequestBody.Fill("<!--\nCloses #12\n-->\n## Notes\n", 42);
        check(spanning.StartsWith("## Issue\n\nCloses #42", StringComparison.Ordinal) && spanning.Contains("\nCloses #12\n-->", StringComparison.Ordinal),
            "Pull request template: a Closes line inside a comment over several lines is left alone");
        check(PullRequestBody.Fill("## Issue\n\n  Closes #12\n", 42) == "## Issue\n\n  Closes #42\n" && PullRequestBody.IssueNumber("Text\r\nCloses #9\r\n") == 9,
            "Pull request body: a real Closes line is replaced in place and read back");
        check(PullRequestBody.IssueNumber(commented) is null && PullRequestBody.IssueNumber("See Closes #5 in the text\n") is null,
            "Pull request body: a Closes inside a comment or mid-sentence is not the issue number");

        var templates = Path.Combine(Path.GetTempPath(), "designated-driver-selftest-templates-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(templates, "docs"));
            check(PullRequestBody.LoadTemplate(templates) == PullRequestBody.FallbackTemplate, "Pull request template: none found gives the convention's");
            File.WriteAllText(Path.Combine(templates, "docs", "pull_request_template.md"), "from docs");
            check(PullRequestBody.LoadTemplate(templates) == "from docs", "Pull request template: one in docs is found");
            File.WriteAllText(Path.Combine(templates, "pull_request_template.md"), "from the root");
            check(PullRequestBody.LoadTemplate(templates) == "from the root", "Pull request template: one in the root is found before docs");
        }
        finally
        {
            try { Directory.Delete(templates, recursive: true); } catch (IOException) { }
        }
        check(!PullRequestBody.FallbackTemplate.Contains("Partner", StringComparison.Ordinal) && !PullRequestBody.FallbackTemplate.Contains("NuGet", StringComparison.Ordinal), "Pull request fallback template has no Partner Manager specifics");

        check(ChecksSummary.Overall(["pass", "pending"]) == "pending" && ChecksSummary.Overall(["pass", "fail"]) == "fail" && ChecksSummary.Overall([]) == "none" && ChecksSummary.Overall(["pass"]) == "pass", "Checks summary over several checks");
    }

    // 4. Which screens and actions an admin and a non-admin see

    private static void ScreenChecks(Action<bool, string> check)
    {
        var admin = ScreenCatalogue.ScreensFor(true);
        var contributor = ScreenCatalogue.ScreensFor(false);
        check(admin.Contains(Screen.Review), "Admin sees the Review screen");
        check(!contributor.Contains(Screen.Review), "Non-admin doesn't see the Review screen");
        check(contributor.SequenceEqual([Screen.Repositories, Screen.Scan, Screen.NewRepository, Screen.Status, Screen.Check, Screen.StartWork, Screen.SaveAndShare, Screen.Submit, Screen.TidyUp, Screen.FixProblem]), "Non-admin sees Repositories, Scan, Set up a repository, Status, Check, Start work, Save and share, Submit, Tidy up and Fix a problem");
        check(admin.SequenceEqual([Screen.Repositories, Screen.Scan, Screen.NewRepository, Screen.Status, Screen.Check, Screen.StartWork, Screen.SaveAndShare, Screen.Submit, Screen.Review, Screen.TidyUp, Screen.FixProblem]), "Admin sees Review between Submit and Tidy up");
        check(ScreenCatalogue.ActionsFor(false).Count == 0, "Non-admin has no approve, request changes, merge or bypass actions");
        check(ScreenCatalogue.ActionsFor(true).SequenceEqual([AdminAction.Approve, AdminAction.RequestChanges, AdminAction.Merge, AdminAction.BypassMerge]), "Admin has approve, request changes, merge and bypass actions");
        check(ScreenCatalogue.IsAdminOnly(Screen.Review) && !ScreenCatalogue.IsAdminOnly(Screen.Submit), "Only Review is admin-only");
        check(!ScreenCatalogue.NeedsRepository(Screen.Repositories) && !ScreenCatalogue.NeedsRepository(Screen.Scan) && ScreenCatalogue.NeedsRepository(Screen.Status), "Repositories and Scan work without a current repository; Status needs one");
        check(Enum.GetValues<Screen>().All(ScreenCatalogue.IsBuilt), "Every screen is built");
        check(ScreenCatalogue.Title(Screen.Scan).Length > 0 && ScreenCatalogue.Title(Screen.Repositories).Length > 0, "Screens have titles from the resource strings");
    }

    // 5. The bypass flow posts the comment before merging, and every repository command names the repository

    private static void BypassFlowChecks(Action<bool, string> check)
    {
        var recorder = new RecordingRunner();
        var repo = SampleRepository();
        var gh = GitHubFor(recorder, repo);

        gh.BypassMergeAsync(33, "Own pull request; nobody else can approve it.", CancellationToken.None).GetAwaiter().GetResult();
        check(recorder.Calls.Count == 2, "Bypass merge runs exactly two commands");
        var comment = recorder.Calls.ElementAtOrDefault(0);
        var merge = recorder.Calls.ElementAtOrDefault(1);
        check(comment is not null && comment.Executable == "gh" && comment.Is("pr", "comment", "33", "--body-file"), "Bypass merge: the first command posts the comment");
        check(comment?.FileContent?.Trim() == "Own pull request; nobody else can approve it.", "Bypass merge: the comment is the typed reason, passed through a file");
        check(merge is not null && merge.Is("pr", "merge", "33", "--squash", "--admin", "--delete-branch"), "Bypass merge: the second command merges with --admin, squash, deleting the branch");
        check(recorder.Calls.All(c => c.Arguments.SkipLast(1).Contains("-R") && c.Arguments[^1] == "paulfilkin/example"), "Repository commands name the repository with -R owner/name");

        recorder.Calls.Clear();
        bool refused = false;
        try { gh.BypassMergeAsync(33, "   ", CancellationToken.None).GetAwaiter().GetResult(); }
        catch (ArgumentException) { refused = true; }
        check(refused && recorder.Calls.Count == 0, "Bypass merge without a reason is refused before anything runs");

        recorder.Calls.Clear();
        recorder.Respond = (exe, args) => args.Count > 1 && args[1] == "comment" ? RecordingRunner.Fail(exe, args, "could not comment") : null;
        bool failed = false;
        try { gh.BypassMergeAsync(33, "A reason", CancellationToken.None).GetAwaiter().GetResult(); }
        catch (CommandFailedException) { failed = true; }
        check(failed && recorder.Calls.Count == 1, "Bypass merge: if the comment fails, the merge doesn't run");
        recorder.Respond = null;

        recorder.Calls.Clear();
        gh.MergeAsync(31, CancellationToken.None).GetAwaiter().GetResult();
        check(recorder.Calls.Count == 1 && recorder.Calls[0].Is("pr", "merge", "31", "--squash", "--delete-branch") && !recorder.Calls[0].Arguments.Contains("--admin"), "Ordinary merge squashes and deletes the branch without --admin");

        recorder.Calls.Clear();
        gh.RequestChangesAsync(31, "Please add the self-test checks.", CancellationToken.None).GetAwaiter().GetResult();
        check(recorder.Calls.Count == 1 && recorder.Calls[0].Is("pr", "review", "31", "--request-changes", "--body-file") && recorder.Calls[0].FileContent == "Please add the self-test checks.", "Request changes passes the comment through a file");

        recorder.Calls.Clear();
        gh.ApproveAsync(31, CancellationToken.None).GetAwaiter().GetResult();
        check(recorder.Calls.Count == 1 && recorder.Calls[0].Is("pr", "review", "31", "--approve"), "Approve runs gh pr review --approve");

        recorder.Calls.Clear();
        var noRepo = GitHubFor(recorder, new RepositorySettings());
        noRepo.LoginAsync(CancellationToken.None).GetAwaiter().GetResult();
        check(recorder.Calls.Count == 1 && !recorder.Calls[0].Arguments.Contains("-R"), "Commands that aren't about a repository don't get -R");
    }

    // 6. Moving a card looks the IDs up by name rather than hard-coding them

    private static void BoardMoveChecks(Action<bool, string> check)
    {
        var recorder = new RecordingRunner
        {
            Respond = (exe, args) =>
            {
                if (args.Count > 1 && args[0] == "project" && args[1] == "view") return RecordingRunner.Ok(exe, args, Sample("project-view.json"));
                if (args.Count > 1 && args[0] == "project" && args[1] == "field-list") return RecordingRunner.Ok(exe, args, Sample("project-field-list.json"));
                return null;
            },
        };
        var gh = GitHubFor(recorder, SampleRepository("example-org", "example-app"), new BoardReference("example-org", 3));

        bool moved = gh.MoveCardAsync("PVTI_lADOAAAAAA4AAAAAzgAAAAY", ConventionBoard.InProgress, CancellationToken.None).GetAwaiter().GetResult();
        var edit = recorder.Calls.FirstOrDefault(c => c.Is("project", "item-edit"));
        check(moved && edit is not null, "Move card: item-edit runs after looking up the project and its fields");
        check(edit is not null && edit.Arguments.SequenceEqual(["project", "item-edit", "--project-id", "PVT_kwDOAAAAAA4AAAAA", "--id", "PVTI_lADOAAAAAA4AAAAAzgAAAAY", "--field-id", "PVTSSF_lADOAAAAAA4AAAAAzgAAAAM", "--single-select-option-id", "c3d4e5f6"]), "Move card: project, field and option ids come from gh, looked up by name");
        check(recorder.Calls.Any(c => c.Is("project", "field-list", "3", "--owner", "example-org")), "Move card: field-list uses the board owner and number the workspace resolved");

        recorder.Calls.Clear();
        bool unknown = gh.MoveCardAsync("PVTI_x", "Done by hand", CancellationToken.None).GetAwaiter().GetResult();
        check(!unknown && !recorder.Calls.Any(c => c.Is("project", "item-edit")), "Move card: an unknown column name moves nothing");

        recorder.Calls.Clear();
        var noBoard = GitHubFor(recorder, SampleRepository());
        bool threw = false;
        try { noBoard.ProjectItemsAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { threw = true; }
        check(threw && recorder.Calls.Count == 0 && !noBoard.HasBoard, "Board commands refuse to run when no board is known");
    }

    // 7. Command lines

    private static void CommandLineChecks(Action<bool, string> check)
    {
        var (exe, args) = CommandLineFormat.Split(@"dotnet run --project DesignatedDriver\DesignatedDriver.csproj -- --self-test");
        check(exe == "dotnet" && args.SequenceEqual(["run", "--project", @"DesignatedDriver\DesignatedDriver.csproj", "--", "--self-test"]), "Self-test command splits into executable and arguments");
        var (exe2, args2) = CommandLineFormat.Split("\"C:\\Program Files\\dotnet\\dotnet.exe\" run");
        check(exe2 == @"C:\Program Files\dotnet\dotnet.exe" && args2.SequenceEqual(["run"]), "Quoted executable path is kept together");
        check(CommandLineFormat.Format("gh", ["pr", "create", "--title", "Add feedback export"]) == "gh pr create --title \"Add feedback export\"", "Activity panel shows arguments with spaces quoted");
    }

    // 8. Review rules: who can approve, when the merge is clean, when the bypass is offered

    private static void ReviewRuleChecks(Action<bool, string> check)
    {
        var waiting = GhJson.ParseList<GhPullRequest>(Sample("pr-list-review.json"))[0];
        check(ReviewRules.Blocker(waiting) == MergeBlocker.NotApproved && !ReviewRules.CanMerge(waiting), "Review: a pull request waiting for review can't be merged by the ordinary route");
        check(ReviewRules.CanApprove(waiting, "paul") && ReviewRules.CanRequestChanges(waiting, "paul"), "Review: someone else's pull request can be approved or sent back");
        check(!ReviewRules.CanApprove(waiting, "contributor") && ReviewRules.IsOwn(waiting, "Contributor"), "Review: the author can't approve their own pull request");
        check(ReviewRules.OffersBypass(waiting, "paul"), "Review: the bypass is offered when the rules block the merge");

        static GhPullRequest Pr(string decision, string state, string conclusion = "SUCCESS", bool draft = false, string author = "contributor") => new()
        {
            Number = 40, Title = "Test", HeadRefName = "feature/40-test", ReviewDecision = decision, MergeStateStatus = state, IsDraft = draft,
            Author = new GhUser { Login = author },
            StatusCheckRollup = [new GhStatusCheck { TypeName = "CheckRun", Name = "Build and test", Status = conclusion.Length == 0 ? "IN_PROGRESS" : "COMPLETED", Conclusion = conclusion }],
        };

        var clean = Pr("APPROVED", "CLEAN");
        check(ReviewRules.CanMerge(clean) && ReviewRules.Blocker(clean) == MergeBlocker.None, "Review: approved, green and clean is ready for squash and merge");
        check(!ReviewRules.OffersBypass(clean, "paul"), "Review: no bypass when the ordinary merge works");
        check(ReviewRules.OffersBypass(clean, "contributor") && !ReviewRules.CanApprove(clean, "contributor"), "Review: the user's own pull request offers the bypass because it can't be approved");
        check(ReviewRules.Blocker(Pr("APPROVED", "DIRTY")) == MergeBlocker.Conflicts && !ReviewRules.OffersBypass(Pr("APPROVED", "DIRTY"), "paul"), "Review: conflicts block the merge and can't be bypassed");
        check(ReviewRules.Blocker(Pr("APPROVED", "CLEAN", "FAILURE")) == MergeBlocker.ChecksFailed, "Review: a failed check is reported before the review state");
        check(ReviewRules.Blocker(Pr("CHANGES_REQUESTED", "BLOCKED")) == MergeBlocker.ChangesRequested, "Review: changes requested is reported as the blocker");
        check(ReviewRules.Blocker(Pr("APPROVED", "BLOCKED", "")) == MergeBlocker.ChecksRunning, "Review: running checks are reported as the blocker");
        check(ReviewRules.Blocker(Pr("APPROVED", "BEHIND")) == MergeBlocker.Behind && ReviewRules.OffersUpdateBranch(Pr("APPROVED", "BEHIND")), "Review: a branch behind main offers update-branch");
        check(ReviewRules.Blocker(Pr("APPROVED", "CLEAN", draft: true)) == MergeBlocker.Draft && !ReviewRules.CanApprove(Pr("APPROVED", "CLEAN", draft: true), "paul") && !ReviewRules.OffersBypass(Pr("APPROVED", "CLEAN", draft: true), "paul"), "Review: a draft can't be approved, merged or bypassed");

        // A state GitHub hasn't worked out yet is not mergeable by either route: it may turn out to be conflicts.
        var unknown = Pr("APPROVED", "UNKNOWN");
        var unread = Pr("APPROVED", "");
        check(ReviewRules.IsComputing(unknown) && ReviewRules.IsComputing(unread) && !ReviewRules.IsComputing(clean), "Review: an unknown or missing merge state is still being worked out");
        check(ReviewRules.Blocker(unknown) == MergeBlocker.Computing && !ReviewRules.CanMerge(unknown) && !ReviewRules.OffersBypass(unknown, "paul") && !ReviewRules.OffersBypass(unknown, "contributor"),
            "Review: while GitHub works the state out there is no merge and no bypass, even on your own pull request");
        check(ReviewRules.MayMerge(clean, bypass: false) && !ReviewRules.MayMerge(Pr("APPROVED", "DIRTY"), bypass: false) && !ReviewRules.MayMerge(unknown, bypass: false) && !ReviewRules.MayMerge(Pr("APPROVED", "BEHIND"), bypass: false),
            "Review: the ordinary merge runs on a re-read only when nothing blocks it");
        check(ReviewRules.MayMerge(Pr("REVIEW_REQUIRED", "BLOCKED"), bypass: true) && ReviewRules.MayMerge(Pr("APPROVED", "BEHIND"), bypass: true)
            && !ReviewRules.MayMerge(Pr("APPROVED", "DIRTY"), bypass: true) && !ReviewRules.MayMerge(unknown, bypass: true) && !ReviewRules.MayMerge(Pr("APPROVED", "CLEAN", draft: true), bypass: true),
            "Review: the bypass runs on a re-read unless the state is conflicts, unknown or a draft");
        var stale = ReviewRules.NeedingUpdate([clean, Pr("APPROVED", "BEHIND"), Pr("APPROVED", "DIRTY"), Pr("APPROVED", "DIRTY", draft: true), unknown]);
        check(stale.Count == 2 && stale.All(p => p.MergeStateStatus is "BEHIND" or "DIRTY"), "Review: behind and conflicting pull requests are the ones needing an update from main, drafts aside");
    }

    /// <summary>The merge re-reads the pull request until GitHub has worked its state out, then refuses on conflicts.</summary>
    private static void MergeRereadChecks(Action<bool, string> check)
    {
        var recorder = new RecordingRunner();
        var gh = GitHubFor(recorder, SampleRepository());
        static string View(string state) => $"{{\"number\":31,\"title\":\"T\",\"headRefName\":\"fix/31-x\",\"reviewDecision\":\"APPROVED\",\"mergeStateStatus\":\"{state}\",\"isDraft\":false,\"state\":\"OPEN\",\"statusCheckRollup\":[],\"author\":{{\"login\":\"paul\"}},\"url\":\"\"}}";

        var states = new Queue<string>(["UNKNOWN", "UNKNOWN", "DIRTY"]);
        recorder.Respond = (exe, args) => args.Count > 1 && args[0] == "pr" && args[1] == "view" ? RecordingRunner.Ok(exe, args, View(states.Dequeue())) : null;
        var settled = gh.SettledPullRequestAsync(31, TimeSpan.Zero, CancellationToken.None).GetAwaiter().GetResult();
        check(recorder.Calls.Count == 3 && recorder.Calls.All(c => c.Is("pr", "view", "31", "--json")) && settled.MergeStateStatus == "DIRTY",
            "Merge re-read: the pull request is read again until GitHub reports a known state");
        check(!ReviewRules.MayMerge(settled, bypass: true) && !ReviewRules.MayMerge(settled, bypass: false), "Merge re-read: conflicts found on the re-read stop both the merge and the bypass");

        recorder.Calls.Clear();
        states = new Queue<string>(Enumerable.Repeat("UNKNOWN", 20));
        var givenUp = gh.SettledPullRequestAsync(31, TimeSpan.Zero, CancellationToken.None).GetAwaiter().GetResult();
        check(recorder.Calls.Count == Services.GitHubService.SettleAttempts && ReviewRules.IsComputing(givenUp), "Merge re-read: the wait gives up after its attempts and the state stays not mergeable");

        recorder.Calls.Clear();
        states = new Queue<string>(["CLEAN"]);
        var clean = gh.SettledPullRequestAsync(31, TimeSpan.Zero, CancellationToken.None).GetAwaiter().GetResult();
        check(recorder.Calls.Count == 1 && ReviewRules.MayMerge(clean, bypass: false), "Merge re-read: a known clean state is read once and merges");

        recorder.Calls.Clear();
        var lists = new Queue<string>(["[" + View("UNKNOWN") + "]", "[" + View("BEHIND") + "]"]);
        recorder.Respond = (exe, args) => args.Count > 1 && args[0] == "pr" && args[1] == "list" ? RecordingRunner.Ok(exe, args, lists.Dequeue()) : null;
        var list = gh.OpenPullRequestsSettledAsync(TimeSpan.Zero, CancellationToken.None).GetAwaiter().GetResult();
        check(recorder.Calls.Count == 2 && list.Count == 1 && list[0].MergeStateStatus == "BEHIND", "Merge re-read: the list is read again while any pull request's state is unknown");
        recorder.Respond = null;
    }

    // 9. Fix a problem: the commands behind each card, and the conflict-marker check

    private static void FixProblemChecks(Action<bool, string> check)
    {
        var recorder = new RecordingRunner();
        var git = new GitService(recorder, () => Path.GetTempPath());
        var gh = GitHubFor(recorder, SampleRepository());

        git.RestoreAsync(["app/Program.cs", "Docs/Guide.md"], CancellationToken.None).GetAwaiter().GetResult();
        check(recorder.Calls.Count == 1 && recorder.Calls[0].Arguments.SequenceEqual(["restore", "--staged", "--worktree", "--", "app/Program.cs", "Docs/Guide.md"]), "Throw away: changed files go through git restore with the paths after --");

        recorder.Calls.Clear();
        git.RemoveUntrackedAsync(["new.txt"], CancellationToken.None).GetAwaiter().GetResult();
        check(recorder.Calls.Count == 1 && recorder.Calls[0].Arguments.SequenceEqual(["clean", "-f", "--", "new.txt"]), "Throw away: new files go through git clean -f");

        recorder.Calls.Clear();
        git.RestoreAsync([], CancellationToken.None).GetAwaiter().GetResult();
        git.RemoveUntrackedAsync([], CancellationToken.None).GetAwaiter().GetResult();
        check(recorder.Calls.Count == 0, "Throw away: nothing runs when no file is ticked");

        recorder.Calls.Clear();
        git.UndoLastCommitAsync(CancellationToken.None).GetAwaiter().GetResult();
        check(recorder.Calls.Count == 1 && recorder.Calls[0].Arguments.SequenceEqual(["reset", "--soft", "HEAD~1"]), "Undo the last commit runs git reset --soft HEAD~1");

        recorder.Calls.Clear();
        gh.UpdateBranchAsync(34, CancellationToken.None).GetAwaiter().GetResult();
        git.PullAsync(CancellationToken.None).GetAwaiter().GetResult();
        check(recorder.Calls.Count == 2 && recorder.Calls[0].Executable == "gh" && recorder.Calls[0].Is("pr", "update-branch", "34") && recorder.Calls[1].Executable == "git" && recorder.Calls[1].Is("pull"), "Branch out of date: gh pr update-branch, then git pull");

        recorder.Calls.Clear();
        git.MergeFromOriginAsync("main", CancellationToken.None).GetAwaiter().GetResult();
        check(recorder.Calls.Count == 1 && recorder.Calls[0].Arguments.SequenceEqual(["merge", "origin/main"]), "Branch out of date without a pull request merges origin/main locally");

        recorder.Calls.Clear();
        git.CommitMergeAsync(CancellationToken.None).GetAwaiter().GetResult();
        git.AbortMergeAsync(CancellationToken.None).GetAwaiter().GetResult();
        check(recorder.Calls.Count == 2 && recorder.Calls[0].Is("commit", "--no-edit") && recorder.Calls[1].Is("merge", "--abort"), "Conflict: finishing commits with the prepared message; abandoning runs git merge --abort");

        const string conflicted = "line\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> origin/main\nend\n";
        check(ConflictMarkers.HasMarkers(conflicted) && ConflictMarkers.StartLines(conflicted).SequenceEqual([2]), "Conflict: markers are found with their line number");
        check(!ConflictMarkers.HasMarkers("| a | b |\n=======\nplain text\n"), "Conflict: a row of equals signs on its own is not a marker");
        check(!ConflictMarkers.HasMarkers("resolved\n"), "Conflict: a resolved file has no markers");

        var status = GitStatus.Parse("# branch.head feature/42-feedback-export\nu UU N... 100644 100644 100644 100644 1111111 2222222 3333333 app/Program.cs\n1 .M N... 100644 100644 100644 4444444 4444444 README.md\n");
        check(status.HasConflicts && status.Changes.Count(c => c.IsConflict) == 1 && status.Changes[0].Path == "app/Program.cs", "Conflict: unmerged entries in git status are read as conflicts");
    }

    // 10. Settings: several repositories, one current

    private static void SettingsChecks(Action<bool, string> check)
    {
        var settings = Settings.Parse(Sample("settings.json"));
        check(settings.Repositories.Count == 2 && settings.Current == "paulfilkin/example", "Settings: two repositories and the current one are read");
        check(settings.CurrentRepository?.Name == "example" && settings.CurrentRepository.RequiredCheck == "Build and test" && settings.CurrentRepository.SelfTestCommand.Length == 0, "Settings: the current repository's overrides");
        check(settings.Find("Example-Org/EXAMPLE-APP")?.BoardNumber == 3 && settings.Find("nobody/nothing") is null, "Settings: repositories are found by owner/name without regard to case");
        check(settings.FindByFolder(@"C:\Users\paul\Documents\GIT\example\")?.Owner == "paulfilkin", "Settings: a repository is found by its folder, trailing backslash or not");
        check(settings.Repositories[1].HasBoardOverride && !settings.Repositories[0].HasBoardOverride, "Settings: the board override applies only when both owner and number are set");

        var again = Settings.Parse(settings.ToJson());
        check(again.Current == settings.Current && again.Repositories.Select(r => r.OwnerAndName).SequenceEqual(settings.Repositories.Select(r => r.OwnerAndName)), "Settings: round trip through JSON keeps every repository and the current one");
        check(!settings.ToJson().Contains("token", StringComparison.OrdinalIgnoreCase), "Settings: no token is ever written");

        var workspace = new Workspace(new Settings());
        check(!workspace.HasRepository && workspace.Current.OwnerAndName.Length == 0 && workspace.Board is null, "Workspace: no repositories means no current one and no board");
        var empty = new RepositorySettings { Owner = "paulfilkin", Name = "x", BoardOwner = "paulfilkin", BoardNumber = 9 };
        var ws = new Workspace(new Settings { Current = "paulfilkin/x", Repositories = [empty] });
        check(ws.Board == new BoardReference("paulfilkin", 9) && ws.Manifest is null, "Workspace: without a manifest the board comes from the settings override");

        // The rename from Repo Starter: settings carried over from the old folder, the old board token variable still read.
        var root = Path.Combine(Path.GetTempPath(), "designated-driver-carryover-" + Guid.NewGuid().ToString("N"));
        var oldFolder = Path.Combine(root, "RepoStarter");
        var newFolder = Path.Combine(root, "DesignatedDriver");
        try
        {
            Directory.CreateDirectory(oldFolder);
            File.WriteAllText(Path.Combine(oldFolder, Settings.FileName), "{\"current\":\"paulfilkin/example\"}");
            bool copied = Settings.CarryOver(oldFolder, newFolder);
            check(copied && File.ReadAllText(Path.Combine(newFolder, Settings.FileName)).Contains("paulfilkin/example", StringComparison.Ordinal),
                "Settings carry-over: the old folder's settings are copied when only the old folder has them");
            check(File.ReadAllText(Path.Combine(oldFolder, Settings.FileName)) == "{\"current\":\"paulfilkin/example\"}" && Directory.GetFiles(oldFolder).Length == 1,
                "Settings carry-over: the old folder is left as it was");
            Directory.CreateDirectory(newFolder);
            File.WriteAllText(Path.Combine(newFolder, Settings.FileName), "{\"current\":\"paulfilkin/newer\"}");
            check(!Settings.CarryOver(oldFolder, newFolder) && File.ReadAllText(Path.Combine(newFolder, Settings.FileName)).Contains("newer", StringComparison.Ordinal),
                "Settings carry-over: settings already in the new folder are never overwritten");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        static Func<string, string?> Env(string? newer, string? older) => name => name == App.BoardTokenVariable ? newer : name == App.OldBoardTokenVariable ? older : null;
        check(App.BoardTokenFrom(Env("new", "old")) == "new", "Board token: the new variable wins when both are set");
        check(App.BoardTokenFrom(Env(null, "old")) == "old" && App.BoardTokenFrom(Env("", "old")) == "old", "Board token: the old variable is read when the new one is not set");
        check(App.BoardTokenFrom(Env(null, null)) is null && App.BoardTokenFrom(Env("", "")) is null, "Board token: neither set gives no token");
    }

    // 11. The manifest

    private static void ManifestChecks(Action<bool, string> check)
    {
        var m = Manifest.Parse(Sample("repo-starter.json"));
        check(m is not null && m.ConventionVersion == 1 && m.SetUp == "2026-11-20" && m.Language == "dotnet", "Manifest: version, date and language are read");
        check(m?.Board == new BoardReference("paulfilkin", 4), "Manifest: the board owner and number are read");
        check(m is { Options.ReleaseWorkflow: true, Options.CodeQl: false } && m.NotAvailable.SequenceEqual(["Ruleset"]), "Manifest: options and the not-available steps are read");
        check(m?.RequiredCheck == "Build and test" && m.SelfTestCommand.Contains("--self-test", StringComparison.Ordinal), "Manifest: the check name and self-test command are read");
        check(Manifest.Parse("not json") is null && Manifest.Load(Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid().ToString("N"))) is null, "Manifest: damaged or missing gives null rather than throwing");
        var again = Manifest.Parse(m!.ToJson());
        check(again is not null && again.Board == m.Board && again.ConventionVersion == m.ConventionVersion, "Manifest: round trip through JSON");
    }

    // 12. What the plan allows, for each visibility and plan

    private static void CapabilityChecks(Action<bool, string> check)
    {
        var pub = Capabilities.From("", isPublic: true, rulesProbe: null);
        check(pub is { Rulesets: true, CodeOwners: true, PushProtection: true, CodeScanning: true, Wiki: true, DraftPullRequests: true, Pages: true }, "Capabilities: a public repository has everything, whatever the plan");
        var freePrivate = Capabilities.From("free", isPublic: false, rulesProbe: null);
        check(freePrivate is { Plan: PlanKind.Free, Rulesets: false, CodeOwners: false, PushProtection: false, CodeScanning: false, Wiki: false, DraftPullRequests: false }, "Capabilities: a private repository on Free has no rulesets, code owners, push protection, CodeQL, wiki or drafts");
        var paidPrivate = Capabilities.From("pro", isPublic: false, rulesProbe: null);
        check(paidPrivate is { Plan: PlanKind.Paid, Rulesets: true, CodeOwners: true, PushProtection: false, CodeScanning: false, Wiki: true }, "Capabilities: a private repository on a paid plan has rulesets and wiki but not the paid-add-on features");
        var probedNo = Capabilities.From("", isPublic: false, rulesProbe: ApiStatus.NotAvailable);
        check(probedNo is { Plan: PlanKind.Unknown, Rulesets: false }, "Capabilities: with the plan hidden, a 403 from the rules endpoint means no rulesets");
        var probedYes = Capabilities.From("", isPublic: false, rulesProbe: ApiStatus.Ok);
        check(probedYes is { Plan: PlanKind.Unknown, Rulesets: true, CodeOwners: true }, "Capabilities: with the plan hidden, a 200 from the rules endpoint means rulesets are available");
        check(Capabilities.From("", isPublic: false, rulesProbe: null).Rulesets == false, "Capabilities: unknown plan and no probe is treated as Free");
        check(pub.Features().Count() == 6 && freePrivate.PlanLabel.Length > 0 && probedNo.PlanLabel != freePrivate.PlanLabel, "Capabilities: six features are listed and the plan label distinguishes Free from unknown");

        check(GhApi.Classify(0, "") == ApiStatus.Ok, "gh api: exit 0 is Ok");
        check(GhApi.Classify(1, "gh: Vulnerability alerts are disabled. (HTTP 404)") == ApiStatus.NotFound, "gh api: HTTP 404 is NotFound");
        check(GhApi.Classify(1, "gh: Upgrade to GitHub Pro or make this repository public to enable this feature. (HTTP 403)") == ApiStatus.NotAvailable, "gh api: HTTP 403 with an upgrade message is NotAvailable");
        check(GhApi.Classify(1, "gh: Validation Failed (HTTP 422)") == ApiStatus.NotAvailable, "gh api: HTTP 422 is NotAvailable");
        check(GhApi.Classify(1, "error connecting to api.github.com") == ApiStatus.Failed, "gh api: anything else is Failed");
    }

    // 13. The convention: labels, files, board columns and language detection

    private static void ConventionChecks(Action<bool, string> check)
    {
        check(ConventionLabels.All.Select(l => l.Name).SequenceEqual(["bug", "feature", "docs", "chore", "question", "good first issue", "blocked", "wontfix"]), "Convention: the eight labels from the design, in order");
        check(ConventionLabels.All.All(l => l.Color.Length == 6 && l.Description.Length > 0), "Convention: every label has a six-digit colour and a description");
        check(ConventionLabels.SameColour("#D73A4A", "d73a4a") && !ConventionLabels.SameColour("d73a4a", "d73a4b"), "Convention: colours compare without case or hash");

        var prTemplate = ConventionFiles.All.First(f => f.Path == ".github/pull_request_template.md");
        check(prTemplate.Matches("PULL_REQUEST_TEMPLATE.md") && prTemplate.Matches(".github/PULL_REQUEST_TEMPLATE.md") && prTemplate.Matches("docs/pull_request_template.md") && !prTemplate.Matches("Docs/template.md"), "Convention: the pull request template is recognised wherever GitHub reads it");
        var codeowners = ConventionFiles.All.First(f => f.Path == ".github/CODEOWNERS");
        check(codeowners.Matches("CODEOWNERS") && codeowners.Matches(".github/CODEOWNERS") && !codeowners.Matches("src/CODEOWNERS"), "Convention: CODEOWNERS is recognised at the root, in .github and in docs");
        check(ConventionFiles.All.First(f => f.Path == ".github/workflows/ci.yml").Matches(".github\\workflows\\CI.yml"), "Convention: paths match without regard to case or slash direction");
        check(ConventionFiles.All.Count(f => f.OnlyWhenMissing) == 2 && ConventionFiles.All.Count(f => f.PublicOnly) == 1 && ConventionFiles.All.Count(f => f.Optional) == 1, "Convention: README and .gitignore only when missing, CodeQL public only, release optional");

        check(ConventionBoard.HasRequiredColumns(["Todo", "In Progress", "Done"]) && ConventionBoard.HasRequiredColumns(["Backlog", "Ready", "In progress", "In review", "Done"]) && !ConventionBoard.HasRequiredColumns(["Doing", "Done"]), "Convention: a board needs Todo (or Ready on an older board), In Progress and Done; GitHub's defaults qualify");

        var dotnet = ProjectLanguage.Detect(["README.md", "src/App/App.csproj", "Example.sln"]);
        check(dotnet is { Language: ProjectLanguage.DotNet, Evidence: "Example.sln" }, "Language: a solution file at the root is found before a project file deeper down");
        check(ProjectLanguage.Detect(["web/package.json"])?.Language == ProjectLanguage.Node, "Language: package.json means Node.js");
        check(ProjectLanguage.Detect(["pyproject.toml"])?.Language == ProjectLanguage.Python && ProjectLanguage.Detect(["go.mod"])?.Language == ProjectLanguage.Go, "Language: Python and Go project files");
        check(ProjectLanguage.Detect(["a/b/c/d/deep.csproj"]) is null && ProjectLanguage.Detect(["README.md", "LICENSE"]) is null, "Language: nothing near the root means no guess");
        check(ProjectLanguage.DisplayName(ProjectLanguage.DotNet) == ".NET", "Language: display names");
    }

    // 14. Sample gh output for the scan's calls

    private static void ScanSampleChecks(Action<bool, string> check)
    {
        var d = GhJson.ParseRest<GhRepositoryDetails>(Sample("repo-details.json"));
        check(d.FullName == "paulfilkin/example" && d.IsPublic && !d.Archived && !d.Fork && d.DefaultBranch == "main", "gh api repos: name, visibility, archived, fork and default branch");
        check(d.ViewerIsAdmin && !d.OwnerIsOrganization && d.Owner?.Login == "paulfilkin", "gh api repos: admin permission and a user owner");
        check(d is { AllowMergeCommit: true, AllowRebaseMerge: true, AllowSquashMerge: true, DeleteBranchOnMerge: false, AllowUpdateBranch: false } && d.SquashMergeCommitTitle == "COMMIT_OR_PR_TITLE", "gh api repos: merge settings as GitHub creates them");
        check(d is { HasIssues: true, HasProjects: false, HasWiki: true } && d.Topics.Count == 3, "gh api repos: features and topics");
        check(!d.SecretScanningOn && !d.PushProtectionOn && !d.DependabotSecurityUpdatesOn, "gh api repos: security features read as disabled");

        var p = GhJson.ParseRest<GhRepositoryDetails>(Sample("repo-details-private.json"));
        check(!p.IsPublic && p.DependabotSecurityUpdatesOn && !p.SecretScanningOn && p.SquashMergeCommitTitle == "PR_TITLE", "gh api repos: a private repository with partial security_and_analysis");

        // A repository made with gh repo create and no description comes back with "description": null.
        var bare = GhJson.ParseRest<GhRepositoryDetails>(System.Text.RegularExpressions.Regex.Replace(Sample("repo-details.json"), "\"(description|language)\": (\"[^\"]*\"|null)", "\"$1\": null"));
        check(bare.Description == "" && bare.Language == "" && bare.Description.Trim().Length == 0, "gh api repos: a null description or language reads as empty, not null");

        var v = GhJson.Parse<GhRepository>(Sample("repo-view-scan.json"));
        check(v.IsAdmin && !v.IsEmpty && !v.IsInOrganization && v.IsBlankIssuesEnabled, "gh repo view (scan fields): flags");
        check(v.LinkedProjects.Count() == 1 && v.LinkedProjects.First().Number == 4 && v.LinkedProjects.First().Title == "example", "gh repo view (scan fields): linked projects under projectsV2.Nodes");
        check(v.PullRequestTemplates.Count == 1 && v.PullRequestTemplates[0].Filename == "PULL_REQUEST_TEMPLATE.md" && v.Languages[0].Node?.Name == "C#", "gh repo view (scan fields): templates and languages");

        var labels = GhJson.ParseList<GhLabel>(Sample("label-list.json"));
        check(labels.Count == 9 && labels.Any(l => l.Name == "bug" && l.Color == "d73a4a"), "gh label list: GitHub's default labels");

        var rules = GhJson.ParseRestList<GhBranchRule>(Sample("branch-rules.json"));
        check(rules.Count == 3 && rules.Any(r => r.Type == "pull_request" && r.RulesetId == 4216830), "gh api rules/branches: rule types and ruleset ids");

        var workflows = GhJson.ParseRest<GhWorkflowList>(Sample("workflows.json"));
        check(workflows.TotalCount == 2 && workflows.Workflows.Any(w => w.Path == ".github/workflows/ci.yml" && w.Name == "Build and test"), "gh api actions/workflows: names and paths");

        var contents = GhJson.ParseRestList<GhContentEntry>(Sample("contents-root.json"));
        check(contents.Count == 7 && contents.Count(c => c.IsDirectory) == 3 && contents.Any(c => c.Path == "Example.sln" && !c.IsDirectory), "gh api contents: files and folders");

        var projects = GhJson.Parse<GhProjectList>(Sample("project-list.json"));
        check(projects.TotalCount == 2 && projects.Projects.Count(pr => !pr.Closed) == 1 && projects.Projects[0].Url.Contains("/users/paulfilkin/projects/1", StringComparison.Ordinal), "gh project list: open and closed projects with URLs");
    }

    // 15. Scan rules: blockers, changes, what's kept, what the plan lacks, and the verdict

    private static void ScanRuleChecks(Action<bool, string> check)
    {
        static GhRepositoryDetails Fresh() => GhJson.ParseRest<GhRepositoryDetails>(Sample("repo-details.json"));
        static GhRepositoryDetails Tidy() => GhJson.ParseRest<GhRepositoryDetails>(Sample("repo-details-private.json"));
        static List<GhLabel> AllConventionLabels() => ConventionLabels.All.Select(l => new GhLabel { Name = l.Name, Color = l.Color, Description = l.Description }).ToList();
        static List<string> AllConventionFiles() => ConventionFiles.All.Where(f => !f.PublicOnly && !f.Optional).Select(f => f.Path).Append("Example.sln").ToList();

        // A fresh public repository as GitHub creates it, seen from a clean clone.
        var fresh = new ScanFacts
        {
            Folder = @"C:\Users\paul\Documents\GIT\example", OwnerAndName = "paulfilkin/example",
            IsGitRepository = true, OriginUrl = "https://github.com/paulfilkin/example.git", HasCommits = true, WorkingTreeClean = true,
            Files = ["README.md", ".gitignore", "Example.sln", "src/App/App.csproj", ".github/workflows/build.yml"],
            RemoteLookedUp = true, Details = Fresh(), View = GhJson.Parse<GhRepository>(Sample("repo-view-scan.json")),
            Labels = GhJson.ParseList<GhLabel>(Sample("label-list.json")),
            LinkedBoards = [new ScanBoard(4, "example", "https://github.com/users/paulfilkin/projects/4", ["Todo", "In Progress", "Done"])],
            BranchRulesStatus = ApiStatus.Ok, BranchRules = [], VulnerabilityAlerts = false,
            Workflows = [new GhWorkflow { Name = "Build", Path = ".github/workflows/build.yml", State = "active" }],
        };
        var report = ScanRules.Assess(fresh);
        check(report.Verdict == ScanVerdict.Convertible && report.Count(ScanSeverity.Blocker) == 0, "Scan: a fresh public repository is convertible");
        var merge = report.Findings.FirstOrDefault(f => f.Code == ScanCode.MergeSettings);
        check(merge is not null && merge.Detail.Split('\n').Length == 5, "Scan: five merge settings would change on a repository with GitHub's defaults");
        check(report.Has(ScanCode.Features) && report.Findings.First(f => f.Code == ScanCode.Features).Detail.Split('\n').Length == 2, "Scan: projects on and wiki off are the feature changes");
        var create = report.Findings.First(f => f.Code == ScanCode.CreateLabels);
        check(create.Detail.Split(", ").SequenceEqual(["feature", "docs", "chore", "blocked"]) && !report.Has(ScanCode.RecolourLabels), "Scan: the four missing labels are named; the matching defaults need no change");
        check(report.Has(ScanCode.OtherLabels) && report.Findings.First(f => f.Code == ScanCode.OtherLabels).Text.StartsWith('5'), "Scan: five other labels are left alone");
        check(report.Has(ScanCode.BoardUsable) && !report.Has(ScanCode.CreateBoard) && !report.Has(ScanCode.ExistingBoardOtherColumns), "Scan: a linked board with GitHub's default columns is usable as it is");
        check(report.Has(ScanCode.CreateRuleset) && report.Has(ScanCode.SecretScanning) && report.Has(ScanCode.DependabotAlerts) && report.Has(ScanCode.DependabotUpdates), "Scan: public repository gets a ruleset, secret scanning and Dependabot changes");
        check(report.Count(ScanSeverity.NotAvailable) == 0, "Scan: nothing is unavailable on a public repository");
        var write = report.Findings.First(f => f.Code == ScanCode.WriteFiles);
        check(!write.Detail.Contains("README.md", StringComparison.Ordinal) && !write.Detail.Contains(".gitignore", StringComparison.Ordinal) && write.Detail.Contains(".github/CODEOWNERS", StringComparison.Ordinal) && write.Detail.Contains(".github/workflows/codeql.yml", StringComparison.Ordinal), "Scan: files to write skip the README and .gitignore that exist and include CodeQL for a public repository");
        check(!report.Has(ScanCode.ExistingFilesKept) && report.Has(ScanCode.OtherWorkflows) && report.Has(ScanCode.PointHooks), "Scan: no convention file exists yet, the Build workflow is left alone, and the hooks path would be set");
        check(report.Findings.First(f => f.Code == ScanCode.LanguageDetected).Text.Contains(".NET", StringComparison.Ordinal) && report.Has(ScanCode.HasHistory), "Scan: .NET is detected and the pull request path applies");
        check(report.Findings.Select(f => f.Severity).SequenceEqual(report.Findings.Select(f => f.Severity).OrderBy(s => s)), "Scan: findings are ordered blockers first, already in place last");

        // The same repository already on the list of changes but with a pull request rule in place.
        var ruled = Clone(fresh);
        ruled.BranchRules = GhJson.ParseRestList<GhBranchRule>(Sample("branch-rules.json"));
        var ruledReport = ScanRules.Assess(ruled);
        check(ruledReport.Has(ScanCode.RulesAlreadyApply) && !ruledReport.Has(ScanCode.CreateRuleset), "Scan: existing rules on main are reported and no ruleset is proposed");

        // A private repository on Free, already tidy: nothing to change.
        var tidy = new ScanFacts
        {
            Folder = @"C:\Users\paul\Documents\GIT\tidy", OwnerAndName = "paulfilkin/tidy",
            IsGitRepository = true, OriginUrl = "git@github.com:paulfilkin/tidy.git", HasCommits = true, WorkingTreeClean = true, HooksPath = ".githooks",
            Files = AllConventionFiles(),
            RemoteLookedUp = true, Details = Tidy(), View = GhJson.Parse<GhRepository>(Sample("repo-view-scan.json")),
            Labels = AllConventionLabels(),
            LinkedBoards = [new ScanBoard(4, "tidy", "https://github.com/users/paulfilkin/projects/4", ["Backlog", "Ready", "In progress", "Done"])],
            BranchRulesStatus = ApiStatus.NotAvailable, VulnerabilityAlerts = true,
            Workflows = [new GhWorkflow { Name = "Build and test", Path = ".github/workflows/ci.yml" }, new GhWorkflow { Name = "Guard", Path = ".github/workflows/guard.yml" }, new GhWorkflow { Name = "Board", Path = ".github/workflows/board.yml" }],
            Manifest = Manifest.Parse(Sample("repo-starter.json")),
        };
        var tidyReport = ScanRules.Assess(tidy);
        check(tidyReport.Verdict == ScanVerdict.AlreadySetUp && tidyReport.Count(ScanSeverity.Change) == 0 && tidyReport.Count(ScanSeverity.Blocker) == 0, "Scan: a private repository already set up reports already set up, with no changes");
        check(tidyReport.Has(ScanCode.AlreadySetUp) && tidyReport.Has(ScanCode.BoardUsable) && tidyReport.Has(ScanCode.LabelsOk) && tidyReport.Has(ScanCode.MergeSettingsOk) && tidyReport.Has(ScanCode.HooksOk), "Scan: manifest, board, labels, merge settings and hooks are all reported in place");
        check(tidyReport.Has(ScanCode.RulesetsUnavailable) && tidyReport.Has(ScanCode.CodeOwnersUnavailable) && tidyReport.Has(ScanCode.PushProtectionUnavailable) && tidyReport.Has(ScanCode.CodeScanningUnavailable), "Scan: on private Free the four unavailable features are listed, not proposed");
        check(tidyReport.Has(ScanCode.ExistingFilesKept) && !tidyReport.Has(ScanCode.WriteFiles) && !tidyReport.Has(ScanCode.OtherWorkflows), "Scan: existing convention files are kept and nothing would be written");
        check(tidyReport.Summary.Length > 0 && ScanReportText.Render(tidy, tidyReport, DateTime.Now).Contains(ScanReportText.GroupTitle(ScanSeverity.Ok), StringComparison.Ordinal), "Scan: the summary and the plain-text report are produced");

        // One label recoloured by hand, and an older manifest: both are changes, so the verdict drops to convertible.
        var drifted = Clone(tidy);
        drifted.Labels = AllConventionLabels();
        drifted.Labels.First(l => l.Name == "bug").Color = "ff0000";
        drifted.Manifest = Manifest.Parse(Sample("repo-starter.json"));
        drifted.Manifest!.ConventionVersion = 0;
        var driftedReport = ScanRules.Assess(drifted);
        check(driftedReport.Verdict == ScanVerdict.Convertible && driftedReport.Has(ScanCode.RecolourLabels) && driftedReport.Findings.First(f => f.Code == ScanCode.RecolourLabels).Detail == "bug", "Scan: a recoloured label is a change and names the label");
        check(driftedReport.Has(ScanCode.UpdateConvention) && !driftedReport.Has(ScanCode.AlreadySetUp), "Scan: an older manifest proposes the update instead of reporting already set up");

        // Blockers.
        var archived = Clone(fresh);
        archived.Details = Fresh();
        archived.Details.Archived = true;
        check(ScanRules.Assess(archived) is { Verdict: ScanVerdict.NotConvertible } a && a.Has(ScanCode.Archived) && a.Findings[0].Severity == ScanSeverity.Blocker, "Scan: an archived repository is not convertible, and the blocker comes first");
        var notAdmin = Clone(fresh);
        notAdmin.Details = Fresh();
        notAdmin.Details.Permissions = new GhPermissions { Admin = false, Push = true };
        notAdmin.View = GhJson.Parse<GhRepository>("{\"viewerPermission\":\"WRITE\"}");
        check(ScanRules.Assess(notAdmin) is { Verdict: ScanVerdict.NotConvertible } n && n.Has(ScanCode.NotAdmin) && n.Findings.First(f => f.Code == ScanCode.NotAdmin).Text.Contains("WRITE", StringComparison.Ordinal), "Scan: not being an admin is a blocker that names the permission");
        var elsewhere = new ScanFacts { Folder = @"C:\x", IsGitRepository = true, OriginUrl = "https://gitlab.com/paul/example.git", HasCommits = true, Files = ["README.md"] };
        check(ScanRules.Assess(elsewhere) is { Verdict: ScanVerdict.NotConvertible } e && e.Has(ScanCode.NotGitHubOrigin) && !e.RemoteFindings(), "Scan: an origin that isn't GitHub is a blocker, and GitHub isn't asked");
        var missing = new ScanFacts { OwnerAndName = "paulfilkin/no-such-repository", RemoteLookedUp = true, Details = null };
        check(ScanRules.Assess(missing) is { Verdict: ScanVerdict.NotConvertible } mr && mr.Has(ScanCode.RepositoryNotFound), "Scan: a repository GitHub doesn't return is a blocker");
        var moved = Clone(fresh);
        moved.OwnerAndName = "paulfilkin/old-name";
        moved.Details = Fresh();
        check(moved.MovedTo == "paulfilkin/example" && !moved.RemoteFound && ScanRules.Assess(moved) is { Verdict: ScanVerdict.NotConvertible } mv && mv.Has(ScanCode.RepositoryMoved) && !mv.Has(ScanCode.MergeSettings)
            && mv.Findings.First(f => f.Code == ScanCode.RepositoryMoved).Text.Contains("paulfilkin/example", StringComparison.Ordinal), "Scan: a name GitHub answers with a renamed repository is a blocker naming where it leads, and that repository isn't judged");
        check(Fresh().IsNamed("PaulFilkin/Example") && !Fresh().IsNamed("paulfilkin/old-name") && new GhRepositoryDetails().IsNamed("a/b"), "Repository details: the full name is compared without regard to case, and a reply without one isn't judged");

        // Workflow triggers, and a repository that already publishes releases another way.
        var onTags = WorkflowTriggers.Parse("name: Release\non:\n  push:\n    tags: ['v*']\n  workflow_dispatch:\njobs: {}\n");
        var build = WorkflowTriggers.Parse("name: Build\non:\n  push:\n    branches:\n      - main\n  pull_request:\n    branches: [ main ]\njobs: {}\n");
        check(onTags.RunsOnRelease && onTags.PushTagsOnly && onTags.Has("workflow_dispatch") && !build.RunsOnRelease && build.Has("pull_request") && build.PushBranches.SequenceEqual(["main"])
            && WorkflowTriggers.Parse("on: [push, pull_request]\n").Has("pull_request") && WorkflowTriggers.Parse("on: release\n").RunsOnRelease && WorkflowTriggers.Parse("name: x\n").Events.Count == 0,
            "Workflow triggers: a single event, a list and a mapping are read, with push's branch and tag filters");
        var releasing = Clone(fresh);
        releasing.Releases = [new GhRelease { TagName = "v2.1.0", Assets = [new GhReleaseAsset { Name = "MyceliaSetup-2.1.0.exe" }] }, new GhRelease { TagName = "v2.0.0", Assets = [new GhReleaseAsset { Name = "MyceliaSetup-2.0.0.exe" }] }];
        var releasingReport = ScanRules.Assess(releasing);
        check(releasingReport.Has(ScanCode.ReleasesElsewhere) && ReleaseEvidence.Reasons(releasing).Single().Contains("v2.1.0", StringComparison.Ordinal) && ReleaseEvidence.Reasons(releasing).Single().Contains("MyceliaSetup-2.1.0.exe", StringComparison.Ordinal),
            "Releases: releases with files attached are reported with the latest tag and its files, and the release workflow is left off");
        var tagged = Clone(fresh);
        tagged.WorkflowTriggers[".github/workflows/publish.yml"] = onTags;
        tagged.WorkflowTriggers[".github/workflows/build.yml"] = build;
        check(ReleaseEvidence.Reasons(tagged).Single().Contains("publish.yml", StringComparison.Ordinal) && !ReleaseEvidence.Reasons(tagged).Single().Contains("build.yml", StringComparison.Ordinal), "Releases: a workflow run on tags counts; one run on pushes and pull requests doesn't");
        // Workflows that build pull requests or main beside ci.yml are warned about; others are left alone as before.
        check(build.BuildsChanges("main") && WorkflowTriggers.Parse("on: push\n").BuildsChanges("main") && !onTags.BuildsChanges("main")
            && !WorkflowTriggers.Parse("on:\n  push:\n    branches: [develop]\n").BuildsChanges("main") && WorkflowTriggers.Parse("on:\n  pull_request_target:\n").BuildsChanges("main"),
            "Workflow triggers: pull requests, or pushes to the default branch, build changes; tags alone or another branch don't");
        var competingFacts = Clone(fresh);
        competingFacts.WorkflowTriggers[".github/workflows/build.yml"] = build;
        competingFacts.WorkflowTriggers[".github/workflows/publish.yml"] = onTags;
        competingFacts.WorkflowTriggers[".github/workflows/ci.yml"] = build;
        var competingReport = ScanRules.Assess(competingFacts);
        check(competingReport.Findings.FirstOrDefault(f => f.Code == ScanCode.CompetingWorkflows)?.Detail == ".github/workflows/build.yml" && !competingReport.Findings.First(f => f.Code == ScanCode.CompetingWorkflows).Text.Contains("{", StringComparison.Ordinal)
            && !(competingReport.Findings.FirstOrDefault(f => f.Code == ScanCode.OtherWorkflows)?.Detail ?? "").Contains("Build", StringComparison.Ordinal) && !ScanRules.Assess(fresh).Has(ScanCode.CompetingWorkflows),
            "Scan: another workflow that builds pull requests is a warning naming it, not listed with the workflows left alone; the convention's ci.yml and a tag workflow aren't");
        check(CompetingWorkflows.MayRemove(".github/workflows/build.yml") && !CompetingWorkflows.MayRemove(".github/workflows/ci.yml") && !CompetingWorkflows.MayRemove("build.yml"), "Workflows: only a workflow file the convention doesn't write may be removed");

        var plain = Clone(fresh);
        plain.Releases = [new GhRelease { TagName = "v0.1", Draft = true, Assets = [new GhReleaseAsset { Name = "a.zip" }] }, new GhRelease { TagName = "v0.0" }];
        var own = Clone(releasing);
        own.Files = [.. fresh.Files, ReleaseEvidence.ConventionWorkflow];
        own.WorkflowTriggers[ReleaseEvidence.ConventionWorkflow] = onTags;
        check(ReleaseEvidence.Reasons(fresh).Count == 0 && ReleaseEvidence.Reasons(plain).Count == 0 && ReleaseEvidence.Reasons(own).Count == 0 && !ScanRules.Assess(fresh).Has(ScanCode.ReleasesElsewhere),
            "Releases: no releases, drafts, releases without files, or the convention's own release workflow leave the default alone");

        // Warnings and the create path.
        var fork = Clone(fresh);
        fork.Details = Fresh();
        fork.Details.Fork = true;
        fork.Details.Owner = new GhUser { Login = "some-org", Type = "Organization" };
        fork.Details.DefaultBranch = "master";
        fork.WorkingTreeClean = false;
        fork.ChangedFiles = 3;
        fork.HooksPath = "tools/hooks";
        var forkReport = ScanRules.Assess(fork);
        check(forkReport.Verdict == ScanVerdict.Convertible && forkReport.Has(ScanCode.Fork) && forkReport.Has(ScanCode.Organisation) && forkReport.Has(ScanCode.DirtyTree) && forkReport.Has(ScanCode.HooksPathElsewhere), "Scan: fork, organisation, dirty tree and a foreign hooks path are warnings, not blockers");
        check(forkReport.Has(ScanCode.DefaultBranchNotMain) && forkReport.Findings.First(f => f.Code == ScanCode.CreateRuleset).Text.Contains("master", StringComparison.Ordinal), "Scan: a default branch other than main is noted and the ruleset names it");

        var notGit = new ScanFacts { Folder = @"C:\new", IsGitRepository = false, Files = ["Program.cs", "New.csproj"] };
        var notGitReport = ScanRules.Assess(notGit);
        check(notGitReport.Verdict == ScanVerdict.Convertible && notGitReport.Has(ScanCode.InitGit) && !notGitReport.Has(ScanCode.PointHooks) && notGitReport.Has(ScanCode.LanguageDetected), "Scan: a folder that isn't a repository yet would get git init, and its language is still detected");
        var noOrigin = new ScanFacts { Folder = @"C:\local", IsGitRepository = true, OriginUrl = "", HasCommits = false, Files = ["README.md"] };
        var noOriginReport = ScanRules.Assess(noOrigin);
        check(noOriginReport.Has(ScanCode.CreateRepository) && noOriginReport.Has(ScanCode.EmptyRepository) && noOriginReport.Has(ScanCode.NoLanguage) && noOriginReport.Count(ScanSeverity.Blocker) == 0, "Scan: a local repository with no origin would be created on GitHub; no commits means the straight-to-main path");

        // By name only: no clone facts, files from the GitHub listing.
        var remoteOnly = new ScanFacts
        {
            OwnerAndName = "paulfilkin/example", RemoteLookedUp = true, Details = Fresh(),
            Files = GhJson.ParseRestList<GhContentEntry>(Sample("contents-root.json")).Where(c => !c.IsDirectory).Select(c => c.Path).ToList(),
            Labels = [], BranchRulesStatus = ApiStatus.Ok,
        };
        var remoteReport = ScanRules.Assess(remoteOnly);
        check(!remoteReport.Has(ScanCode.PointHooks) && !remoteReport.Has(ScanCode.InitGit) && remoteReport.Has(ScanCode.LanguageDetected) && remoteReport.Findings.First(f => f.Code == ScanCode.CreateLabels).Detail.Split(", ").Length == 8, "Scan by name: no clone findings, language from the listing, every label missing when none could be read");
        check(remoteReport.Findings.First(f => f.Code == ScanCode.ExistingFilesKept).Detail == "CONTRIBUTING.md", "Scan by name: an existing CONTRIBUTING.md is reported as kept");
    }

    private static bool RemoteFindings(this ScanReport report)
        => report.Findings.Any(f => f.Code is ScanCode.MergeSettings or ScanCode.CreateLabels or ScanCode.CreateBoard or ScanCode.Visibility);

    private static ScanFacts Clone(ScanFacts f) => new()
    {
        Folder = f.Folder, OwnerAndName = f.OwnerAndName, IsGitRepository = f.IsGitRepository, OriginUrl = f.OriginUrl, HasCommits = f.HasCommits,
        WorkingTreeClean = f.WorkingTreeClean, ChangedFiles = f.ChangedFiles, MergeInProgress = f.MergeInProgress, HooksPath = f.HooksPath, CurrentBranch = f.CurrentBranch,
        Files = [.. f.Files], RemoteLookedUp = f.RemoteLookedUp, Details = f.Details, View = f.View, Labels = [.. f.Labels], LinkedBoards = [.. f.LinkedBoards],
        BranchRulesStatus = f.BranchRulesStatus, BranchRules = [.. f.BranchRules], VulnerabilityAlerts = f.VulnerabilityAlerts, SecurityUpdates = f.SecurityUpdates, Workflows = [.. f.Workflows],
        Manifest = f.Manifest, PlanName = f.PlanName,
    };

    private static string Sample(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + name, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Sample {name} is not embedded.");
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
