using System.Text.RegularExpressions;

namespace DesignatedDriver.Core;

/// <summary>
/// The events a GitHub Actions workflow runs on, read from its top-level on: key: a single event, a list, or a
/// mapping with filters. Only what the scan needs is kept: the events, and for push its branch and tag filters.
/// </summary>
public sealed record WorkflowTriggers(IReadOnlyList<string> Events, IReadOnlyList<string> PushBranches, bool PushHasTags)
{
    public static readonly WorkflowTriggers None = new([], [], false);

    public bool Has(string e) => Events.Contains(e, StringComparer.OrdinalIgnoreCase);

    /// <summary>A push filtered on tags alone runs only for tags, as GitHub reads it.</summary>
    public bool PushTagsOnly => PushHasTags && PushBranches.Count == 0;

    /// <summary>Runs when a release is published or a tag is pushed: the signs of a workflow that makes releases.</summary>
    public bool RunsOnRelease => Has("release") || (Has("push") && PushHasTags);

    public static WorkflowTriggers Parse(string yaml)
    {
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var m = Regex.Match(lines[i], @"^(?:on|""on""|'on')\s*:(.*)$");
            if (!m.Success) continue;
            var rest = StripComment(m.Groups[1].Value).Trim();
            if (rest.StartsWith('[')) return new([.. ListItems(rest)], [], false);
            if (rest.Length > 0) return new([Unquote(rest)], [], false);
            return Block(lines, i + 1);
        }
        return None;
    }

    private static WorkflowTriggers Block(string[] lines, int start)
    {
        var events = new List<string>();
        var branches = new List<string>();
        bool tags = false;
        int childIndent = -1;
        string current = "";
        string pushKey = "";
        for (int j = start; j < lines.Length; j++)
        {
            var line = StripComment(lines[j]);
            if (line.Trim().Length == 0) continue;
            int indent = line.Length - line.TrimStart().Length;
            if (indent == 0) break;
            if (childIndent < 0) childIndent = indent;
            if (indent < childIndent) break;
            var text = line.Trim();
            if (indent == childIndent)
            {
                pushKey = "";
                if (text.StartsWith("- ", StringComparison.Ordinal)) { events.Add(Unquote(text[2..])); current = ""; continue; }
                var key = Regex.Match(text, @"^([A-Za-z_]+)\s*:");
                if (key.Success) { current = key.Groups[1].Value; events.Add(current); }
                continue;
            }
            if (!current.Equals("push", StringComparison.OrdinalIgnoreCase)) continue;
            var filter = Regex.Match(text, @"^(branches|tags)\s*:(.*)$");
            if (filter.Success)
            {
                pushKey = filter.Groups[1].Value;
                var inline = filter.Groups[2].Value.Trim();
                if (pushKey == "tags") tags = true;
                if (pushKey == "branches" && inline.Length > 0) branches.AddRange(inline.StartsWith('[') ? ListItems(inline) : [Unquote(inline)]);
                continue;
            }
            if (text.StartsWith("- ", StringComparison.Ordinal) && pushKey == "branches") branches.Add(Unquote(text[2..]));
            else if (!text.StartsWith("- ", StringComparison.Ordinal)) pushKey = "";
        }
        return new(events, branches, tags);
    }

    private static IEnumerable<string> ListItems(string flow) => flow.Trim().Trim('[', ']').Split(',').Select(Unquote).Where(s => s.Length > 0);

    private static string StripComment(string s)
    {
        int hash = s.IndexOf(" #", StringComparison.Ordinal);
        return hash >= 0 ? s[..hash] : s.StartsWith('#') ? "" : s;
    }

    private static string Unquote(string s) => s.Trim().Trim('"', '\'').Trim();

    /// <summary>True for a workflow file: a .yml or .yaml directly in .github/workflows.</summary>
    public static bool IsWorkflow(string path)
    {
        var p = path.Replace('\\', '/');
        return p.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase) && p.Count(c => c == '/') == 2
            && (p.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Whether a repository already publishes releases another way, so the convention's release workflow is left off.</summary>
public static class ReleaseEvidence
{
    public const string ConventionWorkflow = ".github/workflows/release.yml";

    /// <summary>
    /// The reasons, empty when there are none: releases with files attached, and workflows other than the
    /// convention's that run on tags or releases. A repository that already has the convention's release
    /// workflow, or chose it in its manifest, has none.
    /// </summary>
    public static List<string> Reasons(ScanFacts f)
    {
        var reasons = new List<string>();
        if (f.Files.Any(p => p.Equals(ConventionWorkflow, StringComparison.OrdinalIgnoreCase)) || f.Manifest?.Options.ReleaseWorkflow == true) return reasons;
        var published = f.Releases.Where(r => !r.Draft && r.Assets.Count > 0).ToList();
        if (published.Count > 0)
        {
            var latest = published[0];
            reasons.Add(string.Format(Resources.Strings.Scan_ReleasesPublished, published.Count, latest.TagName, string.Join(", ", latest.Assets.Select(a => a.Name))));
        }
        var workflows = f.WorkflowTriggers.Where(w => !w.Key.Equals(ConventionWorkflow, StringComparison.OrdinalIgnoreCase) && w.Value.RunsOnRelease).Select(w => w.Key).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (workflows.Count > 0) reasons.Add(string.Format(Resources.Strings.Scan_ReleaseWorkflows, string.Join(", ", workflows)));
        return reasons;
    }
}
