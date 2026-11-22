namespace DesignatedDriver.Core;

/// <summary>Finds the markers Git leaves in a conflicted file, so the tool can refuse to commit a half-resolved one.</summary>
public static class ConflictMarkers
{
    public const string Start = "<<<<<<<";
    public const string Middle = "=======";
    public const string End = ">>>>>>>";

    /// <summary>True when any line starts with a conflict marker. The middle marker alone isn't enough: tables and headings use rows of equals signs.</summary>
    public static bool HasMarkers(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith(Start, StringComparison.Ordinal) || line.StartsWith(End, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>Line numbers (1-based) of the start markers, for the message that names where they are.</summary>
    public static List<int> StartLines(string text)
    {
        var lines = new List<int>();
        var all = text.Split('\n');
        for (int i = 0; i < all.Length; i++)
            if (all[i].StartsWith(Start, StringComparison.Ordinal)) lines.Add(i + 1);
        return lines;
    }
}
