using System.Text.RegularExpressions;

namespace DesignatedDriver.Core;

/// <summary>One of the convention's labels: name, colour (six hex digits) and description.</summary>
public sealed record ConventionLabel(string Name, string Color, string Description);

/// <summary>The fixed label set from section 3.2 of the design. Labels not in the set are left alone.</summary>
public static class ConventionLabels
{
    public static readonly IReadOnlyList<ConventionLabel> All =
    [
        new("bug", "d73a4a", "Something isn't working"),
        new("feature", "a2eeef", "New feature or request"),
        new("docs", "0075ca", "Documentation only"),
        new("chore", "cfd3d7", "Maintenance, tooling and dependencies"),
        new("question", "d876e3", "Further information is requested"),
        new("good first issue", "7057ff", "Good for newcomers"),
        new("blocked", "b60205", "Waiting on something else"),
        new("wontfix", "ffffff", "This will not be worked on"),
    ];

    /// <summary>
    /// GitHub's default labels that mean the same as a convention label. A repository that has the default and not
    /// the convention's label gets the default renamed, so the issues it labels keep it, rather than a second label.
    /// </summary>
    public static readonly IReadOnlyList<(string From, string To)> Renames = [("enhancement", "feature"), ("documentation", "docs")];

    public static bool SameColour(string a, string b)
        => string.Equals(a.TrimStart('#'), b.TrimStart('#'), StringComparison.OrdinalIgnoreCase);

    public static bool SameDescription(string a, string b)
        => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>What the labels step does, label by label, given the repository's labels; empty when they are in step.</summary>
    public static List<LabelAction> Plan(IReadOnlyList<Models.GhLabel> have)
    {
        Models.GhLabel? Find(string name) => have.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var plan = new List<LabelAction>();
        foreach (var wanted in All)
        {
            var existing = Find(wanted.Name);
            if (existing is not null)
            {
                if (!SameColour(existing.Color, wanted.Color) || !SameDescription(existing.Description, wanted.Description))
                    plan.Add(new LabelAction(LabelChange.Recolour, wanted));
                continue;
            }
            var from = Renames.Where(r => r.To.Equals(wanted.Name, StringComparison.OrdinalIgnoreCase)).Select(r => Find(r.From)).FirstOrDefault(l => l is not null);
            plan.Add(from is null ? new LabelAction(LabelChange.Create, wanted) : new LabelAction(LabelChange.Rename, wanted, from.Name));
        }
        return plan;
    }

    /// <summary>Default and convention labels that both exist: both are left alone, and the scan says so.</summary>
    public static List<(string From, string To)> BothPresent(IReadOnlyList<Models.GhLabel> have)
        => Renames.Where(r => have.Any(l => l.Name.Equals(r.From, StringComparison.OrdinalIgnoreCase)) && have.Any(l => l.Name.Equals(r.To, StringComparison.OrdinalIgnoreCase))).ToList();
}

public enum LabelChange { Create, Recolour, Rename }

/// <summary>One change to the labels: create or recolour a convention label, or rename a default label (From) to it.</summary>
public sealed record LabelAction(LabelChange Change, ConventionLabel Label, string From = "");

