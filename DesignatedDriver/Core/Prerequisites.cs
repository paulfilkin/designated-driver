using System.Text.RegularExpressions;

namespace DesignatedDriver.Core;

/// <summary>What gh auth status reported.</summary>
public sealed class AuthStatus
{
    public static readonly string[] RequiredScopes = ["repo", "workflow", "project"];

    public bool SignedIn { get; set; }
    public string Account { get; set; } = "";
    public string Host { get; set; } = "github.com";
    public List<string> Scopes { get; } = [];

    public IEnumerable<string> MissingScopes => RequiredScopes.Where(s => !Scopes.Contains(s, StringComparer.OrdinalIgnoreCase));
    public bool HasRequiredScopes => !MissingScopes.Any();

    /// <summary>
    /// True when gh listed the token's scopes and this one isn't among them, so a read that needs it can only fail.
    /// False when it is there, or when gh listed no scopes at all and the answer isn't known.
    /// </summary>
    public bool Lacks(string scope) => Scopes.Count > 0 && !Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);

    public static string FixCommandForScope(string scope) => $"gh auth refresh -h github.com -s {scope}";
    public const string SignInCommand = "gh auth login";
    public const string InstallGitCommand = "winget install Git.Git";
    public const string InstallGhCommand = "winget install GitHub.cli";
}

/// <summary>Reads the text of gh auth status, which gh writes for people rather than programs.</summary>
public static partial class AuthStatusParser
{
    public static AuthStatus Parse(string text)
    {
        var status = new AuthStatus();
        var lines = text.Replace("\r\n", "\n").Split('\n');

        // Only the active account counts. gh lists every account; the active one is marked "Active account: true"
        // after its "Logged in to" line, so the block is read as a whole.
        string? currentAccount = null;
        bool currentLoggedIn = false;
        var currentScopes = new List<string>();
        bool? currentActive = null;
        bool found = false;

        void Commit()
        {
            if (currentAccount is null || found) return;
            if (currentActive == false) return;
            status.SignedIn = currentLoggedIn;
            status.Account = currentAccount;
            status.Scopes.Clear();
            status.Scopes.AddRange(currentScopes);
            found = currentActive == true;
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var login = LoggedIn().Match(line);
            if (login.Success)
            {
                Commit();
                currentAccount = login.Groups[2].Value;
                currentLoggedIn = !line.Contains("not logged", StringComparison.OrdinalIgnoreCase)
                                  && !line.StartsWith("X", StringComparison.Ordinal);
                status.Host = login.Groups[1].Value;
                currentScopes = [];
                currentActive = null;
                continue;
            }
            var active = ActiveAccount().Match(line);
            if (active.Success) { currentActive = active.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase); continue; }

            var scopes = TokenScopes().Match(line);
            if (scopes.Success)
            {
                currentScopes = ScopeName().Matches(scopes.Groups[1].Value).Select(m => m.Groups[1].Value).ToList();
            }
        }
        Commit();
        return status;
    }

    [GeneratedRegex(@"Logged in to (\S+) (?:account|as) (\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex LoggedIn();

    [GeneratedRegex(@"Active account:\s*(true|false)", RegexOptions.IgnoreCase)]
    private static partial Regex ActiveAccount();

    [GeneratedRegex(@"Token scopes:\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex TokenScopes();

    [GeneratedRegex(@"'([^']+)'")]
    private static partial Regex ScopeName();
}

/// <summary>The checks on the Status screen: tools installed, signed in, scopes present.</summary>
public sealed class PrerequisiteReport
{
    public bool GitFound { get; set; }
    public string GitVersion { get; set; } = "";
    public bool GhFound { get; set; }
    public string GhVersion { get; set; } = "";
    public AuthStatus Auth { get; set; } = new();

    public bool AllMet => GitFound && GhFound && Auth.SignedIn && Auth.HasRequiredScopes;
}
