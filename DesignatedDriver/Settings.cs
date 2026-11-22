using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesignatedDriver.Models;

namespace DesignatedDriver;

/// <summary>One repository the tool knows: where its clone is and the per-repository overrides.</summary>
public sealed class RepositorySettings
{
    public const string DefaultRequiredCheck = "Build and test";
    public const string DefaultDefaultBranch = "main";

    public string Folder { get; set; } = "";

    /// <summary>Owner and name read from the origin remote, for example paulfilkin and example.</summary>
    public string Owner { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>The build and self-test command run from Save and share. Empty hides the card.</summary>
    public string SelfTestCommand { get; set; } = "";

    /// <summary>The name of the check the pull request must pass, as GitHub reports it.</summary>
    public string RequiredCheck { get; set; } = DefaultRequiredCheck;
    public string DefaultBranch { get; set; } = DefaultDefaultBranch;

    /// <summary>The board, for a repository that has no manifest yet. The manifest wins when there is one.</summary>
    public string BoardOwner { get; set; } = "";
    public int BoardNumber { get; set; }

    /// <summary>The board's column for issues ready to pick up; empty means the convention's Todo (or Ready).</summary>
    public string ReadyColumn { get; set; } = "";
    /// <summary>The column a card moves to when work starts; empty means the convention's In Progress.</summary>
    public string InProgressColumn { get; set; } = "";
    /// <summary>The column a card moves to when its pull request opens; empty means the card isn't moved.</summary>
    public string InReviewColumn { get; set; } = "";

    /// <summary>
    /// The repository keeps its own conventions: Scan, Set up a repository and Check don't offer to convert it, and
    /// the workflow screens use these settings, the board included, rather than a manifest's. Cleared to convert it.
    /// </summary>
    public bool KeepsOwnConventions { get; set; }

    [JsonIgnore] public string OwnerAndName => string.IsNullOrEmpty(Owner) ? "" : $"{Owner}/{Name}";
    [JsonIgnore] public bool HasFolder => !string.IsNullOrWhiteSpace(Folder) && Directory.Exists(Folder);
    [JsonIgnore] public bool HasBoardOverride => BoardOwner.Length > 0 && BoardNumber > 0;

    public bool Matches(string ownerAndName) => OwnerAndName.Length > 0 && string.Equals(OwnerAndName, ownerAndName, StringComparison.OrdinalIgnoreCase);

    public RepositorySettings Clone() => (RepositorySettings)MemberwiseClone();
}

/// <summary>
/// The tool's settings, kept as JSON under %AppData%\DesignatedDriver: the repositories it knows and which one is
/// current. No token is ever stored here; gh holds the sign-in.
/// </summary>
public sealed class Settings
{
    /// <summary>Owner and name of the current repository, for example paulfilkin/example.</summary>
    public string Current { get; set; } = "";
    public List<RepositorySettings> Repositories { get; set; } = [];

    /// <summary>How clones are made and pushed: https or ssh. Empty until chosen; the tool then follows gh's own setting.</summary>
    public string GitProtocol { get; set; } = "";
    /// <summary>For ssh: github.com, or an alias from ~/.ssh/config such as github-paulfilkin. Empty means the tool suggests one.</summary>
    public string SshHost { get; set; } = "";

    /// <summary>The Repositories screen shows only the repositories starred on GitHub.</summary>
    public bool StarredOnly { get; set; }

    [JsonIgnore] public RepositorySettings? CurrentRepository => Find(Current);

    public RepositorySettings? Find(string ownerAndName)
        => ownerAndName.Length == 0 ? null : Repositories.FirstOrDefault(r => r.Matches(ownerAndName));

    public RepositorySettings? FindByFolder(string folder)
    {
        var wanted = folder.Trim().TrimEnd('\\');
        return Repositories.FirstOrDefault(r => string.Equals(r.Folder.TrimEnd('\\'), wanted, StringComparison.OrdinalIgnoreCase));
    }

    public const string FileName = "settings.json";
    public static string FolderPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesignatedDriver");
    /// <summary>Where the settings were kept before the app was renamed from Repo Starter.</summary>
    public static string OldFolderPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RepoStarter");
    public static string FilePath => Path.Combine(FolderPath, FileName);

    /// <summary>
    /// Copies the files in the old settings folder into the new one when only the old one has a settings file.
    /// The old folder is left as it is. Returns true when it copied.
    /// </summary>
    public static bool CarryOver(string oldFolder, string newFolder)
    {
        if (File.Exists(Path.Combine(newFolder, FileName)) || !File.Exists(Path.Combine(oldFolder, FileName))) return false;
        try
        {
            Directory.CreateDirectory(newFolder);
            foreach (var file in Directory.GetFiles(oldFolder))
                File.Copy(file, Path.Combine(newFolder, Path.GetFileName(file)), overwrite: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed copy leaves the tool with no settings; the user adds the repository again.
            return false;
        }
    }

    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public static Settings Load()
    {
        CarryOver(OldFolderPath, FolderPath);
        try
        {
            if (File.Exists(FilePath)) return Parse(File.ReadAllText(FilePath));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged settings file is treated as no settings; the user adds the repository again.
        }
        return new Settings();
    }

    public static Settings Parse(string json) => JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? new Settings();

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Where Save writes when set; the self-test points it at a temporary file.</summary>
    [JsonIgnore] public string? SavePath { get; set; }

    public void Save()
    {
        var path = SavePath ?? FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ToJson());
    }
}
