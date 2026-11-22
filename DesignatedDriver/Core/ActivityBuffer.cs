using System.Text;
using DesignatedDriver.Services;

namespace DesignatedDriver.Core;

/// <summary>What one flush of the buffer hands the panel: text to append, or the whole kept text when the start was cut.</summary>
public readonly record struct ActivityFlush(string Added, bool Trimmed, string Whole);

/// <summary>
/// The activity panel's text, gathered off the UI thread and handed over in one piece per flush, so a command
/// that prints hundreds of lines costs the window one append rather than one rebuild of the whole panel per line.
/// Pure, so the self-test covers the line format and the trim.
/// </summary>
public sealed class ActivityBuffer(int limit = 400_000, int keep = 300_000)
{
    private readonly object _lock = new();
    private readonly StringBuilder _kept = new();
    private readonly StringBuilder _pending = new();

    /// <summary>One line of the panel: a command, its output, an error, a result or a note, each with its own prefix.</summary>
    public static string Line(ActivityEntry entry) => entry.Kind switch
    {
        ActivityKind.Command => "> " + entry.Text,
        ActivityKind.Error => "! " + entry.Text,
        ActivityKind.Result => "  " + entry.Text,
        ActivityKind.Note => "# " + entry.Text,
        _ => "  " + entry.Text,
    };

    /// <summary>The longest a command is shown in the busy strip, so a long gh api call does not push the Cancel button away.</summary>
    public const int ShortCommandLength = 70;

    /// <summary>A command line short enough for the busy strip.</summary>
    public static string ShortCommand(string commandLine)
        => commandLine.Length <= ShortCommandLength ? commandLine : commandLine[..(ShortCommandLength - 3)] + "...";

    public bool HasPending { get { lock (_lock) return _pending.Length > 0; } }

    /// <summary>The text the panel should be showing once everything pending is flushed.</summary>
    public string Text { get { lock (_lock) return _kept.ToString() + _pending.ToString(); } }

    public void Add(ActivityEntry entry)
    {
        lock (_lock)
        {
            if (entry.Kind == ActivityKind.Command && _kept.Length + _pending.Length > 0) _pending.AppendLine();
            _pending.AppendLine(Line(entry));
        }
    }

    /// <summary>
    /// Moves the pending text into the kept text. When the kept text passes the limit its start is cut back to
    /// the keep length and Trimmed says so, so the panel replaces its text instead of appending.
    /// </summary>
    public ActivityFlush Flush()
    {
        lock (_lock)
        {
            var added = _pending.ToString();
            _pending.Clear();
            _kept.Append(added);
            if (_kept.Length <= limit) return new ActivityFlush(added, false, "");
            _kept.Remove(0, _kept.Length - keep);
            return new ActivityFlush("", true, _kept.ToString());
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _kept.Clear();
            _pending.Clear();
        }
    }
}
