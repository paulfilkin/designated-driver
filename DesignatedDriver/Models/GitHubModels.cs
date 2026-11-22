using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesignatedDriver.Models;

/// <summary>
/// Reads the --json output of gh. Property names are matched without regard to case. GitHub sends null for a
/// text field it has no value for (a repository's description, for example), and every string in these
/// models is read as empty instead, so no caller meets a null.
/// </summary>
public static class GhJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new NullAsEmptyString() },
    };

    /// <summary>For the REST API through gh api, whose names are snake_case.</summary>
    public static readonly JsonSerializerOptions RestOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new NullAsEmptyString() },
    };

    /// <summary>A JSON null read into a string property gives an empty string.</summary>
    private sealed class NullAsEmptyString : JsonConverter<string>
    {
        public override bool HandleNull => true;

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.Null ? "" : reader.GetString() ?? "";

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }

    public static T Parse<T>(string json) where T : class
        => JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("Empty JSON");

    public static List<T> ParseList<T>(string json)
        => JsonSerializer.Deserialize<List<T>>(json, Options) ?? [];

    public static T ParseRest<T>(string json) where T : class
        => JsonSerializer.Deserialize<T>(json, RestOptions) ?? throw new JsonException("Empty JSON");

    public static List<T> ParseRestList<T>(string json)
        => JsonSerializer.Deserialize<List<T>>(json, RestOptions) ?? [];
}

/// <summary>How a gh api call ended, read from its exit code and what it wrote.</summary>
public enum ApiStatus { Ok, NotFound, NotAvailable, Failed }

/// <summary>
/// Sorts a gh api result into found, not found, not available on this plan, or failed. gh exits 1 on any HTTP
/// error and writes "(HTTP 404)" and so on to standard error; a 403 or 422 is how GitHub says a feature needs
/// a paid plan or a public repository.
/// </summary>
public static class GhApi
{
    public static ApiStatus Classify(int exitCode, string standardError)
    {
        if (exitCode == 0) return ApiStatus.Ok;
        if (standardError.Contains("(HTTP 404)", StringComparison.Ordinal)) return ApiStatus.NotFound;
        if (standardError.Contains("(HTTP 403)", StringComparison.Ordinal) || standardError.Contains("(HTTP 422)", StringComparison.Ordinal)) return ApiStatus.NotAvailable;
        return ApiStatus.Failed;
    }
}

