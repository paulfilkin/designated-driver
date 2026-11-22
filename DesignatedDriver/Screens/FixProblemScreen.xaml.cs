using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DesignatedDriver.Core;
using DesignatedDriver.Helpers;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;
using DesignatedDriver.Services;

namespace DesignatedDriver.Screens;

/// <summary>
/// Screen 7: the common situations from Working with Git.md, section 8. Each card says whether it applies
/// to the branch right now and only then offers its action.
/// </summary>
public partial class FixProblemScreen : ScreenBase
{
    private GitStatus _status = new();
    private AheadBehind _aheadBehind = new(0, 0);
    private GhPullRequest? _pullRequest;
    private bool _mergeInProgress;
    private readonly List<CheckBox> _discardBoxes = [];

    public FixProblemScreen(IShell shell) : base(shell)
    {
        InitializeComponent();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = Shell.RunAsync(RefreshAsync);

    private bool OnMain => _status.Branch == Repo.DefaultBranch;

    protected override async Task LoadAsync(CancellationToken ct)
    {
        _status = await Workspace.Git.StatusAsync(ct);
        _mergeInProgress = await Workspace.Git.IsMergeInProgressAsync(ct);
        await Workspace.Git.FetchAsync(ct);
        _aheadBehind = await Workspace.Git.AheadBehindAsync(Repo.DefaultBranch, ct);
        _pullRequest = OnMain ? null : await Workspace.GitHub.PullRequestForBranchAsync(_status.Branch, ct);
        var lastCommit = await Workspace.Git.LastCommitSubjectAsync(ct);

        ShowSituation();
        ShowConflict();
        ShowMove();
        ShowDiscard();
        ShowUndo(lastCommit);
        ShowBehind();
    }

    private void ShowSituation()
    {
        var lines = new List<string> { string.Format(Strings.Fix_OnBranch, _status.Branch) };
        lines.Add(_status.IsClean ? Strings.Fix_NoChanges : string.Format(Strings.Fix_Changes, _status.Changes.Count));
        int conflicted = _status.Changes.Count(c => c.IsConflict);
        if (conflicted > 0) lines.Add(string.Format(Strings.Fix_Conflicted, conflicted));
        if (!OnMain) lines.Add(string.Format(Strings.Fix_AheadBehind, _aheadBehind.Ahead, _aheadBehind.Behind, Repo.DefaultBranch));
        if (_pullRequest is not null) lines.Add(string.Format(Strings.Fix_HasPullRequest, _pullRequest.Number));

        bool anything = conflicted > 0 || (OnMain && !_status.IsClean) || (!OnMain && _aheadBehind.Behind > 0);
        if (!anything && _status.IsClean) lines.Add(Strings.Fix_NothingWrong);
        SituationText.Text = string.Join("\n", lines);
        SituationNotice.Style = (Style)FindResource(conflicted > 0 ? "DangerNoticeStyle" : anything ? "WarningNoticeStyle" : "NoticeStyle");
    }

    // 5. Conflict

    private void ShowConflict()
    {
        var conflicted = _status.Changes.Where(c => c.IsConflict).ToList();
        ConflictCard.Visibility = conflicted.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotAMergeText.Visibility = conflicted.Count > 0 && !_mergeInProgress ? Visibility.Visible : Visibility.Collapsed;
        AbortMergeButton.Visibility = _mergeInProgress ? Visibility.Visible : Visibility.Collapsed;
        ConflictFilesPanel.Children.Clear();
        foreach (var file in conflicted)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var path = UiHelpers.Text(file.Path, "MonoText");
            path.VerticalAlignment = VerticalAlignment.Center;
            var captured = file.Path;
            var open = UiHelpers.LinkButton(Strings.Fix_OpenInEditor, () => Shell.OpenUrl(Path.Combine(Repo.Folder, captured)), string.Format(Strings.Fix_OpenInEditorFor, file.Path));
            Grid.SetColumn(open, 1);
            row.Children.Add(path); row.Children.Add(open);
            AutomationProperties.SetName(row, $"{Strings.Save_FileConflict} {file.Path}");
            ConflictFilesPanel.Children.Add(row);
        }
    }

    private void FinishMerge_Click(object sender, RoutedEventArgs e)
    {
        // Refuse while any conflicted file still has markers: committing them would put the markers into main.
        foreach (var file in _status.Changes.Where(c => c.IsConflict))
        {
            var full = Path.Combine(Repo.Folder, file.Path);
            if (!File.Exists(full)) continue;
            var text = File.ReadAllText(full);
            if (ConflictMarkers.HasMarkers(text))
            {
                Shell.ShowError(string.Format(Strings.Fix_MarkersRemain, file.Path, string.Join(", ", ConflictMarkers.StartLines(text))));
                return;
            }
        }
        if (!Shell.Confirm(Strings.Fix_ConfirmFinish, Strings.Fix_FinishMerge)) return;

        bool first = !_status.HasUpstream;
        var branch = _status.Branch;
        bool merge = _mergeInProgress;
        _ = Shell.RunAsync(async ct =>
        {
            await Workspace.Git.AddAllAsync(ct);
            if (merge) await Workspace.Git.CommitMergeAsync(ct);
            else await Workspace.Git.CommitAsync(Strings.Fix_ResolveCommitMessage + "\n", ct);
            await Workspace.Git.PushAsync(branch, first, ct);
            Shell.Announce(Strings.Fix_DidFinish);
            await RefreshAsync(ct);
        });
    }

