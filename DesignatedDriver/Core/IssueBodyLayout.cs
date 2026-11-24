using System.Text.RegularExpressions;

namespace DesignatedDriver.Core;

/// <summary>The kinds of block Start work's issue card lays an issue's body out in.</summary>
public enum BodyBlockKind { Heading, Paragraph, Bullet, Task, Code, NoResponse }

/// <summary>A run of text in a block: plain, bold, or code shown in a monospaced font.</summary>
public sealed record BodySpan(string Text, bool Bold = false, bool Code = false);

/// <summary>
/// One block of an issue's body. Level is the heading level (1 to 6) or a list item's indent (0 for the outermost);
/// Marker is a numbered item's number with its point, empty for a bullet; Ticked is a task line's box.
/// </summary>
public sealed record BodyBlock(BodyBlockKind Kind, IReadOnlyList<BodySpan> Spans, int Level = 0, bool Ticked = false, string Marker = "")
{
    public string Text => string.Concat(Spans.Select(s => s.Text));
}

/// <summary>
/// Lays out an issue's Markdown body for Start work's issue card (issue 89): the headings issue forms write, paragraphs
/// with their line breaks, bullets and numbered items, task lines, fenced code and GitHub's "_No response_" for an
/// unanswered field, with HTML comments left out. Inline, only code, bold and links (shown as their text) are read;
/// anything else shows as written. A simple layout, not full GitHub Markdown. Pure, so the self-test covers it.
/// </summary>
public static partial class IssueBodyLayout
{
    public static IReadOnlyList<BodyBlock> Parse(string body)
    {
        var blocks = new List<BodyBlock>();
        var lines = Comment().Replace(body.Replace("\r\n", "\n"), "").Split('\n');
        var paragraph = new List<string>();
        void Flush()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new BodyBlock(BodyBlockKind.Paragraph, Inline(string.Join("\n", paragraph))));
            paragraph.Clear();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            var trimmed = line.Trim();

            // A fenced code block runs to its closing fence, or to the end of the body when there is none.
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                Flush();
                var fence = trimmed[..3];
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].Trim().StartsWith(fence, StringComparison.Ordinal); i++) code.Add(lines[i].TrimEnd());
                blocks.Add(new BodyBlock(BodyBlockKind.Code, [new BodySpan(string.Join("\n", code), Code: true)]));
                continue;
            }
            if (trimmed.Length == 0) { Flush(); continue; }

            if (Heading().Match(trimmed) is { Success: true } heading)
            {
                Flush();
                blocks.Add(new BodyBlock(BodyBlockKind.Heading, Inline(heading.Groups["text"].Value.Trim().TrimEnd('#').Trim()), Level: heading.Groups["hashes"].Length));
                continue;
            }
            if (trimmed is "_No response_" or "*No response*")
            {
                Flush();
                blocks.Add(new BodyBlock(BodyBlockKind.NoResponse, [new BodySpan("No response")]));
                continue;
            }
            if (Task().Match(line) is { Success: true } task)
            {
                Flush();
                blocks.Add(new BodyBlock(BodyBlockKind.Task, Inline(task.Groups["text"].Value), Level: Indent(task), Ticked: task.Groups["box"].Value is "x" or "X"));
                continue;
            }
            if (Item().Match(line) is { Success: true } item)
            {
                Flush();
                var marker = item.Groups["marker"].Value;
                blocks.Add(new BodyBlock(BodyBlockKind.Bullet, Inline(item.Groups["text"].Value), Level: Indent(item), Marker: char.IsDigit(marker[0]) ? marker : ""));
                continue;
            }
            paragraph.Add(trimmed);
        }
        Flush();
        return blocks;
    }

    /// <summary>A list item's nesting from its leading spaces, two to a level.</summary>
    private static int Indent(Match m) => m.Groups["indent"].Value.Replace("\t", "  ").Length / 2;

    /// <summary>Inline code, bold and links; everything else is plain text as written.</summary>
    public static IReadOnlyList<BodySpan> Inline(string text)
    {
        var spans = new List<BodySpan>();
        int at = 0;
        foreach (Match m in InlineMark().Matches(text))
        {
            if (m.Index > at) spans.Add(new BodySpan(text[at..m.Index]));
            if (m.Groups["code"].Success) spans.Add(new BodySpan(m.Groups["code"].Value, Code: true));
            else if (m.Groups["bold"].Success) spans.Add(new BodySpan(m.Groups["bold"].Value, Bold: true));
            else spans.Add(new BodySpan(m.Groups["link"].Value));
            at = m.Index + m.Length;
        }
        if (at < text.Length) spans.Add(new BodySpan(text[at..]));
        return spans;
    }

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"^(?<hashes>#{1,6})\s+(?<text>.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(?<indent>\s*)[-*+]\s+\[(?<box>[ xX])\]\s+(?<text>.*)$")]
    private static partial Regex Task();

    [GeneratedRegex(@"^(?<indent>\s*)(?<marker>[-*+]|\d+[.)])\s+(?<text>.*)$")]
    private static partial Regex Item();

    [GeneratedRegex(@"`(?<code>[^`]+)`|\*\*(?<bold>.+?)\*\*|\[(?<link>[^\]]+)\]\([^)\s]+\)")]
    private static partial Regex InlineMark();
}
