using DesignatedDriver.Resources;

namespace DesignatedDriver.Core;

/// <summary>The signed-in user's role on a repository, as GitHub's viewerPermission reports it; Unknown when it couldn't be read.</summary>
public enum Role { Unknown, Read, Triage, Write, Maintain, Admin }

/// <summary>
/// What a role may do in the app (issue 81), so nothing is offered that GitHub would refuse. Approving, requesting
/// changes and merging need write access, as on GitHub; merge with bypass and storing the board token need admin.
/// When the role couldn't be read nothing is greyed out: the place says so, and GitHub refuses what isn't allowed.
/// Pure, so the self-test covers every role.
/// </summary>
public static class Roles
{
    /// <summary>The role from gh's viewerPermission (READ, TRIAGE, WRITE, MAINTAIN, ADMIN); Unknown for anything else, an empty value included.</summary>
    public static Role Parse(string? permission) => permission?.Trim().ToUpperInvariant() switch
    {
        "READ" => Role.Read,
        "TRIAGE" => Role.Triage,
        "WRITE" => Role.Write,
        "MAINTAIN" => Role.Maintain,
        "ADMIN" => Role.Admin,
        _ => Role.Unknown,
    };

    /// <summary>Approve, request changes and merge, and so the Review screen: write access and above, or a role not read.</summary>
    public static bool CanReview(Role role) => role is Role.Unknown or Role.Write or Role.Maintain or Role.Admin;

    /// <summary>Merge with bypass uses admin rights.</summary>
    public static bool CanBypass(Role role) => role is Role.Unknown or Role.Admin;

    /// <summary>Storing a repository secret, such as the board token, needs admin.</summary>
    public static bool CanStoreSecrets(Role role) => role is Role.Unknown or Role.Admin;

    /// <summary>The role as a word for the user, such as WRITE.</summary>
    public static string Name(Role role) => role.ToString().ToUpperInvariant();

    /// <summary>
    /// Review's notice for the role: none for an admin; what write and maintain may do; that the role couldn't be read.
    /// Read and triage don't see Review.
    /// </summary>
    public static string ReviewNotice(Role role) => role switch
    {
        Role.Admin => "",
        Role.Unknown => Strings.Review_RoleUnknown,
        _ => string.Format(Strings.Review_RoleNoBypass, Name(role)),
    };

    /// <summary>
    /// Why something that needs admin is greyed out, from a format naming the repository ({0}) and the role ({1});
    /// empty when the role is admin or wasn't read, since then nothing is greyed out.
    /// </summary>
    public static string NeedsAdmin(Role role, string ownerAndName, string format)
        => role is Role.Admin or Role.Unknown ? "" : string.Format(format, ownerAndName, Name(role));
}
