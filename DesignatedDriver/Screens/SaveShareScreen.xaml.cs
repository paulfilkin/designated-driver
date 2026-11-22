using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using DesignatedDriver.Core;
using DesignatedDriver.Helpers;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;
using DesignatedDriver.Services;
using DesignatedDriver.Windows;

namespace DesignatedDriver.Screens;

/// <summary>Save and share: changed files and diffs, the repository's self-test, commit with live checks, push.</summary>
public partial class SaveShareScreen : ScreenBase
{
    private GitStatus _status = new();

    public SaveShareScreen(IShell shell) : base(shell)
    {
        InitializeComponent();
        UpdateMessageChecks();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = Shell.RunAsync(RefreshAsync);

    protected override async Task LoadAsync(CancellationToken ct)
    {
        _status = await Workspace.Git.StatusAsync(ct);
        SelfTestCommandText.Text = Repo.SelfTestCommand;
        // A repository with no self-test command has no card for it.
        SelfTestCard.Visibility = Repo.SelfTestCommand.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        bool onMain = _status.Branch == Repo.DefaultBranch;
        OnMainNotice.Visibility = onMain ? Visibility.Visible : Visibility.Collapsed;
        OnMainText.Text = string.Format(Strings.Save_OnMain, Repo.DefaultBranch);

        BranchLine.Text = string.Format(Strings.Save_OnBranch, _status.Branch);
        FilesTitle.Text = _status.IsClean ? Strings.Save_ChangedFiles : string.Format(Strings.Save_ChangedFilesCount, _status.Changes.Count);
        ShowFiles();

        // Push state: no upstream yet, or commits waiting.
        if (!_status.HasUpstream)
            PushStatusText.Text = string.Format(Strings.Save_PushFirstTime, _status.Branch);
        else if (_status.AheadOfUpstream > 0)
            PushStatusText.Text = string.Format(Strings.Save_PushAhead, _status.AheadOfUpstream, _status.Upstream);
        else
            PushStatusText.Text = string.Format(Strings.Save_PushUpToDate, _status.Upstream);
        PushButton.IsEnabled = !onMain && (!_status.HasUpstream || _status.AheadOfUpstream > 0);
        UpdateMessageChecks();
    }

    private void ShowFiles()
    {
        FilesPanel.Children.Clear();
        NoFilesText.Visibility = _status.IsClean ? Visibility.Visible : Visibility.Collapsed;
        foreach (var file in _status.Changes)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var kind = UiHelpers.Tag(DescribeCode(file), file.IsConflict ? "DangerSoftBrush" : null, file.IsConflict ? "DangerBrush" : null);
            kind.HorizontalAlignment = HorizontalAlignment.Left;
            var path = UiHelpers.Text(file.OriginalPath.Length > 0 ? $"{file.OriginalPath} → {file.Path}" : file.Path, "MonoText");
            path.VerticalAlignment = VerticalAlignment.Center;
            var captured = file;
            var diff = UiHelpers.LinkButton(Strings.Save_OpenDiff, () => _ = Shell.RunAsync(ct => ShowDiffAsync(captured, ct)), string.Format(Strings.Save_OpenDiffFor, file.Path));

            Grid.SetColumn(path, 1); Grid.SetColumn(diff, 2);
            row.Children.Add(kind); row.Children.Add(path); row.Children.Add(diff);
            AutomationProperties.SetName(row, $"{DescribeCode(file)} {file.Path}");
            FilesPanel.Children.Add(row);
        }
    }

    private static string DescribeCode(ChangedFile file)
    {
        if (file.IsUntracked) return Strings.Save_FileNew;
        if (file.IsConflict) return Strings.Save_FileConflict;
        var code = file.Code.Trim('.', ' ');
        return code switch
        {
            "A" or "AM" or "AD" => Strings.Save_FileAdded,
            "D" or "MD" => Strings.Save_FileDeleted,
            "R" or "RM" => Strings.Save_FileRenamed,
            _ => Strings.Save_FileModified,
        };
    }

    private async Task ShowDiffAsync(ChangedFile file, CancellationToken ct)
    {
        string text;
        if (file.IsUntracked)
        {
            var full = System.IO.Path.Combine(Repo.Folder, file.Path);
            text = System.IO.File.Exists(full) ? await System.IO.File.ReadAllTextAsync(full, ct) : "";
            text = Strings.Save_NewFileDiff + "\n\n" + text;
        }
        else
        {
            text = await Workspace.Git.DiffAsync(file.Path, ct);
            if (text.Trim().Length == 0) text = Strings.Save_EmptyDiff;
        }
        new TextWindow(Strings.Save_DiffTitle, file.Path, text) { Owner = Shell.Window }.ShowDialog();
    }

    // Self-test

