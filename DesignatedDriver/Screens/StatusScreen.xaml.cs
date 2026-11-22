using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DesignatedDriver.Core;
using DesignatedDriver.Helpers;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;
using DesignatedDriver.Services;
using DesignatedDriver.Windows;

namespace DesignatedDriver.Screens;

/// <summary>Status: prerequisites, repository, convention, what the plan allows, branch, open pull requests and the user's issues.</summary>
public partial class StatusScreen : ScreenBase
{
    /// <summary>What gh auth status reported on this load, so reads that need a scope the token lacks are skipped.</summary>
    private AuthStatus _auth = new();
    /// <summary>The repository's visibility from this load's capabilities read, or null when it couldn't be read.</summary>
    private bool? _isPublic;

    public StatusScreen(IShell shell) : base(shell)
    {
        InitializeComponent();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = Shell.RunAsync(RefreshAsync);

    private void ChangeFolder_Click(object sender, RoutedEventArgs e) => Shell.Navigate(Screen.Repositories);

    protected override async Task LoadAsync(CancellationToken ct)
    {
        NoRepositoryNotice.Visibility = Repo.HasFolder ? Visibility.Collapsed : Visibility.Visible;
        FolderText.Text = Repo.HasFolder ? Repo.Folder : Strings.Status_NotChosen;

        // 1. Tools and sign-in.
        var report = new PrerequisiteReport { GitVersion = await Workspace.Git.VersionAsync(ct) };
        report.GitFound = report.GitVersion.Length > 0;
        report.GhVersion = await Workspace.GitHub.VersionAsync(ct);
        report.GhFound = report.GhVersion.Length > 0;
        if (report.GhFound) report.Auth = await Workspace.GitHub.AuthStatusAsync(ct);
        _auth = report.Auth;
        _isPublic = null;
        ShowPrerequisites(report);

        // 2. Repository, identity, convention and board.
        if (Repo.HasFolder) await Workspace.ReadOriginAsync(ct);
        Workspace.ReadManifest();
        OwnerRepoText.Text = Repo.OwnerAndName.Length > 0 ? Repo.OwnerAndName : Strings.Status_Unknown;
        if (report.Auth.SignedIn) await Workspace.RefreshIdentityAsync(ct);
        UserRoleText.Text = Workspace.IdentityKnown
            ? string.Format(Strings.Status_UserAndRole, Workspace.Login, Workspace.IsAdmin ? Strings.Role_Admin : Strings.Role_Contributor, Workspace.Permission)
            : Strings.Status_NotSignedIn;
        // A repository that keeps its own conventions says so, and has no Check to open.
        ConventionText.Text = Repo.KeepsOwnConventions ? Strings.Status_KeepsOwn
            : Workspace.Manifest is { } m
            ? string.Format(Strings.Status_ConventionVersion, m.ConventionVersion, m.SetUp) + (Workspace.LastChecks.TryGetValue(Repo.OwnerAndName, out var lastCheck) ? "  ·  " + lastCheck.Report.Summary : "")
            : Strings.Status_NoConvention;
        CheckLink.Visibility = Workspace.Manifest is not null && !Repo.KeepsOwnConventions ? Visibility.Visible : Visibility.Collapsed;
        BoardText.Text = Workspace.Board is { } b ? string.Format(Strings.Status_BoardLine, b.Owner, b.Number) : Strings.Status_NoBoard;

        // 3. What the plan allows, from the repository's visibility and a probe of the branch rules endpoint.
        CapabilitiesPanel.Children.Clear();
        if (Workspace.IdentityKnown && Repo.OwnerAndName.Length > 0)
            await ShowCapabilitiesAsync(ct);
        else
            CapabilitiesLine.Text = Strings.Status_CapabilitiesUnknown;

        // 3b. Automation: Dependabot alerts, the month's Actions minutes, and the board actions.
        if (Workspace.IdentityKnown && Repo.OwnerAndName.Length > 0)
            await ShowAutomationAsync(ct);
        else
        {
            DependabotText.Text = ActionsText.Text = BoardFeedText.Text = Strings.Status_SignInFirst;
            ActionsNote.Visibility = AutoAddLink.Visibility = Visibility.Collapsed;
            AddMissingButton.IsEnabled = TokenButton.IsEnabled = false;
        }

        // 4. Branch. Fetch may fail offline; the local figures still show.
        BranchText.Text = ChangesText.Text = AheadBehindText.Text = "";
        if (Repo.HasFolder)
        {
            var status = await Workspace.Git.StatusAsync(ct);
            BranchText.Text = status.Branch;
            ChangesText.Text = status.IsClean ? Strings.Status_Clean : string.Format(Strings.Status_ChangedFiles, status.Changes.Count);
            try
            {
                await Workspace.Git.FetchAsync(ct);
                var ab = await Workspace.Git.AheadBehindAsync(Repo.DefaultBranch, ct);
                AheadBehindText.Text = string.Format(Strings.Status_AheadBehind, ab.Ahead, ab.Behind, Repo.DefaultBranch);
            }
            catch (CommandFailedException)
            {
                AheadBehindText.Text = Strings.Status_FetchFailed;
            }
        }

        // 5. Pull requests and issues.
        PullRequestsPanel.Children.Clear();
        IssuesPanel.Children.Clear();
        if (Workspace.IdentityKnown && Repo.HasFolder)
        {
            // Every open pull request, not only the user's own: an admin may need to act on any of them.
            ShowPullRequests(await Workspace.GitHub.OpenPullRequestsAsync(ct));
            ShowIssues(await Workspace.GitHub.MyIssuesAsync(ct));
        }
        else
        {
            PullRequestsPanel.Children.Add(UiHelpers.Text(Strings.Status_SignInFirst, "HelperText"));
            IssuesPanel.Children.Add(UiHelpers.Text(Strings.Status_SignInFirst, "HelperText"));
        }
    }

    private async Task ShowCapabilitiesAsync(CancellationToken ct)
    {
        if (!Workspace.PlanProbed)
        {
            // The plan name needs the user scope; without it the answer is always empty, so it isn't asked for.
            Workspace.PlanName = ActionsUsage.ReadsAccount(_auth) ? await Workspace.GitHub.PlanNameAsync(ct) : "";
            Workspace.PlanProbed = true;
        }
        var details = await Workspace.GitHub.RepositoryDetailsAsync(Repo.OwnerAndName, ct);
        if (details is null)
        {
            CapabilitiesLine.Text = Strings.Status_CapabilitiesUnknown;
            return;
        }
        _isPublic = details.IsPublic;
        ApiStatus? probe = null;
        if (!details.IsPublic) probe = (await Workspace.GitHub.BranchRulesAsync(Repo.OwnerAndName, details.DefaultBranch, ct)).Status;
        var caps = Capabilities.From(Workspace.PlanName, details.IsPublic, probe);

        CapabilitiesLine.Text = string.Format(Strings.Status_CapabilitiesLine, caps.IsPublic ? Strings.Status_Visibility_Public : Strings.Status_Visibility_Private, caps.PlanLabel);
        foreach (var (feature, available) in caps.Features())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(UiHelpers.Mark(available));
            row.Children.Add(new TextBlock { Text = feature, VerticalAlignment = VerticalAlignment.Center, Width = 260 });
            row.Children.Add(new TextBlock { Text = available ? Strings.Capability_Available : Strings.Capability_NotAvailable, Foreground = UiHelpers.Brush(available ? "TextSecondaryBrush" : "TextTertiaryBrush"), VerticalAlignment = VerticalAlignment.Center });
            AutomationProperties.SetName(row, $"{feature}: {(available ? Strings.Capability_Available : Strings.Capability_NotAvailable)}");
            CapabilitiesPanel.Children.Add(row);
        }
    }

