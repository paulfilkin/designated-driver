using System.IO;

namespace DesignatedDriver.Core;

/// <summary>Where the repository comes from: made new, an existing folder on this machine, or an existing repository on GitHub.</summary>
public enum SetupSource { NewRepository, ExistingFolder, ExistingRepository }

/// <summary>
/// The answers from the New repository wizard. Everything the plan and the convention files need, and nothing
/// secret: the board token is handed to the setup service on its own and never kept here.
/// </summary>
public sealed class SetupAnswers
{
    public SetupSource Source { get; set; }

    /// <summary>The owner's login. The wizard resolves @me before the plan is built.</summary>
    public string Owner { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsPrivate { get; set; } = true;

    /// <summary>For a new or cloned repository: the folder the clone is made in. The clone itself is ParentFolder\Name.</summary>
    public string ParentFolder { get; set; } = "";
    /// <summary>For an existing folder: the folder itself.</summary>
    public string ExistingFolder { get; set; } = "";

    public string Language { get; set; } = ProjectLanguage.DotNet;
    /// <summary>.NET: the project to build, relative to the repository root.</summary>
    public string ProjectFile { get; set; } = "";
    /// <summary>.NET: the test project, relative to the repository root; empty when there is none.</summary>
    public string TestProject { get; set; } = "";
    /// <summary>Other languages: the commands the CI job runs.</summary>
    public string BuildCommand { get; set; } = "";
    public string TestCommand { get; set; } = "";
    /// <summary>The self-test command, run locally from Save and share and on CI. Empty when the project has none.</summary>
    public string SelfTestCommand { get; set; } = "";
    public string RequiredCheck { get; set; } = RepositorySettings.DefaultRequiredCheck;
    public string DefaultBranch { get; set; } = RepositorySettings.DefaultDefaultBranch;

    public bool ReviewRequired { get; set; } = true;
    public bool Discussions { get; set; }
    public bool ReleaseWorkflow { get; set; }
    public bool CodeQl { get; set; }
    /// <summary>The guard workflow fails a pull request further behind the default branch than this.</summary>
    public int BehindLimit { get; set; } = 10;
    public List<string> Checklist { get; set; } = [.. ChecklistItems.DefaultIds];
    public List<string> Topics { get; set; } = [];
    /// <summary>True when a board token was given, so the plan includes the secret step.</summary>
    public bool HasBoardToken { get; set; }
    /// <summary>https or ssh for the clone and the pushes; empty means the tool-wide setting.</summary>
    public string GitProtocol { get; set; } = "";
    /// <summary>For ssh: the host or alias to clone with.</summary>
    public string SshHost { get; set; } = "";

    public string OwnerAndName => $"{Owner}/{Name}";

    /// <summary>The clone's folder once the first step has run.</summary>
    public string Folder => Source == SetupSource.ExistingFolder ? ExistingFolder.TrimEnd('\\') : Path.Combine(ParentFolder.TrimEnd('\\'), Name);

    public bool UsesProjectFile => Language == ProjectLanguage.DotNet;

    /// <summary>
    /// The visibility GitHub already has: true for private, false for public. Null when the user chooses, because
    /// set-up creates the repository: a new one, or a folder the scan found no GitHub repository for. Set-up never
    /// changes an existing repository's visibility, so the wizard shows GitHub's and the answers use it.
    /// </summary>
    public static bool? PrivateOnGitHub(SetupSource source, ScanFacts? facts)
        => source != SetupSource.NewRepository && facts is { RemoteFound: true } ? !facts.IsPublic : null;

    /// <summary>The answers for a repository that is already set up, from its manifest and what the scan saw, for rendering the files again.</summary>
    public static SetupAnswers FromManifest(Manifest m, RepositorySettings repo, ScanFacts facts)
    {
        var a = m.Answers;
        return new SetupAnswers
        {
            Source = SetupSource.ExistingFolder,
            Owner = repo.Owner,
            Name = repo.Name,
            ExistingFolder = repo.Folder,
            IsPrivate = !facts.IsPublic,
            Language = m.Language.Length > 0 ? m.Language : ProjectLanguage.Other,
            SelfTestCommand = m.SelfTestCommand,
            RequiredCheck = m.RequiredCheck.Length > 0 ? m.RequiredCheck : repo.RequiredCheck,
            DefaultBranch = facts.DefaultBranch,
            ReviewRequired = m.Options.ReviewRequired,
            Discussions = m.Options.Discussions,
            ReleaseWorkflow = m.Options.ReleaseWorkflow,
            CodeQl = m.Options.CodeQl,
            Description = a?.Description ?? "",
            ProjectFile = a?.ProjectFile ?? "",
            TestProject = a?.TestProject ?? "",
            BuildCommand = a?.BuildCommand ?? "",
            TestCommand = a?.TestCommand ?? "",
            Checklist = a?.Checklist is { Count: > 0 } c ? [.. c] : [.. ChecklistItems.DefaultIds],
            BehindLimit = a?.BehindLimit ?? 10,
            Topics = a?.Topics is { } t ? [.. t] : [],
        };
    }
}

/// <summary>The pull request checklist items the wizard offers. Written into the template as Markdown task lines.</summary>
public static class ChecklistItems
{
    public sealed record Item(string Id, string Text);

