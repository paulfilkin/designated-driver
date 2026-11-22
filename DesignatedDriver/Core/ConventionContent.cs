using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace DesignatedDriver.Core;

/// <summary>
/// The convention's section on how a change gets in: section 2 of its CONTRIBUTING.md, and added to the end of a
/// repository's own CONTRIBUTING.md that lacks it, so a contributor finds the rules Guard enforces.
/// </summary>
public static class WorkflowSection
{
    public const string FragmentPath = "fragments/workflow-section.md";
    public const string ContributingPath = "CONTRIBUTING.md";
    /// <summary>The Check item for a kept CONTRIBUTING.md without the section. A file: key, so applying it runs the files step.</summary>
    public const string DriftKey = "file:CONTRIBUTING.md#workflow-section";

    /// <summary>The section, its heading line first and without the heading's #s, filled for the repository.</summary>
    public static string Text(IReadOnlyDictionary<string, string> values)
        => ConventionContent.Fill(ConventionResources.Read(FragmentPath), values).TrimEnd('\n');

    /// <summary>True when the Markdown has the section's heading, numbered or not, at any level.</summary>
    public static bool IsIn(string markdown)
        => Regex.IsMatch(markdown, @"^#{1,6}\s+(\d+\.\s+)?How a change gets into\b", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>The file with the section added at the end as a level-two heading, in the file's own line endings; the rest is untouched.</summary>
    public static string AppendTo(string existing, IReadOnlyDictionary<string, string> values)
    {
        var nl = existing.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return existing.TrimEnd('\r', '\n') + nl + nl + ("## " + Text(values)).Replace("\n", nl) + nl;
    }
}

/// <summary>
/// The convention's files as embedded resources, one folder per version (section 3.6 of the design). A file
/// name that starts with an underscore stands for a dot file, so the tool's own repository is not affected by
/// a .gitignore or .gitattributes meant for someone else's.
/// </summary>
public static class ConventionResources
{
    private const string Prefix = "Convention/v1/";

    private static readonly Lazy<Dictionary<string, string>> Names = new(() =>
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in typeof(ConventionResources).Assembly.GetManifestResourceNames())
        {
            var normalised = name.Replace('\\', '/');
            if (!normalised.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            map[normalised[Prefix.Length..]] = name;
        }
        return map;
    });

    /// <summary>Every resource path under the version folder, with forward slashes and the underscore names as stored.</summary>
    public static IReadOnlyCollection<string> All => Names.Value.Keys;

    public static bool Exists(string path) => Names.Value.ContainsKey(ToResourcePath(path));

    /// <summary>The text of a resource, with LF line endings whatever the working tree had.</summary>
    public static string Read(string path)
    {
        if (!Names.Value.TryGetValue(ToResourcePath(path), out var name)) throw new FileNotFoundException($"Convention resource {path} is not embedded.");
        using var stream = typeof(ConventionResources).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    /// <summary>A repository path to its resource path: forward slashes, and a leading dot on the file name becomes an underscore.</summary>
    public static string ToResourcePath(string repositoryPath)
    {
        var p = repositoryPath.Replace('\\', '/').TrimStart('/');
        int slash = p.LastIndexOf('/');
        var file = p[(slash + 1)..];
        if (file.StartsWith('.') && file.Length > 1 && !file.Contains('/', StringComparison.Ordinal))
        {
            // Only files with no extension of their own (.gitignore, .editorconfig) are stored underscored.
            if (file.IndexOf('.', 1) < 0) file = "_" + file[1..];
        }
        return slash < 0 ? file : p[..(slash + 1)] + file;
    }
}

/// <summary>
/// Fills the convention's placeholders. A placeholder is {{name}} with letters only, so GitHub's own
/// ${{ expressions }} in the workflows are left alone. Pure, so the self-test can fill every file.
/// </summary>
public static partial class ConventionContent
{
    public const string ConventionBranchSuffix = "repo-starter-v1";
    public const string SetupIssueTitle = "Set up the repository to convention v1";
    public const string UpdateBranchSuffix = "convention-update";
    public const string UpdateIssueTitle = "Update the repository to the convention";

