namespace DesignatedDriver.Core;

public enum StepState { Pending, Checking, AlreadyDone, Running, Applied, Failed }

/// <summary>
/// One step of a set-up plan (section 4.1 of the design): a check that says whether it is already done, and an
/// apply. The runner checks, skips what is done, applies the rest and checks again, so a plan can be rerun
/// after a failure and only does what is left.
/// </summary>
public sealed class PlanStep(SetupStep kind, string title, string detail, Func<CancellationToken, Task<bool>> check, Func<CancellationToken, Task> apply)
{
    public SetupStep Kind { get; } = kind;
    public string Title { get; } = title;
    /// <summary>What applying it does, in a sentence, for the checklist.</summary>
    public string Detail { get; } = detail;
    public Func<CancellationToken, Task<bool>> Check { get; } = check;
    public Func<CancellationToken, Task> Apply { get; } = apply;

    public StepState State { get; set; } = StepState.Pending;
    /// <summary>What happened, for the checklist: the failure, or a note such as the pull request's URL.</summary>
    public string Message { get; set; } = "";

    public bool IsDone => State is StepState.AlreadyDone or StepState.Applied;
}

public sealed class SetupPlan(SetupAnswers answers, IReadOnlyList<PlanStep> steps)
{
    public SetupAnswers Answers { get; } = answers;
    public IReadOnlyList<PlanStep> Steps { get; } = steps;

    public bool Succeeded => Steps.All(s => s.IsDone);
    public PlanStep? FailedStep => Steps.FirstOrDefault(s => s.State == StepState.Failed);
    public bool HasRun => Steps.Any(s => s.State != StepState.Pending);

    /// <summary>Back to the start, keeping nothing, for a rerun.</summary>
    public void Reset()
    {
        foreach (var s in Steps) { s.State = StepState.Pending; s.Message = ""; }
    }
}

/// <summary>Which steps a plan has, from the answers and the capabilities. Pure, so the self-test can list them.</summary>
public static class PlanOutline
{
    public static IReadOnlyList<SetupStep> For(SetupAnswers answers, Capabilities? capabilities)
    {
        var steps = new List<SetupStep> { SetupStep.CreateRepository, SetupStep.Settings, SetupStep.Labels, SetupStep.Board };
        if (answers.HasBoardToken) steps.Add(SetupStep.BoardToken);
        steps.AddRange([SetupStep.Files, SetupStep.Hooks, SetupStep.Security]);
        // Push protection and the ruleset exist only where the plan allows them: public repositories and paid plans.
        if (capabilities?.PushProtection == true) steps.Add(SetupStep.PushProtection);
        if (capabilities?.Rulesets == true) steps.Add(SetupStep.Ruleset);
        steps.Add(SetupStep.Register);
        return steps;
    }

    /// <summary>The steps an apply from the Check screen needs for the ticked items, in the same order.</summary>
    public static IReadOnlyList<SetupStep> ForUpdate(IEnumerable<string> selectedKeys, bool manifestOlder)
    {
        var keys = selectedKeys.ToList();
        bool Any(string prefix) => keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal));
        var steps = new List<SetupStep>();
        if (Any("settings")) steps.Add(SetupStep.Settings);
        if (Any("label:")) steps.Add(SetupStep.Labels);
        if (Any("board")) steps.Add(SetupStep.Board);
        if (Any("file:") || Any("manifest:") || manifestOlder) steps.Add(SetupStep.Files);
        if (Any("hooks")) steps.Add(SetupStep.Hooks);
        if (Any("security:")) steps.Add(SetupStep.Security);
        if (Any("pushprotection")) steps.Add(SetupStep.PushProtection);
        if (Any("ruleset")) steps.Add(SetupStep.Ruleset);
        return steps;
    }
}