    private void ShowPrerequisites(PrerequisiteReport report)
    {
        PrerequisitesPanel.Children.Clear();
        AddPrerequisite(report.GitFound, Strings.Prereq_Git, report.GitFound ? report.GitVersion : Strings.Prereq_NotFound, report.GitFound ? null : AuthStatus.InstallGitCommand);
        AddPrerequisite(report.GhFound, Strings.Prereq_Gh, report.GhFound ? report.GhVersion : Strings.Prereq_NotFound, report.GhFound ? null : AuthStatus.InstallGhCommand);
        if (!report.GhFound) return;

        var auth = report.Auth;
        AddPrerequisite(auth.SignedIn, Strings.Prereq_SignedIn,
            auth.SignedIn ? string.Format(Strings.Prereq_SignedInAs, auth.Account, auth.Host) : Strings.Prereq_NotSignedIn,
            auth.SignedIn ? null : AuthStatus.SignInCommand);
        if (!auth.SignedIn) return;

        foreach (var scope in AuthStatus.RequiredScopes)
        {
            var has = auth.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
            AddPrerequisite(has, string.Format(Strings.Prereq_Scope, scope), has ? Strings.Prereq_ScopePresent : Strings.Prereq_ScopeMissing, has ? null : AuthStatus.FixCommandForScope(scope));
        }
    }

