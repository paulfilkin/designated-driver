using System.Text.RegularExpressions;

namespace DesignatedDriver.Core;

/// <summary>
/// What a repository without a manifest already uses, read from a scan: the default branch, the required check,
/// the linked board and its columns, and the self-test command from its CI workflow. Each is empty when the scan
/// found nothing for it. Pure, so the self-test can feed it facts by hand; nothing is written anywhere.
/// </summary>
public sealed class RepositoryProfile
{
    public string DefaultBranch { get; init; } = "";
    public string RequiredCheck { get; init; } = "";
    public BoardReference? Board { get; init; }
    public string ReadyColumn { get; init; } = "";
    public string InProgressColumn { get; init; } = "";
    public string InReviewColumn { get; init; } = "";
    public string SelfTestCommand { get; init; } = "";

    public static RepositoryProfile From(ScanFacts f)
    {
        if (!f.RemoteFound) return new RepositoryProfile();

        // The required check: from a ruleset first, then from classic branch protection.
        var requiredCheck = f.BranchRules.SelectMany(r => r.RequiredChecks).Concat(f.ClassicRequiredChecks).FirstOrDefault() ?? "";

        // The first linked board with a pick-up and an in-progress column. Its owner is the repository's owner.
        var owner = f.Details?.Owner?.Login is { Length: > 0 } login ? login : f.OwnerAndName.Split('/')[0];
        BoardReference? board = null;
        string ready = "", inProgress = "", inReview = "";
        foreach (var b in f.LinkedBoards)
        {
            var columns = BoardColumns.Match(b.StatusOptions);
            if (columns.Ready.Length == 0 || columns.InProgress.Length == 0) continue;
            board = new BoardReference(owner, b.Number);
            (ready, inProgress, inReview) = columns;
            break;
        }

        return new RepositoryProfile
        {
            DefaultBranch = f.Details?.DefaultBranch ?? "",
            RequiredCheck = requiredCheck,
            Board = board,
            ReadyColumn = ready,
            InProgressColumn = inProgress,
            InReviewColumn = inReview,
            SelfTestCommand = CiCommands.SelfTest(f.WorkflowTexts, f.WorkflowTriggers, requiredCheck, f.DefaultBranch),
        };
    }

    /// <summary>The profile's values by field, the board written as owner #number, for comparing with what the user has.</summary>
    public IReadOnlyDictionary<ProfileField, string> Values() => new Dictionary<ProfileField, string>
    {
        [ProfileField.DefaultBranch] = DefaultBranch,
        [ProfileField.RequiredCheck] = RequiredCheck,
        [ProfileField.Board] = Board is { } b ? ProfileComparison.BoardText(b.Owner, b.Number) : "",
        [ProfileField.ReadyColumn] = ReadyColumn,
        [ProfileField.InProgressColumn] = InProgressColumn,
        [ProfileField.InReviewColumn] = InReviewColumn,
        [ProfileField.SelfTestCommand] = SelfTestCommand,
    };
}

/// <summary>
/// Board columns by name, without regard to case. Ready is the pick-up column ahead of Todo, since a board with
/// both uses Todo as a backlog; In progress and In review are recognised by those names.
/// </summary>
public static class BoardColumns
{
    public static readonly IReadOnlyList<string> PickUpNames = ["Ready", "Todo", "To do"];
    public static readonly IReadOnlyList<string> InProgressNames = ["In progress"];
    public static readonly IReadOnlyList<string> InReviewNames = ["In review"];

    /// <summary>The board's own spelling of the first name it has, or empty.</summary>
    public static string Find(IEnumerable<string> options, IEnumerable<string> names)
    {
        var list = options.ToList();
        foreach (var name in names)
            if (list.FirstOrDefault(o => o.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) is { } hit) return hit;
        return "";
    }

    public static (string Ready, string InProgress, string InReview) Match(IEnumerable<string> options)
    {
        var list = options.ToList();
        return (Find(list, PickUpNames), Find(list, InProgressNames), Find(list, InReviewNames));
    }

    /// <summary>Whether a card in this column is ready to pick up: the profile's column when it names one, else the convention's.</summary>
    public static bool IsPickUp(string status, string profileColumn)
        => profileColumn.Length > 0 ? status.Trim().Equals(profileColumn.Trim(), StringComparison.OrdinalIgnoreCase) : ConventionBoard.IsReady(status);

    /// <summary>The column a card moves to when work starts: the profile's, else the convention's.</summary>
    public static string InProgressFor(string profileColumn) => profileColumn.Length > 0 ? profileColumn : ConventionBoard.InProgress;
}

/// <summary>The settings a profile fills.</summary>
public enum ProfileField { DefaultBranch, RequiredCheck, Board, ReadyColumn, InProgressColumn, InReviewColumn, SelfTestCommand }

public sealed record ProfileValue(ProfileField Field, string Current, string FromGitHub);

/// <summary>
/// What a read from GitHub does to the values the user has. GitHub's value fills an empty one; on the first read,
/// when the boxes hold only the app's defaults, it fills every one it has. A value the user has that differs from
/// GitHub's is never replaced without asking.
/// </summary>
public static class ProfileComparison
{
    public static string BoardText(string owner, int number) => owner.Trim().Length > 0 && number > 0 ? $"{owner.Trim()} #{number}" : "";