public sealed class GhLabel
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class GhUser
{
    public string Login { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>User or Organization, where gh reports it.</summary>
    public string Type { get; set; } = "";
}

/// <summary>gh issue view / gh issue list --json number,title,labels,assignees,body,url</summary>
public sealed class GhIssue
{
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Url { get; set; } = "";
    public string State { get; set; } = "";
    public List<GhLabel> Labels { get; set; } = [];
    public List<GhUser> Assignees { get; set; } = [];

    public IEnumerable<string> LabelNames => Labels.Select(l => l.Name);
}

/// <summary>gh repo view --json viewerPermission,defaultBranchRef and, for the scan, the GraphQL-only fields.</summary>
public sealed class GhRepository
{
    public string ViewerPermission { get; set; } = "";
    public GhRef? DefaultBranchRef { get; set; }
    public bool IsEmpty { get; set; }
    public bool IsInOrganization { get; set; }
    public bool IsBlankIssuesEnabled { get; set; }
    public GhProjectNodes? ProjectsV2 { get; set; }
    public List<GhPullRequestTemplate> PullRequestTemplates { get; set; } = [];
    public List<GhIssueTemplate> IssueTemplates { get; set; } = [];
    public List<GhLanguageEdge> Languages { get; set; } = [];

    public bool IsAdmin => string.Equals(ViewerPermission, "ADMIN", StringComparison.OrdinalIgnoreCase);
    public IEnumerable<GhProject> LinkedProjects => ProjectsV2?.Nodes ?? [];

    public const string ScanFields = "viewerPermission,defaultBranchRef,isEmpty,isInOrganization,isBlankIssuesEnabled,projectsV2,pullRequestTemplates,issueTemplates,languages";
}

public sealed class GhRef
{
    public string Name { get; set; } = "";
}

public sealed class GhProjectNodes
{
    public List<GhProject> Nodes { get; set; } = [];
}

public sealed class GhPullRequestTemplate
{
    public string Filename { get; set; } = "";
    public string Body { get; set; } = "";
}

public sealed class GhIssueTemplate
{
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string About { get; set; } = "";
}

public sealed class GhLanguageEdge
{
    public long Size { get; set; }
    public GhLanguageNode? Node { get; set; }
}

public sealed class GhLanguageNode
{
    public string Name { get; set; } = "";
}

/// <summary>gh api repos/{owner}/{name}: the settings and flags the REST API reports, snake_case.</summary>
public sealed class GhRepositoryDetails
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Private { get; set; }
    public string Visibility { get; set; } = "";
    public bool Archived { get; set; }
    public bool Fork { get; set; }
    public bool Disabled { get; set; }
    public string DefaultBranch { get; set; } = "";
    public GhUser? Owner { get; set; }
    public GhPermissions? Permissions { get; set; }
    public bool AllowSquashMerge { get; set; }
    public bool AllowMergeCommit { get; set; }
    public bool AllowRebaseMerge { get; set; }
    public bool DeleteBranchOnMerge { get; set; }
    public bool AllowUpdateBranch { get; set; }
    public bool AllowAutoMerge { get; set; }
    public string SquashMergeCommitTitle { get; set; } = "";
    public string SquashMergeCommitMessage { get; set; } = "";
    public bool HasIssues { get; set; }
    public bool HasProjects { get; set; }
    public bool HasWiki { get; set; }
    public bool HasDiscussions { get; set; }
    public List<string> Topics { get; set; } = [];
    public string Language { get; set; } = "";
    public GhSecurityAndAnalysis? SecurityAndAnalysis { get; set; }

    public bool IsPublic => !Private;
    public bool ViewerIsAdmin => Permissions?.Admin == true;
    public bool OwnerIsOrganization => string.Equals(Owner?.Type, "Organization", StringComparison.OrdinalIgnoreCase);
    public bool SecretScanningOn => IsEnabled(SecurityAndAnalysis?.SecretScanning);
    public bool PushProtectionOn => IsEnabled(SecurityAndAnalysis?.SecretScanningPushProtection);
    public bool DependabotSecurityUpdatesOn => IsEnabled(SecurityAndAnalysis?.DependabotSecurityUpdates);

    private static bool IsEnabled(GhFeatureStatus? s) => string.Equals(s?.Status, "enabled", StringComparison.OrdinalIgnoreCase);
}

public sealed class GhPermissions
{
    public bool Admin { get; set; }
    public bool Maintain { get; set; }
    public bool Push { get; set; }
    public bool Pull { get; set; }
}

public sealed class GhSecurityAndAnalysis
{
    public GhFeatureStatus? SecretScanning { get; set; }
    public GhFeatureStatus? SecretScanningPushProtection { get; set; }
    public GhFeatureStatus? DependabotSecurityUpdates { get; set; }
}

public sealed class GhFeatureStatus
{
    public string Status { get; set; } = "";
}

/// <summary>gh api repos/{owner}/{name}/rules/branches/{branch}: one rule in effect, from any ruleset.</summary>
public sealed class GhBranchRule
{
    public string Type { get; set; } = "";
    public long RulesetId { get; set; }
}

/// <summary>gh api repos/{owner}/{name}/rulesets</summary>
public sealed class GhRuleset
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string Enforcement { get; set; } = "";
}

/// <summary>gh api repos/{owner}/{name}/actions/workflows</summary>
public sealed class GhWorkflowList
{
    public int TotalCount { get; set; }
    public List<GhWorkflow> Workflows { get; set; } = [];
}

public sealed class GhWorkflow
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string State { get; set; } = "";
}

