using DesignatedDriver.Resources;

namespace DesignatedDriver.Core;

/// <summary>How new issues and pull requests reach a board, if anything puts them there.</summary>
public enum BoardFeedKind
{
    /// <summary>Not read: no board, or the reads failed.</summary>
    Unknown,
    /// <summary>The board's own Auto-add to project workflow is on: no token needed, a bot's pull requests included.</summary>
    AutoAdd,
    /// <summary>board.yml has PROJECT_TOKEN for Actions and for Dependabot.</summary>
    Token,
    /// <summary>board.yml has PROJECT_TOKEN for Actions only, so Dependabot's pull requests aren't added.</summary>
    TokenNotForDependabot,
    /// <summary>Nothing adds them: no token and no Auto-add; only what the app creates or adds itself reaches the board.</summary>
    None,
}

/// <summary>What was read about a board and its repository: the board's Auto-add workflow and where PROJECT_TOKEN is stored. Null is not read.</summary>
public sealed record BoardFeedFacts(bool? AutoAddOn, bool? ActionsToken, bool? DependabotToken);

/// <summary>
/// Whether a repository's board is fed (issue 67). board.yml needs PROJECT_TOKEN, and a run started by Dependabot reads
/// only Dependabot's secrets; the board's built-in Auto-add to project workflow needs no token at all, which is why it
/// is the fix offered first. GitHub's API can read that workflow but not turn it on, so the user is sent to the board's
/// Workflows page with the filter to use. Pure, so the self-test covers it.
/// </summary>
public static class BoardFeed
{
    /// <summary>The built-in workflow's name, as GitHub's API returns it.</summary>
    public const string AutoAddName = "Auto-add to project";

    /// <summary>The Auto-add filter that takes every open issue and pull request.</summary>
    public const string AutoAddFilter = "is:issue,pr is:open";

    public const string TokenSecret = "PROJECT_TOKEN";

    /// <summary>A read that failed only counts as Unknown when the reads that did succeed don't settle it.</summary>
    public static BoardFeedKind Assess(BoardFeedFacts f) =>
        f.AutoAddOn == true ? BoardFeedKind.AutoAdd
        : f.ActionsToken == true && f.DependabotToken == true ? BoardFeedKind.Token
        : f.AutoAddOn is null || f.ActionsToken is null || (f.ActionsToken == true && f.DependabotToken is null) ? BoardFeedKind.Unknown
        : f.ActionsToken == true ? BoardFeedKind.TokenNotForDependabot
        : BoardFeedKind.None;

    /// <summary>True when the board's own workflows include Auto-add, switched on; null when they couldn't be read.</summary>
    public static bool? AutoAddOn(IReadOnlyList<(string Name, bool Enabled)>? workflows)
        => workflows is null ? null : workflows.Any(w => w.Enabled && w.Name.Equals(AutoAddName, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the board gets every new issue and pull request, Dependabot's included.</summary>
    public static bool IsFed(BoardFeedKind kind) => kind is BoardFeedKind.AutoAdd or BoardFeedKind.Token;

    /// <summary>The board's Workflows page, where Auto-add is turned on; a user's board or an organisation's.</summary>
    public static string WorkflowsUrl(BoardReference board, bool organisation)
        => $"https://github.com/{(organisation ? "orgs" : "users")}/{board.Owner}/projects/{board.Number}/workflows";

    /// <summary>One line saying what feeds the board, or that nothing does and how to fix it.</summary>
    public static string Text(BoardFeedKind kind) => kind switch
    {
        BoardFeedKind.AutoAdd => Strings.BoardFeed_AutoAdd,
        BoardFeedKind.Token => Strings.BoardFeed_Token,
        BoardFeedKind.TokenNotForDependabot => string.Format(Strings.BoardFeed_TokenNotForDependabot, AutoAddFilter),
        BoardFeedKind.None => string.Format(Strings.BoardFeed_None, AutoAddFilter),
        _ => Strings.BoardFeed_Unknown,
    };
}
