using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DesignatedDriver.Core;
using DesignatedDriver.Helpers;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;

namespace DesignatedDriver.Screens;

/// <summary>
/// Screen 5 (admins): open pull requests with their checks and merge state; for the chosen one its files,
/// approve, request changes, squash and merge, and the bypass merge that posts its reason first.
/// </summary>
public partial class ReviewScreen : ScreenBase
{
    private List<GhPullRequest> _pullRequests = [];
    private GhPullRequest? _selected;
    private int? _selectOnLoad;
    private int _shownNumber;
    private bool _rebuilding;

    public ReviewScreen(IShell shell) : base(shell)
    {
        InitializeComponent();
    }

    /// <summary>Selects this pull request on the next load, for a Review link on another screen.</summary>
    public void SelectOnLoad(int number) => _selectOnLoad = number;

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = Shell.RunAsync(RefreshAsync);
    private void OpenOnGitHub_Click(object sender, RoutedEventArgs e) { if (_selected is not null) Shell.OpenUrl(_selected.Url); }

    protected override async Task LoadAsync(CancellationToken ct)
    {
        int keep = _selectOnLoad ?? _selected?.Number ?? 0;
        _selectOnLoad = null;
        NotAdminNotice.Visibility = Workspace.IdentityKnown && !Workspace.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        NotAdminText.Text = string.Format(Strings.Review_NotAdmin, Workspace.Permission);

        // Read again while GitHub is still working a merge state out, as it is for a few seconds after a merge.
        _pullRequests = await Workspace.GitHub.OpenPullRequestsSettledAsync(Services.GitHubService.SettleDelay, ct);
        // Waiting for review first, then the rest, newest first within each group.
        _pullRequests = _pullRequests
            .OrderBy(p => p.IsDraft ? 2 : ReviewRules.CanMerge(p) ? 1 : 0)
            .ThenByDescending(p => p.Number)
            .ToList();

        // Rebuilding the list raises SelectionChanged; that is this load reselecting, not the user choosing.
        _rebuilding = true;
        try
        {
            PullRequestList.Items.Clear();
            foreach (var pr in _pullRequests) PullRequestList.Items.Add(BuildRow(pr));
            NoPullRequestsText.Visibility = _pullRequests.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var stale = ReviewRules.NeedingUpdate(_pullRequests);
            NeedsUpdateText.Text = string.Format(Strings.Review_NeedsUpdate, stale.Count, Repo.DefaultBranch, string.Join(", ", stale.Select(p => $"#{p.Number}")));
            NeedsUpdateText.Visibility = stale.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            var again = _pullRequests.FirstOrDefault(p => p.Number == keep);
            if (again is not null) PullRequestList.SelectedIndex = _pullRequests.IndexOf(again);
            else ShowDetail(null);
            _selected = again;
        }
        finally
        {
            _rebuilding = false;
        }
        if (_selected is not null) await ShowDetailAsync(_selected, ct);
    }

