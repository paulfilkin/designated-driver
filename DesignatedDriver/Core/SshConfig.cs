using System.IO;

namespace DesignatedDriver.Core;

/// <summary>One Host block of ~/.ssh/config: the alias, the real host name and the key file, where given.</summary>
public sealed record SshHostEntry(string Alias, string HostName, string IdentityFile);

/// <summary>
/// Reads the Host blocks of ~/.ssh/config. Someone with more than one GitHub account keeps a key per account
/// under an alias such as github-paulfilkin, so a clone URL has to name the alias, not github.com, and a URL
/// that names the alias is still a GitHub URL.
/// </summary>
public static class SshConfig
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");

    private static readonly Lazy<IReadOnlyList<SshHostEntry>> Loaded = new(() =>
    {
        try { return File.Exists(DefaultPath) ? Parse(File.ReadAllText(DefaultPath)) : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    });

    /// <summary>The entries of the user's own config, read once.</summary>
    public static IReadOnlyList<SshHostEntry> Entries => Loaded.Value;

    public static IReadOnlyList<SshHostEntry> Parse(string text)
    {
        var entries = new List<SshHostEntry>();
        string? alias = null, hostName = null, identity = null;
        void Flush()
        {
            if (alias is not null) entries.Add(new SshHostEntry(alias, hostName ?? alias, identity ?? ""));
            alias = hostName = identity = null;
        }
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split([' ', '\t', '='], 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;
            var key = parts[0];
            var value = parts[1];
            if (key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                // "Host a b" names two aliases; the first is enough for a suggestion, and wildcards aren't aliases.
                alias = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(a => !a.Contains('*') && !a.Contains('?'));
            }
            else if (alias is not null && key.Equals("HostName", StringComparison.OrdinalIgnoreCase)) hostName = value;
            else if (alias is not null && key.Equals("IdentityFile", StringComparison.OrdinalIgnoreCase)) identity = value;
        }
        Flush();
        return entries;
    }

    /// <summary>Aliases that stand for github.com, github.com itself included.</summary>
    public static IEnumerable<string> GitHubHosts(IEnumerable<SshHostEntry> entries)
        => entries.Where(e => e.HostName.Equals("github.com", StringComparison.OrdinalIgnoreCase)).Select(e => e.Alias).Prepend("github.com").Distinct(StringComparer.OrdinalIgnoreCase);

    public static bool IsGitHubHost(string host) => GitHubHosts(Entries).Contains(host, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The SSH host to clone with for a login: the alias whose name or key file names the login, else the only
    /// GitHub alias there is, else github.com.
    /// </summary>
    public static string HostFor(string login, IEnumerable<SshHostEntry> entries)
    {
        var github = entries.Where(e => e.HostName.Equals("github.com", StringComparison.OrdinalIgnoreCase)).ToList();
        if (login.Length > 0)
        {
            var named = github.FirstOrDefault(e => e.Alias.Contains(login, StringComparison.OrdinalIgnoreCase) || e.IdentityFile.Contains(login, StringComparison.OrdinalIgnoreCase));
            if (named is not null) return named.Alias;
        }
        return github.Count == 1 ? github[0].Alias : "github.com";
    }

    public static string HostFor(string login) => HostFor(login, Entries);
}
