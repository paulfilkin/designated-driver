using System.Text;
using System.Text.RegularExpressions;

namespace DesignatedDriver.Core;

/// <summary>Branch names: feature/, fix/, docs/ or chore/, then the issue number and a short name.</summary>
public static partial class BranchNames
{
    public const int MaxSlugLength = 40;

    /// <summary>The convention's label to prefix table: bug gives fix/, docs gives docs/, chore gives chore/, anything else feature/.</summary>
    public static readonly IReadOnlyList<(string Label, string Prefix)> PrefixByLabel =
    [
        ("bug", "fix/"),
        ("docs", "docs/"),
        ("chore", "chore/"),
    ];

    public const string DefaultPrefix = "feature/";

    public static string Prefix(IEnumerable<string> labels)
    {
        var set = new HashSet<string>(labels, StringComparer.OrdinalIgnoreCase);
        foreach (var (label, prefix) in PrefixByLabel)
            if (set.Contains(label)) return prefix;
        return DefaultPrefix;
    }

    public static string Build(IEnumerable<string> labels, int issueNumber, string title)
    {
        var slug = Slug(title);
        return slug.Length == 0 ? $"{Prefix(labels)}{issueNumber}" : $"{Prefix(labels)}{issueNumber}-{slug}";
    }

    /// <summary>Lower case, letters and digits only, words joined by hyphens, cut at a word boundary.</summary>
    public static string Slug(string title)
    {
        var lowered = title.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in lowered)
        {
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        }
        var slug = HyphenRuns().Replace(sb.ToString(), "-").Trim('-');
        if (slug.Length <= MaxSlugLength) return slug;

        var cut = slug.LastIndexOf('-', MaxSlugLength);
        return (cut > 0 ? slug[..cut] : slug[..MaxSlugLength]).Trim('-');
    }

    /// <summary>The issue number a branch was made for, if its name follows the convention.</summary>
    public static int? IssueNumber(string branch)
    {
        var m = IssueBranch().Match(branch);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
    }

    public static bool FollowsConvention(string branch) => IssueBranch().IsMatch(branch);

    [GeneratedRegex("-{2,}")]
    private static partial Regex HyphenRuns();

    [GeneratedRegex(@"^(?:feature|fix|docs|chore)/(\d+)(?:-|$)")]
    private static partial Regex IssueBranch();
}
