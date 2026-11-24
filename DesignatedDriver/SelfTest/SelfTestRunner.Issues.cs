using DesignatedDriver.Core;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// New issue checks: each built-in choice has the fields of its issue form and the label of its name, a
/// repository's own forms are read into choices (issue 88), Create waits for the title and the required fields,
/// and the body is written the way GitHub's forms write it.
/// </summary>
public static partial class SelfTestRunner
{
    /// <summary>A made-up form with every field type GitHub's issue forms have.</summary>
    private const string SampleForm = """
        name: Feature request
        description: "Ask for something new: one feature per form."
        title: "[Feature]: "
        labels: ["enhancement", 'needs triage']   # applied on create
        body:
          - type: markdown
            attributes:
              value: |
                Thanks for taking the time.
                Search the open issues first.

          # The request itself
          - type: textarea
            id: summary
            attributes:
              label: What should it do?
              description: >
                Describe the change
                from the user's side.
              value: |
                As a user, I want
            validations:
              required: true
          - type: dropdown
            id: areas
            attributes:
              label: Which areas?
              multiple: true
              options:
                - Editor
                - "Files: import and export"
                - Settings
            validations:
              required: true
          - type: dropdown
            id: size
            attributes:
              label: How big?
              options: [Small, Medium, Large]
              default: 1
          - type: input
            id: link
            attributes:
              label: Link to a mock-up
              placeholder: https://example.com/mock-up
          - type: checkboxes
            id: checks
            attributes:
              label: Before you submit
              options:
                - label: I searched the open issues
                  required: true
                - label: I can help test it
          - type: textarea
            id: log
            attributes:
              label: Example output
              render: shell
        """;

