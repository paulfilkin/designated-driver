using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DesignatedDriver.Helpers;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;

namespace DesignatedDriver.Screens;

/// <summary>Screen 6: back to main and pull, delete local branches whose pull requests are merged, prune.</summary>
public partial class TidyUpScreen : ScreenBase
{
    private GitStatus _status = new();
    private readonly List<CheckBox> _branchBoxes = [];

    public TidyUpScreen(IShell shell) : base(shell)
    {
        InitializeComponent();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = Shell.RunAsync(RefreshAsync);

    protected override async Task LoadAsync(CancellationToken ct)
    {
        _status = await Workspace.Git.StatusAsync(ct);
        bool onMain = _status.Branch == Repo.DefaultBranch;
        MainStatusText.Text = onMain
            ? string.Format(Strings.Tidy_OnMain, Repo.DefaultBranch, _status.BehindUpstream)
            : string.Format(Strings.Tidy_OnBranch, _status.Branch, Repo.DefaultBranch);
        if (!_status.IsClean) MainStatusText.Text += "\n" + string.Format(Strings.Tidy_Uncommitted, _status.Changes.Count);
        SwitchButton.IsEnabled = _status.IsClean;

        var local = (await Workspace.Git.LocalBranchesAsync(ct)).Where(b => b != Repo.DefaultBranch).ToList();
        var merged = new HashSet<string>(await Workspace.GitHub.MergedBranchesAsync(ct), StringComparer.Ordinal);
        var open = new HashSet<string>((await Workspace.GitHub.MyPullRequestsAsync(ct)).Select(p => p.HeadRefName), StringComparer.Ordinal);

        BranchesPanel.Children.Clear();
        _branchBoxes.Clear();
        foreach (var branch in local)
        {
            bool isMerged = merged.Contains(branch);
            bool isCurrent = branch == _status.Branch;
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var box = new CheckBox
            {
                Content = new TextBlock { Text = branch, Style = (Style)FindResource("MonoText") },
                IsChecked = isMerged && !isCurrent,
                IsEnabled = !isCurrent,
                Tag = branch,
            };
            AutomationProperties.SetName(box, string.Format(Strings.Tidy_DeleteBranchName, branch));
            box.Checked += (_, _) => UpdateDeleteButton();
            box.Unchecked += (_, _) => UpdateDeleteButton();

            var tag = isCurrent ? UiHelpers.Tag(Strings.Tidy_Current, "InfoSoftBrush", "TertiaryBrush")
                    : isMerged ? UiHelpers.Tag(Strings.Tidy_Merged, "SuccessSoftBrush", "SuccessBrush")
                    : open.Contains(branch) ? UiHelpers.Tag(Strings.Tidy_OpenPullRequest, "QuaternarySofterBrush", "NeutralMutedBrush")
                    : UiHelpers.Tag(Strings.Tidy_NoPullRequest);
            Grid.SetColumn(tag, 1);
            row.Children.Add(box); row.Children.Add(tag);
            BranchesPanel.Children.Add(row);
            _branchBoxes.Add(box);
        }
        NoBranchesText.Visibility = local.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDeleteButton();
    }

    private IEnumerable<string> Ticked => _branchBoxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag);

    private void UpdateDeleteButton() => DeleteButton.IsEnabled = Ticked.Any();

    private void SwitchAndPull_Click(object sender, RoutedEventArgs e)
    {
        _ = Shell.RunAsync(async ct =>
        {
            if (_status.Branch != Repo.DefaultBranch) await Workspace.Git.SwitchAsync(Repo.DefaultBranch, ct);
            await Workspace.Git.PullAsync(ct);
            Shell.Announce(string.Format(Strings.Tidy_SwitchedAndPulled, Repo.DefaultBranch));
            await RefreshAsync(ct);
        });
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var branches = Ticked.ToList();
        if (branches.Count == 0) return;
        var message = string.Format(Strings.Tidy_ConfirmDelete, branches.Count, string.Join("\n", branches));
        if (!Shell.Confirm(message, Strings.Tidy_DeleteTicked)) return;

        _ = Shell.RunAsync(async ct =>
        {
            foreach (var branch in branches) await Workspace.Git.DeleteBranchAsync(branch, ct);
            await Workspace.Git.PruneAsync(ct);
            Shell.Announce(string.Format(Strings.Tidy_Deleted, branches.Count));
            await RefreshAsync(ct);
        });
    }

    private void Prune_Click(object sender, RoutedEventArgs e)
    {
        _ = Shell.RunAsync(async ct =>
        {
            await Workspace.Git.PruneAsync(ct);
            Shell.Announce(Strings.Tidy_Pruned);
            await RefreshAsync(ct);
        });
    }
}
