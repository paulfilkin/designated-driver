using System.IO;
using System.Windows;
using DesignatedDriver.Core;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;
using DesignatedDriver.Screens;
using DesignatedDriver.Services;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// A repository that keeps its own conventions: Scan's verdict and next step, the set-up's refusal, Check's notice,
/// the board its own settings name, and the setting itself.
/// </summary>
public static partial class SelfTestRunner
{
    private static void OwnConventionsChecks(Action<bool, string> check)
    {
        // Scan: the verdict says so, whatever else the scan found, and the findings stay for information.
        var facts = new ScanFacts { OwnerAndName = "example-org/example-app", Details = new GhRepositoryDetails { FullName = "example-org/example-app" } };
        var plain = ScanRules.Assess(facts);
        facts.KeepsOwnConventions = true;
        var kept = ScanRules.Assess(facts);
        check(plain.Verdict != ScanVerdict.KeepsOwnConventions && kept.Verdict == ScanVerdict.KeepsOwnConventions && kept.Findings.Count == plain.Findings.Count,
            "Own conventions: Scan's verdict says the repository keeps its own conventions, with the findings still listed");
        check(ScanReportText.VerdictTitle(kept.Verdict) == Strings.Scan_Verdict_KeepsOwn && kept.Summary == Strings.Scan_Summary_KeepsOwn
            && ScanReportText.NextStep(kept.Verdict, isCurrentRepository: true) == Strings.Scan_Next_KeepsOwn && ScanReportText.NextStepText(kept.Verdict) == Strings.Scan_Next_KeepsOwn,
            "Own conventions: Scan's title, summary and next step say so, and offer no conversion");
        var moved = new ScanFacts { OwnerAndName = "example-org/old-name", Details = new GhRepositoryDetails { FullName = "example-org/new-name" }, KeepsOwnConventions = true };
        check(ScanRules.Assess(moved).Verdict == ScanVerdict.KeepsOwnConventions, "Own conventions: the verdict holds even when the scan has blockers");

        // Set up a repository and --setup refuse it, saying why and how to change it; Check says so instead of drift.
        check(OwnConventions.SetupRefusal(true, "example-org/example-app") is { } refusal && refusal.Contains("example-org/example-app", StringComparison.Ordinal)
            && OwnConventions.SetupRefusal(false, "example-org/example-app") is null,
            "Own conventions: set-up refuses the repository, naming it, and goes ahead for any other");
        var flagged = new RepositorySettings { Owner = "example-org", Name = "example-app", KeepsOwnConventions = true };
        check(OwnConventions.CheckNotice(flagged) is { } notice && notice.Contains("example-org/example-app", StringComparison.Ordinal)
            && OwnConventions.CheckNotice(new RepositorySettings { Owner = "example-org", Name = "example-app" }) is null && OwnConventions.CheckNotice(null) is null,
            "Own conventions: Check shows a notice instead of a drift report, only for such a repository");

        // The board: the repository's own settings win over a manifest's, and only for such a repository.
        var manifest = new Manifest { Board = new BoardReference("example-org", 7) };
        var withBoard = new RepositorySettings { Owner = "example-org", Name = "example-app", BoardOwner = "example-org", BoardNumber = 9 };
        check(OwnConventions.BoardFor(manifest, withBoard) == new BoardReference("example-org", 7) && OwnConventions.BoardFor(null, withBoard) == new BoardReference("example-org", 9),
            "Own conventions: otherwise the manifest's board wins, and the settings' board applies without one");
        withBoard.KeepsOwnConventions = true;
        check(OwnConventions.BoardFor(manifest, withBoard) == new BoardReference("example-org", 9)
            && OwnConventions.BoardFor(manifest, new RepositorySettings { KeepsOwnConventions = true }) is null,
            "Own conventions: the repository's own board wins over a manifest's, and a manifest's isn't used in its place");

        // The setting round-trips, and an older entry doesn't have it.
        var parsed = Settings.Parse("{\"repositories\":[{\"owner\":\"example-org\",\"name\":\"example-app\",\"keepsOwnConventions\":true},{\"owner\":\"example-org\",\"name\":\"old\"}]}");
        check(parsed.Repositories[0].KeepsOwnConventions && !parsed.Repositories[1].KeepsOwnConventions, "Own conventions: the setting is kept in settings.json, and is off for an entry without it");

        var root = Path.Combine(Path.GetTempPath(), "designated-driver-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // The scan reads the setting from the repository's entry in the list.
            var host = new FakeHost(Path.Combine(root, "example"), hasHistory: true);
            // The fake host answers what a set-up asks; any other gh api read here is a 404, as GitHub would say.
            var answers = host.Runner.Respond!;
            host.Runner.Respond = (exe, a) => answers(exe, a) ?? (exe == "gh" && a[0] == "api" ? RecordingRunner.Fail(exe, a, "gh: Not Found (HTTP 404)") : null);
            var settings = new Settings { SavePath = Path.Combine(root, "settings.json"), Repositories = [new RepositorySettings { Owner = "paulfilkin", Name = "example", KeepsOwnConventions = true }] };
            var scanned = new ScanService(new Workspace(settings, host.Runner)).ScanAsync(null, "paulfilkin/example", CancellationToken.None).GetAwaiter().GetResult();
            var other = new ScanService(new Workspace(new Settings { SavePath = Path.Combine(root, "other.json") }, host.Runner)).ScanAsync(null, "paulfilkin/example", CancellationToken.None).GetAwaiter().GetResult();
            check(scanned.KeepsOwnConventions && !other.KeepsOwnConventions && ScanRules.Assess(scanned).Verdict == ScanVerdict.KeepsOwnConventions,
                "Own conventions: the scan takes the setting from the repository's entry in the list");

            // The Check screen shows the notice and runs nothing.
            if (Application.Current is not null)
            {
                var folder = Path.Combine(root, "clone");
                Directory.CreateDirectory(folder);
                var recorder = new RecordingRunner();
                var current = new RepositorySettings { Folder = folder, Owner = "example-org", Name = "example-app", KeepsOwnConventions = true };
                var workspace = new Workspace(new Settings { SavePath = Path.Combine(root, "screen.json"), Current = "example-org/example-app", Repositories = [current] }, recorder);
                var screen = new CheckScreen(new StubShell(workspace));
                screen.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
                check(screen.NoticeText.Text == OwnConventions.CheckNotice(current) && screen.NoticeCard.Visibility == Visibility.Visible && recorder.Calls.Count == 0,
                    "Own conventions: the Check screen shows the notice and runs no command");
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
