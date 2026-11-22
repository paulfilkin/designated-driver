namespace DesignatedDriver.Models;

/// <summary>One line of git status --porcelain=v2.</summary>
public sealed class ChangedFile
{
    public string Path { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    /// <summary>Two-letter XY code; "??" for untracked, "UU" and friends for conflicts.</summary>
    public string Code { get; set; } = "";
    public bool IsUntracked => Code == "??";
    public bool IsConflict => Code.Length == 2 && (Code[0] == 'U' || Code[1] == 'U' || Code is "AA" or "DD");
}

/// <summary>git status --porcelain=v2 --branch, parsed.</summary>
public sealed class GitStatus
{
    public string Branch { get; set; } = "";
    public string Upstream { get; set; } = "";
    public bool IsDetached => Branch == "(detached)";
    public bool HasUpstream => Upstream.Length > 0;
    public int AheadOfUpstream { get; set; }
    public int BehindUpstream { get; set; }
    public List<ChangedFile> Changes { get; } = [];
    public bool IsClean => Changes.Count == 0;
    public bool HasConflicts => Changes.Any(c => c.IsConflict);

    public static GitStatus Parse(string porcelainV2)
    {
        var status = new GitStatus();
        foreach (var raw in porcelainV2.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;

            if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                status.Branch = line["# branch.head ".Length..];
            }
            else if (line.StartsWith("# branch.upstream ", StringComparison.Ordinal))
            {
                status.Upstream = line["# branch.upstream ".Length..];
            }
            else if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                // "# branch.ab +1 -2"
                var parts = line["# branch.ab ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    if (p.StartsWith('+') && int.TryParse(p[1..], out var a)) status.AheadOfUpstream = a;
                    else if (p.StartsWith('-') && int.TryParse(p[1..], out var b)) status.BehindUpstream = b;
                }
            }
            else if (line.StartsWith('#'))
            {
                // other header lines
            }
            else if (line.StartsWith("1 ", StringComparison.Ordinal))
            {
                // 1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>
                var parts = line.Split(' ', 9);
                if (parts.Length == 9) status.Changes.Add(new ChangedFile { Code = parts[1], Path = parts[8] });
            }
            else if (line.StartsWith("2 ", StringComparison.Ordinal))
            {
                // 2 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <X><score> <path><tab><origPath>
                var parts = line.Split(' ', 10);
                if (parts.Length == 10)
                {
                    var paths = parts[9].Split('\t');
                    status.Changes.Add(new ChangedFile { Code = parts[1], Path = paths[0], OriginalPath = paths.Length > 1 ? paths[1] : "" });
                }
            }
            else if (line.StartsWith("u ", StringComparison.Ordinal))
            {
                // u <XY> <sub> <m1> <m2> <m3> <mW> <h1> <h2> <h3> <path>
                var parts = line.Split(' ', 11);
                if (parts.Length == 11) status.Changes.Add(new ChangedFile { Code = parts[1], Path = parts[10] });
            }
            else if (line.StartsWith("? ", StringComparison.Ordinal))
            {
                status.Changes.Add(new ChangedFile { Code = "??", Path = line[2..] });
            }
            // "! " ignored files are not reported unless asked for.
        }
        return status;
    }
}

public sealed record AheadBehind(int Ahead, int Behind)
{
    /// <summary>Reads "3\t1" from git rev-list --left-right --count HEAD...origin/main (left is HEAD, right is origin/main).</summary>
    public static AheadBehind Parse(string text)
    {
        var parts = text.Trim().Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
        int ahead = parts.Length > 0 && int.TryParse(parts[0], out var a) ? a : 0;
        int behind = parts.Length > 1 && int.TryParse(parts[1], out var b) ? b : 0;
        return new AheadBehind(ahead, behind);
    }
}

public static class RemoteUrl
{
    public const string Https = "https";
    public const string Ssh = "ssh";

    /// <summary>The clone URL for a repository over the chosen protocol; sshHost is github.com or an alias from ~/.ssh/config.</summary>
    public static string For(string protocol, string sshHost, string ownerAndName)
        => protocol.Equals(Ssh, StringComparison.OrdinalIgnoreCase) ? $"git@{(sshHost.Length > 0 ? sshHost : "github.com")}:{ownerAndName}.git" : $"https://github.com/{ownerAndName}.git";

    /// <summary>The host of an SSH URL (github.com or an alias), or empty for anything else.</summary>
    public static string SshHostOf(string url)
    {
        var u = url.Trim();
        if (u.StartsWith("git@", StringComparison.OrdinalIgnoreCase)) { int colon = u.IndexOf(':'); return colon > 4 ? u[4..colon] : ""; }
        if (u.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(u, UriKind.Absolute, out var uri)) return uri.Host;
        return "";
    }

    /// <summary>True when a remote URL uses the protocol: git@ or ssh:// for SSH, https:// for HTTPS.</summary>
    public static bool UsesProtocol(string url, string protocol)
    {
        var u = url.Trim();
        bool ssh = u.StartsWith("git@", StringComparison.OrdinalIgnoreCase) || u.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase);
        return protocol.Equals(Ssh, StringComparison.OrdinalIgnoreCase) ? ssh : u.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Owner and repository from an origin URL, HTTPS or SSH, with or without .git.</summary>
    public static (string Owner, string Repository)? ParseOwnerAndRepository(string url)
    {
        var u = url.Trim();
        if (u.Length == 0) return null;
        string path;
        if (u.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(u, UriKind.Absolute, out var uri)) return null;
            path = uri.AbsolutePath;
        }
        else
        {
            // git@github.com:Owner/Repo.git
            var colon = u.IndexOf(':');
            if (colon < 0) return null;
            path = u[(colon + 1)..];
        }
        var parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        var repo = parts[^1];
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo[..^4];
        return (parts[^2], repo);
    }
}
