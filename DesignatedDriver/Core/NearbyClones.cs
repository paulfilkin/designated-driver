using System.IO;
using DesignatedDriver.Models;

namespace DesignatedDriver.Core;

/// <summary>
/// Finds a clone of a repository beside the clones the tool already knows: in the folders that hold them, one level
/// deep, by reading each subfolder's .git/config for its origin. No command runs and nothing is changed, so a scan by
/// name can say where a clone already is instead of offering to clone it again.
/// </summary>
public static class NearbyClones
{
    /// <summary>The folders that hold the known clones, each once, leaving out any that no longer exist.</summary>
    public static IReadOnlyList<string> SearchFolders(IEnumerable<string> knownClones)
        => knownClones
            .Select(folder => Path.GetDirectoryName(folder.Trim().TrimEnd('\\', '/')) ?? "")
            .Where(parent => parent.Length > 0 && Directory.Exists(parent))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The first subfolder of the search folders whose origin is the repository on GitHub, matched without regard to
    /// case, or empty. A folder that can't be read is passed over.
    /// </summary>
    public static string Find(string ownerAndName, IEnumerable<string> searchFolders)
    {
        var wanted = ownerAndName.Trim().Trim('/');
        if (wanted.Length == 0) return "";
        foreach (var parent in searchFolders)
        {
            IEnumerable<string> folders;
            try { folders = Directory.EnumerateDirectories(parent).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            foreach (var folder in folders)
            {
                var config = Path.Combine(folder, ".git", "config");
                string text;
                try
                {
                    if (!File.Exists(config)) continue;
                    text = File.ReadAllText(config);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

                var origin = OriginIn(text);
                if (origin.Length == 0 || !ScanFacts.IsGitHubUrl(origin)) continue;
                if (RemoteUrl.ParseOwnerAndRepository(origin) is { } parsed && $"{parsed.Owner}/{parsed.Repository}".Equals(wanted, StringComparison.OrdinalIgnoreCase))
                    return folder;
            }
        }
        return "";
    }

    /// <summary>The url of the [remote "origin"] section of a .git/config, or empty when there is none.</summary>
    public static string OriginIn(string configText)
    {
        bool inOrigin = false;
        foreach (var raw in configText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inOrigin = line.Replace(" ", "", StringComparison.Ordinal).Equals("[remote\"origin\"]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inOrigin) continue;
            int equals = line.IndexOf('=');
            if (equals > 0 && line[..equals].Trim().Equals("url", StringComparison.OrdinalIgnoreCase)) return line[(equals + 1)..].Trim();
        }
        return "";
    }
}
