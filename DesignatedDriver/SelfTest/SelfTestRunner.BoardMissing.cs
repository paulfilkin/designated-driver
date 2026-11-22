using DesignatedDriver.Core;
using DesignatedDriver.Models;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// Open issues and pull requests missing from the board are found by URL and added, a bot's pull requests included,
/// so a board with no token and no Auto-add catches up from Status.
/// </summary>
public static partial class SelfTestRunner
{
    private static void BoardMissingChecks(Action<bool, string> check)
    {
        const string repo = "https://github.com/paulfilkin/example";
        GhIssue Issue(int n) => new() { Number = n, Url = $"{repo}/issues/{n}" };
        GhPullRequest Pr(int n) => new() { Number = n, Url = $"{repo}/pull/{n}" };
        GhProjectItem Item(string url, string type, int n) => new() { Id = "PVTI_" + n, Content = new GhProjectItemContent { Url = url, Type = type, Number = n } };

        // Issue 2 is on the board; pull request 2 isn't, though it shares the number; another repository's 3 doesn't count.
        var items = new[] { Item($"{repo}/issues/2", "Issue", 2), Item("https://github.com/someone/else/pull/3", "PullRequest", 3) };
        var (issues, pulls) = BoardMissing.Find([Issue(1), Issue(2)], [Pr(2), Pr(3)], items);
        check(issues.SequenceEqual([$"{repo}/issues/1"]) && pulls.SequenceEqual([$"{repo}/pull/2", $"{repo}/pull/3"]),
            "Board missing: open issues and pull requests not on the board are found by URL, so an issue and a pull request sharing a number, or another repository's item, don't hide one");

        // Status's action: every missing item is added with gh project item-add, and the counts come back.
        var recorder = new RecordingRunner
        {
            Respond = (exe, a) =>
                exe != "gh" ? null
                : a[0] == "issue" && a[1] == "list" ? RecordingRunner.Ok(exe, a, $"[{{\"number\":1,\"url\":\"{repo}/issues/1\"}},{{\"number\":2,\"url\":\"{repo}/issues/2\"}}]")
                : a[0] == "pr" && a[1] == "list" ? RecordingRunner.Ok(exe, a, $"[{{\"number\":4,\"url\":\"{repo}/pull/4\",\"author\":{{\"login\":\"app/dependabot\"}}}}]")
                : a[0] == "project" && a[1] == "item-list" ? RecordingRunner.Ok(exe, a, $"{{\"items\":[{{\"id\":\"PVTI_2\",\"content\":{{\"type\":\"Issue\",\"number\":2,\"url\":\"{repo}/issues/2\"}}}}],\"totalCount\":1}}")
                : a[0] == "project" && a[1] == "item-add" ? RecordingRunner.Ok(exe, a, "{\"id\":\"PVTI_new\"}")
                : null,
        };
        var board = new BoardReference("paulfilkin", 9);
        var (addedIssues, addedPulls) = GitHubFor(recorder, SampleRepository(), board).AddMissingToBoardAsync(board, CancellationToken.None).GetAwaiter().GetResult();
        var adds = recorder.Calls.Where(c => c.Is("project", "item-add")).Select(c => c.Arguments[c.Arguments.ToList().IndexOf("--url") + 1]).ToList();
        check(addedIssues == 1 && addedPulls == 1 && adds.SequenceEqual([$"{repo}/issues/1", $"{repo}/pull/4"]) && recorder.Calls.Any(c => c.Is("project", "item-add", "9", "--owner", "paulfilkin")),
            "Board missing: Status adds the missing issue and Dependabot's pull request to the board, and says one of each");
    }
}
