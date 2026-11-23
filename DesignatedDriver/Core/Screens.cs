using DesignatedDriver.Resources;

namespace DesignatedDriver.Core;

public enum Screen { Repositories, Scan, NewRepository, Status, Check, StartWork, SaveAndShare, Submit, Review, TidyUp, FixProblem }

/// <summary>The actions Review offers on a pull request.</summary>
public enum ReviewOffer { Approve, RequestChanges, Merge, BypassMerge }

/// <summary>
/// Which screens and actions a user sees. Review, with approve, request changes and merge, appears for write access
/// and above; merge with bypass only for an admin (Roles, issue 81). A role that couldn't be read hides nothing.
/// </summary>
public static class ScreenCatalogue
{
    private static readonly Screen[] Everyone = [Screen.Repositories, Screen.Scan, Screen.NewRepository, Screen.Status, Screen.Check, Screen.StartWork, Screen.SaveAndShare, Screen.Submit, Screen.TidyUp, Screen.FixProblem];

    public static IReadOnlyList<Screen> ScreensFor(Role role)
    {
        var list = new List<Screen>(Everyone);
        if (Roles.CanReview(role)) list.Insert(list.IndexOf(Screen.TidyUp), Screen.Review);
        return list;
    }

    public static IReadOnlyList<ReviewOffer> ActionsFor(Role role)
        => !Roles.CanReview(role) ? []
         : Roles.CanBypass(role) ? [ReviewOffer.Approve, ReviewOffer.RequestChanges, ReviewOffer.Merge, ReviewOffer.BypassMerge]
         : [ReviewOffer.Approve, ReviewOffer.RequestChanges, ReviewOffer.Merge];

    /// <summary>Review needs write access; the other screens are open to anyone with the repository.</summary>
    public static bool NeedsWriteAccess(Screen screen) => screen == Screen.Review;

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