    [GeneratedRegex(@"(?<!\$)\{\{([A-Za-z]+)\}\}")]
    public static partial Regex Placeholder();

    public static string Fill(string template, IReadOnlyDictionary<string, string> values)
        => Placeholder().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : throw new InvalidOperationException($"No value for the placeholder {m.Value}."));

    public static IEnumerable<string> PlaceholdersIn(string template) => Placeholder().Matches(template).Select(m => m.Groups[1].Value).Distinct();

    public static bool HasUnfilled(string text) => Placeholder().IsMatch(text);

    /// <summary>
    /// Every placeholder value for a repository, from the wizard's answers. The board may be null before the
    /// board step has run; the files are rendered after it, so by then it is known.
    /// </summary>
    public static Dictionary<string, string> ValuesFor(SetupAnswers a, BoardReference? board, string projectFileText = "", IReadOnlyList<string>? files = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = a.Name,
            ["description"] = a.Description.Length > 0 ? a.Description : "<!-- One line on what it is for. -->",
            ["owner"] = a.Owner,
            ["ownerAndName"] = a.OwnerAndName,
            ["defaultBranch"] = a.DefaultBranch,
            ["requiredCheck"] = a.RequiredCheck,
            ["selfTestCommand"] = a.SelfTestCommand,
            ["checkCommand"] = CheckCommand(a),
            ["languageName"] = ProjectLanguage.DisplayName(a.Language),
            ["projectFile"] = a.ProjectFile.Replace('\\', '/'),
            ["dotnetVersion"] = DotNetVersion(projectFileText),
            ["buildCommand"] = a.UsesProjectFile ? DotNetBuildCommand(a.ProjectFile) : a.BuildCommand,
            ["testCommand"] = a.TestCommand,
            ["boardOwner"] = board?.Owner ?? a.Owner,
            ["boardNumber"] = (board?.Number ?? 0).ToString(CultureInfo.InvariantCulture),
            ["boardUrl"] = board is null ? $"https://github.com/{a.Owner}?tab=projects" : $"https://github.com/users/{board.Owner}/projects/{board.Number}",
            ["behindLimit"] = a.BehindLimit.ToString(CultureInfo.InvariantCulture),
            ["runner"] = LanguageDefaults.Runner(a.Language),
            ["checklist"] = ChecklistItems.Render(a.Checklist),
            ["ecosystems"] = Ecosystems(a.Language, files),
            ["codeqlLanguage"] = CodeQlLanguage(a.Language),
            ["codeqlBuildMode"] = a.Language == ProjectLanguage.Go ? "autobuild" : "none",
        };
        values["testStep"] = TestStep(a);
        values["selfTestStep"] = a.SelfTestCommand.Length > 0 ? Step("Self-test", a.SelfTestCommand) : "";
        values["languageSteps"] = Fill(ConventionResources.Read($"languages/{LanguageFolder(a.Language)}/ci-steps.yml"), values).TrimEnd('\n');
        values["workflowSection"] = WorkflowSection.Text(values);
        return values;
    }

    /// <summary>The CI build for a .NET project: the project when one is named, else whatever the root holds.</summary>
    public static string DotNetBuildCommand(string projectFile)
        => projectFile.Length > 0 ? $"dotnet build \"{projectFile.Replace('\\', '/')}\" -c Release -warnaserror" : "dotnet build -c Release -warnaserror";

    /// <summary>The one command the documents tell a newcomer to run: the self-test, else the tests, else the build.</summary>
    public static string CheckCommand(SetupAnswers a)
    {
        if (a.SelfTestCommand.Length > 0) return a.SelfTestCommand;
        if (a.UsesProjectFile) return a.TestProject.Length > 0 ? $"dotnet test \"{a.TestProject}\"" : a.ProjectFile.Length > 0 ? $"dotnet build \"{a.ProjectFile}\" -warnaserror" : "dotnet build -warnaserror";
        return a.TestCommand.Length > 0 ? a.TestCommand : a.BuildCommand.Length > 0 ? a.BuildCommand : "the build";
    }

    private static string TestStep(SetupAnswers a)
    {
        if (a.UsesProjectFile) return a.TestProject.Length > 0 ? Step("Unit tests", $"dotnet test \"{a.TestProject.Replace('\\', '/')}\" -c Release -warnaserror") : "";
        return a.TestCommand.Length > 0 ? Step("Test", a.TestCommand) : "";
    }

    private static string Step(string name, string run) => $"\n      - name: {name}\n        run: {run}\n";

    /// <summary>
    /// The Dependabot update blocks: github-actions always, and a package ecosystem only when its manifest is in
    /// the repository, so Dependabot is never pointed at a file that isn't there. Without a file list the
    /// language's ecosystem is assumed.
    /// </summary>
    public static string Ecosystems(string language, IReadOnlyList<string>? files = null)
    {
        var sb = new StringBuilder();
        foreach (var ecosystem in EcosystemsFor(language, files))
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("  - package-ecosystem: ").Append(ecosystem).Append('\n')
              .Append("    directory: /\n")
              .Append("    schedule:\n")
              .Append("      interval: weekly");
        }
        return sb.ToString();
    }

    /// <summary>The ecosystems whose manifest is among the files; the language's own when no files are known.</summary>
    public static IReadOnlyList<string> EcosystemsFor(string language, IReadOnlyList<string>? files)
    {
        if (files is null) return LanguageDefaults.Ecosystems(language);
        var names = files.Select(f => f.Replace('\\', '/')).Select(f => f[(f.LastIndexOf('/') + 1)..]).ToList();
        bool Any(Func<string, bool> test) => names.Any(test);
        var list = new List<string> { "github-actions" };
        if (Any(n => n.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) || n.Equals("packages.config", StringComparison.OrdinalIgnoreCase) || n.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase))) list.Add("nuget");
        if (Any(n => n.Equals("package.json", StringComparison.OrdinalIgnoreCase))) list.Add("npm");
        if (Any(n => n.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase) || n.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase) || n.Equals("Pipfile", StringComparison.OrdinalIgnoreCase))) list.Add("pip");
        if (Any(n => n.Equals("go.mod", StringComparison.OrdinalIgnoreCase))) list.Add("gomod");
        if (Any(n => n.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase))) list.Add("cargo");
        if (Any(n => n.Equals("pom.xml", StringComparison.OrdinalIgnoreCase))) list.Add("maven");
        if (Any(n => n.Equals("build.gradle", StringComparison.OrdinalIgnoreCase) || n.Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase))) list.Add("gradle");
        return list;
    }

    /// <summary>net10.0 in the project file gives 10.0.x; 10.0.x when the project file cannot be read or says nothing.</summary>
    public static string DotNetVersion(string projectFileText)
    {
        var m = TargetFramework().Match(projectFileText);
        return m.Success ? $"{m.Groups[1].Value}.0.x" : "10.0.x";
    }

    [GeneratedRegex(@"<TargetFrameworks?>\s*net(\d+)\.\d", RegexOptions.IgnoreCase)]
    private static partial Regex TargetFramework();

    public static string LanguageFolder(string language)
        => ConventionResources.Exists($"languages/{language}/ci-steps.yml") ? language : "other";

    /// <summary>
    /// The files set-up writes for these answers: public-only files on public repositories, optional ones when
    /// chosen, and only files the tool has a template for, so a workflow that arrives in a later phase is not
    /// promised before it exists.
    /// </summary>
    public static IReadOnlyList<ConventionFile> FilesFor(SetupAnswers a, bool isPublic)
    {
        var list = new List<ConventionFile>();
        foreach (var file in ConventionFiles.All)
        {
            if (file.PublicOnly && !isPublic) continue;
            if ((file.Optional || file.PublicOnly) && !IsChosen(file, a)) continue;
            if (!HasTemplate(file)) continue;
            list.Add(file);
        }
        return list;
    }

    private static bool IsChosen(ConventionFile file, SetupAnswers a) => file.Path switch
    {
        ".github/workflows/release.yml" => a.ReleaseWorkflow && a.UsesProjectFile,
        ".github/workflows/codeql.yml" => a.CodeQl && CodeQlLanguage(a.Language).Length > 0,
        _ => true,
    };

    /// <summary>The CodeQL language for a project language; empty when CodeQL has none for it.</summary>
    public static string CodeQlLanguage(string language) => language switch
    {
        ProjectLanguage.DotNet => "csharp",
        ProjectLanguage.Node => "javascript-typescript",
        ProjectLanguage.Python => "python",
        ProjectLanguage.Go => "go",
        ProjectLanguage.Rust => "rust",
        ProjectLanguage.Java => "java-kotlin",
        _ => "",
    };

    public static bool HasTemplate(ConventionFile file)
        => file.Path == ManifestPath || file.Path == ".gitignore" || ConventionResources.Exists(file.Path);

    public static string ManifestPath => Manifest.RelativePath.Replace('\\', '/');

    /// <summary>The text of one convention file for these answers. The manifest is rendered by the caller from its model.</summary>
    public static string Render(ConventionFile file, SetupAnswers a, IReadOnlyDictionary<string, string> values)
    {
        if (file.Path == ManifestPath) throw new InvalidOperationException("The manifest is written from its model, not a template.");
        var resource = file.Path == ".gitignore" ? $"languages/{GitIgnoreFolder(a.Language)}/_gitignore" : file.Path;
        var text = Fill(ConventionResources.Read(resource), values);
        return text.EndsWith('\n') ? text : text + "\n";
    }

    private static string GitIgnoreFolder(string language)
        => ConventionResources.Exists($"languages/{language}/_gitignore") ? language : "other";

    /// <summary>True when a manifest records these answers and options, so a rerun with changed answers knows to rewrite it.</summary>
    public static bool ManifestReflects(Manifest m, SetupAnswers a)
    {
        if (m.Answers is not { } r) return false;
        return m.Language == a.Language && m.SelfTestCommand == a.SelfTestCommand && m.RequiredCheck == a.RequiredCheck
            && m.Options.ReleaseWorkflow == a.ReleaseWorkflow && m.Options.CodeQl == a.CodeQl && m.Options.Discussions == a.Discussions && m.Options.ReviewRequired == a.ReviewRequired
            && r.Description == a.Description && r.ProjectFile == a.ProjectFile && r.TestProject == a.TestProject && r.BuildCommand == a.BuildCommand && r.TestCommand == a.TestCommand
            && r.BehindLimit == a.BehindLimit && r.Checklist.SequenceEqual(a.Checklist) && r.Topics.SequenceEqual(a.Topics);
    }

    /// <summary>The manifest for these answers, the board and what this plan did not allow.</summary>
    public static Manifest ManifestFor(SetupAnswers a, BoardReference? board, Capabilities? capabilities, DateTime now, Manifest? existing = null)
    {
        var notAvailable = new List<string>();
        if (capabilities is not null)
        {
            if (!capabilities.Rulesets) notAvailable.Add(SetupStep.Ruleset.ToString());
            if (!capabilities.CodeOwners) notAvailable.Add("CodeOwners");
            if (!capabilities.PushProtection) notAvailable.Add("PushProtection");
            if (!capabilities.CodeScanning) notAvailable.Add("CodeScanning");
        }
        return new Manifest
        {
            ConventionVersion = Manifest.CurrentConventionVersion,
            // The set-up date is the first one; an update keeps it.
            SetUp = existing is { SetUp.Length: > 0 } ? existing.SetUp : now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Language = a.Language,
            Options = new ManifestOptions { ReleaseWorkflow = a.ReleaseWorkflow, CodeQl = a.CodeQl, Discussions = a.Discussions, ReviewRequired = a.ReviewRequired },
            Board = board,
            SelfTestCommand = a.SelfTestCommand,
            RequiredCheck = a.RequiredCheck,
            NotAvailable = notAvailable,
            Answers = new ManifestAnswers
            {
                Description = a.Description, ProjectFile = a.ProjectFile, TestProject = a.TestProject, BuildCommand = a.BuildCommand, TestCommand = a.TestCommand,
                Checklist = [.. a.Checklist], BehindLimit = a.BehindLimit, Topics = [.. a.Topics],
            },
        };
    }
}
