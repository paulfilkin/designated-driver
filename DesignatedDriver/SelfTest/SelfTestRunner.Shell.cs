using DesignatedDriver.Core;
using DesignatedDriver.Services;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// Shell checks: the activity buffer formats each kind of entry, hands a burst over as one append, cuts its
/// start only when the limit is passed, and shortens a long command for the busy strip. A reload held while busy
/// runs only for the screen still shown, only the latest one, and not after a newer load.
/// </summary>
public static partial class SelfTestRunner
{
    private static void ActivityBufferChecks(Action<bool, string> check)
    {
        check(ActivityBuffer.Line(new ActivityEntry(ActivityKind.Command, "gh api x")) == "> gh api x"
            && ActivityBuffer.Line(new ActivityEntry(ActivityKind.Output, "{}")) == "  {}"
            && ActivityBuffer.Line(new ActivityEntry(ActivityKind.Error, "oops")) == "! oops"
            && ActivityBuffer.Line(new ActivityEntry(ActivityKind.Result, "exit 0")) == "  exit 0"
            && ActivityBuffer.Line(new ActivityEntry(ActivityKind.Note, "hello")) == "# hello",
            "Activity: each kind of entry has its prefix");

        var buffer = new ActivityBuffer(limit: 200, keep: 100);
        buffer.Add(new ActivityEntry(ActivityKind.Command, "git status"));
        buffer.Add(new ActivityEntry(ActivityKind.Output, "clean"));
        buffer.Add(new ActivityEntry(ActivityKind.Result, "exit 0"));
        buffer.Add(new ActivityEntry(ActivityKind.Command, "git log"));
        var first = buffer.Flush();
        var lines = first.Added.Split(Environment.NewLine);
        check(!first.Trimmed && lines.Length == 6 && lines[0] == "> git status" && lines[3] == "" && lines[4] == "> git log" && !buffer.HasPending,
            "Activity: a burst flushes as one append, with a blank line before each command after the first");
        check(buffer.Flush() is { Added: "", Trimmed: false }, "Activity: nothing pending flushes nothing");

        for (var i = 0; i < 20; i++) buffer.Add(new ActivityEntry(ActivityKind.Output, new string('x', 20)));
        var trimmed = buffer.Flush();
        check(trimmed.Trimmed && trimmed.Added.Length == 0 && trimmed.Whole.Length == 100 && buffer.Text == trimmed.Whole,
            "Activity: past the limit the kept text is cut to the keep length and handed over whole");

        buffer.Clear();
        check(buffer.Text.Length == 0 && !buffer.HasPending, "Activity: clear empties kept and pending text");

        var longCommand = "gh api graphql -f query=" + new string('q', 100);
        check(ActivityBuffer.ShortCommand("git status").Length == 10 && ActivityBuffer.ShortCommand(longCommand).Length == ActivityBuffer.ShortCommandLength && ActivityBuffer.ShortCommand(longCommand).EndsWith("...", StringComparison.Ordinal),
            "Activity: a long command is shortened for the busy strip");
    }

    private static void HeldReloadChecks(Action<bool, string> check)
    {
        var t0 = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
        var review = new object();
        var scan = new object();
        var began = new Dictionary<object, DateTime> { [review] = DateTime.MinValue, [scan] = DateTime.MinValue };
        DateTime Began(object v) => began[v];

        var held = new HeldReload<object>();
        check(held.Take(scan, Began) is null, "Held reload: nothing held gives nothing");

        held.Hold(scan, t0);
        check(ReferenceEquals(held.Take(scan, Began), scan), "Held reload: a held screen that is still shown runs");
        check(!held.HasHeld && held.Take(scan, Began) is null, "Held reload: taking clears it, so a second take gives nothing");

        held.Hold(review, t0);
        held.Hold(scan, t0.AddSeconds(1));
        check(ReferenceEquals(held.Take(scan, Began), scan), "Held reload: only the latest of two requests is kept");

        held.Hold(scan, t0);
        check(held.Take(review, Began) is null && !held.HasHeld, "Held reload: a screen the user has left does not run, and is dropped");

        began[scan] = t0.AddSeconds(2);
        held.Hold(scan, t0.AddSeconds(1));
        check(held.Take(scan, Began) is null, "Held reload: a screen whose load began after the request does not run again");

        held.Hold(scan, t0.AddSeconds(3));
        check(ReferenceEquals(held.Take(scan, Began), scan), "Held reload: a load that began before the request does not count");

        check(HeldReload.GivesWay(runningIsReload: true, review, scan), "Leaving a screen: its running reload gives way to the new screen's");
        check(!HeldReload.GivesWay(runningIsReload: true, scan, scan), "Leaving a screen: a reload of the same screen is not stopped");
        check(!HeldReload.GivesWay<object>(runningIsReload: false, null, scan), "Leaving a screen: a user action is never stopped");
        check(!HeldReload.GivesWay<object>(runningIsReload: true, null, scan), "Leaving a screen: a reload with no screen named is not stopped");
    }
}
