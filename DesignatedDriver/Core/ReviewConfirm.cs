using DesignatedDriver.Resources;

namespace DesignatedDriver.Core;

/// <summary>The four things Review does to a pull request on GitHub, each confirmed first.</summary>
public enum ReviewAction { Approve, RequestChanges, Merge, Bypass }

/// <summary>
/// What Review's confirmation says and asks for: the repository in large type, a button that names it, and for a
/// merge the repository's name typed first unless its settings say otherwise. Pure, so the self-test covers it.
/// </summary>
public static class ReviewConfirm
{
    /// <summary>True for the actions that merge, which ask for the name typed when the repository's setting is on.</summary>
    public static bool Merges(ReviewAction action) => action is ReviewAction.Merge or ReviewAction.Bypass;

    /// <summary>Whether the confirmation asks for the repository's name typed before its button enables.</summary>
    public static bool NeedsTypedName(ReviewAction action, bool typeNameToMerge) => Merges(action) && typeNameToMerge;

    /// <summary>True when the typed text is the repository's owner and name, without regard to case or surrounding spaces.</summary>
    public static bool Matches(string typed, string ownerAndName)
        => ownerAndName.Length > 0 && typed.Trim().Equals(ownerAndName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The confirm button's label, naming the repository the action happens in.</summary>
    public static string ButtonLabel(ReviewAction action, string ownerAndName) => string.Format(action switch
    {
        ReviewAction.Approve => Strings.Confirm_ApproveIn,
        ReviewAction.RequestChanges => Strings.Confirm_RequestChangesIn,
        ReviewAction.Merge => Strings.Confirm_MergeInto,
        _ => Strings.Confirm_BypassInto,
    }, ownerAndName);

    /// <summary>The window's title: the action, so the taskbar and screen readers say what is being confirmed.</summary>
    public static string Title(ReviewAction action) => action switch
    {
        ReviewAction.Approve => Strings.Review_Approve,
        ReviewAction.RequestChanges => Strings.Review_RequestChanges,
        ReviewAction.Merge => Strings.Review_Merge,
        _ => Strings.Review_Bypass,
    };
}
