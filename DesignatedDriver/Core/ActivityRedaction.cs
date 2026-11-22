using System.Text.RegularExpressions;

namespace DesignatedDriver.Core;

/// <summary>
/// Masks token values in what the activity panel shows: the JSON fields GitHub puts tokens in, such as the
/// temp_clone_token a private repository's details carry, and anything shaped like a GitHub token. The panel can be
/// copied, and its text ends up in chats and issues; the commands themselves still get the real output. Pure, so the
/// self-test covers it.
/// </summary>
public static partial class ActivityRedaction
{
    public const string Mask = "[hidden]";

    /// <summary>The line with every token value replaced by the mask; a line without one comes back unchanged.</summary>
    public static string Apply(string line)
    {
        if (line.Length == 0) return line;
        var masked = TokenField().Replace(line, m => $"\"{m.Groups["key"].Value}\":\"{Mask}\"");
        return GitHubToken().Replace(masked, Mask);
    }

    // "temp_clone_token":"…", "token": "…", "access_token":"…" and the like; an empty or null value is left alone.
    [GeneratedRegex("\"(?<key>[a-z_]*token|client_secret|password)\"\\s*:\\s*\"(?:[^\"\\\\]|\\\\.)+\"", RegexOptions.IgnoreCase)]
    private static partial Regex TokenField();

    // GitHub's token prefixes: gho_, ghp_, ghu_, ghs_, ghr_ and fine-grained github_pat_.
    [GeneratedRegex(@"\b(?:gh[opusr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})\b")]
    private static partial Regex GitHubToken();
}
