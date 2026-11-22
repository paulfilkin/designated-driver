using System.IO;
using DesignatedDriver.Core;
using DesignatedDriver.Models;
using DesignatedDriver.Services;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// Clone and work on it as it is: only gh reads and one git clone run, the URL follows Settings, the entry is saved
/// and made current, and a moved repository, a used folder or one already in the list stops before anything runs.
/// </summary>
public static partial class SelfTestRunner
{
    private static void CloneAsIsChecks(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "designated-driver-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // GitHub knows paulfilkin/example; the clone writes a convention manifest and hooks folder when asked to.
            bool convention = false;
            string movedTo = "";
            var recorder = new RecordingRunner();
            recorder.Respond = (exe, a) =>
            {
                if (exe == "gh" && a[0] == "api" && a[1] == "user") return RecordingRunner.Ok(exe, a, "paulfilkin");
                if (exe == "gh" && a[0] == "api" && a[1] == "repos/paulfilkin/example")
                    return RecordingRunner.Ok(exe, a, movedTo.Length > 0 ? Sample("repo-details.json").Replace("paulfilkin/example", movedTo) : Sample("repo-details.json"));
                if (exe == "gh" && a[0] == "api" && a[1].StartsWith("repos/", StringComparison.Ordinal)) return RecordingRunner.Fail(exe, a, "gh: Not Found (HTTP 404)");
                if (exe == "git" && a[0] == "clone")
                {
                    Directory.CreateDirectory(a[2]);
                    if (convention)
                    {
                        Directory.CreateDirectory(Path.Combine(a[2], ".github"));
                        Directory.CreateDirectory(Path.Combine(a[2], ConventionFiles.HooksFolder));
                        File.WriteAllText(Manifest.PathIn(a[2]), "{\"conventionVersion\":1,\"selfTestCommand\":\"dotnet run -- --self-test\",\"requiredCheck\":\"build-test\"}");
                    }
                }
                return null;
            };
            Workspace NewWorkspace(string file) => new(new Settings { SavePath = Path.Combine(root, file), GitProtocol = RemoteUrl.Ssh, SshHost = "github-paulfilkin" }, recorder);

            // A plain repository: reads, one clone over the SSH alias, and an entry made current.
            var parent = Path.Combine(root, "plain");
            var workspace = NewWorkspace("settings.json");
            var added = workspace.CloneAndAddAsync(" paulfilkin/example ", parent, CancellationToken.None).GetAwaiter().GetResult();
            var clones = recorder.Calls.Where(c => c.Executable == "git").ToList();
            var writes = recorder.Calls.Where(c => c.Executable == "gh" && !c.Is("api", "repos/paulfilkin/example") && !c.Is("api", "user")).ToList();
            check(clones.Count == 1 && clones[0].Is("clone", "git@github-paulfilkin:paulfilkin/example.git", Path.Combine(parent, "example")) && writes.Count == 0,
                "Clone as it is: gh only reads, and one git clone runs over the SSH alias from Settings");
            check(added.OwnerAndName == "paulfilkin/example" && added.Folder == Path.Combine(parent, "example") && added.DefaultBranch == "main"
                && workspace.Current.OwnerAndName == "paulfilkin/example" && File.Exists(Path.Combine(root, "settings.json")),
                "Clone as it is: the entry is saved with owner, name, folder and default branch, and made current");
            check(added.SelfTestCommand.Length == 0 && added.RequiredCheck == RepositorySettings.DefaultRequiredCheck,
                "Clone as it is: a repository without a manifest keeps the default required check and no self-test command");

            // One set up to the convention: its manifest is read and its hooks are pointed at, locally.
            recorder.Calls.Clear();
            convention = true;
            var conventionParent = Path.Combine(root, "convention");
            var set = NewWorkspace("settings-convention.json").CloneAndAddAsync("paulfilkin/example", conventionParent, CancellationToken.None).GetAwaiter().GetResult();
            var git = recorder.Calls.Where(c => c.Executable == "git").ToList();
            check(set.SelfTestCommand == "dotnet run -- --self-test" && set.RequiredCheck == "build-test"
                && git.Count == 2 && git[1].Is("config", "core.hooksPath", ConventionFiles.HooksFolder),
                "Clone as it is: a convention repository brings its self-test command and required check, and its hooks are pointed at");
            convention = false;

            // Refusals, each before any command runs, except the read that finds the repository moved.
            bool Refused(Func<Task> work)
            {
                try { work().GetAwaiter().GetResult(); return false; }
                catch (InvalidOperationException) { return true; }
            }

            recorder.Calls.Clear();
            check(Refused(() => workspace.CloneAndAddAsync("paulfilkin/example", parent, CancellationToken.None)) && recorder.Calls.Count == 0,
                "Clone as it is: a repository already in the list with a folder is refused before anything runs");

            recorder.Calls.Clear();
            check(Refused(() => NewWorkspace("settings-bad.json").CloneAndAddAsync("example", parent, CancellationToken.None)) && recorder.Calls.Count == 0,
                "Clone as it is: a name without an owner is refused before anything runs");

            recorder.Calls.Clear();
            File.WriteAllText(Path.Combine(parent, "example", "README.md"), "Already here");
            check(Refused(() => NewWorkspace("settings-used.json").CloneAndAddAsync("paulfilkin/example", parent, CancellationToken.None))
                && !recorder.Calls.Any(c => c.Executable == "git"),
                "Clone as it is: a folder that already has files in it is refused before the clone");

            recorder.Calls.Clear();
            movedTo = "someone-else/example";
            check(Refused(() => NewWorkspace("settings-moved.json").CloneAndAddAsync("paulfilkin/example", Path.Combine(root, "moved"), CancellationToken.None))
                && !recorder.Calls.Any(c => c.Executable == "git") && !Directory.Exists(Path.Combine(root, "moved")),
                "Clone as it is: a repository GitHub answers for under another name is refused before the clone");
            movedTo = "";

            recorder.Calls.Clear();
            check(Refused(() => NewWorkspace("settings-missing.json").CloneAndAddAsync("paulfilkin/missing", Path.Combine(root, "missing"), CancellationToken.None))
                && !recorder.Calls.Any(c => c.Executable == "git"),
                "Clone as it is: a repository GitHub doesn't know is refused before the clone");

            // Scan offers it only for a repository found by name, with no clone here and not in the list.
            var byName = new ScanFacts { OwnerAndName = "paulfilkin/example", Details = new GhRepositoryDetails { FullName = "paulfilkin/example" } };
            var withClone = new ScanFacts { Folder = parent, OwnerAndName = "paulfilkin/example", Details = byName.Details };
            var notFound = new ScanFacts { OwnerAndName = "paulfilkin/example" };
            check(ScanRules.CanCloneAsIs(byName, known: false) && !ScanRules.CanCloneAsIs(byName, known: true)
                && !ScanRules.CanCloneAsIs(withClone, known: false) && !ScanRules.CanCloneAsIs(notFound, known: false),
                "Scan: cloning as it is is offered only for a repository found on GitHub, with no clone and not in the list");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
