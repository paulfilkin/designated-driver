using DesignatedDriver.Core;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// Rules already in force that cover the convention's ruleset: Scan says so and proposes none, Check doesn't call it
/// missing, and a ruleset that is still added requires the check the repository's rules already name.
/// </summary>
public static partial class SelfTestRunner
{
    private static void RuleCoverageChecks(Action<bool, string> check)
    {
        static GhBranchRule Rule(string type, params string[] checks) => new()
        {
            Type = type,
            Parameters = checks.Length > 0 ? new GhBranchRuleParameters { RequiredStatusChecks = checks.Select(c => new GhRequiredCheck { Context = c }).ToList() } : null,
        };
        // A repository's own ruleset, as the branch rules endpoint returns it, with a check of its own name.
        List<GhBranchRule> own = [Rule("deletion"), Rule("non_fast_forward"), Rule("required_linear_history"), Rule("pull_request"), Rule("required_status_checks", "Build and run checks")];
        List<GhBranchRule> noPullRequests = [.. own.Where(r => r.Type != "pull_request")];
        List<GhBranchRule> noCheck = [.. own.Where(r => r.Type != "required_status_checks"), Rule("required_status_checks")];

        check(RuleCoverage.Covers(own) && !RuleCoverage.Covers(noPullRequests) && !RuleCoverage.Covers(noCheck) && !RuleCoverage.Covers([]),
            "Rule coverage: rules with no deletion, no force pushes, pull requests and a required check cover the convention's ruleset; missing any one, or a check, don't");
        check(RuleCoverage.Covers(own.Where(r => r.Type != "required_linear_history")),
            "Rule coverage: linear history isn't needed for the rules to cover it");
        check(RuleCoverage.CheckFor(own, "Build and test") == "Build and run checks" && RuleCoverage.CheckFor(noCheck, "Build and test") == "Build and test",
            "Rule coverage: a ruleset set-up adds requires the check the rules already name, else the convention's");

        // Scan, on a public repository, where rulesets apply.
        ScanReport Scan(List<GhBranchRule> rules) => ScanRules.Assess(new ScanFacts
        {
            OwnerAndName = "example-org/example-app",
            RemoteLookedUp = true,
            Details = new GhRepositoryDetails { FullName = "example-org/example-app" },
            BranchRules = rules,
        });
        var covered = Scan(own);
        check(covered.Findings.FirstOrDefault(f => f.Code == ScanCode.RulesCover) is { Severity: ScanSeverity.Ok } ok && ok.Text.Contains("Build and run checks", StringComparison.Ordinal)
            && !covered.Has(ScanCode.CreateRuleset) && !covered.Has(ScanCode.RulesAlreadyApply),
            "Rule coverage: Scan says rules that cover the convention's ruleset are already in place, naming their check, and proposes no ruleset");
        var partial = Scan(noPullRequests);
        check(partial.Has(ScanCode.RulesAlreadyApply) && partial.Findings.FirstOrDefault(f => f.Code == ScanCode.CreateRuleset) is { Severity: ScanSeverity.Change } add
            && add.Text == string.Format(Strings.Scan_CreateRuleset, "main", "Build and run checks"),
            "Rule coverage: Scan lists a ruleset added beside rules that don't cover it as a change, requiring the check those rules name");

        // Check, on the tidy repository made public so rulesets apply: covering rules aren't a missing ruleset.
        var (facts, expected, actual) = TidyRepository();
        var open = Clone(facts);
        open.Details = new GhRepositoryDetails { FullName = "paulfilkin/example", Private = false };
        open.BranchRulesStatus = ApiStatus.Ok;
        open.BranchRules = own;
        var withRules = Drift(open, expected, actual);
        var bare = Clone(open);
        bare.BranchRules = [];
        var without = Drift(bare, expected, actual);
        check(!withRules.Items.Any(i => i.Key == "ruleset") && without.Items.Any(i => i.Key == "ruleset" && i.Kind == DriftKind.Missing),
            "Rule coverage: Check doesn't call the ruleset missing when the rules in force cover it, and still does when there are none");
    }
}
