using DesignatedDriver.Models;
using DesignatedDriver.Resources;

namespace DesignatedDriver.Core;

public enum PlanKind { Unknown, Free, Paid }

/// <summary>
/// What GitHub allows for a repository, from the account's plan and the repository's visibility (section 2 of
/// the design). A public repository gets everything. A private one on Free gets no rulesets, code owner
/// enforcement, drafts, wiki or pages, and push protection and code scanning need a paid add-on either way.
/// </summary>
public sealed record Capabilities(
    PlanKind Plan,
    string PlanName,
    bool IsPublic,
    bool Rulesets,
    bool CodeOwners,
    bool PushProtection,
    bool CodeScanning,
    bool Wiki,
    bool DraftPullRequests,
    bool Pages)
{
    public static PlanKind KindOf(string planName)
        => planName.Length == 0 ? PlanKind.Unknown
         : planName.Equals("free", StringComparison.OrdinalIgnoreCase) ? PlanKind.Free
         : PlanKind.Paid;

    /// <summary>
    /// The plan name is empty when the token lacks the user scope, so the answer GitHub gives for the branch
    /// rules endpoint settles rulesets on a private repository: Ok means available, NotAvailable (a 403 with an
    /// upgrade message) means not. With neither, a paid plan is assumed to have them and anything else not.
    /// </summary>
    public static Capabilities From(string planName, bool isPublic, ApiStatus? rulesProbe)
    {
        var kind = KindOf(planName);
        if (isPublic) return new Capabilities(kind, planName, true, true, true, true, true, true, true, true);

        bool rules = rulesProbe switch
        {
            ApiStatus.Ok => true,
            ApiStatus.NotAvailable => false,
            _ => kind == PlanKind.Paid,
        };
        return new Capabilities(kind, planName, false, rules, rules, false, false, rules, rules, rules);
    }

    /// <summary>The plan as shown on Status: Free, the plan's own name, or unknown with the reason.</summary>
    public string PlanLabel => Plan switch
    {
        PlanKind.Free => Strings.Plan_Free,
        PlanKind.Paid => PlanName,
        _ => Strings.Plan_Unknown,
    };

    /// <summary>Each feature with whether it applies, in the order the design's table lists them.</summary>
    public IEnumerable<(string Feature, bool Available)> Features()
    {
        yield return (Strings.Capability_Rulesets, Rulesets);
        yield return (Strings.Capability_CodeOwners, CodeOwners);
        yield return (Strings.Capability_DraftPullRequests, DraftPullRequests);
        yield return (Strings.Capability_PushProtection, PushProtection);
        yield return (Strings.Capability_CodeScanning, CodeScanning);
        yield return (Strings.Capability_Wiki, Wiki);
    }
}