    private ListBoxItem BuildRow(GhPullRequest pr)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = $"#{pr.Number}  {pr.Title}", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = string.Format(Strings.Review_By, pr.Author?.Login ?? "", pr.HeadRefName), Style = (Style)FindResource("HelperText"), Margin = new Thickness(0, 2, 0, 0) });

        var tags = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        foreach (var tag in TagsFor(pr)) { tag.Margin = new Thickness(4, 2, 0, 2); tags.Children.Add(tag); }

        Grid.SetColumn(tags, 1);
        row.Children.Add(text); row.Children.Add(tags);
        var item = new ListBoxItem { Content = row, Tag = pr, Padding = new Thickness(8, 6, 8, 6) };
        AutomationProperties.SetName(item, $"#{pr.Number} {pr.Title}, {pr.Author?.Login}, {StateText(pr)}");
        return item;
    }

    private IEnumerable<Border> TagsFor(GhPullRequest pr)
    {
        if (pr.IsDraft) yield return UiHelpers.Tag(Strings.Review_Draft);
        if (ReviewRules.IsOwn(pr, Workspace.Login)) yield return UiHelpers.Tag(Strings.Review_Own, "PrimarySofterBrush", "TertiaryBrush");
        yield return UiHelpers.ChecksTag(pr.ChecksBucket);
        yield return UiHelpers.ReviewTag(pr.ReviewDecision);
        yield return StateTag(pr);
    }

    private static Border StateTag(GhPullRequest pr) => pr.MergeStateStatus.ToUpperInvariant() switch
    {
        "CLEAN" => UiHelpers.Tag(Strings.Review_State_Clean, "SuccessSoftBrush", "SuccessBrush"),
        "BEHIND" => UiHelpers.Tag(Strings.Review_State_Behind, "QuaternarySofterBrush", "NeutralMutedBrush"),
        "DIRTY" => UiHelpers.Tag(Strings.Review_State_Dirty, "DangerSoftBrush", "DangerBrush"),
        "BLOCKED" => UiHelpers.Tag(Strings.Review_State_Blocked, "QuaternarySofterBrush", "NeutralMutedBrush"),
        "UNSTABLE" => UiHelpers.Tag(Strings.Review_State_Unstable, "DangerSoftBrush", "DangerBrush"),
        "DRAFT" => UiHelpers.Tag(Strings.Review_Draft),
        "" or "UNKNOWN" => UiHelpers.Tag(Strings.Review_State_Computing, "QuaternarySofterBrush", "NeutralMutedBrush"),
        _ => UiHelpers.Tag(Strings.Review_State_Unknown),
    };

    private static string StateText(GhPullRequest pr) => ReviewRules.Blocker(pr) switch
    {
        MergeBlocker.None => Strings.Review_Why_None,
        MergeBlocker.Draft => Strings.Review_Why_Draft,
        MergeBlocker.NotApproved => Strings.Review_Why_NotApproved,
        MergeBlocker.ChangesRequested => Strings.Review_Why_ChangesRequested,
        MergeBlocker.ChecksFailed => Strings.Review_Why_ChecksFailed,
        MergeBlocker.ChecksRunning => Strings.Review_Why_ChecksRunning,
        MergeBlocker.Conflicts => Strings.Review_Why_Conflicts,
        MergeBlocker.Behind => Strings.Review_Why_Behind,
        MergeBlocker.Computing => Strings.Review_Why_Computing,
        _ => Strings.Review_Why_Blocked,
    };

    /// <summary>
    /// Reads the pull request again just before a merge and stops when main has moved under it: the buttons were
    /// set from an earlier read, and a merge on a stale state is what gh would otherwise refuse, or worse, do.
    /// </summary>
    private async Task<bool> StillMergeableAsync(GhPullRequest pr, bool bypass, CancellationToken ct)
    {
        var fresh = await Workspace.GitHub.SettledPullRequestAsync(pr.Number, Services.GitHubService.SettleDelay, ct);
        if (ReviewRules.MayMerge(fresh, bypass)) return true;
        var why = StateText(fresh);
        Shell.Announce(string.Format(Strings.Review_StateChanged, pr.Number, why));
        await RefreshAsync(ct);
        Shell.ShowError(string.Format(Strings.Review_StateChanged, pr.Number, why));
        return false;
    }

    private void PullRequestList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rebuilding) return;
        if (PullRequestList.SelectedItem is not ListBoxItem { Tag: GhPullRequest pr }) return;
        if (ReferenceEquals(pr, _selected)) return;
        // Only a load: a newer pick takes its place rather than being turned away.
        _ = Shell.RunPickAsync(ct => ShowDetailAsync(pr, ct));
    }

    private async Task ShowDetailAsync(GhPullRequest pr, CancellationToken ct)
    {
        ShowDetail(pr);
        var withFiles = await Workspace.GitHub.PullRequestFilesAsync(pr.Number, ct);
        if (!ReferenceEquals(_selected, pr)) return;
        FilesPanel.Children.Clear();
        foreach (var file in withFiles.Files)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var path = UiHelpers.Text(file.Path, "MonoText");
            var counts = new TextBlock { Text = string.Format(Strings.Review_FileCounts, file.Additions, file.Deletions), Style = (Style)FindResource("HelperText"), Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(counts, 1);
            row.Children.Add(path); row.Children.Add(counts);
            AutomationProperties.SetName(row, $"{file.Path} {counts.Text}");
            FilesPanel.Children.Add(row);
        }
        FilesSummary.Text = string.Format(Strings.Review_FilesCount, withFiles.Files.Count, withFiles.Files.Sum(f => f.Additions), withFiles.Files.Sum(f => f.Deletions));
    }

    /// <summary>Fills the detail card from what the list already knows; the files come afterwards.</summary>
    private void ShowDetail(GhPullRequest? pr)
    {
        _selected = pr;
        DetailCard.Visibility = pr is null ? Visibility.Collapsed : Visibility.Visible;
        SelectPromptText.Visibility = pr is null && _pullRequests.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (pr is null) return;

        bool own = ReviewRules.IsOwn(pr, Workspace.Login);
        DetailTitle.Text = $"#{pr.Number}  {pr.Title}";
        DetailTags.Children.Clear();
        foreach (var tag in TagsFor(pr)) { tag.Margin = new Thickness(0, 2, 6, 2); DetailTags.Children.Add(tag); }
        WhyText.Text = StateText(pr);
        WhyText.Foreground = UiHelpers.Brush(ReviewRules.CanMerge(pr) ? "SuccessBrush" : "TextSecondaryBrush");
        FilesPanel.Children.Clear();
        FilesSummary.Text = Strings.Review_FilesLoading;

        OwnNote.Visibility = own && !pr.IsDraft ? Visibility.Visible : Visibility.Collapsed;
        // A reload of the same pull request keeps what the user has typed; a different one starts clean.
        if (pr.Number != _shownNumber)
        {
            CommentBox.Clear();
            ReasonBox.Clear();
        }
        _shownNumber = pr.Number;
        ApproveButton.IsEnabled = ReviewRules.CanApprove(pr, Workspace.Login);
        UpdateCommentButtons();
        MergeButton.IsEnabled = ReviewRules.CanMerge(pr);
        UpdateBranchButton.Visibility = ReviewRules.OffersUpdateBranch(pr) ? Visibility.Visible : Visibility.Collapsed;
        BypassPanel.Visibility = ReviewRules.OffersBypass(pr, Workspace.Login) ? Visibility.Visible : Visibility.Collapsed;
        BypassOwnText.Visibility = own ? Visibility.Visible : Visibility.Collapsed;
        UpdateBypassButton();
    }

    private void CommentBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateCommentButtons();
    private void ReasonBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateBypassButton();

    private void UpdateCommentButtons()
        => RequestChangesButton.IsEnabled = _selected is not null && ReviewRules.CanRequestChanges(_selected, Workspace.Login) && CommentBox.Text.Trim().Length > 0;

    private void UpdateBypassButton() => BypassButton.IsEnabled = _selected is not null && ReasonBox.Text.Trim().Length > 0;

    // Actions

    /// <summary>
    /// Review's confirmation for an action on a pull request: the repository across the top in large type, the
    /// action in words, a button that names the repository, and for a merge the name typed first unless the
    /// repository's settings turn that off (issue 66). True only when the named button was pressed.
    /// </summary>
    private bool ConfirmAction(ReviewAction action, string actionText)
    {
        var notes = new List<string>();
        if (Workspace.Login.Length > 0 && Repo.Owner.Length > 0 && !Repo.Owner.Equals(Workspace.Login, StringComparison.OrdinalIgnoreCase))
            notes.Add(string.Format(Strings.Confirm_OwnedBy, Repo.Owner));
        if (Repo.KeepsOwnConventions) notes.Add(Strings.Confirm_KeepsOwn);
        var window = new Windows.ConfirmActionWindow(action, Repo.OwnerAndName, actionText, string.Join("  ", notes),
            ReviewConfirm.NeedsTypedName(action, Repo.TypeNameToMerge));
        return Shell.ShowDialog(window);
    }

    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } pr) return;
        if (!ConfirmAction(ReviewAction.Approve, string.Format(Strings.Review_ConfirmApprove, pr.Number, pr.Title))) return;
        _ = Shell.RunAsync(async ct =>
        {
            await Workspace.GitHub.ApproveAsync(pr.Number, ct);
            Shell.Announce(string.Format(Strings.Review_DidApprove, pr.Number));
            await RefreshAsync(ct);
        });
    }

    private void RequestChanges_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } pr) return;
        var comment = CommentBox.Text.Trim();
        if (comment.Length == 0) { Shell.ShowError(Strings.Review_CommentNeeded); return; }
        if (!ConfirmAction(ReviewAction.RequestChanges, string.Format(Strings.Review_ConfirmRequestChanges, pr.Number, comment))) return;
        _ = Shell.RunAsync(async ct =>
        {
            await Workspace.GitHub.RequestChangesAsync(pr.Number, comment, ct);
            Shell.Announce(string.Format(Strings.Review_DidRequestChanges, pr.Number));
            await RefreshAsync(ct);
        });
    }

    private void Merge_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } pr) return;
        if (!ConfirmAction(ReviewAction.Merge, string.Format(Strings.Review_ConfirmMerge, pr.Number, pr.Title, Repo.DefaultBranch, pr.HeadRefName))) return;
        _ = Shell.RunAsync(async ct =>
        {
            if (!await StillMergeableAsync(pr, bypass: false, ct)) return;
            await Workspace.GitHub.MergeAsync(pr.Number, ct);
            Shell.Announce(string.Format(Strings.Review_DidMerge, pr.Number));
            _selected = null;
            await RefreshAsync(ct);
        });
    }

    private void UpdateBranch_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } pr) return;
        _ = Shell.RunAsync(async ct =>
        {
            await Workspace.GitHub.UpdateBranchAsync(pr.Number, ct);
            Shell.Announce(string.Format(Strings.Review_DidUpdateBranch, pr.Number));
            await RefreshAsync(ct);
        });
    }

    private void Bypass_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } pr) return;
        var reason = ReasonBox.Text.Trim();
        if (reason.Length == 0) { Shell.ShowError(Strings.Review_ReasonNeeded); return; }
        if (!ConfirmAction(ReviewAction.Bypass, string.Format(Strings.Review_ConfirmBypass, pr.Number, reason, pr.HeadRefName))) return;
        _ = Shell.RunAsync(async ct =>
        {
            if (!await StillMergeableAsync(pr, bypass: true, ct)) return;
            await Workspace.GitHub.BypassMergeAsync(pr.Number, reason, ct);
            Shell.Announce(string.Format(Strings.Review_DidBypass, pr.Number));
            _selected = null;
            await RefreshAsync(ct);
        });
    }
}