    public static readonly IReadOnlyList<Item> All =
    [
        new("one-issue", "The pull request closes one issue, named above."),
        new("builds", "It builds with no warnings, and the checks pass on my machine."),
        new("tests", "New behaviour has tests or self-test checks."),
        new("docs", "Documentation in `Docs\\` is updated where the change affects it."),
        new("no-secrets", "No data files, keys, passwords or tokens are included."),
        new("no-packages", "No new packages, or they were agreed beforehand."),
        new("style", "Text in the UI, comments and docs is British English, with no em-dashes."),
    ];

    public static IReadOnlyList<string> DefaultIds => All.Select(i => i.Id).ToList();

    /// <summary>The chosen items as "- [ ] text" lines, in the catalogue's order; every item when none is chosen.</summary>
    public static string Render(IEnumerable<string> ids)
    {
        var chosen = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        var items = All.Where(i => chosen.Count == 0 || chosen.Contains(i.Id)).ToList();
        return string.Join("\n", items.Select(i => "- [ ] " + i.Text));
    }
}

/// <summary>Build and test commands the wizard suggests for each language, as a starting point.</summary>
public static class LanguageDefaults
{
    public static (string Build, string Test) CommandsFor(string language) => language switch
    {
        ProjectLanguage.Node => ("npm run build --if-present", "npm test"),
        ProjectLanguage.Python => ("pip install -r requirements.txt", "python -m pytest"),
        ProjectLanguage.Go => ("go build ./...", "go test ./..."),
        ProjectLanguage.Rust => ("cargo build --locked", "cargo test --locked"),
        ProjectLanguage.Java => ("mvn -B package -DskipTests", "mvn -B test"),
        _ => ("", ""),
    };

    /// <summary>The runner the CI job needs: Windows for .NET, because a Windows desktop project does not build elsewhere.</summary>
    public static string Runner(string language) => language == ProjectLanguage.DotNet ? "windows-latest" : "ubuntu-latest";

    /// <summary>The Dependabot ecosystems for a language, github-actions always first.</summary>
    public static IReadOnlyList<string> Ecosystems(string language)
    {
        var list = new List<string> { "github-actions" };
        var own = language switch
        {
            ProjectLanguage.DotNet => "nuget",
            ProjectLanguage.Node => "npm",
            ProjectLanguage.Python => "pip",
            ProjectLanguage.Go => "gomod",
            ProjectLanguage.Rust => "cargo",
            ProjectLanguage.Java => "maven",
            _ => "",
        };
        if (own.Length > 0) list.Add(own);
        return list;
    }
}
