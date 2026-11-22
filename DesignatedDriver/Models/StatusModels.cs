namespace DesignatedDriver.Models;

/// <summary>GET /users/{user}/settings/billing/actions, snake_case. Needs the user scope.</summary>
public sealed class GhActionsBilling
{
    public double TotalMinutesUsed { get; set; }
    public double TotalPaidMinutesUsed { get; set; }
    public int IncludedMinutes { get; set; }
}

/// <summary>GET /repos/{o}/{r}/actions/runs</summary>
public sealed class GhWorkflowRunList
{
    public int TotalCount { get; set; }
    public List<GhWorkflowRun> WorkflowRuns { get; set; } = [];
}

public sealed class GhWorkflowRun
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string Conclusion { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>GET /repos/{o}/{r}/actions/runs/{id}/timing: billable milliseconds per runner operating system.</summary>
public sealed class GhRunTiming
{
    public long RunDurationMs { get; set; }
    public Dictionary<string, GhBillableTime> Billable { get; set; } = [];
}

public sealed class GhBillableTime
{
    public long TotalMs { get; set; }
    public int Jobs { get; set; }
}

/// <summary>GET /repos/{o}/{r}/dependabot/alerts?state=open: only what Status shows.</summary>
public sealed class GhDependabotAlert
{
    public int Number { get; set; }
    public string State { get; set; } = "";
    public GhSecurityAdvisory? SecurityAdvisory { get; set; }
}

public sealed class GhSecurityAdvisory
{
    public string Severity { get; set; } = "";
    public string Summary { get; set; } = "";
}