    private static void IssueTextChecks(Action<bool, string> check)
    {
        check(IssueTexts.Bug.Fields.Count == 5 && IssueTexts.Feature.Fields.Count == 4 && IssueTexts.Chore.Fields.Count == 3 && IssueTexts.Docs.Fields.Count == 3,
            "New issue: a bug has the five fields of bug.yml, a feature the four of feature.yml, a chore and docs three");
        check(IssueTexts.Bug.Fields.Count(f => f.Required) == 3 && IssueTexts.Feature.Fields.Count(f => f.Required) == 3
            && IssueTexts.Bug.Fields.Single(f => f.Id == "version") is { Required: false, Type: IssueFieldType.Input },
            "New issue: the required fields match the forms, and Version is a single optional line");
        check(IssueTexts.BuiltIn.Select(c => c.Labels.Single()).SequenceEqual(["bug", "feature", "chore", "docs"])
            && new[] { "bug", "feature", "chore", "docs" }.All(l => ConventionLabels.All.Any(c => c.Name == l)),
            "New issue: each built-in choice's label is one the convention creates");

        var values = new IssueAnswers { Text = { ["happened"] = "It crashed.", ["expected"] = "No crash.", ["steps"] = "1. Open\r\n2. Click" } };
        var blankSteps = new IssueAnswers { Text = { ["happened"] = "It crashed.", ["expected"] = "No crash.", ["steps"] = "  " } };
        check(!IssueTexts.IsComplete(IssueTexts.Bug, "", values) && IssueTexts.IsComplete(IssueTexts.Bug, "Crash on open", values)
            && !IssueTexts.IsComplete(IssueTexts.Bug, "Crash on open", blankSteps),
            "New issue: complete needs the title and every required field, and spaces do not count");

        check(IssueTexts.Body(IssueTexts.Bug, values) == "### What happened?\n\nIt crashed.\n\n### What did you expect?\n\nNo crash.\n\n### Steps to reproduce\n\n1. Open\n2. Click\n\n### Version\n\n_No response_\n\n### Notes\n\n_No response_\n",
            "New issue: the body has a level-three heading and the answer per field, in the form's order, and an empty optional field reads _No response_");
        var withVersion = new IssueAnswers { Text = { ["happened"] = "It crashed.", ["version"] = "v0.1.1" } };
        check(IssueTexts.Body(IssueTexts.Bug, withVersion).Contains("### Version\n\nv0.1.1\n\n### Notes", StringComparison.Ordinal), "New issue: a filled optional field is written in its place");
        check(IssueTexts.Body(IssueTexts.Chore, new IssueAnswers { Text = { ["what"] = "Bump the pins.", ["why"] = "Dependabot." } }).StartsWith("### What needs doing?\n\nBump the pins.\n\n### Why?\n\nDependabot.\n", StringComparison.Ordinal),
            "New issue: a chore body uses the plain headings");

        // A repository's own form (issue 88).
        var form = IssueForms.Read(SampleForm.Replace("\r\n", "\n").Replace("\n", "\r\n"), "feature_request.yml");
        check(form is { Name: "Feature request", Description: "Ask for something new: one feature per form.", TitlePrefix: "[Feature]: " }
            && form.Labels.SequenceEqual(["enhancement", "needs triage"])
            && form.Fields.Select(f => f.Type).SequenceEqual([IssueFieldType.Markdown, IssueFieldType.Textarea, IssueFieldType.Dropdown, IssueFieldType.Dropdown, IssueFieldType.Input, IssueFieldType.Checkboxes, IssueFieldType.Textarea]),
            "Issue forms: the name, description, title prefix, flow labels and every field type are read in order, past comments and blank lines");
        if (form is null) return;
        var summary = form.Fields[1];
        var areas = form.Fields[2];
        var size = form.Fields[3];
        var checks = form.Fields[5];
        check(form.Fields[0].Value == "Thanks for taking the time.\nSearch the open issues first." && summary is { Id: "summary", Label: "What should it do?", Required: true, Value: "As a user, I want\n" }
            && summary.Description == "Describe the change from the user's side.\n",
            "Issue forms: a literal block keeps its lines, a folded block joins them, and required is read from validations");
        check(areas is { Multiple: true, Required: true } && areas.Options.Select(o => o.Label).SequenceEqual(["Editor", "Files: import and export", "Settings"])
            && size is { Multiple: false, Required: false, Default: 1 } && size.Options.Select(o => o.Label).SequenceEqual(["Small", "Medium", "Large"]),
            "Issue forms: dropdown options are read in block and flow form, with a quoted colon, multiple and default");
        check(checks.Options.SequenceEqual([new IssueOption("I searched the open issues", true), new IssueOption("I can help test it")]) && form.Fields[6].Render == "shell" && form.Fields[4].Id == "link",
            "Issue forms: checkbox options keep their own required, and render is read");
        check(IssueForms.Read("name: Labels\nlabels: bug, triage\nbody:\n- type: input\n  attributes:\n    label: Where?\n", "x.yml") is { Labels: ["bug", "triage"], Fields: [{ Id: "field1", Label: "Where?" }] }
            && IssueForms.Read("body:\n  - type: textarea\n    attributes:\n      label: What?\n", "bug_report.yaml")?.Name == "bug_report",
            "Issue forms: a comma-separated labels string is read, the body list may sit at the key's indent, a field without an id gets one, and a form without a name takes its file name");
        check(IssueForms.Read("name: Empty\nbody:\n  - type: markdown\n    attributes:\n      value: Hi\n") is null && IssueForms.Read("blank_issues_enabled: false\n") is null && IssueForms.Read("") is null,
            "Issue forms: config.yml, an empty file and a form with nothing to fill are not choices");

        var answers = new IssueAnswers { Text = { ["summary"] = "Export to PDF." }, Picks = { ["areas"] = ["Settings", "Editor"], ["size"] = [], ["checks"] = ["I searched the open issues"] } };
        check(IssueTexts.Body(form, answers) == "### What should it do?\n\nExport to PDF.\n\n### Which areas?\n\nEditor, Settings\n\n### How big?\n\n_No response_\n\n### Link to a mock-up\n\n_No response_\n\n"
                + "### Before you submit\n\n- [X] I searched the open issues\n- [ ] I can help test it\n\n### Example output\n\n_No response_\n",
            "Issue forms: the body skips markdown, joins a dropdown's choices in the form's order, writes a line per checkbox and _No response_ for an empty optional field");
        var withLog = new IssueAnswers { Text = { ["summary"] = "x", ["log"] = "dotnet run\r\nok" }, Picks = { ["areas"] = ["Editor"], ["checks"] = ["I searched the open issues"] } };
        check(IssueTexts.Body(form, withLog).EndsWith("### Example output\n\n```shell\ndotnet run\nok\n```\n", StringComparison.Ordinal),
            "Issue forms: a textarea with render is fenced as its language");
        var noArea = new IssueAnswers { Text = { ["summary"] = "x" }, Picks = { ["checks"] = ["I searched the open issues"] } };
        var noTick = new IssueAnswers { Text = { ["summary"] = "x" }, Picks = { ["areas"] = ["Editor"], ["checks"] = ["I can help test it"] } };
        check(IssueTexts.IsComplete(form, "[Feature]: Export", withLog) && !IssueTexts.IsComplete(form, "[Feature]: ", withLog)
            && !IssueTexts.IsComplete(form, "Export", noArea) && !IssueTexts.IsComplete(form, "Export", noTick),
            "Issue forms: Create waits for a title beyond the prefix, a choice in a required dropdown and every required tick");

        var bugForm = IssueForms.Read("name: Bug report\nlabels: [bug]\nbody:\n  - type: textarea\n    attributes:\n      label: What?\n")!;
        var docsForm = IssueForms.Read("name: Documentation\nlabels: [Docs]\nbody:\n  - type: textarea\n    attributes:\n      label: What?\n")!;
        check(IssueTexts.Choices([bugForm, form]).Select(c => c.Name).SequenceEqual(["Bug report", "Feature request", IssueTexts.Chore.Name, IssueTexts.Docs.Name])
            && IssueTexts.Choices([docsForm]).Select(c => c.Name).SequenceEqual(["Documentation", IssueTexts.Chore.Name])
            && IssueTexts.Choices([]).Select(c => c.Name).SequenceEqual(IssueTexts.BuiltIn.Select(c => c.Name)),
            "Issue forms: one choice per form, then Chore and Docs where no form carries their label; without forms, the built-in four");
        check(IssueTexts.SplitLabels(form, ["Enhancement", "bug"]) is (["Enhancement"], ["needs triage"]),
            "Issue forms: only the labels the repository has are passed, in its spelling, and the rest are named");
    }
}
