using DesignatedDriver.Core;
using DesignatedDriver.Models;
using DesignatedDriver.Services;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// Status reads only what it shows: no plan name or billing without the user scope, no run counting on a public
/// repository, and the runs list cut to the ids.
/// </summary>
public static partial class SelfTestRunner
{
    private static void StatusReadChecks(Action<bool, string> check)
    {
        var withUser = new AuthStatus { SignedIn = true };
        withUser.Scopes.AddRange(["repo", "workflow", "project", "user"]);
        var withoutUser = new AuthStatus { SignedIn = true };
        withoutUser.Scopes.AddRange(["gist", "project", "read:org", "repo", "workflow"]);
        var unlisted = new AuthStatus { SignedIn = true };

        check(!withoutUser.Lacks("repo") && withoutUser.Lacks("user") && !withUser.Lacks("USER") && !unlisted.Lacks("user"),
            "Status reads: a scope is lacking only when gh listed the token's scopes without it");
        check(ActionsUsage.ReadsAccount(withUser) && !ActionsUsage.ReadsAccount(withoutUser) && ActionsUsage.ReadsAccount(unlisted),
            "Status reads: the plan name and billing are asked for unless the token is known to lack the user scope");
        check(!ActionsUsage.CountsRuns(true) && ActionsUsage.CountsRuns(false) && ActionsUsage.CountsRuns(null),
            "Status reads: a public repository's runs aren't counted, since its Actions minutes are free; a private or unknown one's are");

        // The runs list asks gh for the ids alone, and still reads them.
        var recorder = new RecordingRunner
        {
            Respond = (exe, a) => exe == "gh" && a[0] == "api" && a[1].StartsWith("repos/paulfilkin/example/actions/runs?", StringComparison.Ordinal)
                ? RecordingRunner.Ok(exe, a, "{\"workflow_runs\":[{\"id\":11},{\"id\":12}]}")
                : null,
        };
        var runs = GitHubFor(recorder, SampleRepository()).RunsSinceAsync("paulfilkin/example", new DateTime(2026, 11, 22), CancellationToken.None).GetAwaiter().GetResult();
        var call = recorder.Calls.Single();
        check(call.Arguments.Contains("--jq") && call.Arguments[^1] == ActionsUsage.RunIdsOnly && runs.Select(r => r.Id).SequenceEqual([11L, 12L]),
            "Status reads: the runs list is cut to the run ids with --jq, and the ids are read");
    }
}
