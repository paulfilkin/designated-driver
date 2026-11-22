using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DesignatedDriver.Core;
using DesignatedDriver.Helpers;
using DesignatedDriver.Resources;
using DesignatedDriver.Services;

namespace DesignatedDriver.Screens;

/// <summary>
/// The repositories the tool knows, with convention version, open pull requests and the last scan's verdict.
/// Add a clone, make one current, open its settings, scan it, or take it off the list.
/// </summary>
public partial class RepositoriesScreen : ScreenBase
{
    private readonly List<(RepositorySettings Repo, Grid Row)> _rows = [];
    private bool _loading;

    public RepositoriesScreen(IShell shell) : base(shell)
    {
        InitializeComponent();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = Shell.RunAsync(RefreshAsync);
    private void Filter_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void Starred_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Workspace.Settings.StarredOnly = StarredBox.IsChecked == true;
        Workspace.Settings.Save();
        ApplyFilter();
    }

    /// <summary>Shows the rows that match the text and, when ticked, the starred set; no command runs for this.</summary>
    private void ApplyFilter()
    {
        if (_rows.Count == 0 && ListPanel is null) return;
        var starred = Workspace.Starred ?? RepositoryFilter.StarredSet([]);
        bool starredOnly = StarredBox.IsChecked == true && Workspace.Starred is not null;
        var text = FilterBox.Text;
        int shown = 0;
        foreach (var (repo, row) in _rows)
        {
            bool visible = RepositoryFilter.Matches(repo, text, starredOnly, starred);
            row.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible) shown++;
        }
        NoMatchText.Visibility = _rows.Count > 0 && shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        FilterNote.Text = Workspace.StarredError.Length > 0 ? string.Format(Strings.Repos_StarredUnavailable, Workspace.StarredError)
                        : text.Trim().Length > 0 || starredOnly ? string.Format(Strings.Repos_Showing, shown, _rows.Count)
                        : "";
    }
    private void Add_Click(object sender, RoutedEventArgs e) => Shell.EditRepository(null);
    private void Scan_Click(object sender, RoutedEventArgs e) => Shell.Navigate(Screen.Scan);
    private void Wizard_Click(object sender, RoutedEventArgs e) => Shell.Navigate(Screen.NewRepository);

    protected override async Task LoadAsync(CancellationToken ct)
    {
        var repositories = Workspace.Settings.Repositories.ToList();
        ListPanel.Children.Clear();
        _rows.Clear();
        NoneText.Visibility = repositories.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // The starred set first, so the rows carry their star and the filter can apply before the slower counts arrive.
        var starred = await Workspace.ReadStarredAsync(ct);
        _loading = true;
        try
        {
            StarredBox.IsEnabled = starred is not null;
            StarredBox.IsChecked = starred is not null && Workspace.Settings.StarredOnly;
        }
        finally
        {
            _loading = false;
        }

        var details = new List<(RepositorySettings Repo, TextBlock Detail)>();
        foreach (var repo in repositories)
        {
            bool isCurrent = repo.Matches(Workspace.Settings.Current);
            var manifest = repo.HasFolder ? Manifest.Load(repo.Folder) : null;

            var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel();
            var titleLine = new WrapPanel();
            titleLine.Children.Add(new TextBlock { Text = repo.OwnerAndName, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            if (isCurrent)
            {
                var tag = UiHelpers.Tag(Strings.Repos_Current, "PrimarySofterBrush", "TertiaryBrush");
                tag.Margin = new Thickness(8, 0, 0, 0);
                titleLine.Children.Add(tag);
            }
            var convention = manifest is not null
                ? UiHelpers.Tag(string.Format(Strings.Repos_Convention, manifest.ConventionVersion), "SuccessSoftBrush", "SuccessBrush")
                : UiHelpers.Tag(Strings.Repos_NotSetUp);
            convention.Margin = new Thickness(8, 0, 0, 0);
            titleLine.Children.Add(convention);
            if (starred is not null && RepositoryFilter.IsStarred(repo, starred))
            {
                var star = UiHelpers.Tag("★ " + Strings.Repos_Starred, "PrimarySoftBrush", "TertiaryBrush");
                star.Margin = new Thickness(8, 0, 0, 0);
                titleLine.Children.Add(star);
            }
            text.Children.Add(titleLine);

            var folder = UiHelpers.Text(repo.HasFolder ? repo.Folder : $"{Strings.Repos_FolderMissing}: {repo.Folder}", "MonoText");
            folder.Foreground = UiHelpers.Brush(repo.HasFolder ? "TextSecondaryBrush" : "DangerBrush");
            folder.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(folder);

            var detail = new TextBlock { Style = (Style)FindResource("HelperText"), Margin = new Thickness(0, 2, 0, 0) };
            detail.Text = Strings.Repos_PullRequestsUnknown + "  ·  " + LastScanText(repo);
            text.Children.Add(detail);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var captured = repo;
            if (!isCurrent)
            {
                var make = UiHelpers.LinkButton(Strings.Repos_MakeCurrent, () => Workspace.SelectRepository(captured.OwnerAndName), string.Format(Strings.Repos_MakeCurrentFor, repo.OwnerAndName));
                make.Margin = new Thickness(0, 0, 12, 0);
                actions.Children.Add(make);
            }
            var settings = UiHelpers.LinkButton(Strings.Repos_Settings, () => Shell.EditRepository(captured), string.Format(Strings.Repos_SettingsFor, repo.OwnerAndName));
            settings.Margin = new Thickness(0, 0, 12, 0);
            actions.Children.Add(settings);
            var scan = UiHelpers.LinkButton(Strings.Repos_Scan, () => Shell.NavigateToScan(captured.HasFolder ? captured.Folder : null, captured.OwnerAndName), string.Format(Strings.Repos_ScanFor, repo.OwnerAndName));
            scan.Margin = new Thickness(0, 0, 12, 0);
            actions.Children.Add(scan);
            actions.Children.Add(UiHelpers.LinkButton(Strings.Repos_Remove, () => Remove(captured), string.Format(Strings.Repos_RemoveFor, repo.OwnerAndName)));

            Grid.SetColumn(actions, 1);
            row.Children.Add(text);
            row.Children.Add(actions);
            AutomationProperties.SetName(row, $"{repo.OwnerAndName}: {(convention.Child is TextBlock t ? t.Text : "")}");
            ListPanel.Children.Add(row);
            _rows.Add((repo, row));
            details.Add((repo, detail));
        }
        ApplyFilter();

        // Open pull requests, one gh call per repository; a failure (offline, no access) leaves the count unread.
        if (details.Count > 0 && (Workspace.IdentityKnown || await SignedInAsync(ct)))
        {
            foreach (var (repo, detail) in details)
            {
                try
                {
                    var open = await Workspace.GitHub.OpenPullRequestsAsync(repo.OwnerAndName, ct);
                    detail.Text = string.Format(Strings.Repos_OpenPullRequests, open.Count) + "  ·  " + LastScanText(repo);
                }
                catch (CommandFailedException) { }
            }
        }
    }

    private bool? _signedIn;

    private async Task<bool> SignedInAsync(CancellationToken ct)
    {
        if (_signedIn is null)
        {
            var auth = await Workspace.GitHub.AuthStatusAsync(ct);
            _signedIn = auth.SignedIn;
        }
        return _signedIn.Value;
    }

    /// <summary>The last Check this session, else the last scan, else nothing yet.</summary>
    private string LastScanText(RepositorySettings repo)
        => Workspace.LastChecks.TryGetValue(repo.OwnerAndName, out var check) ? string.Format(Strings.Repos_LastCheck, check.Report.Summary)
         : Workspace.LastScans.TryGetValue(repo.OwnerAndName, out var last) ? string.Format(Strings.Repos_LastScan, last.Report.Summary)
         : Strings.Repos_NotScanned;

    private void Remove(RepositorySettings repo)
    {
        if (!Shell.Confirm(string.Format(Strings.Repos_ConfirmRemove, repo.OwnerAndName), Strings.Repos_Remove)) return;
        Workspace.RemoveRepository(repo.OwnerAndName);
    }
}