    private void AbortMerge_Click(object sender, RoutedEventArgs e)
    {
        if (!Shell.Confirm(Strings.Fix_ConfirmAbort, Strings.Fix_AbortMerge)) return;
        _ = Shell.RunAsync(async ct =>
        {
            await Workspace.Git.AbortMergeAsync(ct);
            Shell.Announce(Strings.Fix_DidAbort);
            await RefreshAsync(ct);
        });
    }

    // 1. Changes made on main

    private void ShowMove()
    {
        bool applies = OnMain && !_status.IsClean && !_status.HasConflicts;
        MovePanel.Visibility = applies ? Visibility.Visible : Visibility.Collapsed;
        MoveNote.Text = applies ? ""
            : !OnMain ? string.Format(Strings.Fix_MoveNotOnMain, _status.Branch, Repo.DefaultBranch)
            : string.Format(Strings.Fix_MoveNoChanges, Repo.DefaultBranch);
        MoveNote.Visibility = applies ? Visibility.Collapsed : Visibility.Visible;
        if (applies && BranchNameBox.Text.Length == 0) BranchNameBox.Text = "feature/";
        UpdateMoveButton();
    }

    private void BranchNameBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateMoveButton();

    private void UpdateMoveButton()
    {
        var name = BranchNameBox.Text.Trim();
        MoveButton.IsEnabled = name.Length > 0 && !name.EndsWith('/') && name != Repo.DefaultBranch;
    }

    private void Move_Click(object sender, RoutedEventArgs e)
    {
        var branch = BranchNameBox.Text.Trim();
        if (!Shell.Confirm(string.Format(Strings.Fix_ConfirmMove, branch, _status.Changes.Count), Strings.Fix_Move)) return;
        _ = Shell.RunAsync(async ct =>
        {
            if (!await Workspace.Git.IsValidBranchNameAsync(branch, ct)) throw new InvalidOperationException(string.Format(Strings.StartWork_BranchInvalid, branch));
            if (await Workspace.Git.LocalBranchExistsAsync(branch, ct)) throw new InvalidOperationException(string.Format(Strings.StartWork_BranchExists, branch));
            await Workspace.Git.CreateBranchAsync(branch, ct);
            BranchNameBox.Clear();
            Shell.Announce(string.Format(Strings.Fix_DidMove, branch));
            await RefreshAsync(ct);
        });
    }

    // 2. Throw changes away

