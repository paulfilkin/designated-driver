using DesignatedDriver.Core;
using DesignatedDriver.Resources;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// What each role may do (issue 81): review needs write access, merge with bypass and storing the board token need
/// admin, and a role that couldn't be read greys out nothing.
/// </summary>
public static partial class SelfTestRunner
{
    private static void RoleChecks(Action<bool, string> check)
    {
        check(Roles.Parse("ADMIN") == Role.Admin && Roles.Parse("maintain") == Role.Maintain && Roles.Parse(" WRITE ") == Role.Write
            && Roles.Parse("TRIAGE") == Role.Triage && Roles.Parse("READ") == Role.Read && Roles.Parse("") == Role.Unknown
            && Roles.Parse(null) == Role.Unknown && Roles.Parse("OWNER") == Role.Unknown,
            "Roles: gh's viewerPermission is read without regard to case or spaces; anything else is Unknown");

        var expected = new (Role Role, bool Review, bool Bypass, bool Secrets)[]
        {
            (Role.Read, false, false, false), (Role.Triage, false, false, false), (Role.Write, true, false, false),
            (Role.Maintain, true, false, false), (Role.Admin, true, true, true), (Role.Unknown, true, true, true),
        };
        check(expected.All(e => Roles.CanReview(e.Role) == e.Review && Roles.CanBypass(e.Role) == e.Bypass && Roles.CanStoreSecrets(e.Role) == e.Secrets),
            "Roles: review needs write, maintain or admin; bypass and the board token need admin; an unread role greys out nothing");

        check(Roles.ReviewNotice(Role.Admin) == "" && Roles.ReviewNotice(Role.Unknown) == Strings.Review_RoleUnknown
            && Roles.ReviewNotice(Role.Write) == string.Format(Strings.Review_RoleNoBypass, "WRITE") && Roles.ReviewNotice(Role.Maintain).Contains("MAINTAIN", StringComparison.Ordinal),
            "Roles: Review says nothing to an admin, what write and maintain may do, and that a role couldn't be read");

        const string repo = "example-org/example-app";
        check(Roles.NeedsAdmin(Role.Write, repo, Strings.Settings_BypassNeedsAdmin) == string.Format(Strings.Settings_BypassNeedsAdmin, repo, "WRITE")
            && Roles.NeedsAdmin(Role.Read, repo, Strings.Status_TokenNeedsAdmin).Contains("READ", StringComparison.Ordinal)
            && Roles.NeedsAdmin(Role.Admin, repo, Strings.Settings_BypassNeedsAdmin) == "" && Roles.NeedsAdmin(Role.Unknown, repo, Strings.Settings_BypassNeedsAdmin) == "",
            "Roles: a greyed-out control names the repository and the role; an admin or an unread role gets no note");
    }
}
