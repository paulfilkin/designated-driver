using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DesignatedDriver.Core;
using DesignatedDriver.Helpers;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;
using DesignatedDriver.Services;

namespace DesignatedDriver.Screens;

/// <summary>Submit: open the pull request from the template, then watch the required check.</summary>
public partial class SubmitScreen : ScreenBase
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollLimit = TimeSpan.FromMinutes(30);

    private GitStatus _status = new();
    private GhPullRequest? _existing;
    private string _issueTitle = "";
    private CancellationTokenSource? _watch;

    public SubmitScreen(IShell shell) : base(shell)
    {
        InitializeComponent();
        Unloaded += (_, _) => StopWatching();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = Shell.RunAsync(RefreshAsync);
    private void OpenExisting_Click(object sender, RoutedEventArgs e) { if (_existing is not null) Shell.OpenUrl(_existing.Url); }
    private void CheckNow_Click(object sender, RoutedEventArgs e) { if (_existing is not null) _ = Shell.RunAsync(ct => ShowChecksAsync(_existing.Number, ct)); }

    protected override async Task LoadAsync(CancellationToken ct)
    {
        StopWatching();
        _status = await Workspace.Git.StatusAsync(ct);
        bool onMain = _status.Branch == Repo.DefaultBranch;
        OnMainNotice.Visibility = onMain ? Visibility.Visible : Visibility.Collapsed;
        OnMainText.Text = string.Format(Strings.Submit_OnMain, Repo.DefaultBranch);
        BranchLine.Text = string.Format(Strings.Submit_FromBranch, _status.Branch, Repo.DefaultBranch);

        // An open pull request for this branch means there is nothing to create, only checks to watch.
        _existing = onMain ? null : await Workspace.GitHub.PullRequestForBranchAsync(_status.Branch, ct);
        if (_existing is not null)
        {
            ExistingText.Text = string.Format(Strings.Submit_Existing, _existing.Number, _existing.Title);
            ExistingNotice.Visibility = Visibility.Visible;
            CreateCard.Visibility = Visibility.Collapsed;
            ChecksCard.Visibility = Visibility.Visible;
            await ShowChecksAsync(_existing.Number, ct);
            StartWatching(_existing.Number);
            return;
        }

        ExistingNotice.Visibility = Visibility.Collapsed;
        CreateCard.Visibility = onMain ? Visibility.Collapsed : Visibility.Visible;
        ChecksCard.Visibility = Visibility.Collapsed;
        if (onMain) return;

        // Issue number from the branch name; title from the issue, or the first commit when there is no issue.
        var number = BranchNames.IssueNumber(_status.Branch);
        IssueBox.Text = number?.ToString() ?? "";
        _issueTitle = "";
        if (number is not null) await LoadIssueTitleAsync(number.Value, ct);

        var commits = await Workspace.Git.CommitSubjectsSinceAsync(Repo.DefaultBranch, ct);
        TitleBox.Text = _issueTitle.Length > 0 ? _issueTitle : commits.FirstOrDefault() ?? "";
        BodyBox.Text = PullRequestBody.Fill(PullRequestBody.LoadTemplate(Repo.Folder), number ?? 0);

        if (commits.Count == 0) PushNote.Text = Strings.Submit_NoCommits;
        else if (!_status.HasUpstream) PushNote.Text = string.Format(Strings.Submit_WillPushFirst, _status.Branch);
        else if (_status.AheadOfUpstream > 0) PushNote.Text = string.Format(Strings.Submit_WillPushAhead, _status.AheadOfUpstream);
        else PushNote.Text = "";
        if (!_status.IsClean) PushNote.Text = (PushNote.Text + "\n" + string.Format(Strings.Submit_Uncommitted, _status.Changes.Count)).Trim();

        // A branch behind main opens no pull request until it is updated, so the checks run on what will be merged.
        await Workspace.Git.FetchAsync(ct);
        var distance = await Workspace.Git.AheadBehindAsync(Repo.DefaultBranch, ct);
        _behind = distance.Behind;
        BehindNotice.Visibility = _behind > 0 ? Visibility.Visible : Visibility.Collapsed;
        BehindText.Text = string.Format(Strings.Submit_Behind, _behind, Repo.DefaultBranch);
        UpdateFromMainButton.Content = string.Format(Strings.Submit_UpdateFromMain, Repo.DefaultBranch);
        AutomationProperties.SetName(UpdateFromMainButton, string.Format(Strings.Submit_UpdateFromMain, Repo.DefaultBranch));
        UpdateFromMainButton.IsEnabled = _status.IsClean;

        UpdateCreateButton(commits.Count > 0);
    }

    private int _behind;

    /// <summary>Merges origin/main into the branch. Conflicts are left in place for Fix a problem, which knows how to finish or abandon a merge.</summary>
    private void UpdateFromMain_Click(object sender, RoutedEventArgs e)
    {
        if (!_status.IsClean) { Shell.ShowError(string.Format(Strings.Submit_Uncommitted, _status.Changes.Count)); return; }
        _ = Shell.RunAsync(async ct =>
        {
            try
            {
                await Workspace.Git.MergeFromOriginAsync(Repo.DefaultBranch, ct);
            }
            catch (CommandFailedException)
            {
                if (!await Workspace.Git.IsMergeInProgressAsync(ct)) throw;
                Shell.ShowError(string.Format(Strings.Submit_UpdateConflicts, Repo.DefaultBranch));
                Shell.Navigate(Screen.FixProblem);
                return;
            }
            Shell.Announce(string.Format(Strings.Submit_DidUpdate, Repo.DefaultBranch));
            await RefreshAsync(ct);
        });
    }

    private async Task LoadIssueTitleAsync(int number, CancellationToken ct)
    {
        try
        {
            var issue = await Workspace.GitHub.IssueAsync(number, ct);
            _issueTitle = issue.Title;
            IssueNote.Text = $"#{issue.Number}  {issue.Title}";
            IssueNote.Foreground = UiHelpers.Brush("TextTertiaryBrush");
        }
        catch (CommandFailedException)
        {
            _issueTitle = "";
            IssueNote.Text = string.Format(Strings.Submit_IssueNotFound, number);
            IssueNote.Foreground = UiHelpers.Brush("DangerBrush");
        }
    }

    private bool _hasCommits;

    private void UpdateCreateButton(bool? hasCommits = null)
    {
        if (hasCommits is not null) _hasCommits = hasCommits.Value;
        CreateButton.IsEnabled = _hasCommits && _behind == 0 && int.TryParse(IssueBox.Text.Trim(), out var n) && n > 0 && TitleBox.Text.Trim().Length > 0;
    }

    private void IssueBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(IssueBox.Text.Trim(), out var n) && n > 0)
            BodyBox.Text = PullRequestBody.Fill(BodyBox.Text.Length > 0 ? BodyBox.Text : PullRequestBody.LoadTemplate(Repo.Folder), n);
        UpdateCreateButton();
    }

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateCreateButton();

    private void UseIssueTitle_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IssueBox.Text.Trim(), out var n) || n <= 0) return;
        _ = Shell.RunAsync(async ct =>
        {
            await LoadIssueTitleAsync(n, ct);
            if (_issueTitle.Length > 0) TitleBox.Text = _issueTitle;
        });
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IssueBox.Text.Trim(), out var issueNumber) || issueNumber <= 0) return;
        var title = TitleBox.Text.Trim();
        var body = BodyBox.Text.Replace("\r\n", "\n");
        if (PullRequestBody.IssueNumber(body) != issueNumber)
        {
            Shell.ShowError(string.Format(Strings.Submit_BodyIssueMismatch, issueNumber));
            return;
        }

        bool needsPush = !_status.HasUpstream || _status.AheadOfUpstream > 0;
        var message = string.Format(Strings.Submit_Confirm, title, issueNumber, Repo.DefaultBranch) + (needsPush ? "\n\n" + string.Format(Strings.Submit_ConfirmPush, _status.Branch) : "");
        if (!Shell.Confirm(message, Strings.Submit_Create)) return;

        _ = Shell.RunAsync(async ct =>
        {
            if (needsPush) await Workspace.Git.PushAsync(_status.Branch, !_status.HasUpstream, ct);
            var (number, url) = await Workspace.GitHub.CreatePullRequestAsync(title, body, ct);
            Shell.Announce(string.Format(Strings.Submit_Created, number, url));
            await RefreshAsync(ct);
        });
    }

    // Checks

    private async Task<bool> ShowChecksAsync(int number, CancellationToken ct)
    {
        var checks = await Workspace.GitHub.ChecksAsync(number, ct);
        ChecksPanel.Children.Clear();
        foreach (var check in checks)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock { Text = check.Name, VerticalAlignment = VerticalAlignment.Center, FontWeight = check.Name == Repo.RequiredCheck ? FontWeights.SemiBold : FontWeights.Normal };
            var tag = UiHelpers.ChecksTag(check.Bucket switch { "pass" => "pass", "fail" or "cancel" => "fail", "skipping" => "none", _ => "pending" });
            tag.Margin = new Thickness(8, 0, 8, 0);
            var link = UiHelpers.LinkButton(check.Bucket == "fail" ? Strings.Submit_OpenLog : Strings.Submit_OpenCheck, () => Shell.OpenUrl(check.Link), string.Format(Strings.Submit_OpenCheckFor, check.Name));
            link.IsEnabled = check.Link.Length > 0;

            Grid.SetColumn(tag, 1); Grid.SetColumn(link, 2);
            row.Children.Add(name); row.Children.Add(tag); row.Children.Add(link);
            AutomationProperties.SetName(row, $"{check.Name}: {check.Bucket}");
            ChecksPanel.Children.Add(row);
        }

        var overall = ChecksSummary.Overall(checks.Select(c => c.Bucket switch { "pass" or "skipping" => "pass", "fail" or "cancel" => "fail", _ => "pending" }));
        bool finished = checks.Count > 0 && checks.All(c => c.IsFinished);
        ChecksStatusText.Text = overall switch
        {
            "none" => Strings.Submit_NoChecksYet,
            "pending" => Strings.Submit_ChecksRunning,
            "fail" => Strings.Submit_ChecksFailed,
            _ => Strings.Submit_ChecksPassed,
        };
        ChecksStatusText.Foreground = UiHelpers.Brush(overall switch { "fail" => "DangerBrush", "pass" => "SuccessBrush", _ => "TextSecondaryBrush" });
        if (finished) Shell.Announce(ChecksStatusText.Text);
        return finished;
    }

    /// <summary>Polls the checks until they finish. Runs outside the busy strip so the rest of the tool stays usable.</summary>
    private void StartWatching(int number)
    {
        StopWatching();
        var cts = _watch = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            var started = DateTime.UtcNow;
            try
            {
                while (!cts.IsCancellationRequested && DateTime.UtcNow - started < PollLimit)
                {
                    await Task.Delay(PollInterval, cts.Token);
                    if (Shell.IsBusy) continue;
                    bool finished = await Dispatcher.InvokeAsync(() => ShowChecksAsync(number, cts.Token)).Task.Unwrap();
                    if (finished) break;
                }
            }
            catch (OperationCanceledException) { }
            catch (CommandFailedException) { /* the next refresh shows the error */ }
        }, cts.Token);
    }

    private void StopWatching()
    {
        _watch?.Cancel();
        _watch = null;
    }
}