    private void AddPrerequisite(bool ok, string name, string detail, string? fixCommand)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var mark = UiHelpers.Mark(ok);
        var nameText = new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(nameText, 1);
        var detailPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        detailPanel.Children.Add(new TextBlock { Text = detail, Foreground = UiHelpers.Brush(ok ? "TextSecondaryBrush" : "DangerBrush") });
        if (fixCommand is not null)
        {
            var fixRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            fixRow.Children.Add(new TextBlock { Text = Strings.Prereq_FixWith, Foreground = UiHelpers.Brush("TextTertiaryBrush"), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            var cmd = new Border
            {
                Background = UiHelpers.Brush("PrimarySoftestBrush"),
                BorderBrush = UiHelpers.Brush("PrimarySubtleBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 2, 8, 2),
                Child = UiHelpers.Text(fixCommand, "MonoText"),
            };
            fixRow.Children.Add(cmd);
            fixRow.Children.Add(UiHelpers.LinkButton(Strings.Common_Copy, () => UiHelpers.CopyToClipboard(fixCommand), string.Format(Strings.Prereq_CopyCommand, fixCommand)));
            detailPanel.Children.Add(fixRow);
        }
        Grid.SetColumn(detailPanel, 2);

        grid.Children.Add(mark);
        grid.Children.Add(nameText);
        grid.Children.Add(detailPanel);
        AutomationProperties.SetName(grid, $"{name}: {detail}");
        PrerequisitesPanel.Children.Add(grid);
    }

    private void ShowPullRequests(List<GhPullRequest> pulls)
    {
        PullRequestsPanel.Children.Clear();
        if (pulls.Count == 0)
        {
            PullRequestsPanel.Children.Add(UiHelpers.Text(Strings.Status_NoPullRequests, "HelperText"));
            return;
        }
        foreach (var pr in pulls)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var number = new TextBlock { Text = $"#{pr.Number}", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var titlePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titlePanel.Children.Add(new TextBlock { Text = pr.Title });
            titlePanel.Children.Add(new TextBlock { Text = string.Format(Strings.Review_By, pr.Author?.Login ?? "", pr.HeadRefName), Style = (Style)FindResource("HelperText"), Margin = new Thickness(0) });
            var tags = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            if (ReviewRules.IsOwn(pr, Workspace.Login)) tags.Children.Add(UiHelpers.Tag(Strings.Review_Own, "PrimarySofterBrush", "TertiaryBrush"));
            tags.Children.Add(UiHelpers.ChecksTag(pr.ChecksBucket));
            tags.Children.Add(UiHelpers.ReviewTag(pr.ReviewDecision));
            var open = UiHelpers.LinkButton(Strings.Common_OpenOnGitHub, () => Shell.OpenUrl(pr.Url), string.Format(Strings.Status_OpenPullRequest, pr.Number));

            Grid.SetColumn(titlePanel, 1); Grid.SetColumn(tags, 2); Grid.SetColumn(open, 3);
            row.Children.Add(number); row.Children.Add(titlePanel); row.Children.Add(tags); row.Children.Add(open);

            // Admins go straight to the Review screen with this pull request selected.
            if (Workspace.IsAdmin)
            {
                var captured = pr.Number;
                var review = UiHelpers.LinkButton(Strings.Status_ReviewPullRequest, () => Shell.Navigate(Screen.Review, captured), string.Format(Strings.Status_ReviewPullRequestFor, pr.Number));
                review.Margin = new Thickness(12, 0, 0, 0);
                Grid.SetColumn(review, 4);
                row.Children.Add(review);
            }
            PullRequestsPanel.Children.Add(row);
        }
    }

    private void ShowIssues(List<GhIssue> issues)
    {
        IssuesPanel.Children.Clear();
        if (issues.Count == 0)
        {
            IssuesPanel.Children.Add(UiHelpers.Text(Strings.Status_NoIssues, "HelperText"));
            return;
        }
        foreach (var issue in issues)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var number = new TextBlock { Text = $"#{issue.Number}", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            body.Children.Add(new TextBlock { Text = issue.Title });
            var labels = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
            foreach (var l in issue.Labels) labels.Children.Add(UiHelpers.LabelChip(l));
            body.Children.Add(labels);
            var open = UiHelpers.LinkButton(Strings.Common_OpenOnGitHub, () => Shell.OpenUrl(issue.Url), string.Format(Strings.Status_OpenIssue, issue.Number));

            Grid.SetColumn(body, 1); Grid.SetColumn(open, 2);
            row.Children.Add(number); row.Children.Add(body); row.Children.Add(open);
            IssuesPanel.Children.Add(row);
        }
    }

    private void CheckLink_Click(object sender, RoutedEventArgs e) => Shell.Navigate(Screen.Check);
    // Automation: Dependabot alerts, the month's Actions minutes, and the board actions

    private async Task ShowAutomationAsync(CancellationToken ct)
    {
        AddMissingButton.IsEnabled = Workspace.Board is not null;
        TokenButton.IsEnabled = true;

        var alerts = await Workspace.GitHub.DependabotOpenAlertsAsync(Repo.OwnerAndName, ct);
        if (alerts is null)
        {
            DependabotText.Text = Strings.Status_DependabotUnknown;
            AlertsLink.Visibility = Visibility.Collapsed;
        }
        else
        {
            int high = alerts.Count(a => a.SecurityAdvisory?.Severity is "high" or "critical");
            DependabotText.Text = alerts.Count == 0 ? Strings.Status_DependabotNone : string.Format(Strings.Status_DependabotOpen, alerts.Count, high);
            AlertsLink.Visibility = alerts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        await ShowBoardFeedAsync(ct);

        // Account-wide from billing when the token has the user scope; this repository's runs otherwise, unless the
        // repository is public, where Actions minutes are free and no run is billable.
        var billing = ActionsUsage.ReadsAccount(_auth) ? await Workspace.GitHub.ActionsBillingAsync(Workspace.Login, ct) : null;
        if (billing is not null)
        {
            ActionsText.Text = string.Format(Strings.Status_ActionsAccount, Math.Round(billing.TotalMinutesUsed), billing.IncludedMinutes);
            ActionsNote.Visibility = Visibility.Collapsed;
            return;
        }
        if (!ActionsUsage.CountsRuns(_isPublic))
        {
            ActionsText.Text = Strings.Status_ActionsFree;
            ActionsNote.Visibility = Visibility.Collapsed;
            return;
        }
        if (!Workspace.ActionsMinutes.TryGetValue(Repo.OwnerAndName, out var cached) || cached.When < ActionsUsage.MonthStart(DateTime.Now))
        {
            var runs = await Workspace.GitHub.RunsSinceAsync(Repo.OwnerAndName, DateTime.Now, ct);
            var timings = new List<Models.GhRunTiming>();
            foreach (var run in runs.Take(ActionsUsage.MaxRunsToTime))
            {
                var timing = await Workspace.GitHub.RunTimingAsync(Repo.OwnerAndName, run.Id, ct);
                if (timing is not null) timings.Add(timing);
            }
            cached = (ActionsUsage.BillableMinutes(timings), runs.Count, DateTime.Now);
            Workspace.ActionsMinutes[Repo.OwnerAndName] = cached;
        }
        ActionsText.Text = string.Format(Strings.Status_ActionsRepository, cached.Minutes, cached.Runs);
        ActionsNote.Text = string.Format(Strings.Status_ActionsNeedsUserScope, AuthStatus.FixCommandForScope("user"));
        ActionsNote.Visibility = Visibility.Visible;
    }

    /// <summary>The board's Workflows page, set when nothing feeds the board.</summary>
    private string _workflowsUrl = "";

    /// <summary>What puts new issues and pull requests on the board; when nothing does, a link to the board's Auto-add (issue 67).</summary>
    private async Task ShowBoardFeedAsync(CancellationToken ct)
    {
        AutoAddLink.Visibility = Visibility.Collapsed;
        if (Workspace.Board is not { } board)
        {
            BoardFeedText.Text = Strings.Status_NoBoard;
            return;
        }
        var kind = BoardFeed.Assess(await Workspace.GitHub.BoardFeedAsync(Repo.OwnerAndName, board, ct));
        BoardFeedText.Text = BoardFeed.Text(kind);
        if (kind is BoardFeedKind.None or BoardFeedKind.TokenNotForDependabot)
        {
            bool organisation = string.Equals(await Workspace.GitHub.OwnerTypeAsync(board.Owner, ct), "Organization", StringComparison.OrdinalIgnoreCase);
            _workflowsUrl = BoardFeed.WorkflowsUrl(board, organisation);
            AutoAddLink.Visibility = Visibility.Visible;
        }
    }

    private void AutoAddLink_Click(object sender, RoutedEventArgs e)
    {
        if (_workflowsUrl.Length > 0) Shell.OpenUrl(_workflowsUrl);
    }

    private void AlertsLink_Click(object sender, RoutedEventArgs e) => Shell.OpenUrl($"https://github.com/{Repo.OwnerAndName}/security/dependabot");

    private void AddMissing_Click(object sender, RoutedEventArgs e)
    {
        if (Workspace.Board is not { } board) return;
        _ = Shell.RunAsync(async ct =>
        {
            var (issues, pullRequests) = await Workspace.GitHub.AddMissingToBoardAsync(board, ct);
            Shell.ShowInfo(issues + pullRequests == 0 ? Strings.Status_AddMissingNone : string.Format(Strings.Status_AddMissingDone, issues, pullRequests));
        });
    }

    private void Token_Click(object sender, RoutedEventArgs e)
    {
        var window = new TokenWindow();
        if (!Shell.ShowDialog(window)) return;
        var token = window.Token;
        _ = Shell.RunAsync(async ct =>
        {
            await Workspace.GitHub.SetBoardTokenAsync(Repo.OwnerAndName, token, ct);
            Shell.ShowInfo(Strings.Status_TokenStored);
        });
    }
}