    private void ShowDiscard()
    {
        var files = _status.Changes.Where(c => !c.IsConflict).ToList();
        DiscardNone.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DiscardPanel.Visibility = files.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        DiscardFilesPanel.Children.Clear();
        _discardBoxes.Clear();
        TickAllBox.IsChecked = false;
        foreach (var file in files)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var box = new CheckBox { Content = new TextBlock { Text = file.Path, Style = (Style)FindResource("MonoText") }, Tag = file };
            AutomationProperties.SetName(box, string.Format(Strings.Fix_DiscardFileName, file.Path));
            box.Checked += (_, _) => UpdateDiscardButton();
            box.Unchecked += (_, _) => UpdateDiscardButton();
            var tag = UiHelpers.Tag(file.IsUntracked ? Strings.Save_FileNew : Strings.Save_FileModified);
            Grid.SetColumn(tag, 1);
            row.Children.Add(box); row.Children.Add(tag);
            DiscardFilesPanel.Children.Add(row);
            _discardBoxes.Add(box);
        }
        UpdateDiscardButton();
    }

    private IEnumerable<ChangedFile> TickedFiles => _discardBoxes.Where(b => b.IsChecked == true).Select(b => (ChangedFile)b.Tag);

    private void UpdateDiscardButton() => DiscardButton.IsEnabled = TickedFiles.Any();

    private void TickAll_Click(object sender, RoutedEventArgs e)
    {
        bool tick = TickAllBox.IsChecked == true;
        foreach (var box in _discardBoxes) box.IsChecked = tick;
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        var files = TickedFiles.ToList();
        if (files.Count == 0) return;
        var tracked = files.Where(f => !f.IsUntracked).Select(f => f.Path).ToList();
        var untracked = files.Where(f => f.IsUntracked).Select(f => f.Path).ToList();
        var shown = string.Join("\n", files.Select(f => f.Path).Take(25));
        if (files.Count > 25) shown += "\n" + string.Format(Strings.Save_AndMore, files.Count - 25);
        var message = string.Format(Strings.Fix_ConfirmDiscard, files.Count, shown);
        if (untracked.Count > 0) message += "\n\n" + string.Format(Strings.Fix_ConfirmDiscardNew, untracked.Count);
        if (!Shell.Confirm(message, Strings.Fix_Discard)) return;

        _ = Shell.RunAsync(async ct =>
        {
            await Workspace.Git.RestoreAsync(tracked, ct);
            await Workspace.Git.RemoveUntrackedAsync(untracked, ct);
            Shell.Announce(string.Format(Strings.Fix_DidDiscard, files.Count));
            await RefreshAsync(ct);
        });
    }

    // 3. Undo the last commit

    private void ShowUndo(string lastCommit)
    {
        // Only a commit that GitHub doesn't have yet: ahead of the upstream, or on a branch that was never pushed.
        bool hasOwnCommits = OnMain ? _aheadBehind.Ahead > 0 : (_status.HasUpstream ? _status.AheadOfUpstream > 0 : _aheadBehind.Ahead > 0);
        bool applies = hasOwnCommits && lastCommit.Length > 0 && !_mergeInProgress;
        UndoButton.IsEnabled = applies;
        UndoButton.Tag = lastCommit;
        UndoNote.Text = applies ? string.Format(Strings.Fix_UndoLast, lastCommit)
            : lastCommit.Length == 0 ? string.Format(Strings.Fix_UndoNoCommits, Repo.DefaultBranch)
            : _mergeInProgress ? Strings.Fix_UndoMerging
            : !OnMain && !_status.HasUpstream && _aheadBehind.Ahead == 0 ? string.Format(Strings.Fix_UndoNoCommits, Repo.DefaultBranch)
            : string.Format(Strings.Fix_UndoPushed, lastCommit);
        UndoNote.Foreground = UiHelpers.Brush(applies ? "TextSecondaryBrush" : "TextTertiaryBrush");
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        var subject = UndoButton.Tag as string ?? "";
        if (!Shell.Confirm(string.Format(Strings.Fix_ConfirmUndo, subject), Strings.Fix_Undo)) return;
        _ = Shell.RunAsync(async ct =>
        {
            await Workspace.Git.UndoLastCommitAsync(ct);
            Shell.Announce(Strings.Fix_DidUndo);
            await RefreshAsync(ct);
        });
    }

    // 4. Branch out of date with main

    private void ShowBehind()
    {
        bool behind = !OnMain && _aheadBehind.Behind > 0;
        bool blocked = !_status.IsClean;
        var lines = new List<string>();
        if (OnMain) lines.Add(string.Format(Strings.Fix_BehindOnMain, Repo.DefaultBranch));
        else if (!behind) lines.Add(string.Format(Strings.Fix_BehindUpToDate, Repo.DefaultBranch));
        else
        {
            lines.Add(string.Format(Strings.Fix_BehindCount, _aheadBehind.Behind, Repo.DefaultBranch));
            lines.Add(_pullRequest is not null ? string.Format(Strings.Fix_BehindWithPr, _pullRequest.Number) : Strings.Fix_BehindNoPr);
            if (blocked) lines.Add(Strings.Fix_BehindDirty);
        }
        BehindNote.Text = string.Join("\n", lines);
        BehindNote.Foreground = UiHelpers.Brush(behind && blocked ? "WarningBrush" : "TextTertiaryBrush");
        UpdateButton.Content = _pullRequest is not null ? Strings.Fix_UpdateWithPr : Strings.Fix_UpdateLocal;
        AutomationProperties.SetName(UpdateButton, (string)UpdateButton.Content);
        AutomationProperties.SetHelpText(UpdateButton, _pullRequest is not null ? Strings.Fix_UpdateWithPrHelp : Strings.Fix_UpdateLocalHelp);
        UpdateButton.IsEnabled = behind && !blocked;
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (!Shell.Confirm(string.Format(Strings.Fix_ConfirmUpdate, _status.Branch, Repo.DefaultBranch), (string)UpdateButton.Content)) return;
        var pr = _pullRequest;
        var branch = _status.Branch;
        _ = Shell.RunAsync(async ct =>
        {
            try
            {
                if (pr is not null)
                {
                    await Workspace.GitHub.UpdateBranchAsync(pr.Number, ct);
                    await Workspace.Git.PullAsync(ct);
                }
                else
                {
                    await Workspace.Git.MergeFromOriginAsync(Repo.DefaultBranch, ct);
                }
                Shell.Announce(string.Format(Strings.Fix_DidUpdate, branch));
            }
            catch (CommandFailedException ex) when (ex.Message.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase) || ex.Result.StandardOutput.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase))
            {
                Shell.Announce(Strings.Fix_UpdateConflict);
            }
            await RefreshAsync(ct);
        });
    }
}
