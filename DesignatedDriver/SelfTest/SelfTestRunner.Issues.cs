using DesignatedDriver.Core;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// New issue checks: each kind has the fields of its issue form and the label of its name, Create waits for the
/// title and the required fields, and the body is written the way GitHub's forms write it.
/// </summary>
public static partial class SelfTestRunner
{
    private static void IssueTextChecks(Action<bool, string> check)
    {
        check(IssueTexts.FieldsFor(IssueKind.Bug).Count == 5 && IssueTexts.FieldsFor(IssueKind.Feature).Count == 4
            && IssueTexts.FieldsFor(IssueKind.Chore).Count == 3 && IssueTexts.FieldsFor(IssueKind.Docs).Count == 3,
            "New issue: a bug has the five fields of bug.yml, a feature the four of feature.yml, a chore and docs three");
        check(IssueTexts.FieldsFor(IssueKind.Bug).Count(f => f.Required) == 3 && IssueTexts.FieldsFor(IssueKind.Feature).Count(f => f.Required) == 3
            && IssueTexts.FieldsFor(IssueKind.Bug).Single(f => f.Id == "version") is { Required: false, MultiLine: false },
            "New issue: the required fields match the forms, and Version is a single optional line");
        check(IssueTexts.Label(IssueKind.Bug) == "bug" && IssueTexts.Label(IssueKind.Feature) == "feature" && IssueTexts.Label(IssueKind.Chore) == "chore" && IssueTexts.Label(IssueKind.Docs) == "docs"
            && new[] { "bug", "feature", "chore", "docs" }.All(l => ConventionLabels.All.Any(c => c.Name == l)),
            "New issue: each kind's label is one the convention creates");

        var values = new Dictionary<string, string> { ["happened"] = "It crashed.", ["expected"] = "No crash.", ["steps"] = "1. Open\r\n2. Click" };
        check(!IssueTexts.IsComplete(IssueKind.Bug, "", values) && IssueTexts.IsComplete(IssueKind.Bug, "Crash on open", values)
            && !IssueTexts.IsComplete(IssueKind.Bug, "Crash on open", new Dictionary<string, string>(values) { ["steps"] = "  " }),
            "New issue: complete needs the title and every required field, and spaces do not count");

        var body = IssueTexts.Body(IssueKind.Bug, values);
        check(body == "### What happened?\n\nIt crashed.\n\n### What did you expect?\n\nNo crash.\n\n### Steps to reproduce\n\n1. Open\n2. Click\n",
            "New issue: the body has a level-three heading and the answer per field, in the form's order, without the empty optional ones");
        var withVersion = IssueTexts.Body(IssueKind.Bug, new Dictionary<string, string>(values) { ["version"] = "v0.1.1", ["notes"] = "" });
        check(withVersion.EndsWith("### Version\n\nv0.1.1\n", StringComparison.Ordinal), "New issue: a filled optional field is written after the required ones");
        check(IssueTexts.Body(IssueKind.Chore, new Dictionary<string, string> { ["what"] = "Bump the pins.", ["why"] = "Dependabot." }).StartsWith("### What needs doing?\n\nBump the pins.\n\n### Why?\n\nDependabot.\n", StringComparison.Ordinal),
            "New issue: a chore body uses the plain headings");
    }
}
