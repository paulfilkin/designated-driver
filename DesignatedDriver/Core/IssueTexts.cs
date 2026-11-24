using System.Text;
using DesignatedDriver.Resources;

namespace DesignatedDriver.Core;

/// <summary>The field types of GitHub's issue forms that New issue shows.</summary>
public enum IssueFieldType { Markdown, Textarea, Input, Dropdown, Checkboxes }

/// <summary>An option of a dropdown or a checkboxes field; a required checkbox must be ticked.</summary>
public sealed record IssueOption(string Label, bool Required = false);

/// <summary>
/// One field of an issue form: its heading in the body, whether it must be filled, and whether it takes several
/// lines. The rest follow the form: the type, help text, a dropdown's or checkboxes' options, a dropdown's multiple
/// and default, a box's starting value (a markdown field's text), and the language a textarea is rendered as.
/// </summary>
public sealed record IssueField(string Id, string Label, bool Required, bool MultiLine = true)
{
    public IssueFieldType Type { get; init; } = MultiLine ? IssueFieldType.Textarea : IssueFieldType.Input;
    public string Description { get; init; } = "";
    public IReadOnlyList<IssueOption> Options { get; init; } = [];
    public bool Multiple { get; init; }
    public int? Default { get; init; }
    public string Value { get; init; } = "";
    public string Render { get; init; } = "";
}

/// <summary>A choice on New issue: one of the repository's issue forms, or a built-in one when it has none.</summary>
public sealed record IssueChoice(string Name, IReadOnlyList<string> Labels, IReadOnlyList<IssueField> Fields, string TitlePrefix = "", string Description = "");

/// <summary>What was entered on New issue: text by field id, and the options picked in dropdowns and ticked in checkboxes.</summary>
public sealed class IssueAnswers
{
    public Dictionary<string, string> Text { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<string>> Picks { get; } = new(StringComparer.Ordinal);

    public string TextOf(string id) => Text.TryGetValue(id, out var v) ? v : "";
    public IReadOnlyList<string> PicksOf(string id) => Picks.TryGetValue(id, out var v) ? v : [];
}

/// <summary>
/// The choices and fields of New issue and the body it writes. A repository's own issue forms give the choices
/// (issue 88); without them, the built-in Bug, Feature, Chore and Docs mirror the convention's bug.yml and
/// feature.yml. Either way the body is written as GitHub's issue forms write it, so an issue made here reads the
/// same as one made on GitHub. Field labels are repository content, so English and not in the resources.
/// </summary>
public static class IssueTexts
{
    /// <summary>What GitHub writes for an optional field left empty.</summary>
    public const string NoResponse = "_No response_";

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

    public static IssueChoice Bug => new(Strings.NewIssue_Bug, ["bug"], BugFields);
    public static IssueChoice Feature => new(Strings.NewIssue_Feature, ["feature"], FeatureFields);
    public static IssueChoice Chore => new(Strings.NewIssue_Chore, ["chore"], PlainFields);
    public static IssueChoice Docs => new(Strings.NewIssue_Docs, ["docs"], PlainFields);

    /// <summary>The choices for a repository with no issue forms of any kind.</summary>
    public static IReadOnlyList<IssueChoice> BuiltIn => [Bug, Feature, Chore, Docs];

    /// <summary>
    /// The choices New issue offers: one per form, in the order given, then Chore and Docs where no form carries
    /// those labels. With no forms, the built-in four.
    /// </summary>
    public static List<IssueChoice> Choices(IReadOnlyList<IssueChoice> forms)
    {
        if (forms.Count == 0) return [.. BuiltIn];
        var list = forms.ToList();
        foreach (var extra in new[] { Chore, Docs })
            if (!forms.Any(f => f.Labels.Contains(extra.Labels[0], StringComparer.OrdinalIgnoreCase))) list.Add(extra);
        return list;
    }

    /// <summary>
    /// True when the title has text beyond the form's title prefix, every required box has text, every required
    /// dropdown has a choice, and every required checkbox is ticked.
    /// </summary>
    public static bool IsComplete(IssueChoice choice, string title, IssueAnswers answers)
    {
        var t = title.Trim();
        if (t.Length == 0 || t == choice.TitlePrefix.Trim()) return false;
        foreach (var f in choice.Fields)
        {
            bool ok = f.Type switch
            {
                IssueFieldType.Textarea or IssueFieldType.Input => !f.Required || answers.TextOf(f.Id).Trim().Length > 0,
                IssueFieldType.Dropdown => !f.Required || answers.PicksOf(f.Id).Count > 0,
                IssueFieldType.Checkboxes => f.Options.Where(o => o.Required).All(o => answers.PicksOf(f.Id).Contains(o.Label)),
                _ => true,
            };
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>
    /// The body as GitHub's issue forms write it: a "### Label" heading, a blank line and the answer for every field
    /// in the form's order. An empty answer reads _No response_; a dropdown's choices are joined with ", " in the
    /// form's order; each checkbox option gives a "- [X]" or "- [ ]" line; a textarea with render is fenced as that
    /// language; markdown fields write nothing. Trailing spaces are kept out and the text ends with one newline.
    /// </summary>
    public static string Body(IssueChoice choice, IssueAnswers answers)
    {
        var sb = new StringBuilder();
        foreach (var field in choice.Fields)
        {
            if (field.Type == IssueFieldType.Markdown) continue;
            var picks = answers.PicksOf(field.Id);
            var text = field.Type switch
            {
                IssueFieldType.Checkboxes => string.Join("\n", field.Options.Select(o => (picks.Contains(o.Label) ? "- [X] " : "- [ ] ") + o.Label)),
                IssueFieldType.Dropdown => string.Join(", ", field.Options.Select(o => o.Label).Where(picks.Contains)),
                _ => answers.TextOf(field.Id).Replace("\r\n", "\n").Trim(),
            };
            if (text.Length == 0) text = NoResponse;
            else if (field.Type == IssueFieldType.Textarea && field.Render.Length > 0) text = $"```{field.Render}\n{text}\n```";
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("### ").Append(field.Label).Append("\n\n").Append(text).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>The choice's labels that the repository has, in the repository's spelling, and those it lacks.</summary>
    public static (List<string> Use, List<string> Missing) SplitLabels(IssueChoice choice, IReadOnlyCollection<string> repositoryLabels)
    {
        var use = new List<string>();
        var missing = new List<string>();
        foreach (var label in choice.Labels)
        {
            var have = repositoryLabels.FirstOrDefault(l => l.Equals(label, StringComparison.OrdinalIgnoreCase));
            if (have is null) missing.Add(label);
            else if (!use.Contains(have)) use.Add(have);
        }
        return (use, missing);
    }
}