/// <summary>Reads the labels an issue form applies, from its top-level labels key, in flow or block form.</summary>
public static class IssueForms
{
    public static List<string> Labels(string yaml)
    {
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var m = Regex.Match(lines[i], @"^labels\s*:\s*(.*)$");
            if (!m.Success) continue;
            var rest = StripComment(m.Groups[1].Value).Trim();
            if (rest.StartsWith('['))
                return [.. rest.Trim('[', ']').Split(',').Select(Unquote).Where(s => s.Length > 0)];
            if (rest.Length > 0) return [.. rest.Split(',').Select(Unquote).Where(s => s.Length > 0)];
            var list = new List<string>();
            for (int j = i + 1; j < lines.Length; j++)
            {
                var item = Regex.Match(lines[j], @"^\s*-\s*(.+)$");
                if (item.Success) { list.Add(Unquote(StripComment(item.Groups[1].Value))); continue; }
                if (lines[j].Trim().Length == 0) continue;
                break;
            }
            return [.. list.Where(s => s.Length > 0)];
        }
        return [];
    }

    private static string StripComment(string s)
    {
        int hash = s.IndexOf(" #", StringComparison.Ordinal);
        return hash >= 0 ? s[..hash] : s;
    }

    private static string Unquote(string s) => s.Trim().Trim('"', '\'').Trim();

    /// <summary>True for a path that is an issue form: a .yml or .yaml file in .github/ISSUE_TEMPLATE other than config.yml.</summary>
    public static bool IsForm(string path)
    {
        var p = path.Replace('\\', '/');
        if (!p.StartsWith(".github/ISSUE_TEMPLATE/", StringComparison.OrdinalIgnoreCase) || p.Count(c => c == '/') != 2) return false;
        var name = p[(p.LastIndexOf('/') + 1)..];
        return (name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            && !name.Equals("config.yml", StringComparison.OrdinalIgnoreCase) && !name.Equals("config.yaml", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The board columns, by name, as the workflow screens look them up. The convention keeps GitHub's own Status
/// options (Todo, In Progress, Done) and never rewrites the field: rewriting it regenerates the option ids and
/// GitHub then disables its built-in automations (item added, item closed, pull request merged). "Ready" is
/// accepted as the pick-up column for boards made before that was known, Partner Manager's among them.
/// </summary>
public static class ConventionBoard
{
    public const string Todo = "Todo";
    public const string InProgress = "In Progress";
    public const string Done = "Done";

    /// <summary>The columns that mean "ready to pick up", the first being GitHub's default.</summary>
    public static readonly IReadOnlyList<string> ReadyColumns = [Todo, "Ready"];

    public static bool IsReady(string status) => ReadyColumns.Contains(status, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when a Status field has every column the workflow screens need.</summary>
    public static bool HasRequiredColumns(IEnumerable<string> statusOptions)
    {
        var set = new HashSet<string>(statusOptions, StringComparer.OrdinalIgnoreCase);
        return set.Overlaps(ReadyColumns) && set.Contains(InProgress) && set.Contains(Done);
    }
}

/// <summary>
/// One file the convention writes (section 3.1 of the design). OnlyWhenMissing files are never offered as a
/// change when present; PublicOnly files apply to public repositories; Optional ones depend on a wizard choice.
/// </summary>
public sealed record ConventionFile(string Path, bool OnlyWhenMissing = false, bool PublicOnly = false, bool Optional = false)
{
    /// <summary>True when a repository path is this file. GitHub reads some of these in more than one place or case.</summary>
    public bool Matches(string repositoryPath)
    {
        var p = repositoryPath.Replace('\\', '/').TrimStart('/');
        if (string.Equals(p, Path, StringComparison.OrdinalIgnoreCase)) return true;
        return Path switch
        {
            ".github/pull_request_template.md" => ConventionFiles.PullRequestTemplate().IsMatch(p),
            ".github/CODEOWNERS" => ConventionFiles.CodeOwners().IsMatch(p),
            _ => false,
        };
    }
}

public static partial class ConventionFiles
{
    public static readonly IReadOnlyList<ConventionFile> All =
    [
        new("README.md", OnlyWhenMissing: true),
        new("CONTRIBUTING.md"),
        new("Docs/Working with Git.md"),
        new(".github/pull_request_template.md"),
        new(".github/ISSUE_TEMPLATE/bug.yml"),
        new(".github/ISSUE_TEMPLATE/feature.yml"),
        new(".github/ISSUE_TEMPLATE/config.yml"),
        new(".github/CODEOWNERS"),
        new(".github/workflows/ci.yml"),
        new(".github/workflows/guard.yml"),
        new(".github/workflows/board.yml"),
        new(".github/workflows/release.yml", Optional: true),
        new(".github/workflows/codeql.yml", PublicOnly: true),
        new(".github/dependabot.yml"),
        new(".githooks/commit-msg"),
        new(".githooks/pre-push"),
        new(".gitignore", OnlyWhenMissing: true),
        new(".gitattributes"),
        new(".editorconfig"),
        new(Manifest.RelativePath.Replace('\\', '/')),
    ];

    public const string HooksFolder = ".githooks";

    [GeneratedRegex(@"^(?:\.github/|docs/)?pull_request_template\.md$|^\.github/PULL_REQUEST_TEMPLATE/", RegexOptions.IgnoreCase)]
    public static partial Regex PullRequestTemplate();

    [GeneratedRegex(@"^(?:\.github/|docs/)?CODEOWNERS$", RegexOptions.IgnoreCase)]
    public static partial Regex CodeOwners();
}

/// <summary>What a repository is written in, guessed from the project files near its root.</summary>
public sealed record LanguageGuess(string Language, string Evidence);

public static class ProjectLanguage
{
    public const string DotNet = "dotnet";
    public const string Node = "node";
    public const string Python = "python";
    public const string Go = "go";
    public const string Rust = "rust";
    public const string Java = "java";
    public const string Other = "other";

    private static readonly (string Language, Func<string, bool> IsProjectFile)[] Markers =
    [
        (DotNet, f => f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)),
        (Node, f => f.Equals("package.json", StringComparison.OrdinalIgnoreCase)),
        (Python, f => f.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase) || f.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase) || f.Equals("setup.py", StringComparison.OrdinalIgnoreCase)),
        (Go, f => f.Equals("go.mod", StringComparison.OrdinalIgnoreCase)),
        (Rust, f => f.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase)),
        (Java, f => f.Equals("pom.xml", StringComparison.OrdinalIgnoreCase) || f.Equals("build.gradle", StringComparison.OrdinalIgnoreCase) || f.Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase)),
    ];

    /// <summary>Looks at paths up to two folders deep, shallowest first, and names the first project file found.</summary>
    public static LanguageGuess? Detect(IEnumerable<string> repositoryPaths)
    {
        var candidates = repositoryPaths
            .Select(p => p.Replace('\\', '/').TrimStart('/'))
            .Where(p => p.Count(c => c == '/') <= 2)
            .OrderBy(p => p.Count(c => c == '/'))
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var (language, isProjectFile) in Markers)
        {
            var hit = candidates.FirstOrDefault(p => isProjectFile(p[(p.LastIndexOf('/') + 1)..]));
            if (hit is not null) return new LanguageGuess(language, hit);
        }
        return null;
    }

    public static string DisplayName(string language) => language switch
    {
        DotNet => ".NET",
        Node => "Node.js",
        Python => "Python",
        Go => "Go",
        Rust => "Rust",
        Java => "Java",
        Other => "Other",
        _ => language,
    };
}