/// <summary>gh api repos/{owner}/{name}/contents/{path}: one entry of a folder listing.</summary>
public sealed class GhContentEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Type { get; set; } = "";

    public bool IsDirectory => string.Equals(Type, "dir", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One entry of statusCheckRollup on a pull request.</summary>
public sealed class GhStatusCheck
{
    [JsonPropertyName("__typename")]
    public string TypeName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Context { get; set; } = "";
    public string Status { get; set; } = "";
    public string Conclusion { get; set; } = "";
    public string State { get; set; } = "";
    public string DetailsUrl { get; set; } = "";
    public string TargetUrl { get; set; } = "";
    public string WorkflowName { get; set; } = "";

    public string DisplayName => string.IsNullOrEmpty(Name) ? Context : Name;
    public string Link => string.IsNullOrEmpty(DetailsUrl) ? TargetUrl : DetailsUrl;

    /// <summary>pass, fail or pending, from either a check run or a commit status.</summary>
    public string Bucket
    {
        get
        {
            var c = (string.IsNullOrEmpty(Conclusion) ? State : Conclusion).ToUpperInvariant();
            return c switch
            {
                "SUCCESS" or "NEUTRAL" or "SKIPPED" => "pass",
                "FAILURE" or "ERROR" or "TIMED_OUT" or "CANCELLED" or "ACTION_REQUIRED" or "STARTUP_FAILURE" => "fail",
                _ => "pending",
            };
        }
    }
}

/// <summary>gh pr list / gh pr view --json ...</summary>
public sealed class GhPullRequest
{
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Url { get; set; } = "";
    public string State { get; set; } = "";
    public string HeadRefName { get; set; } = "";
    public string BaseRefName { get; set; } = "";
    public string ReviewDecision { get; set; } = "";
    public string MergeStateStatus { get; set; } = "";
    public string Mergeable { get; set; } = "";
    public bool IsDraft { get; set; }
    public GhUser? Author { get; set; }
    public List<GhStatusCheck> StatusCheckRollup { get; set; } = [];
    public List<GhPullRequestFile> Files { get; set; } = [];

    /// <summary>pass, fail, pending or none, over every check on the head commit.</summary>
    public string ChecksBucket => ChecksSummary.Overall(StatusCheckRollup.Select(c => c.Bucket));
}

public sealed class GhPullRequestFile
{
    public string Path { get; set; } = "";
    public int Additions { get; set; }
    public int Deletions { get; set; }
    public string ChangeType { get; set; } = "";
}

/// <summary>gh pr checks --json name,state,bucket,link</summary>
public sealed class GhCheck
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string Link { get; set; } = "";
    public string Workflow { get; set; } = "";

    public bool IsFinished => Bucket is "pass" or "fail" or "skipping" or "cancel";
}

public static class ChecksSummary
{
    /// <summary>fail if any failed, pending if any still running, pass if all passed, none if there are no checks.</summary>
    public static string Overall(IEnumerable<string> buckets)
    {
        var list = buckets.ToList();
        if (list.Count == 0) return "none";
        if (list.Any(b => b is "fail" or "cancel")) return "fail";
        if (list.Any(b => b == "pending")) return "pending";
        return "pass";
    }
}

/// <summary>gh project item-list --format json</summary>
public sealed class GhProjectItemList
{
    public List<GhProjectItem> Items { get; set; } = [];
    public int TotalCount { get; set; }
}

public sealed class GhProjectItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public List<string> Labels { get; set; } = [];
    public GhProjectItemContent? Content { get; set; }

    public bool IsIssue => string.Equals(Content?.Type, "Issue", StringComparison.OrdinalIgnoreCase);
    public int Number => Content?.Number ?? 0;
}

public sealed class GhProjectItemContent
{
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
    public string Url { get; set; } = "";
    public string Body { get; set; } = "";
    public string Repository { get; set; } = "";
}

/// <summary>gh project field-list --format json</summary>
public sealed class GhProjectFieldList
{
    public List<GhProjectField> Fields { get; set; } = [];

    /// <summary>The names of the Status field's options, in order; empty when there is no Status field.</summary>
    public List<string> StatusOptions
        => Fields.FirstOrDefault(f => f.Name.Equals("Status", StringComparison.OrdinalIgnoreCase))?.Options.Select(o => o.Name).ToList() ?? [];
}

public sealed class GhProjectField
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public List<GhProjectFieldOption> Options { get; set; } = [];
}

public sealed class GhProjectFieldOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>gh project view --format json, and one entry of gh project list or a repository's projectsV2.</summary>
public sealed class GhProject
{
    public string Id { get; set; } = "";
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Closed { get; set; }
    public GhUser? Owner { get; set; }
}

/// <summary>gh project list --format json</summary>
public sealed class GhProjectList
{
    public List<GhProject> Projects { get; set; } = [];
    public int TotalCount { get; set; }
}
