using System.IO;
using DesignatedDriver.Core;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// A clone beside the known ones: the folders that hold known clones are searched one level deep by reading each
/// subfolder's .git/config, a clone of another repository or another host is passed over, and a scan by name that finds
/// one says where it is and doesn't offer to clone again.
/// </summary>
public static partial class SelfTestRunner
{
    private static void NearbyCloneChecks(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "designated-driver-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Beside a known clone: a clone of example (the origin spelt in other case, after an upstream remote), a
            // clone of another repository, one of the same path on another host, and a folder that isn't a clone.
            var parent = Path.Combine(root, "GIT");
            void Clone(string name, string config)
            {
                Directory.CreateDirectory(Path.Combine(parent, name, ".git"));
                File.WriteAllText(Path.Combine(parent, name, ".git", "config"), config);
            }
            Directory.CreateDirectory(Path.Combine(parent, "known"));
            Clone("another", "[remote \"origin\"]\n\turl = https://github.com/someone/else.git\n");
            Clone("elsewhere", "[remote \"origin\"]\n\turl = https://gitlab.com/paulfilkin/example.git\n");
            Clone("example-clone", "[core]\n\tbare = false\n[remote \"upstream\"]\n\turl = https://github.com/someone/example.git\n[remote \"origin\"]\n\turl = git@github.com:PaulFilkin/Example.git\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n");
            Directory.CreateDirectory(Path.Combine(parent, "plain"));

            var folders = NearbyClones.SearchFolders([Path.Combine(parent, "known"), Path.Combine(parent, "known") + "\\", Path.Combine(root, "gone", "missing")]);
            check(folders.Count == 1 && folders[0].Equals(parent, StringComparison.OrdinalIgnoreCase),
                "Nearby clone: the folders searched are the ones holding known clones, each once, and only those that exist");

            check(NearbyClones.Find("paulfilkin/example", folders).Equals(Path.Combine(parent, "example-clone"), StringComparison.OrdinalIgnoreCase),
                "Nearby clone: a clone of the repository beside a known one is found by its origin, without regard to case");
            check(NearbyClones.Find("paulfilkin/missing", folders).Length == 0,
                "Nearby clone: with no clone of the repository, nothing is found");
            check(NearbyClones.Find("someone/else", folders).Equals(Path.Combine(parent, "another"), StringComparison.OrdinalIgnoreCase)
                && NearbyClones.OriginIn("[remote \"upstream\"]\n\turl = https://github.com/a/b.git\n").Length == 0,
                "Nearby clone: only the origin remote counts, so a clone of another repository, or an upstream, doesn't match");
            check(NearbyClones.Find("paulfilkin/example", [Path.Combine(root, "gone")]).Length == 0,
                "Nearby clone: a search folder that doesn't exist is passed over");

            // What the scan makes of it: a finding naming the folder, no Clone, and a next step that scans it.
            var byName = new ScanFacts { OwnerAndName = "paulfilkin/example", Details = new GhRepositoryDetails { FullName = "paulfilkin/example" } };
            var found = new ScanFacts { OwnerAndName = "paulfilkin/example", Details = byName.Details, NearbyClone = Path.Combine(parent, "example-clone") };
            var report = ScanRules.Assess(found);
            check(report.Findings.Any(f => f.Code == ScanCode.NearbyClone && f.Severity == ScanSeverity.Warning && f.Text.Contains(found.NearbyClone, StringComparison.Ordinal))
                && !ScanRules.Assess(byName).Has(ScanCode.NearbyClone),
                "Nearby clone: a scan by name that found one says where, as worth knowing");
            check(!ScanRules.CanCloneAsIs(found, known: false) && ScanRules.AsItIs(found, ScanVerdict.Convertible, known: false) == AsItIsRoute.None
                && ScanRules.CanCloneAsIs(byName, known: false),
                "Nearby clone: cloning isn't offered when a clone was found");

            var step = ScanReportText.NextStep(ScanVerdict.Convertible, false, canKeepAsIs: false);
            var withClone = new ScanFacts { Folder = parent, OwnerAndName = "paulfilkin/example", NearbyClone = found.NearbyClone };
            check(ScanReportText.NextStepForName(step, found, AsItIsRoute.None) == string.Format(Strings.Scan_Next_ScanNearbyClone, found.NearbyClone)
                && ScanReportText.NextStepForName(step, byName, AsItIsRoute.Clone) == step + " " + Strings.Scan_Next_CloneElsewhere
                && ScanReportText.NextStepForName(step, withClone, AsItIsRoute.Add) == step,
                "Nearby clone: the next step scans a clone found, says a clone elsewhere can be scanned when none was, and is unchanged for a scan of a clone");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
