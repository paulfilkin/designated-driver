using System.Text;

namespace DesignatedDriver.Core;

/// <summary>
/// A line diff between the convention's text and the file in the clone, for the Check screen. Longest common
/// subsequence on lines; the files are short, so the quadratic table is fine.
/// </summary>
public static class LineDiff
{
    public enum Kind { Same, Removed, Added }

    public sealed record Line(Kind Kind, string Text);

    public static IReadOnlyList<Line> Compute(string expected, string actual)
    {
        var a = Split(expected);
        var b = Split(actual);
        var table = new int[a.Length + 1, b.Length + 1];
        for (int i = a.Length - 1; i >= 0; i--)
            for (int j = b.Length - 1; j >= 0; j--)
                table[i, j] = a[i] == b[j] ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);

        var lines = new List<Line>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y]) { lines.Add(new Line(Kind.Same, a[x])); x++; y++; }
            else if (table[x + 1, y] >= table[x, y + 1]) { lines.Add(new Line(Kind.Removed, a[x])); x++; }
            else { lines.Add(new Line(Kind.Added, b[y])); y++; }
        }
        while (x < a.Length) lines.Add(new Line(Kind.Removed, a[x++]));
        while (y < b.Length) lines.Add(new Line(Kind.Added, b[y++]));
        return lines;
    }

    /// <summary>True when the two texts are the same apart from line endings and trailing whitespace on lines.</summary>
    public static bool Same(string expected, string actual) => Split(expected).SequenceEqual(Split(actual));

    /// <summary>
    /// The differences with a little context, in the familiar form: "-" is the convention's line, "+" the clone's.
    /// Empty when the texts are the same.
    /// </summary>
    public static string Render(string expected, string actual, int context = 2)
    {
        var lines = Compute(expected, actual);
        if (lines.All(l => l.Kind == Kind.Same)) return "";
        var show = new bool[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind == Kind.Same) continue;
            for (int k = Math.Max(0, i - context); k <= Math.Min(lines.Count - 1, i + context); k++) show[k] = true;
        }
        var sb = new StringBuilder();
        bool gap = false;
        for (int i = 0; i < lines.Count; i++)
        {
            if (!show[i]) { gap = true; continue; }
            if (gap && sb.Length > 0) sb.Append("...\n");
            gap = false;
            sb.Append(lines[i].Kind switch { Kind.Removed => "- ", Kind.Added => "+ ", _ => "  " }).Append(lines[i].Text).Append('\n');
        }
        return sb.ToString();
    }

    public static (int Removed, int Added) Counts(string expected, string actual)
    {
        var lines = Compute(expected, actual);
        return (lines.Count(l => l.Kind == Kind.Removed), lines.Count(l => l.Kind == Kind.Added));
    }

    private static string[] Split(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return [.. lines];
    }
}