    private void RunSelfTest_Click(object sender, RoutedEventArgs e)
    {
        var (exe, args) = CommandLineFormat.Split(Repo.SelfTestCommand);
        if (exe.Length == 0) { Shell.ShowError(Strings.Save_NoSelfTestCommand); return; }

        SelfTestList.Items.Clear();
        SelfTestList.Visibility = Visibility.Visible;
        SelfTestSummary.Text = Strings.Save_SelfTestRunning;
        SelfTestSummary.Foreground = UiHelpers.Brush("TextSecondaryBrush");

        _ = Shell.RunAsync(async ct =>
        {
            int pass = 0, fail = 0;
            var result = await Workspace.Runner.RunAsync(exe, args, Repo.Folder, line =>
            {
                bool isPass = line.StartsWith("PASS", StringComparison.Ordinal);
                bool isFail = line.StartsWith("FAIL", StringComparison.Ordinal);
                if (!isPass && !isFail) return;
                if (isPass) pass++; else fail++;
                Dispatcher.BeginInvoke(() =>
                {
                    var item = new ListBoxItem
                    {
                        Content = new TextBlock { Text = line, Foreground = UiHelpers.Brush(isPass ? "SuccessBrush" : "DangerBrush"), FontFamily = new FontFamily("Cascadia Mono, Consolas") },
                    };
                    SelfTestList.Items.Add(item);
                    SelfTestList.ScrollIntoView(item);
                });
            }, ct);

            bool ok = result.Succeeded && fail == 0;
            SelfTestSummary.Text = ok
                ? string.Format(Strings.Save_SelfTestPassed, pass)
                : string.Format(Strings.Save_SelfTestFailed, fail, pass, result.ExitCode);
            SelfTestSummary.Foreground = UiHelpers.Brush(ok ? "SuccessBrush" : "DangerBrush");
            Shell.Announce(SelfTestSummary.Text);
        });
    }

    // Commit

    private void Message_TextChanged(object sender, TextChangedEventArgs e) => UpdateMessageChecks();

    private void UpdateMessageChecks()
    {
        var summary = SummaryBox.Text;
        var issues = CommitMessageChecks.Check(summary, DetailBox.Text);
        SummaryCount.Text = string.Format(Strings.Save_SummaryCount, summary.Trim().Length, CommitMessageChecks.SummaryLimit);
        SummaryCount.Foreground = UiHelpers.Brush(summary.Trim().Length > CommitMessageChecks.SummaryLimit ? "DangerBrush" : "TextTertiaryBrush");

        ChecksPanel.Children.Clear();
        if (issues.Count == 0 && summary.Trim().Length > 0)
        {
            var okRow = new StackPanel { Orientation = Orientation.Horizontal };
            okRow.Children.Add(UiHelpers.Mark(true));
            okRow.Children.Add(new TextBlock { Text = Strings.Save_MessageOk, Foreground = UiHelpers.Brush("SuccessBrush"), VerticalAlignment = VerticalAlignment.Center });
            ChecksPanel.Children.Add(okRow);
        }
        foreach (var issue in issues)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            var mark = UiHelpers.Mark(false);
            if (issue.Severity == CheckSeverity.Warning) { mark.Text = "!"; mark.Foreground = UiHelpers.Brush("WarningBrush"); }
            row.Children.Add(mark);
            row.Children.Add(new TextBlock { Text = issue.Message, Foreground = UiHelpers.Brush(issue.Severity == CheckSeverity.Error ? "DangerBrush" : "TextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center });
            ChecksPanel.Children.Add(row);
        }
        CommitButton.IsEnabled = !_status.IsClean && CommitMessageChecks.IsAcceptable(summary, DetailBox.Text);
    }

    private void Commit_Click(object sender, RoutedEventArgs e)
    {
        var summary = SummaryBox.Text.Trim();
        var detail = DetailBox.Text;
        var issues = CommitMessageChecks.Check(summary, detail);
        if (issues.Any(i => i.Severity == CheckSeverity.Error)) return;

        // The file list is shown before anything is staged: git add -A takes everything.
        var files = _status.Changes.Select(c => c.Path).ToList();
        var shown = string.Join("\n", files.Take(25));
        if (files.Count > 25) shown += "\n" + string.Format(Strings.Save_AndMore, files.Count - 25);
        var warnings = issues.Count > 0 ? "\n\n" + string.Join("\n", issues.Select(i => "- " + i.Message)) : "";
        var message = string.Format(Strings.Save_ConfirmCommit, files.Count, summary, shown) + warnings;
        if (!Shell.Confirm(message, Strings.Save_CommitAll)) return;

        _ = Shell.RunAsync(async ct =>
        {
            await Workspace.Git.AddAllAsync(ct);
            await Workspace.Git.CommitAsync(CommitMessageChecks.Compose(summary, detail), ct);
            SummaryBox.Clear();
            DetailBox.Clear();
            Shell.Announce(string.Format(Strings.Save_Committed, summary));
            await RefreshAsync(ct);
        });
    }

    // Push

    private void Push_Click(object sender, RoutedEventArgs e)
    {
        bool first = !_status.HasUpstream;
        var message = first
            ? string.Format(Strings.Save_ConfirmPushFirst, _status.Branch)
            : string.Format(Strings.Save_ConfirmPush, _status.AheadOfUpstream, _status.Upstream);
        if (!Shell.Confirm(message, Strings.Save_PushButton)) return;

        _ = Shell.RunAsync(async ct =>
        {
            await Workspace.Git.PushAsync(_status.Branch, first, ct);
            Shell.Announce(string.Format(Strings.Save_Pushed, _status.Branch));
            await RefreshAsync(ct);
        });
    }
}