    public static (List<ProfileValue> Fill, List<ProfileValue> Ask) Compare(IReadOnlyDictionary<ProfileField, string> current, RepositoryProfile profile, bool firstRead)
    {
        var fill = new List<ProfileValue>();
        var ask = new List<ProfileValue>();
        foreach (var (field, fromGitHub) in profile.Values())
        {
            if (fromGitHub.Length == 0) continue;
            var mine = current.TryGetValue(field, out var value) ? value.Trim() : "";
            if (mine == fromGitHub) continue;
            var change = new ProfileValue(field, mine, fromGitHub);
            if (firstRead || mine.Length == 0) fill.Add(change);
            else ask.Add(change);
        }
        return (fill, ask);
    }
}

/// <summary>
/// The jobs in a workflow file and the commands their steps run, read line by line: enough to find a test command,
/// not a YAML parser.
/// </summary>
public static class WorkflowJobs
{
    public sealed record Job(string Id, string Name, IReadOnlyList<string> RunLines);

    public static List<Job> Parse(string yaml)
    {
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        int jobsAt = Array.FindIndex(lines, l => Regex.IsMatch(l, @"^jobs\s*:\s*(#.*)?$"));
        if (jobsAt < 0) return [];

        var jobs = new List<Job>();
        int jobIndent = -1, keyIndent = -1, blockAbove = -1;
        string id = "", name = "";
        var runs = new List<string>();
        void Flush() { if (id.Length > 0) jobs.Add(new Job(id, name, runs)); }

        for (int i = jobsAt + 1; i < lines.Length; i++)
        {
            var raw = lines[i];
            if (raw.Trim().Length == 0) continue;
            int indent = raw.Length - raw.TrimStart().Length;
            var text = raw.Trim();

            // Inside a run: | or run: > block, every more-indented line is one command.
            if (blockAbove >= 0)
            {
                if (indent > blockAbove) { if (!text.StartsWith('#')) runs.Add(text); continue; }
                blockAbove = -1;
            }
            if (text.StartsWith('#')) continue;
            if (indent == 0) break;
            if (jobIndent < 0) jobIndent = indent;
            if (indent < jobIndent) break;

            if (indent == jobIndent)
            {
                Flush();
                var key = Regex.Match(text, @"^(""[^""]+""|'[^']+'|[^\s:]+)\s*:");
                id = key.Success ? Unquote(key.Groups[1].Value) : "";
                name = "";
                runs = [];
                keyIndent = -1;
                continue;
            }
            if (keyIndent < 0) keyIndent = indent;
            if (indent == keyIndent)
            {
                var n = Regex.Match(text, @"^name\s*:\s*(.+)$");
                if (n.Success) name = Unquote(StripComment(n.Groups[1].Value));
                continue;
            }

            var run = Regex.Match(text, @"^(-\s+)?run\s*:\s*(.*)$");
            if (!run.Success) continue;
            var rest = StripComment(run.Groups[2].Value).Trim();
            if (rest.Length == 0 || rest.StartsWith('|') || rest.StartsWith('>'))
                blockAbove = indent + run.Groups[1].Value.Length;
            else
                runs.Add(Unquote(rest));
        }
        Flush();
        return jobs;
    }

    private static string StripComment(string s)
    {
        int hash = s.IndexOf(" #", StringComparison.Ordinal);
        return hash >= 0 ? s[..hash] : s;
    }

    private static string Unquote(string s) => s.Trim().Trim('"', '\'').Trim();
}

/// <summary>The self-test command a repository's CI runs, for Save and share to run before a push.</summary>
public static class CiCommands
{
    private static readonly Regex TestCommand = new(
        @"--self-test\b|(?<![\w./-])(?:dotnet|npm|pnpm|yarn|go|cargo|mvn|gradle|gradlew|\./gradlew)\s+(?:run\s+)?test\b|(?<![\w-])pytest\b|(?<![\w-])python3?\s+-m\s+(?:pytest|unittest)\b",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// The first test command in the job named for the required check, else in any workflow that builds pull requests
    /// or the default branch. A line using a workflow expression is skipped, since it can't run outside Actions, and
    /// --no-build and --no-restore are dropped, since locally nothing has been built first.
    /// </summary>
    public static string SelfTest(IReadOnlyDictionary<string, string> workflowTexts, IReadOnlyDictionary<string, WorkflowTriggers> triggers, string requiredCheck, string defaultBranch)
    {
        var all = workflowTexts.OrderBy(w => w.Key, StringComparer.OrdinalIgnoreCase)
            .Select(w => (Path: w.Key, Jobs: WorkflowJobs.Parse(w.Value))).ToList();

        IEnumerable<WorkflowJobs.Job> named = requiredCheck.Length == 0 ? [] : all.SelectMany(w => w.Jobs)
            .Where(j => j.Name.Equals(requiredCheck, StringComparison.OrdinalIgnoreCase) || j.Id.Equals(requiredCheck, StringComparison.OrdinalIgnoreCase));
        IEnumerable<WorkflowJobs.Job> building = all
            .Where(w => triggers.TryGetValue(w.Path, out var t) && t.BuildsChanges(defaultBranch))
            .SelectMany(w => w.Jobs);

        foreach (var job in named.Concat(building))
        {
            var line = job.RunLines.FirstOrDefault(l => !l.Contains("${{", StringComparison.Ordinal) && TestCommand.IsMatch(l));
            if (line is not null) return Tidy(line);
        }
        return "";
    }

    private static string Tidy(string line)
        => Regex.Replace(line, @"\s--no-(?:build|restore)\b", "").Trim();
}
