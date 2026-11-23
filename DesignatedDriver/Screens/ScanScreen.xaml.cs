using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using DesignatedDriver.Core;
using DesignatedDriver.Helpers;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;
using DesignatedDriver.Services;

namespace DesignatedDriver.Screens;

/// <summary>
/// Scan: a read-only assessment of a repository against the convention. The target is the current repository,
/// any folder, or a repository on GitHub by owner and name. Nothing is changed.
/// </summary>
public partial class ScanScreen : ScreenBase
{
    private ScanFacts? _facts;
    private ScanReport? _report;
    private DateTime _when;
    /// <summary>How the last scan's repository can be taken on as it is, and whether that marks it as keeping its own conventions.</summary>
    private AsItIsRoute _asItIs;
    private bool _keepsOwn;

    public ScanScreen(IShell shell) : base(shell)
    {
        InitializeComponent();
        CurrentOption.IsChecked = true;
    }

    /// <summary>The result stays until the user scans again; coming back to the window doesn't rerun it.</summary>
    public override bool ReloadsOnReturn => false;

    protected override Task LoadAsync(CancellationToken ct)
    {
        CurrentOption.Content = string.Format(Strings.Scan_TargetCurrent, Repo.OwnerAndName.Length > 0 ? Repo.OwnerAndName : Strings.Header_NoRepository);
        CurrentOption.IsEnabled = Repo.HasFolder || Repo.OwnerAndName.Length > 0;
        if (!CurrentOption.IsEnabled && CurrentOption.IsChecked == true) FolderOption.IsChecked = true;
        UpdateScanButton();
        return Task.CompletedTask;
    }

    /// <summary>Chooses a target from another screen and scans it.</summary>
    public void ScanTarget(string? folder, string ownerAndName)
    {
        if (folder is not null) { FolderBox.Text = folder; FolderOption.IsChecked = true; }
        else { NameBox.Text = ownerAndName; NameOption.IsChecked = true; }
        Scan_Click(this, new RoutedEventArgs());
    }

