using DesignatedDriver.Resources;

namespace DesignatedDriver.Core;

public enum CheckSeverity { Error, Warning }

public enum CommitMessageRule
{
    SummaryEmpty,
    SummaryTooLong,
    SummaryNotImperative,
    SummaryLowerCase,
    SummaryTrailingFullStop,
    SummaryMultipleLines,
    EmDash,
    DetailLineTooLong,
}

public sealed record CommitMessageIssue(CommitMessageRule Rule, CheckSeverity Severity)
{
    public string Message => Rule switch
    {
        CommitMessageRule.SummaryEmpty => Strings.Commit_Check_SummaryEmpty,
        CommitMessageRule.SummaryTooLong => string.Format(Strings.Commit_Check_SummaryTooLong, CommitMessageChecks.SummaryLimit),
        CommitMessageRule.SummaryNotImperative => Strings.Commit_Check_SummaryNotImperative,
        CommitMessageRule.SummaryLowerCase => Strings.Commit_Check_SummaryLowerCase,
        CommitMessageRule.SummaryTrailingFullStop => Strings.Commit_Check_SummaryTrailingFullStop,
        CommitMessageRule.SummaryMultipleLines => Strings.Commit_Check_SummaryMultipleLines,
        CommitMessageRule.EmDash => Strings.Commit_Check_EmDash,
        CommitMessageRule.DetailLineTooLong => string.Format(Strings.Commit_Check_DetailLineTooLong, CommitMessageChecks.DetailLimit),
        _ => Rule.ToString(),
    };
}

/// <summary>
/// The commit message rules from CONTRIBUTING.md section 6: a short summary in the imperative, no longer than
/// about 70 characters, then a blank line and detail wrapped at about 72. British English, no em-dashes.
/// </summary>
public static class CommitMessageChecks
{
    public const int SummaryLimit = 70;
    public const int DetailLimit = 72;

    // Common past-tense and third-person first words that aren't imperative. A list rather than a rule,
    // because "Address", "Process" and "Pass" are fine and "Added" isn't.
    private static readonly HashSet<string> NotImperative = new(StringComparer.OrdinalIgnoreCase)
    {
        "added", "adds", "adding", "fixed", "fixes", "fixing", "updated", "updates", "updating",
        "removed", "removes", "removing", "changed", "changes", "changing", "moved", "moves", "moving",
        "renamed", "renames", "renaming", "improved", "improves", "improving", "refactored", "refactors",
        "refactoring", "implemented", "implements", "implementing", "corrected", "corrects", "correcting",
        "created", "creates", "creating", "deleted", "deletes", "deleting", "made", "makes", "making",
        "replaced", "replaces", "replacing", "documented", "documents", "documenting", "reworded",
        "rewrote", "tidied", "tidies", "cleaned", "cleans", "merged", "merges", "reverted", "reverts",
        "introduced", "introduces", "allowed", "allows", "enabled", "enables", "disabled", "disables",
        "handled", "handles", "wip", "misc", "stuff",
    };

    public static IReadOnlyList<CommitMessageIssue> Check(string summary, string detail)
    {
        var issues = new List<CommitMessageIssue>();
        var s = (summary ?? "").Trim();
        var d = detail ?? "";

        if (s.Length == 0)
        {
            issues.Add(new CommitMessageIssue(CommitMessageRule.SummaryEmpty, CheckSeverity.Error));
        }
        else
        {
            if (s.Contains('\n')) issues.Add(new CommitMessageIssue(CommitMessageRule.SummaryMultipleLines, CheckSeverity.Error));
            if (s.Length > SummaryLimit) issues.Add(new CommitMessageIssue(CommitMessageRule.SummaryTooLong, CheckSeverity.Warning));

            var firstWord = s.Split([' ', '\t', '\n', ':'], 2, StringSplitOptions.RemoveEmptyEntries)[0].Trim('(', ')', ',', '.');
            if (NotImperative.Contains(firstWord)) issues.Add(new CommitMessageIssue(CommitMessageRule.SummaryNotImperative, CheckSeverity.Warning));
            if (char.IsLower(s[0])) issues.Add(new CommitMessageIssue(CommitMessageRule.SummaryLowerCase, CheckSeverity.Warning));
            if (s.EndsWith('.')) issues.Add(new CommitMessageIssue(CommitMessageRule.SummaryTrailingFullStop, CheckSeverity.Warning));
        }

        if (s.Contains('—') || d.Contains('—'))
            issues.Add(new CommitMessageIssue(CommitMessageRule.EmDash, CheckSeverity.Error));

        if (d.Split('\n').Any(line => line.TrimEnd('\r').Length > DetailLimit && !line.Contains("://", StringComparison.Ordinal)))
            issues.Add(new CommitMessageIssue(CommitMessageRule.DetailLineTooLong, CheckSeverity.Warning));

        return issues;
    }

    public static bool IsAcceptable(string summary, string detail) => Check(summary, detail).All(i => i.Severity != CheckSeverity.Error);

    /// <summary>The message as it is written to the file for git commit -F.</summary>
    public static string Compose(string summary, string detail)
    {
        var s = (summary ?? "").Trim();
        var d = (detail ?? "").Replace("\r\n", "\n").Trim();
        return d.Length == 0 ? s + "\n" : s + "\n\n" + d + "\n";
    }
}
