using System.IO;
using System.Text.Json;

namespace DesignatedDriver.Core;

/// <summary>A user-owned or organisation project board, by owner login and number.</summary>
public sealed record BoardReference(string Owner, int Number);

/// <summary>The options chosen in the New repository wizard, as recorded in the manifest.</summary>
public sealed class ManifestOptions
{
    public bool ReleaseWorkflow { get; set; }
    public bool CodeQl { get; set; }
    public bool Discussions { get; set; }
    public bool ReviewRequired { get; set; }
}

/// <summary>The answers the convention files are rendered from, so the Check screen can render them again and compare.</summary>
public sealed class ManifestAnswers
{
    public string Description { get; set; } = "";
    public string ProjectFile { get; set; } = "";
    public string TestProject { get; set; } = "";
    public string BuildCommand { get; set; } = "";
    public string TestCommand { get; set; } = "";
    public List<string> Checklist { get; set; } = [];
    public int BehindLimit { get; set; } = 10;
    public List<string> Topics { get; set; } = [];
}

/// <summary>
/// .github/repo-starter.json: what a repository was set up to. Written by the set-up (phase 2) and read by the
/// Check screen and the workflow screens, so a second machine needs no settings beyond the folder. Phase 1 only
/// reads it.
/// </summary>
public sealed class Manifest
{
    public const string RelativePath = @".github\repo-starter.json";
    public const int CurrentConventionVersion = 1;

    public int ConventionVersion { get; set; }
    /// <summary>The date of the set-up, as yyyy-MM-dd.</summary>
    public string SetUp { get; set; } = "";
    public string Language { get; set; } = "";
    public ManifestOptions Options { get; set; } = new();
    public BoardReference? Board { get; set; }
    public string SelfTestCommand { get; set; } = "";
    public string RequiredCheck { get; set; } = "";
    /// <summary>Steps that could not run on this plan, by step name.</summary>
    public List<string> NotAvailable { get; set; } = [];
    /// <summary>The set-up answers, from phase 3 on. A manifest without them cannot have its tailored files compared.</summary>
    public ManifestAnswers? Answers { get; set; }

    public bool HasAnswers => Answers is not null;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public static string PathIn(string repositoryFolder) => Path.Combine(repositoryFolder, RelativePath);

    /// <summary>The manifest in a clone, or null when there is none or it cannot be read.</summary>
    public static Manifest? Load(string repositoryFolder)
    {
        try
        {
            var path = PathIn(repositoryFolder);
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static Manifest? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Manifest>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