    private void Target_Changed(object sender, RoutedEventArgs e) => UpdateScanButton();
    private void FolderBox_TextChanged(object sender, TextChangedEventArgs e) { FolderOption.IsChecked = true; UpdateScanButton(); }
    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) { NameOption.IsChecked = true; UpdateScanButton(); }

    private void UpdateScanButton()
    {
        if (ScanButton is null) return;
        ScanButton.IsEnabled = CurrentOption.IsChecked == true ? CurrentOption.IsEnabled
            : FolderOption.IsChecked == true ? FolderBox.Text.Trim().Length > 0
            : NameBox.Text.Trim().Length > 0;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Strings.Settings_BrowseTitle,
            Multiselect = false,
            InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (dialog.ShowDialog(Shell.Window) == true) FolderBox.Text = dialog.FolderName;
    }

    private void Scan_Click(object sender, RoutedEventArgs e)
    {
        string? folder = null;
        string name = "";
        if (CurrentOption.IsChecked == true)
        {
            folder = Repo.HasFolder ? Repo.Folder : null;
            name = Repo.OwnerAndName;
        }
        else if (FolderOption.IsChecked == true)
        {
            folder = FolderBox.Text.Trim();
            if (folder.Length == 0) { Shell.ShowError(Strings.Scan_NoTarget); return; }
            if (!Directory.Exists(folder)) { Shell.ShowError(Strings.Settings_FolderMissing); return; }
        }
        else
        {
            name = NameBox.Text.Trim();
            if (name.Length == 0) { Shell.ShowError(Strings.Scan_NoTarget); return; }
            if (RemoteUrl.ParseOwnerAndRepository("https://github.com/" + name) is not { } parsed || name.Count(c => c == '/') != 1) { Shell.ShowError(Strings.Scan_BadName); return; }
            name = $"{parsed.Owner}/{parsed.Repository}";
        }

        _ = Shell.RunAsync(async ct =>
        {
            var facts = await new ScanService(Workspace).ScanAsync(folder, name, ct);
            var report = ScanRules.Assess(facts);
            _facts = facts;
            _report = report;
            _when = DateTime.Now;
            if (facts.OwnerAndName.Length > 0) Workspace.LastScans[facts.OwnerAndName] = (report, _when);
            ShowReport(facts, report);
            Shell.Announce(string.Format(Strings.Scan_Done, report.Summary));
        });
    }

    private void ShowReport(ScanFacts facts, ScanReport report)
    {
        ResultCard.Visibility = Visibility.Visible;
        var what = facts.OwnerAndName.Length > 0 ? facts.OwnerAndName : facts.Folder;
        ScannedText.Text = string.Format(Strings.Scan_ScannedAt, what, _when.ToString("g"));

        VerdictText.Text = ScanReportText.VerdictTitle(report.Verdict);
        SummaryText.Text = report.Summary;
        VerdictNotice.Style = (Style)FindResource(report.Verdict switch
        {
            ScanVerdict.NotConvertible => "DangerNoticeStyle",
            ScanVerdict.AlreadySetUp => "SuccessNoticeStyle",
            _ => "NoticeStyle",
        });

        // The next step in words, then as buttons with a line each. A convertible repository can take the convention or
        // be kept as it is: its clone added, the repository cloned, or its entry marked, each marking it as keeping its
        // own conventions. Other verdicts add or clone it plainly; one already set up and current goes straight to Check.
        bool known = facts.OwnerAndName.Length > 0 && Workspace.Settings.Find(facts.OwnerAndName) is not null;
        bool current = facts.OwnerAndName.Length > 0 && Repo.Matches(facts.OwnerAndName);
        _asItIs = ScanRules.AsItIs(facts, report.Verdict, known);
        _keepsOwn = ScanRules.KeepsOwnAsItIs(report.Verdict);
        bool convertible = report.Verdict == ScanVerdict.Convertible;
        bool canKeep = convertible && _asItIs != AsItIsRoute.None;
        // A repository that already runs its own way has Keep it as it is recommended, with the reason (issue 75).
        string keepReason = canKeep ? ScanReportText.KeepReason(report) : "";
        NextStepText.Text = ScanReportText.NextStepForName(
            ScanReportText.NextStep(report.Verdict, current, canKeepAsIs: canKeep, keepReason: keepReason), facts, _asItIs);
        RecommendKeep(keepReason.Length > 0);
        // A scan by name that found a clone beside the known ones offers scanning that folder first.
        bool nearby = !facts.HasClone && facts.NearbyClone.Length > 0;
        ScanCloneChoice.Visibility = nearby ? Visibility.Visible : Visibility.Collapsed;
        ScanCloneText.Text = nearby ? string.Format(Strings.Scan_ScanNearbyCloneHelp, facts.NearbyClone) : "";
        AutomationProperties.SetHelpText(ScanCloneButton, ScanCloneText.Text);
        SetUpChoice.Visibility = convertible ? Visibility.Visible : Visibility.Collapsed;
        OpenCheckChoice.Visibility = report.Verdict == ScanVerdict.AlreadySetUp && current ? Visibility.Visible : Visibility.Collapsed;
        ShowAsItIs();
        ActionRow.Visibility = ScanCloneChoice.Visibility == Visibility.Visible || SetUpChoice.Visibility == Visibility.Visible
            || OpenCheckChoice.Visibility == Visibility.Visible || AsItIsChoice.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;

        FindingsPanel.Children.Clear();
        foreach (var severity in Enum.GetValues<ScanSeverity>())
        {
            var group = report.Of(severity).ToList();
            if (group.Count == 0) continue;
            var heading = new TextBlock { Text = ScanReportText.GroupTitle(severity, report.Verdict), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4), Foreground = UiHelpers.Brush(GroupBrush(severity)) };
            FindingsPanel.Children.Add(heading);
            foreach (var finding in group) FindingsPanel.Children.Add(FindingRow(finding, report.Verdict));
        }
    }

    /// <summary>
    /// When Keep it as it is is recommended, its choice comes before Apply the convention and is the main button, with
    /// Apply the convention as the secondary one; otherwise the order and styles are as the XAML has them.
    /// </summary>
    private void RecommendKeep(bool recommend)
    {
        ActionRow.Children.Remove(AsItIsChoice);
        int at = ActionRow.Children.IndexOf(recommend ? SetUpChoice : OpenCheckChoice);
        ActionRow.Children.Insert(recommend ? at : at + 1, AsItIsChoice);
        AsItIsButton.Style = (Style)FindResource(recommend ? "ModernButton" : "SecondaryButton");
        SetUpButton.Style = (Style)FindResource(recommend ? "SecondaryButton" : "ModernButton");
    }

    /// <summary>The "as it is" button for the route the last scan found, with its label and line, or hidden when there is none.</summary>
    private void ShowAsItIs()
    {
        var (label, help) = ScanReportText.AsItIsButton(_asItIs, _keepsOwn);
        AsItIsChoice.Visibility = _asItIs == AsItIsRoute.None ? Visibility.Collapsed : Visibility.Visible;
        AsItIsButton.Content = label;
        AsItIsText.Text = help;
        AutomationProperties.SetName(AsItIsButton, label);
        AutomationProperties.SetHelpText(AsItIsButton, help);
    }

    private FrameworkElement FindingRow(ScanFinding finding, ScanVerdict verdict)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var mark = UiHelpers.Mark(finding.Severity == ScanSeverity.Ok);
        mark.VerticalAlignment = VerticalAlignment.Top;
        switch (finding.Severity)
        {
            case ScanSeverity.Blocker: break;
            case ScanSeverity.Warning: mark.Text = "!"; mark.Foreground = UiHelpers.Brush("WarningBrush"); break;
            case ScanSeverity.Change: mark.Text = "→"; mark.Foreground = UiHelpers.Brush("PrimaryStrongBrush"); break;
            case ScanSeverity.NotAvailable: mark.Text = "–"; mark.Foreground = UiHelpers.Brush("TextTertiaryBrush"); break;
        }

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = finding.Text, TextWrapping = TextWrapping.Wrap });
        if (finding.Detail.Length > 0)
            text.Children.Add(new TextBlock { Text = finding.Detail, Style = (Style)FindResource("MonoText"), Foreground = UiHelpers.Brush("TextSecondaryBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        if (finding.Step != SetupStep.None)
            text.Children.Add(new TextBlock { Text = string.Format(Strings.Scan_Step, ScanReportText.StepTitle(finding.Step)), Style = (Style)FindResource("HelperText"), Margin = new Thickness(0, 1, 0, 0) });

        Grid.SetColumn(text, 1);
        row.Children.Add(mark);
        row.Children.Add(text);
        AutomationProperties.SetName(row, $"{ScanReportText.GroupTitle(finding.Severity, verdict)}: {finding.Text}");
        return row;
    }

    private static string GroupBrush(ScanSeverity severity) => severity switch
    {
        ScanSeverity.Blocker => "DangerBrush",
        ScanSeverity.Warning => "WarningBrush",
        ScanSeverity.Change => "PrimaryStrongBrush",
        ScanSeverity.NotAvailable => "TextTertiaryBrush",
        _ => "SuccessBrush",
    };

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_facts is null || _report is null) return;
        UiHelpers.CopyToClipboard(ScanReportText.Render(_facts, _report, _when));
    }

    private void SetUp_Click(object sender, RoutedEventArgs e)
    {
        if (_facts is null) return;
        Shell.NavigateToSetup(_facts.HasClone ? _facts.Folder : null, _facts.OwnerAndName, _facts);
    }

    private void OpenCheck_Click(object sender, RoutedEventArgs e) => Shell.Navigate(Screen.Check);

    /// <summary>Scans the clone found beside the known ones, by its folder.</summary>
    private void ScanClone_Click(object sender, RoutedEventArgs e)
    {
        if (_facts is null || _facts.NearbyClone.Length == 0) return;
        ScanTarget(_facts.NearbyClone, "");
    }

    private void AsItIs_Click(object sender, RoutedEventArgs e)
    {
        switch (_asItIs)
        {
            case AsItIsRoute.Add: Add(); break;
            case AsItIsRoute.Clone: Clone(); break;
            case AsItIsRoute.Mark: Mark(); break;
        }
    }

    /// <summary>Opens the clone card on Repositories; from a convertible scan the clone is marked as keeping its own conventions.</summary>
    private void Clone()
    {
        if (_facts is null || _facts.OwnerAndName.Length == 0) return;
        Shell.NavigateToClone(_facts.OwnerAndName, _keepsOwn);
    }

    /// <summary>Adds the scanned clone to the list and makes it current; from a convertible scan, marked as keeping its own conventions.</summary>
    private void Add()
    {
        if (_facts is null || !_facts.HasClone) return;
        var parsed = RemoteUrl.ParseOwnerAndRepository(_facts.OriginUrl);
        if (parsed is null) return;
        var added = new RepositorySettings
        {
            Folder = _facts.Folder,
            Owner = parsed.Value.Owner,
            Name = parsed.Value.Repository,
            DefaultBranch = _facts.DefaultBranch,
            SelfTestCommand = _facts.Manifest?.SelfTestCommand ?? "",
            RequiredCheck = string.IsNullOrEmpty(_facts.Manifest?.RequiredCheck) ? RepositorySettings.DefaultRequiredCheck : _facts.Manifest!.RequiredCheck,
            KeepsOwnConventions = _keepsOwn && _facts.Manifest is null,
        };
        Workspace.SaveRepository(added);
        _asItIs = AsItIsRoute.None;
        ShowAsItIs();
        // Without a manifest, Settings opens with what the repository already uses read from GitHub, to confirm.
        if (_facts.Manifest is null) Shell.EditRepository(added, readFromGitHub: true);
        Rescan(added.OwnerAndName);
    }

    /// <summary>Marks the entry already in the list as keeping its own conventions, without making it current, and opens its Settings.</summary>
    private void Mark()
    {
        if (_facts is null || Workspace.Settings.Find(_facts.OwnerAndName) is not { } entry) return;
        entry.KeepsOwnConventions = true;
        Workspace.Settings.Save();
        _asItIs = AsItIsRoute.None;
        ShowAsItIs();
        Shell.EditRepository(entry);
        Rescan(entry.OwnerAndName);
    }

    /// <summary>Scans the same target again once Settings has closed, so the verdict shows what the entry now says.</summary>
    private void Rescan(string ownerAndName)
    {
        if (_facts is null) return;
        ScanTarget(_facts.HasClone ? _facts.Folder : null, ownerAndName);
    }
}
