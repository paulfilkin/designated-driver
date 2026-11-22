using System.IO;
using DesignatedDriver.Core;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;
using DesignatedDriver.Services;

namespace DesignatedDriver;

public partial class App
{
    /// <summary>
    /// --check folder: the Check screen's report for a clone, printed, with every command echoed. With --apply the
    /// ticked items are put right as the screen's Apply would; --all ticks the tailored files as well.
    /// </summary>
    private static async Task<int> CheckFromCommandLine(string folder, bool apply, bool all)
    {
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            Console.Error.WriteLine(Strings.Check_Cli_Usage);
            return 3;
        }
        var workspace = new Workspace(Settings.Load());
        workspace.Runner.Activity += (_, entry) => { if (entry.Kind == ActivityKind.Command) Console.Out.WriteLine("> " + entry.Text); };
        var ct = CancellationToken.None;

        // The repository the tool knows for this folder, or one made from its origin.
        var repo = workspace.Settings.FindByFolder(folder);
        if (repo is null)
        {
            var origin = await workspace.Git.OriginUrlInAsync(folder, ct);
            var parsed = RemoteUrl.ParseOwnerAndRepository(origin);
            if (parsed is null) { Console.Error.WriteLine(Strings.Settings_NoOrigin); return 3; }
            repo = new RepositorySettings { Folder = folder.TrimEnd('\\'), Owner = parsed.Value.Owner, Name = parsed.Value.Repository };
        }

        // A repository that keeps its own conventions has no drift to report: nothing is checked or applied.
        if (OwnConventions.CheckNotice(repo) is { } keepsOwn)
        {
            Console.Out.WriteLine(keepsOwn);
            return 0;
        }

        var result = await new CheckService(workspace).CheckAsync(repo, ct);
        Console.Out.WriteLine();
        if (!result.IsSetUp)
        {
            Console.Out.WriteLine(Strings.Check_NotSetUp);
            return 2;
        }
        var report = result.Report!;
        Console.Out.WriteLine(string.Format(Strings.Check_CheckedAt, repo.OwnerAndName, report.Manifest.ConventionVersion, report.Manifest.SetUp, DateTime.Now.ToString("g")));
        Console.Out.WriteLine(report.Summary);
        foreach (var group in report.Items.GroupBy(i => i.Step).OrderBy(g => g.Key))
        {
            Console.Out.WriteLine();
            Console.Out.WriteLine(ScanReportText.StepTitle(group.Key));
            foreach (var item in group)
            {
                var mark = !item.IsActionable ? "-" : item.TickedByDefault ? "[x]" : "[ ]";
                Console.Out.WriteLine($"  {mark} {item.Text}  ({item.Key})");
                if (item.Detail.Length > 0 && !item.IsFile)
                    foreach (var line in item.Detail.Split('\n')) Console.Out.WriteLine("      " + line);
                if (item.IsFile && item.Kind == DriftKind.Different)
                    foreach (var line in item.Detail.Split('\n').Take(12)) Console.Out.WriteLine("      " + line);
            }
        }
        if (!apply || report.IsClean) return report.IsClean ? 0 : 1;

        // The same plan the Check screen's Apply builds, from the ticked items.
        var selected = report.Actionable.Where(i => all || i.TickedByDefault).Select(i => i.Key).ToList();
        var setup = new SetupService(workspace);
        var plan = setup.BuildUpdate(result.Answers!, result.Facts, report, selected);
        if (plan.Steps.Count == 0)
        {
            Console.Out.WriteLine(Strings.Check_Cli_NothingTicked);
            return 1;
        }
        Console.Out.WriteLine();
        Console.Out.WriteLine(string.Format(Strings.Check_Cli_Applying, selected.Count, plan.Steps.Count));
        setup.StepChanged += (_, step) =>
        {
            int n = plan.Steps.ToList().IndexOf(step) + 1;
            var line = step.State switch
            {
                StepState.Checking => $"== [{n}/{plan.Steps.Count}] {step.Title}: {Strings.Setup_StepChecking}",
                StepState.Running => $"   {Strings.Setup_StepRunning}",
                StepState.AlreadyDone => $"   {Strings.Setup_StepAlreadyDone}" + (step.Message.Length > 0 ? ": " + step.Message : ""),
                StepState.Applied => $"   {Strings.Setup_StepApplied}" + (step.Message.Length > 0 ? ": " + step.Message : ""),
                StepState.Failed => $"   FAILED: {step.Message}",
                _ => "",
            };
            if (line.Length > 0) Console.Out.WriteLine(line);
        };
        bool ok = await setup.RunAsync(plan, ct);
        Console.Out.WriteLine();
        if (ok)
        {
            Console.Out.WriteLine(Strings.Check_AppliedTitle);
            if (setup.PullRequestUrl.Length > 0) Console.Out.WriteLine(string.Format(Strings.Setup_Cli_PullRequest, setup.PullRequestUrl));
            return 0;
        }
        var failed = plan.FailedStep;
        Console.Out.WriteLine(failed is null ? Strings.Setup_FailedUnknown : string.Format(Strings.Setup_Failed, failed.Title, failed.Message));
        return 1;
    }
}
