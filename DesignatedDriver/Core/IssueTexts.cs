using System.Text;

namespace DesignatedDriver.Core;

/// <summary>The kinds of issue the tool writes, each with the convention's label of the same name.</summary>
public enum IssueKind { Bug, Feature, Chore, Docs }

/// <summary>One field of an issue form: its heading in the body, whether it must be filled, and whether it takes several lines.</summary>
public sealed record IssueField(string Id, string Label, bool Required, bool MultiLine = true);

/// <summary>
/// The fields and the body of an issue written from the tool, mirroring the repository's issue forms (bug.yml
/// and feature.yml in the convention) so an issue made here reads the same as one made on GitHub: GitHub
/// renders each form field as a level-three heading followed by the answer. Repository content, so English
/// and not in the resources.
/// </summary>
public static class IssueTexts
{
    private static readonly IReadOnlyList<IssueField> BugFields =
    [
        new("happened", "What happened?", Required: true),
        new("expected", "What did you expect?", Required: true),
        new("steps", "Steps to reproduce", Required: true),
        new("version", "Version", Required: false, MultiLine: false),
        new("notes", "Notes", Required: false),
    ];

    private static readonly IReadOnlyList<IssueField> FeatureFields =
    [
        new("what", "What should it do?", Required: true),
        new("why", "Who needs it and why?", Required: true),
        new("acceptance", "Acceptance criteria", Required: true),
        new("notes", "Notes", Required: false),
    ];

    private static readonly IReadOnlyList<IssueField> PlainFields =
    [
        new("what", "What needs doing?", Required: true),
        new("why", "Why?", Required: true),
        new("notes", "Notes", Required: false),
    ];

    public static IReadOnlyList<IssueField> FieldsFor(IssueKind kind) => kind switch
    {
        IssueKind.Bug => BugFields,
        IssueKind.Feature => FeatureFields,
        _ => PlainFields,
    };

    /// <summary>The convention label the kind carries; the four exist in every repository the tool sets up.</summary>
    public static string Label(IssueKind kind) => kind switch
    {
        IssueKind.Bug => "bug",
        IssueKind.Feature => "feature",
        IssueKind.Chore => "chore",
        _ => "docs",
    };

    /// <summary>True when the title and every required field have text.</summary>
    public static bool IsComplete(IssueKind kind, string title, IReadOnlyDictionary<string, string> values)
        => title.Trim().Length > 0 && FieldsFor(kind).Where(f => f.Required).All(f => values.TryGetValue(f.Id, out var v) && v.Trim().Length > 0);

    /// <summary>
    /// The body as GitHub's issue forms write it: a "### Label" heading and the answer for each field, in the
    /// form's order, leaving out optional fields with no answer. Trailing spaces are kept out and the text ends
    /// with one newline.
    /// </summary>
    public static string Body(IssueKind kind, IReadOnlyDictionary<string, string> values)
    {
        var sb = new StringBuilder();
        foreach (var field in FieldsFor(kind))
        {
            var text = values.TryGetValue(field.Id, out var v) ? v.Replace("\r\n", "\n").Trim() : "";
            if (text.Length == 0) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("### ").Append(field.Label).Append("\n\n").Append(text).Append('\n');
        }
        return sb.ToString();
    }
}
