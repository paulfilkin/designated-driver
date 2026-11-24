using DesignatedDriver.Core;
using DesignatedDriver.Models;

namespace DesignatedDriver.SelfTest;

/// <summary>Start work's issue card lays the body out: form headings, paragraphs, lists, task lines, code and No response.</summary>
public static partial class SelfTestRunner
{
    private static void IssueBodyLayoutChecks(Action<bool, string> check)
    {
        // A made-up body as an issue form writes it, with a comment, a list, a numbered step, code and inline marks.
        const string body = "<!-- Filled in from the form. -->\r\n"
            + "### What happened?\n\nThe **Save** button stays grey.\nIt did yesterday too.\n\n"
            + "### Steps to reproduce\n\n1. Open `Settings`.\n2. Change the folder.\n   - nested note\n\n"
            + "### Areas\n\n- [x] Windows\n- [ ] Pages\n\n"
            + "### Version\n\n_No response_\n\n"
            + "### Notes\n\nSee [the guide](https://example.org/guide).\n\n```\nexit 1\n```\n";
        var blocks = IssueBodyLayout.Parse(body);

        var headings = blocks.Where(b => b.Kind == BodyBlockKind.Heading).ToList();
        check(headings.Select(h => h.Text).SequenceEqual(["What happened?", "Steps to reproduce", "Areas", "Version", "Notes"]) && headings.All(h => h.Level == 3),
            "Issue body: a form's ### sections are headings, in order, and the HTML comment is left out");

        var first = blocks.First(b => b.Kind == BodyBlockKind.Paragraph);
        check(first.Text == "The Save button stays grey.\nIt did yesterday too." && first.Spans.Any(s => s.Bold && s.Text == "Save"),
            "Issue body: a paragraph keeps its line break, and bold text is read without its marks");

        var items = blocks.Where(b => b.Kind == BodyBlockKind.Bullet).ToList();
        check(items.Count == 3 && items[0].Marker == "1." && items[1].Marker == "2." && items[2].Marker == "" && items[2].Level == 1
            && items[0].Spans.Any(s => s.Code && s.Text == "Settings"),
            "Issue body: numbered steps keep their numbers, a nested bullet its level, and inline code is marked");

        var tasks = blocks.Where(b => b.Kind == BodyBlockKind.Task).ToList();
        check(tasks.Count == 2 && tasks[0] is { Ticked: true, Text: "Windows" } && tasks[1] is { Ticked: false, Text: "Pages" },
            "Issue body: checkbox lines are task lines, ticked and not");

        int version = blocks.ToList().FindIndex(b => b.Kind == BodyBlockKind.Heading && b.Text == "Version");
        check(version >= 0 && blocks[version + 1].Kind == BodyBlockKind.NoResponse,
            "Issue body: an unanswered field's _No response_ is its own block, to be greyed");

        check(blocks.Any(b => b.Kind == BodyBlockKind.Paragraph && b.Text == "See the guide.")
            && blocks.Last() is { Kind: BodyBlockKind.Code, Text: "exit 1" },
            "Issue body: a link shows as its text, and fenced code is kept as written");

        check(IssueBodyLayout.Parse("").Count == 0 && IssueBodyLayout.Parse("Just one line").Single() is { Kind: BodyBlockKind.Paragraph, Text: "Just one line" },
            "Issue body: an empty body has no blocks, and plain text is one paragraph");

        var issue = GhJson.Parse<GhIssue>("{\"number\":7,\"title\":\"Example\",\"author\":{\"login\":\"example-user\"},\"createdAt\":\"2026-11-24T09:30:00Z\"}");
        check(issue.Author?.Login == "example-user" && issue.CreatedAt == new DateTime(2026, 11, 24, 9, 30, 0, DateTimeKind.Utc),
            "Issue body: gh's author and createdAt are read for the card's opened-by line");
    }
}
