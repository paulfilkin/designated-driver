using DesignatedDriver.Models;

namespace DesignatedDriver.Core;

/// <summary>
/// The month's Actions minutes. The account-wide figure comes from the billing endpoint, which needs the user
/// scope; without it the tool sums the billable time of this repository's runs since the first of the month,
/// which is what GitHub charges against the allowance. Pure, so the self-test can feed it timings.
/// </summary>
public static class ActionsUsage
{
    public static DateTime MonthStart(DateTime now) => new(now.Year, now.Month, 1, 0, 0, 0, now.Kind);

    /// <summary>The created filter for the runs endpoint: runs since the first of the month.</summary>
    public static string CreatedSince(DateTime now) => ">=" + MonthStart(now).ToString("yyyy-MM-dd");

    /// <summary>Billable minutes across runs, each job rounded up to the minute as GitHub bills it.</summary>
    public static int BillableMinutes(IEnumerable<GhRunTiming> timings)
    {
        int minutes = 0;
        foreach (var timing in timings)
            foreach (var os in timing.Billable.Values)
                minutes += (int)Math.Ceiling(os.TotalMs / 60000.0);
        return minutes;
    }

    /// <summary>Runs in the month, oldest excluded past the cap so a busy repository costs a bounded number of calls.</summary>
    public const int MaxRunsToTime = 50;

    /// <summary>
    /// Whether to ask for the account's billing, and the plan name, which both need the user scope: not when gh
    /// auth status shows the token lacks it, since the answer can only be a 404 or nothing.
    /// </summary>
    public static bool ReadsAccount(AuthStatus auth) => !auth.Lacks("user");

    /// <summary>
    /// Whether to count this repository's runs. Actions minutes are free on a public repository, so its runs are
    /// never billable and counting them costs a call per run for nothing. Unknown visibility counts, as before.
    /// </summary>
    public static bool CountsRuns(bool? isPublic) => isPublic != true;

    /// <summary>The runs endpoint's output cut to the run ids, all the count needs, so the activity panel stays readable.</summary>
    public const string RunIdsOnly = "{workflow_runs: [.workflow_runs[] | {id}]}";
}
