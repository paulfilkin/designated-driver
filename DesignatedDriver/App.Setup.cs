using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesignatedDriver.Core;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;
using DesignatedDriver.Services;

namespace DesignatedDriver;

public partial class App
{
    /// <summary>
    /// --setup answers.json: the same plan the wizard runs, from a terminal, with each step and every command
    /// printed as it happens. The answers file holds a SetupAnswers object in camelCase; a board token, if
    /// wanted, comes from the DESIGNATED_DRIVER_BOARD_TOKEN environment variable (REPO_STARTER_BOARD_TOKEN, the name before the
    /// rename, when that is not set) and is never an argument.
    /// </summary>
    private static async Task<int> SetupFromCommandLine(string answersFile)
    {
        if (answersFile.Length == 0 || !File.Exists(answersFile))
        {
            Console.Error.WriteLine(Strings.Setup_Cli_Usage);
            return 3;
        }
        SetupAnswers? answers;
        try
        {
            var options = new JsonSerializerOptions(Settings.JsonOptions) { Converters = { new JsonStringEnumConverter() } };
            answers = JsonSerializer.Deserialize<SetupAnswers>(await File.ReadAllTextAsync(answersFile), options);
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
        if (answers is null) return 3;

        var workspace = new Workspace(Settings.Load());
        workspace.Runner.Activity += (_, entry) =>
        {
            switch (entry.Kind)
            {
                case ActivityKind.Command: Console.Out.WriteLine("> " + entry.Text); break;
                case ActivityKind.Error: Console.Out.WriteLine("! " + entry.Text); break;
                case ActivityKind.Result: Console.Out.WriteLine("  " + entry.Text); break;
                case ActivityKind.Output: Console.Out.WriteLine("  " + entry.Text); break;
            }
        };
        var ct = CancellationToken.None;

        if (answers.Owner.Length == 0 || answers.Owner.Equals("@me", StringComparison.OrdinalIgnoreCase))
            answers.Owner = await workspace.GitHub.LoginAsync(ct);

        // An existing folder or repository is scanned first, as the wizard's first page does.
        ScanFacts? facts = null;
        if (answers.Source != SetupSource.NewRepository)
        {
            facts = await new ScanService(workspace).ScanAsync(
                answers.Source == SetupSource.ExistingFolder ? answers.ExistingFolder : null,
                answers.Source == SetupSource.ExistingRepository ? answers.OwnerAndName : "", ct);
            if (answers.Source == SetupSource.ExistingFolder && facts.OwnerAndName.Length > 0 && !facts.OwnerAndName.Equals(answers.OwnerAndName, StringComparison.OrdinalIgnoreCase))
            {
                var parts = facts.OwnerAndName.Split('/');
                answers.Owner = parts[0];
                answers.Name = parts[1];
            }
            var report = ScanRules.Assess(facts);
            Console.Out.WriteLine();
            Console.Out.WriteLine(ScanReportText.VerdictTitle(report.Verdict) + ". " + report.Summary);
            foreach (var f in report.Of(ScanSeverity.Blocker).Concat(report.Of(ScanSeverity.Warning)))
                Console.Out.WriteLine((f.Severity == ScanSeverity.Blocker ? "  x " : "  ! ") + f.Text);
            if (report.Verdict == ScanVerdict.NotConvertible)
            {
                Console.Out.WriteLine(Strings.Setup_Cli_Blocked);
                return 2;
            }
        }

        if (answers.GitProtocol.Length == 0 || (answers.GitProtocol.Equals(RemoteUrl.Ssh, StringComparison.OrdinalIgnoreCase) && answers.SshHost.Length == 0))
        {
            var (protocol, host) = await workspace.ResolveTransportAsync(answers.Owner, ct);
            if (answers.GitProtocol.Length == 0) answers.GitProtocol = protocol;
            if (answers.SshHost.Length == 0) answers.SshHost = host;
            // Remembered for the next repository, as the wizard does.
            if (workspace.Settings.GitProtocol.Length == 0)
            {
                workspace.Settings.GitProtocol = answers.GitProtocol;
                workspace.Settings.SshHost = answers.SshHost;
                workspace.Settings.Save();
            }
        }

        var setup = new SetupService(workspace);
        if (answers.HasBoardToken) setup.BoardToken = BoardTokenFrom(Environment.GetEnvironmentVariable);
        var plan = setup.Build(answers, facts);

        Console.Out.WriteLine();
        Console.Out.WriteLine(string.Format(Strings.Setup_PlanIntro, plan.Steps.Count, answers.OwnerAndName));
        for (int i = 0; i < plan.Steps.Count; i++)
            Console.Out.WriteLine($"  {i + 1}. {plan.Steps[i].Title}: {plan.Steps[i].Detail}");
        Console.Out.WriteLine();

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
            Console.Out.WriteLine(Strings.Setup_DoneTitle);
            Console.Out.WriteLine(string.Format(Strings.Setup_Cli_Repository, setup.RepositoryUrl));
            if (setup.Board is { } b) Console.Out.WriteLine(string.Format(Strings.Setup_Cli_Board, $"https://github.com/users/{b.Owner}/projects/{b.Number}"));
            if (setup.PullRequestUrl.Length > 0) Console.Out.WriteLine(string.Format(Strings.Setup_Cli_PullRequest, setup.PullRequestUrl));
            return 0;
        }
        var failed = plan.FailedStep;
        Console.Out.WriteLine(failed is null ? Strings.Setup_FailedUnknown : string.Format(Strings.Setup_Failed, failed.Title, failed.Message));
        Console.Out.WriteLine(Strings.Setup_Cli_Resume);
        return 1;
    }

    public const string BoardTokenVariable = "DESIGNATED_DRIVER_BOARD_TOKEN";
    public const string OldBoardTokenVariable = "REPO_STARTER_BOARD_TOKEN";

    /// <summary>The board token from the environment: the new variable, or the one used before the rename when the new one is not set.</summary>
    public static string? BoardTokenFrom(Func<string, string?> variable)
    {
        var token = variable(BoardTokenVariable);
        return string.IsNullOrEmpty(token) ? NullIfEmpty(variable(OldBoardTokenVariable)) : token;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
