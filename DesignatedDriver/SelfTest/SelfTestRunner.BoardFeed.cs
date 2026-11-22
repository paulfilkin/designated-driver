using DesignatedDriver.Core;
using DesignatedDriver.Services;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// Whether the board is fed (issue 67): the board's Auto-add workflow or PROJECT_TOKEN for Actions and for Dependabot.
/// The token is stored in both places, and Scan and Check say so when nothing feeds the board.
/// </summary>
public static partial class SelfTestRunner
{
    private static void BoardFeedChecks(Action<bool, string> check)
    {
        // Assess
        check(BoardFeed.Assess(new(true, null, null)) == BoardFeedKind.AutoAdd && BoardFeed.Assess(new(true, false, false)) == BoardFeedKind.AutoAdd,
            "Board feed: Auto-add on feeds the board, whatever the secrets");
        check(BoardFeed.Assess(new(false, true, true)) == BoardFeedKind.Token && BoardFeed.Assess(new(null, true, true)) == BoardFeedKind.Token,
            "Board feed: PROJECT_TOKEN for Actions and for Dependabot feeds the board, even when Auto-add couldn't be read");
        check(BoardFeed.Assess(new(false, true, false)) == BoardFeedKind.TokenNotForDependabot,
            "Board feed: a token for Actions alone misses Dependabot's pull requests");
        check(BoardFeed.Assess(new(false, false, false)) == BoardFeedKind.None && BoardFeed.Assess(new(false, false, null)) == BoardFeedKind.None,
            "Board feed: no Auto-add and no token is not fed");
        check(BoardFeed.Assess(new(null, null, null)) == BoardFeedKind.Unknown && BoardFeed.Assess(new(false, null, null)) == BoardFeedKind.Unknown
            && BoardFeed.Assess(new(null, false, false)) == BoardFeedKind.Unknown && BoardFeed.Assess(new(false, true, null)) == BoardFeedKind.Unknown,
            "Board feed: a failed read is Unknown when the reads that worked don't settle it");
        check(BoardFeed.IsFed(BoardFeedKind.AutoAdd) && BoardFeed.IsFed(BoardFeedKind.Token) && !BoardFeed.IsFed(BoardFeedKind.TokenNotForDependabot)
            && !BoardFeed.IsFed(BoardFeedKind.None) && !BoardFeed.IsFed(BoardFeedKind.Unknown),
            "Board feed: only Auto-add and a token for both count as fed");

        // The board's workflows
        check(BoardFeed.AutoAddOn([("Item closed", true), ("Auto-add to project", true)]) == true
            && BoardFeed.AutoAddOn([("Item closed", true), ("Auto-add to project", false)]) == false
            && BoardFeed.AutoAddOn([]) == false && BoardFeed.AutoAddOn(null) is null,
            "Board feed: Auto-add is on only when the board lists it enabled; null when the workflows couldn't be read");
        check(BoardFeed.WorkflowsUrl(new BoardReference("paulfilkin", 9), false) == "https://github.com/users/paulfilkin/projects/9/workflows"
            && BoardFeed.WorkflowsUrl(new BoardReference("example-org", 3), true) == "https://github.com/orgs/example-org/projects/3/workflows",
            "Board feed: the Workflows page is under users/ for a user's board and orgs/ for an organisation's");
        check(BoardFeed.Text(BoardFeedKind.None).Contains(BoardFeed.AutoAddFilter, StringComparison.Ordinal)
            && BoardFeed.Text(BoardFeedKind.TokenNotForDependabot).Contains(BoardFeed.AutoAddFilter, StringComparison.Ordinal),
            "Board feed: the not-fed text gives the Auto-add filter");

        // The token is stored for Actions and for Dependabot, on standard input only.
        var store = new RecordingRunner();
        GitHubFor(store, SampleRepository()).SetBoardTokenAsync("paulfilkin/example", "ghp_example", CancellationToken.None).GetAwaiter().GetResult();
        check(store.Calls.Count == 2
            && store.Calls[0].Arguments.SequenceEqual(["secret", "set", "PROJECT_TOKEN", "-R", "paulfilkin/example"])
            && store.Calls[1].Arguments.SequenceEqual(["secret", "set", "PROJECT_TOKEN", "-R", "paulfilkin/example", "--app", "dependabot"])
            && store.Calls.All(c => c.StandardInput == "ghp_example" && !c.Arguments.Contains("ghp_example")),
            "Board feed: the board token is stored for Actions and for Dependabot, on standard input and never on the command line");

        // The reads: the board's workflows through GraphQL, then the two secret lists when Auto-add is off.
        BoardFeedFacts Read(bool autoAdd, string actions, string dependabot, RecordingRunner runner)
        {
            runner.Respond = (exe, a) =>
                a[0] == "api" && a[1] == "graphql" ? RecordingRunner.Ok(exe, a, $"[{{\"name\":\"Item closed\",\"enabled\":true}},{{\"name\":\"Auto-add to project\",\"enabled\":{(autoAdd ? "true" : "false")}}}]")
                : a[0] == "secret" && a[1] == "list" ? RecordingRunner.Ok(exe, a, a.Contains("dependabot") ? dependabot : actions)
                : null;
            return GitHubFor(runner, SampleRepository()).BoardFeedAsync("paulfilkin/example", new BoardReference("paulfilkin", 9), CancellationToken.None).GetAwaiter().GetResult();
        }
        var offRunner = new RecordingRunner();
        var off = Read(false, "[{\"name\":\"PROJECT_TOKEN\"}]", "[]", offRunner);
        var graphql = offRunner.Calls.FirstOrDefault(c => c.Is("api", "graphql"));
        check(BoardFeed.Assess(off) == BoardFeedKind.TokenNotForDependabot && offRunner.Calls.Count(c => c.Is("secret", "list")) == 2
            && graphql is not null && graphql.Arguments.Contains("owner=paulfilkin") && graphql.Arguments.Contains("number=9"),
            "Board feed: with Auto-add off, both secret lists are read, and a token for Actions alone is found");
        var onRunner = new RecordingRunner();
        check(BoardFeed.Assess(Read(true, "[]", "[]", onRunner)) == BoardFeedKind.AutoAdd && !onRunner.Calls.Any(c => c.Is("secret", "list")),
            "Board feed: with Auto-add on, the secrets aren't read");
        var deniedRunner = new RecordingRunner();
        deniedRunner.Respond = (exe, a) => a[0] == "secret" ? RecordingRunner.Fail(exe, a, "HTTP 403: Resource not accessible by integration") : null;
        var denied = GitHubFor(deniedRunner, SampleRepository()).BoardFeedAsync("paulfilkin/example", new BoardReference("paulfilkin", 9), CancellationToken.None).GetAwaiter().GetResult();
        check(BoardFeed.Assess(denied) == BoardFeedKind.Unknown,
            "Board feed: when neither the workflows nor the secrets can be read, it is Unknown, not unfed");

        // Scan and Check say so when nothing feeds the board, and link to its Workflows page.
        var (facts, expected, actual) = TidyRepository();
        facts.BoardFeed = BoardFeedKind.None;
        var notFed = ScanRules.Assess(facts).Findings.FirstOrDefault(f => f.Code == ScanCode.BoardNotFed);
        check(notFed is { Severity: ScanSeverity.Warning } && notFed.Detail == "https://github.com/users/paulfilkin/projects/7/workflows",
            "Board feed: Scan warns when nothing feeds the board, with its Workflows page");
        var note = Drift(facts, expected, actual).Items.FirstOrDefault(i => i.Key == "board:feed");
        check(note is { IsActionable: false, TickedByDefault: false } && note.Detail.EndsWith("/projects/7/workflows", StringComparison.Ordinal),
            "Board feed: Check notes an unfed board, unticked, as nothing it can apply");
        facts.BoardFeed = BoardFeedKind.AutoAdd;
        var report = ScanRules.Assess(facts);
        check(report.Findings.Any(f => f.Code == ScanCode.BoardFed && f.Severity == ScanSeverity.Ok) && !report.Findings.Any(f => f.Code == ScanCode.BoardNotFed)
            && !Drift(facts, expected, actual).Items.Any(i => i.Key == "board:feed"),
            "Board feed: a fed board is an Ok line in Scan and nothing in Check");
    }
}
