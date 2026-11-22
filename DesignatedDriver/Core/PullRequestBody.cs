using System.IO;
using System.Text.RegularExpressions;

namespace DesignatedDriver.Core;

/// <summary>Fills the repository's pull request template: Closes #n under Issue, the checklist left for the user.</summary>
public static partial class PullRequestBody
{
    public const string TemplateRelativePath = @".github\pull_request_template.md";

    /// <summary>
    /// Where GitHub reads a single pull request template, in the order the tool looks. A template in a
    /// .github\PULL_REQUEST_TEMPLATE folder is only used when the pull request link names it, so it is not a default.
    /// </summary>
    public static readonly IReadOnlyList<string> TemplatePaths = [TemplateRelativePath, "pull_request_template.md", @"docs\pull_request_template.md"];

    /// <summary>
    /// The convention's template, used when the repository has none. Phase 2 moves this into the embedded
    /// convention files; until then it is the generic shape from the design: issue, what and why, how it was
    /// tested, checklist, AI assistance.
    /// </summary>
    public const string FallbackTemplate =
        "## Issue\n\nCloses #<!-- the issue number, for example 42 -->\n\n" +
        "## What and why\n\n<!-- What this changes and why. -->\n\n" +
        "## How it was tested\n\n<!-- What you ran and what you looked at. -->\n\n" +
        "## Checklist\n\n" +
        "- [ ] The pull request closes one issue, named above.\n" +
        "- [ ] It builds with no warnings and the tests pass on my machine.\n" +
        "- [ ] No data files, keys, passwords or tokens are included.\n" +
        "- [ ] Text in the UI, comments and docs is British English, with no em-dashes.\n\n" +
        "## AI assistance\n\n<!-- Say whether an AI coding assistant was used and for what. You are responsible for every line either way. -->\n";

    public static string LoadTemplate(string repositoryFolder)
    {
        foreach (var relative in TemplatePaths)
        {
            var path = Path.Combine(repositoryFolder, relative);
            try
            {
                if (File.Exists(path)) return File.ReadAllText(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return FallbackTemplate;
    }

    /// <summary>
    /// Replaces the Closes line, with any comment after it on the line, with "Closes #n". Adds an Issue section at
    /// the top when the template has no Closes line, including one whose only "Closes #" is inside a comment.
    /// </summary>
    public static string Fill(string template, int issueNumber)
    {
        var text = template.Replace("\r\n", "\n");
        if (FindClosesLine(text) is { } line)
        {
            var closes = line.Groups["closes"];
            return text[..closes.Index] + $"Closes #{issueNumber}" + text[(closes.Index + closes.Length)..];
        }
        return $"## Issue\n\nCloses #{issueNumber}\n\n" + text;
    }

    /// <summary>The number on the Closes line, or null when there is none or it has no number yet.</summary>
    public static int? IssueNumber(string body)
    {
        var line = FindClosesLine(body.Replace("\r\n", "\n"));
        return line is not null && int.TryParse(line.Groups["number"].Value, out var n) ? n : null;
    }

    /// <summary>
    /// The first "Closes #" that starts a line, after optional spaces, and is not inside an HTML comment: the line
    /// GitHub reads to close the issue and the guard workflow checks for. A comment can follow it on the line.
    /// </summary>
    private static Match? FindClosesLine(string text)
    {
        var comments = HtmlComment().Matches(text);
        foreach (Match m in ClosesLine().Matches(text))
        {
            int at = m.Groups["closes"].Index;
            if (!comments.Any(c => at > c.Index && at < c.Index + c.Length)) return m;
        }
        return null;
    }

    [GeneratedRegex(@"^[ \t]*(?<closes>Closes[ \t]+#(?<number>\d+)?(?:[ \t]*<!--.*?-->)?)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex ClosesLine();

    /// <summary>An HTML comment; one left open runs to the end, as GitHub hides the rest of the text.</summary>
    [GeneratedRegex(@"<!--.*?(?:-->|\z)", RegexOptions.Singleline)]
    private static partial Regex HtmlComment();
}
