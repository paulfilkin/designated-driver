using DesignatedDriver.Resources;

namespace DesignatedDriver.Core;

public enum Screen { Repositories, Scan, NewRepository, Status, Check, StartWork, SaveAndShare, Submit, Review, TidyUp, FixProblem }

/// <summary>Actions that only repository admins get.</summary>
public enum AdminAction { Approve, RequestChanges, Merge, BypassMerge }

/// <summary>
/// Which screens and actions a user sees. Review, approve, merge and bypass appear only for users GitHub
/// reports as repository admins, which on a personal repository is the owner.
/// </summary>
public static class ScreenCatalogue
{
    private static readonly Screen[] Everyone = [Screen.Repositories, Screen.Scan, Screen.NewRepository, Screen.Status, Screen.Check, Screen.StartWork, Screen.SaveAndShare, Screen.Submit, Screen.TidyUp, Screen.FixProblem];

    public static IReadOnlyList<Screen> ScreensFor(bool isAdmin)
    {
        var list = new List<Screen>(Everyone);
        if (isAdmin) list.Insert(list.IndexOf(Screen.TidyUp), Screen.Review);
        return list;
    }

    public static IReadOnlyList<AdminAction> ActionsFor(bool isAdmin)
        => isAdmin ? [AdminAction.Approve, AdminAction.RequestChanges, AdminAction.Merge, AdminAction.BypassMerge] : [];

    public static bool IsAdminOnly(Screen screen) => screen == Screen.Review;

    /// <summary>Repositories, Scan and New repository work before any repository is chosen; the workflow screens need one.</summary>
    public static bool NeedsRepository(Screen screen) => screen is not (Screen.Repositories or Screen.Scan or Screen.NewRepository);

    /// <summary>Every screen is built; kept so the navigation can hide a screen that is being worked on.</summary>
    public static bool IsBuilt(Screen screen) => Enum.IsDefined(screen);

    public static string Title(Screen screen) => screen switch
    {
        Screen.Repositories => Strings.Nav_Repositories,
        Screen.Scan => Strings.Nav_Scan,
        Screen.NewRepository => Strings.Nav_NewRepository,
        Screen.Status => Strings.Nav_Status,
        Screen.Check => Strings.Nav_Check,
        Screen.StartWork => Strings.Nav_StartWork,
        Screen.SaveAndShare => Strings.Nav_SaveAndShare,
        Screen.Submit => Strings.Nav_Submit,
        Screen.Review => Strings.Nav_Review,
        Screen.TidyUp => Strings.Nav_TidyUp,
        Screen.FixProblem => Strings.Nav_FixProblem,
        _ => screen.ToString(),
    };
}
