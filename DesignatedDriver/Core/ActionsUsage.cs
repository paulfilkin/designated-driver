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
}
