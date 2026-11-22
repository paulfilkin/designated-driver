using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesignatedDriver.Core;

/// <summary>Convention names that other parts look up.</summary>
public static class Convention
{
    /// <summary>The ruleset the tool creates on public repositories and paid plans, named for the branch it protects.</summary>
    public static string RulesetName(string defaultBranch) => $"Protect {defaultBranch}";

    public static bool IsConventionRuleset(string name) => name.StartsWith("Protect ", StringComparison.Ordinal);
}

/// <summary>
/// The body of POST /repos/{owner}/{repo}/rulesets, in the shape Partner Manager's ruleset returns (section 3.4
/// of the design): the default branch; no deletion, no force pushes, linear history; a pull request with squash
/// only, stale reviews dismissed and threads resolved, one approving review and a code owner's when review is
/// required; the required check; repository admins able to bypass through a pull request.
/// </summary>
public sealed class RulesetDefinition
{
    /// <summary>GitHub Actions' app id, so the required check is the one Actions reports.</summary>
    public const int ActionsIntegrationId = 15368;
    /// <summary>The repository role "admin" as a bypass actor.</summary>
    public const int AdminRoleId = 5;

    public string Name { get; set; } = "";
    public string Target { get; set; } = "branch";
    public string Enforcement { get; set; } = "active";
    public List<BypassActor> BypassActors { get; set; } = [];
    public RulesetConditions Conditions { get; set; } = new();
    public List<RulesetRule> Rules { get; set; } = [];

    public static RulesetDefinition Build(string defaultBranch, string requiredCheck, bool reviewRequired) => new()
    {
        Name = Convention.RulesetName(defaultBranch),
        BypassActors = [new BypassActor { ActorId = AdminRoleId, ActorType = "RepositoryRole", BypassMode = "pull_request" }],
        Conditions = new RulesetConditions { RefName = new RefNameCondition { Include = ["~DEFAULT_BRANCH"], Exclude = [] } },
        Rules =
        [
            new RulesetRule { Type = "deletion" },
            new RulesetRule { Type = "non_fast_forward" },
            new RulesetRule { Type = "required_linear_history" },
            new RulesetRule
            {
                Type = "pull_request",
                Parameters = new PullRequestParameters
                {
                    RequiredApprovingReviewCount = reviewRequired ? 1 : 0,
                    DismissStaleReviewsOnPush = true,
                    RequireCodeOwnerReview = reviewRequired,
                    RequireLastPushApproval = false,
                    RequiredReviewThreadResolution = true,
                    AllowedMergeMethods = ["squash"],
                },
            },
            new RulesetRule
            {
                Type = "required_status_checks",
                Parameters = new StatusChecksParameters
                {
                    RequiredStatusChecks = [new StatusCheck { Context = requiredCheck, IntegrationId = ActionsIntegrationId }],
                    StrictRequiredStatusChecksPolicy = true,
                    DoNotEnforceOnCreate = false,
                },
            },
        ],
    };

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public IEnumerable<string> RuleTypes => Rules.Select(r => r.Type);
}

public sealed class BypassActor
{
    public int ActorId { get; set; }
    public string ActorType { get; set; } = "";
    public string BypassMode { get; set; } = "";
}

public sealed class RulesetConditions
{
    public RefNameCondition RefName { get; set; } = new();
}

public sealed class RefNameCondition
{
    public List<string> Include { get; set; } = [];
    public List<string> Exclude { get; set; } = [];
}

public sealed class RulesetRule
{
    public string Type { get; set; } = "";
    public object? Parameters { get; set; }
}

public sealed class PullRequestParameters
{
    public int RequiredApprovingReviewCount { get; set; }
    public bool DismissStaleReviewsOnPush { get; set; }
    public bool RequireCodeOwnerReview { get; set; }
    public bool RequireLastPushApproval { get; set; }
    public bool RequiredReviewThreadResolution { get; set; }
    public List<string> AllowedMergeMethods { get; set; } = [];
}

public sealed class StatusChecksParameters
{
    public List<StatusCheck> RequiredStatusChecks { get; set; } = [];
    public bool StrictRequiredStatusChecksPolicy { get; set; }
    public bool DoNotEnforceOnCreate { get; set; }
}

public sealed class StatusCheck
{
    public string Context { get; set; } = "";
    public int? IntegrationId { get; set; }
}

/// <summary>A ruleset as GET /repos/{o}/{r}/rulesets/{id} returns it, for the self-test's comparison with a saved real one.</summary>
public sealed class GhRulesetDetail
{
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string Enforcement { get; set; } = "";
    public List<BypassActor> BypassActors { get; set; } = [];
    public RulesetConditions Conditions { get; set; } = new();
    public List<GhRulesetRule> Rules { get; set; } = [];
}

public sealed class GhRulesetRule
{
    public string Type { get; set; } = "";
}
