using DesignatedDriver.Models;

namespace DesignatedDriver.Core;

/// <summary>
/// Whether the rules already in force on the default branch do what the convention's ruleset would (section 3.4 of
/// the design): no deletion, no force pushes, pull requests only, and a required check. Read from the branch rules
/// endpoint, which merges every ruleset that applies, so a repository's own ruleset counts whatever it is called.
/// Pure, so the self-test can feed it rules.
/// </summary>
public static class RuleCoverage
{
    /// <summary>The rule types the convention's ruleset relies on; linear history is the convention's taste, not a protection.</summary>
    public static readonly IReadOnlyList<string> Needed = ["deletion", "non_fast_forward", "pull_request", "required_status_checks"];

    /// <summary>True when every needed rule is in force and at least one check is required, so set-up adds no ruleset.</summary>
    public static bool Covers(IEnumerable<GhBranchRule> rules)
    {
        var list = rules.ToList();
        return Needed.All(type => list.Any(r => r.Type.Equals(type, StringComparison.Ordinal))) && RequiredCheck(list).Length > 0;
    }

    /// <summary>The first check the rules require, or empty when they require none.</summary>
    public static string RequiredCheck(IEnumerable<GhBranchRule> rules) => rules.SelectMany(r => r.RequiredChecks).FirstOrDefault() ?? "";

    /// <summary>
    /// The check a ruleset set-up adds should require: the one the repository's rules already name, since that is
    /// the check its workflows report, else the fallback (the convention's own ci.yml job).
    /// </summary>
    public static string CheckFor(IEnumerable<GhBranchRule> rules, string fallback)
    {
        var check = RequiredCheck(rules);
        return check.Length > 0 ? check : fallback;
    }
}
