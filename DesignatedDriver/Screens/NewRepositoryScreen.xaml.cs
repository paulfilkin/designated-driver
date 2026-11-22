using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using DesignatedDriver.Core;
using DesignatedDriver.Helpers;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;
using DesignatedDriver.Services;
using DesignatedDriver.Windows;

namespace DesignatedDriver.Screens;

/// <summary>
/// The New repository wizard (section 5 of the design): Where, Options, Create. An existing folder or repository
/// is scanned on the first page; a blocker stops the wizard there. The third page is the plan as a checklist
/// that ticks as it runs, and can be rerun after a failure.
/// </summary>
public partial class NewRepositoryScreen : ScreenBase
{
    private readonly SetupService _setup;
    private int _page;
    private ScanFacts? _facts;
    private SetupPlan? _plan;
    private readonly Dictionary<PlanStep, (TextBlock Mark, TextBlock Message)> _rows = [];
    private bool _running;

    private static readonly string[] Languages = [ProjectLanguage.DotNet, ProjectLanguage.Node, ProjectLanguage.Python, ProjectLanguage.Go, ProjectLanguage.Rust, ProjectLanguage.Java, ProjectLanguage.Other];

    public NewRepositoryScreen(IShell shell) : base(shell)
    {
        InitializeComponent();
        _setup = new SetupService(Workspace);
        _setup.StepChanged += (_, step) => Dispatcher.BeginInvoke(() => ShowStep(step));

        foreach (var language in Languages)
        {
            var item = new ComboBoxItem { Content = ProjectLanguage.DisplayName(language), Tag = language };
            AutomationProperties.SetName(item, ProjectLanguage.DisplayName(language));
            LanguageCombo.Items.Add(item);
        }
        LanguageCombo.SelectedIndex = 0;

        foreach (var item in ChecklistItems.All)
        {
            var box = new CheckBox { Content = item.Text, Tag = item.Id, IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
            AutomationProperties.SetName(box, item.Text);
            ChecklistPanel.Children.Add(box);
        }
        ShowPage(0);
    }

    /// <summary>The answers and the plan's progress stay until the user starts over.</summary>
    public override bool ReloadsOnReturn => false;

    protected override async Task LoadAsync(CancellationToken ct)
    {
        if (OwnerBox.Text.Trim().Length == 0)
        {
            if (!Workspace.IdentityKnown) await Workspace.RefreshIdentityAsync(ct);
            OwnerBox.Text = Workspace.Login;
        }
        if (ParentBox.Text.Trim().Length == 0) ParentBox.Text = DefaultParentFolder();
        if (!_transportShown)
        {
            _transportShown = true;
            var (protocol, host) = await Workspace.ResolveTransportAsync(OwnerBox.Text.Trim(), ct);
            if (protocol.Equals(RemoteUrl.Ssh, StringComparison.OrdinalIgnoreCase)) SshOption.IsChecked = true; else HttpsOption.IsChecked = true;
            SshHostBox.Text = host;
            Protocol_Changed(this, new RoutedEventArgs());
        }
    }

    private bool _transportShown;

    private void Protocol_Changed(object sender, RoutedEventArgs e)
    {
        if (SshHostPanel is null) return;
        SshHostPanel.Visibility = SshOption.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private string DefaultParentFolder()
    {
        if (Repo.HasFolder && Path.GetDirectoryName(Repo.Folder) is { Length: > 0 } parent) return parent;
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    /// <summary>
    /// From Scan or Repositories: an existing folder, or an existing repository by owner and name. Scan passes what
    /// it found, so the first page shows the visibility, description, language and project before Next scans again.
    /// </summary>
    public void Prefill(string? folder, string ownerAndName, ScanFacts? facts = null)
    {
        if (_running) return;
        StartOver();
        if (folder is not null) { FolderBox.Text = folder; FolderOption.IsChecked = true; }
        else RemoteOption.IsChecked = true;
        var parts = ownerAndName.Split('/');
        if (parts.Length == 2) { OwnerBox.Text = parts[0]; NameBox.Text = parts[1]; }
        // After the source is chosen: choosing it clears the facts.
        if (facts is not null)
        {
            _facts = facts;
            TakeFromScan(facts);
        }
        ApplyVisibility();
    }

    // Page 1

    private SetupSource Source => FolderOption.IsChecked == true ? SetupSource.ExistingFolder : RemoteOption.IsChecked == true ? SetupSource.ExistingRepository : SetupSource.NewRepository;

    private string ChosenLanguage => (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? ProjectLanguage.DotNet;

    private void Source_Changed(object sender, RoutedEventArgs e)
    {
        if (ParentPanel is null) return;
        var source = Source;
        ParentPanel.Visibility = source == SetupSource.ExistingFolder ? Visibility.Collapsed : Visibility.Visible;
        FolderBox.IsEnabled = source == SetupSource.ExistingFolder;
        ScanNotice.Visibility = Visibility.Collapsed;
        _facts = null;
        ApplyVisibility();
    }

    /// <summary>
    /// GitHub's visibility when the repository is already there, shown and fixed; otherwise the user's choice. An
    /// existing repository on GitHub is fixed before its scan too, since GitHub will decide.
    /// </summary>
    private void ApplyVisibility()
    {
        if (VisibilityHelp is null) return;
        var known = SetupAnswers.PrivateOnGitHub(Source, _facts);
        if (known is { } isPrivate)
        {
            if (isPrivate) PrivateOption.IsChecked = true; else PublicOption.IsChecked = true;
        }
        bool fromGitHub = known is not null || Source == SetupSource.ExistingRepository;
        PrivateOption.IsEnabled = PublicOption.IsEnabled = !fromGitHub;
        VisibilityHelp.Text = fromGitHub ? Strings.Setup_VisibilityFromGitHub : PrivateOption.IsChecked == true ? Strings.Setup_VisibilityHelpPrivate : Strings.Setup_VisibilityHelpPublic;
    }

    private void Visibility_Changed(object sender, RoutedEventArgs e)
    {
        if (VisibilityHelp is null) return;
        // CodeQL is free on public repositories only, so the option follows the visibility.
        bool isPublic = PublicOption.IsChecked == true;
        if (CodeQlBox is not null && (CodeQlBox.IsEnabled != isPublic)) { CodeQlBox.IsEnabled = isPublic; CodeQlBox.IsChecked = isPublic; }
        if (!PublicOption.IsEnabled) return;
        VisibilityHelp.Text = isPublic ? Strings.Setup_VisibilityHelpPublic : Strings.Setup_VisibilityHelpPrivate;
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DotNetPanel is null) return;
        bool dotnet = ChosenLanguage == ProjectLanguage.DotNet;
        // The release workflow publishes a .NET exe, so the option follows the language.
        if (ReleaseBox is not null && ReleaseBox.IsEnabled != dotnet) { ReleaseBox.IsEnabled = dotnet; ReleaseBox.IsChecked = dotnet; }
        DotNetPanel.Visibility = dotnet ? Visibility.Visible : Visibility.Collapsed;
        CommandsPanel.Visibility = dotnet ? Visibility.Collapsed : Visibility.Visible;
        if (!dotnet)
        {
            var (build, test) = LanguageDefaults.CommandsFor(ChosenLanguage);
            if (BuildBox.Text.Trim().Length == 0) BuildBox.Text = build;
            if (TestBox.Text.Trim().Length == 0) TestBox.Text = test;
        }
    }

    private void FolderBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (FolderOption is not null && FolderBox.Text.Length > 0) FolderOption.IsChecked = true;
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ParentHelp is null) return;
        var name = NameBox.Text.Trim();
        ParentHelp.Text = name.Length > 0 && ParentBox.Text.Trim().Length > 0 ? string.Format(Strings.Setup_CloneWillBe, Path.Combine(ParentBox.Text.Trim(), name)) : Strings.Setup_ParentFolderHelp;
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e) => Browse(FolderBox);
    private void BrowseParent_Click(object sender, RoutedEventArgs e) => Browse(ParentBox);

    private void Browse(TextBox box)
    {
        // The .NET folder picker, where a path can be typed or pasted; not the legacy tree-only dialog.
        var dialog = new OpenFolderDialog
        {
            Title = Strings.Settings_BrowseTitle,
            Multiselect = false,
            InitialDirectory = Directory.Exists(box.Text) ? box.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (dialog.ShowDialog(Shell.Window) == true) box.Text = dialog.FolderName;
    }

    // Navigation between pages

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_running || _page == 0) return;
        ShowPage(_page - 1);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        if (_page == 0) _ = Shell.RunAsync(LeaveWhereAsync);
        else if (_page == 1) LeaveOptions();
    }

    private void StartOver_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        StartOver();
    }

    private void StartOver()
    {
        _facts = null;
        _plan = null;
        _rows.Clear();
        PlanPanel.Children.Clear();
        ResultNotice.Visibility = Visibility.Collapsed;
        ScanNotice.Visibility = Visibility.Collapsed;
        StartButton.Content = Strings.Setup_Start;
        ShowPage(0);
    }

    private void ShowPage(int page)
    {
        _page = page;
        WherePage.Visibility = page == 0 ? Visibility.Visible : Visibility.Collapsed;
        OptionsPage.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
        CreatePage.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.IsEnabled = page > 0 && !_running;
        NextButton.Visibility = page < 2 ? Visibility.Visible : Visibility.Collapsed;
        StartButton.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
        StartButton.IsEnabled = !_running;
        StartOverButton.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
        var title = page switch { 0 => Strings.Setup_WhereCard, 1 => Strings.Setup_OptionsCard, _ => Strings.Setup_CreateCard };
        PageIndicator.Text = string.Format(Strings.Setup_PageOf, page + 1, 3, title);
    }

    /// <summary>Checks the first page's answers, scans an existing folder or repository, and moves on unless something blocks.</summary>
    private async Task LeaveWhereAsync(CancellationToken ct)
    {
        var owner = OwnerBox.Text.Trim().TrimStart('@');
        if (owner.Length == 0 || owner.Equals("me", StringComparison.OrdinalIgnoreCase))
        {
            if (!Workspace.IdentityKnown) await Workspace.RefreshIdentityAsync(ct);
            owner = Workspace.Login;
            OwnerBox.Text = owner;
        }
        var name = NameBox.Text.Trim();
        var source = Source;

        if (source == SetupSource.ExistingFolder)
        {
            var folder = FolderBox.Text.Trim();
            if (!Directory.Exists(folder)) { Shell.ShowError(Strings.Settings_FolderMissing); return; }
            // The origin decides owner and name when there is one; the boxes are filled from it.
            _facts = await new ScanService(Workspace).ScanAsync(folder, "", ct);
            if (_facts.OwnerAndName.Length > 0)
            {
                var parts = _facts.OwnerAndName.Split('/');
                owner = parts[0]; name = parts[1];
                OwnerBox.Text = owner; NameBox.Text = name;
            }
            else
            {
                if (name.Length == 0) name = Path.GetFileName(folder.TrimEnd('\\'));
                NameBox.Text = name;
                if (!IsValidName(owner, name)) return;
                if (await Workspace.GitHub.RepositoryDetailsAsync($"{owner}/{name}", ct) is { } taken) { Shell.ShowError(AlreadyTaken(taken, $"{owner}/{name}")); return; }
            }
        }
        else if (source == SetupSource.ExistingRepository)
        {
            if (!IsValidName(owner, name)) return;
            if (ParentBox.Text.Trim().Length == 0) { Shell.ShowError(Strings.Setup_ParentMissing); return; }
            _facts = await new ScanService(Workspace).ScanAsync(null, $"{owner}/{name}", ct);
            // A renamed repository is found but blocked: the scan report below says where the name leads.
            if (_facts.Details is null) { Shell.ShowError(string.Format(Strings.Scan_RepositoryNotFound, $"{owner}/{name}")); return; }
        }
        else
        {
            if (!IsValidName(owner, name)) return;
            if (ParentBox.Text.Trim().Length == 0) { Shell.ShowError(Strings.Setup_ParentMissing); return; }
            var folder = Path.Combine(ParentBox.Text.Trim(), name);
            if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) { Shell.ShowError(string.Format(Strings.Setup_FolderNotEmpty, folder)); return; }
            if (await Workspace.GitHub.RepositoryDetailsAsync($"{owner}/{name}", ct) is { } taken) { Shell.ShowError(AlreadyTaken(taken, $"{owner}/{name}")); return; }
            _facts = null;
        }

        if (_facts is not null)
        {
            var report = ScanRules.Assess(_facts);
            ShowScan(report);
            TakeFromScan(_facts);
            ApplyVisibility();
            if (report.Verdict == ScanVerdict.NotConvertible) return;
        }

        if (ChosenLanguage == ProjectLanguage.DotNet && ProjectBox.Text.Trim().Length == 0 && source != SetupSource.NewRepository)
        {
            Shell.ShowError(Strings.Setup_ProjectFileMissing);
            return;
        }
        if (ChosenLanguage != ProjectLanguage.DotNet && BuildBox.Text.Trim().Length == 0)
        {
            Shell.ShowError(Strings.Setup_BuildCommandMissing);
            return;
        }
        ShowPage(1);
    }

    private bool IsValidName(string owner, string name)
    {
        if (owner.Length == 0) { Shell.ShowError(Strings.Setup_OwnerMissing); return false; }
        if (!RepositoryName().IsMatch(name)) { Shell.ShowError(Strings.Setup_NameInvalid); return false; }
        return true;
    }

    /// <summary>Why a name can't be created: the repository exists, or the name leads to a renamed or transferred one.</summary>
    private static string AlreadyTaken(Models.GhRepositoryDetails details, string ownerAndName)
        => details.IsNamed(ownerAndName) ? string.Format(Strings.Setup_AlreadyExists, ownerAndName) : string.Format(Strings.Scan_RepositoryMoved, ownerAndName, details.FullName);

    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,100}$")]
    private static partial Regex RepositoryName();

    /// <summary>Blockers and things worth knowing from the scan, under the first page.</summary>
    private void ShowScan(ScanReport report)
    {
        ScanFindings.Children.Clear();
        bool blocked = report.Verdict == ScanVerdict.NotConvertible;
        ScanNotice.Style = (Style)FindResource(blocked ? "DangerNoticeStyle" : "NoticeStyle");
        ScanTitle.Text = blocked ? Strings.Setup_ScanBlocked : report.Verdict == ScanVerdict.AlreadySetUp ? Strings.Setup_ScanAlreadySetUp : string.Format(Strings.Setup_ScanSummary, report.Summary);
        foreach (var finding in report.Of(ScanSeverity.Blocker).Concat(report.Of(ScanSeverity.Warning)))
        {
            var row = new TextBlock { Text = (finding.Severity == ScanSeverity.Blocker ? "✗  " : "!  ") + finding.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
            row.Foreground = UiHelpers.Brush(finding.Severity == ScanSeverity.Blocker ? "DangerBrush" : "TextPrimaryBrush");
            ScanFindings.Children.Add(row);
        }
        ScanNotice.Visibility = Visibility.Visible;
    }

    /// <summary>Language, project files and the self-test command, from what the scan saw, where the boxes are empty.</summary>
    private void TakeFromScan(ScanFacts facts)
    {
        var guess = ProjectLanguage.Detect(facts.Files);
        if (guess is not null)
        {
            foreach (ComboBoxItem item in LanguageCombo.Items)
                if ((string)item.Tag == guess.Language) LanguageCombo.SelectedItem = item;
        }
        var projects = facts.Files.Where(IsDotNetProject).OrderBy(p => p.Count(c => c == '/')).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        var test = projects.FirstOrDefault(p => p.Contains("test", StringComparison.OrdinalIgnoreCase));
        var main = projects.FirstOrDefault(p => p != test);
        if (ProjectBox.Text.Trim().Length == 0 && main is not null) ProjectBox.Text = main.Replace('/', '\\');
        if (TestProjectBox.Text.Trim().Length == 0 && test is not null) TestProjectBox.Text = test.Replace('/', '\\');
        if (SelfTestBox.Text.Trim().Length == 0 && facts.Manifest is { SelfTestCommand.Length: > 0 } m) SelfTestBox.Text = m.SelfTestCommand;
        if (DescriptionBox.Text.Trim().Length == 0 && facts.Details is { Description.Length: > 0 } d) DescriptionBox.Text = d.Description;
    }

    private static bool IsDotNetProject(string path)
        => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);

    // Page 2

    private void Token_Click(object sender, RoutedEventArgs e)
    {
        var window = new TokenWindow();
        if (!Shell.ShowDialog(window)) return;
        _setup.BoardToken = window.Token;
        TokenStatus.Text = Strings.Setup_TokenHeld;
        ClearTokenButton.Visibility = Visibility.Visible;
    }

    private void ClearToken_Click(object sender, RoutedEventArgs e)
    {
        _setup.BoardToken = null;
        TokenStatus.Text = Strings.Setup_TokenNone;
        ClearTokenButton.Visibility = Visibility.Collapsed;
    }

    private void LeaveOptions()
    {
        if (!int.TryParse(BehindBox.Text.Trim(), out var behind) || behind < 1) { Shell.ShowError(Strings.Setup_BehindLimitInvalid); return; }
        var answers = Answers(behind);
        // The protocol is a tool-wide choice: remembered for the next repository.
        if (!Workspace.Settings.GitProtocol.Equals(answers.GitProtocol, StringComparison.OrdinalIgnoreCase) || !Workspace.Settings.SshHost.Equals(answers.SshHost, StringComparison.OrdinalIgnoreCase))
        {
            Workspace.Settings.GitProtocol = answers.GitProtocol;
            Workspace.Settings.SshHost = answers.SshHost;
            Workspace.Settings.Save();
        }
        _plan = _setup.Build(answers, _facts);
        ShowPlan(_plan);
        ShowPage(2);
    }

    private SetupAnswers Answers(int behindLimit) => new()
    {
        Source = Source,
        Owner = OwnerBox.Text.Trim().TrimStart('@'),
        Name = NameBox.Text.Trim(),
        Description = DescriptionBox.Text.Trim(),
        IsPrivate = SetupAnswers.PrivateOnGitHub(Source, _facts) ?? PrivateOption.IsChecked == true,
        ParentFolder = ParentBox.Text.Trim(),
        ExistingFolder = FolderBox.Text.Trim(),
        Language = ChosenLanguage,
        ProjectFile = ProjectBox.Text.Trim(),
        TestProject = TestProjectBox.Text.Trim(),
        BuildCommand = BuildBox.Text.Trim(),
        TestCommand = TestBox.Text.Trim(),
        SelfTestCommand = SelfTestBox.Text.Trim(),
        DefaultBranch = _facts?.DefaultBranch ?? RepositorySettings.DefaultDefaultBranch,
        ReviewRequired = ReviewBox.IsChecked == true,
        Discussions = DiscussionsBox.IsChecked == true,
        ReleaseWorkflow = ReleaseBox.IsChecked == true,
        CodeQl = CodeQlBox.IsChecked == true,
        BehindLimit = behindLimit,
        Checklist = ChecklistPanel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList(),
        HasBoardToken = !string.IsNullOrEmpty(_setup.BoardToken),
        GitProtocol = SshOption.IsChecked == true ? RemoteUrl.Ssh : RemoteUrl.Https,
        SshHost = SshHostBox.Text.Trim().Length > 0 ? SshHostBox.Text.Trim() : "github.com",
    };

    // Page 3

    private void ShowPlan(SetupPlan plan)
    {
        PlanPanel.Children.Clear();
        _rows.Clear();
        PlanIntro.Text = string.Format(Strings.Setup_PlanIntro, plan.Steps.Count, plan.Answers.OwnerAndName);
        foreach (var step in plan.Steps)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var mark = UiHelpers.Mark(true);
            mark.Text = "·";
            mark.Foreground = UiHelpers.Brush("TextTertiaryBrush");
            mark.VerticalAlignment = VerticalAlignment.Top;
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = step.Title, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = step.Detail, TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("HelperText") });
            var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), Visibility = Visibility.Collapsed };
            text.Children.Add(message);
            Grid.SetColumn(text, 1);
            row.Children.Add(mark);
            row.Children.Add(text);
            AutomationProperties.SetName(row, step.Title);
            PlanPanel.Children.Add(row);
            _rows[step] = (mark, message);
        }
    }

    private void ShowStep(PlanStep step)
    {
        if (!_rows.TryGetValue(step, out var row)) return;
        var (mark, message) = row;
        switch (step.State)
        {
            case StepState.Pending: mark.Text = "·"; mark.Foreground = UiHelpers.Brush("TextTertiaryBrush"); break;
            case StepState.Checking:
            case StepState.Running: mark.Text = "…"; mark.Foreground = UiHelpers.Brush("PrimaryStrongBrush"); break;
            case StepState.AlreadyDone:
            case StepState.Applied: mark.Text = "✓"; mark.Foreground = UiHelpers.Brush("SuccessBrush"); break;
            case StepState.Failed: mark.Text = "✗"; mark.Foreground = UiHelpers.Brush("DangerBrush"); break;
        }
        var note = step.State switch
        {
            StepState.AlreadyDone => step.Message.Length > 0 ? step.Message : Strings.Setup_StepAlreadyDone,
            StepState.Applied => step.Message.Length > 0 ? step.Message : Strings.Setup_StepApplied,
            StepState.Failed => step.Message,
            StepState.Running => Strings.Setup_StepRunning,
            StepState.Checking => Strings.Setup_StepChecking,
            _ => "",
        };
        message.Text = note;
        message.Foreground = UiHelpers.Brush(step.State == StepState.Failed ? "DangerBrush" : "TextSecondaryBrush");
        message.Visibility = note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_plan is null || _running) return;
        _running = true;
        ResultNotice.Visibility = Visibility.Collapsed;
        ShowPage(2);
        Shell.Announce(Strings.Setup_Started);
        _ = Shell.RunAsync(async ct =>
        {
            bool ok;
            try { ok = await _setup.RunAsync(_plan, ct); }
            finally { _running = false; }
            ShowResult(ok);
        });
    }

    private void ShowResult(bool ok)
    {
        ShowPage(2);
        ResultLinks.Children.Clear();
        ResultNotice.Style = (Style)FindResource(ok ? "SuccessNoticeStyle" : "DangerNoticeStyle");
        if (ok)
        {
            ResultTitle.Text = Strings.Setup_DoneTitle;
            ResultText.Text = _setup.PullRequestUrl.Length > 0 ? Strings.Setup_DonePullRequest : Strings.Setup_Done;
            ResultLinks.Children.Add(Link(Strings.Setup_OpenStatus, () => Shell.Navigate(Screen.Status)));
            ResultLinks.Children.Add(Link(Strings.Setup_OpenOnGitHub, () => Shell.OpenUrl(_setup.RepositoryUrl)));
            if (_setup.PullRequestUrl.Length > 0) ResultLinks.Children.Add(Link(Strings.Setup_OpenPullRequest, () => Shell.OpenUrl(_setup.PullRequestUrl)));
            StartButton.Visibility = Visibility.Collapsed;
            Shell.Announce(Strings.Setup_DoneTitle);
        }
        else
        {
            var failed = _plan?.FailedStep;
            ResultTitle.Text = Strings.Setup_FailedTitle;
            ResultText.Text = failed is null ? Strings.Setup_FailedUnknown : string.Format(Strings.Setup_Failed, failed.Title, failed.Message);
            StartButton.Content = Strings.Setup_Retry;
            StartButton.IsEnabled = true;
            Shell.Announce(ResultText.Text);
        }
        ResultNotice.Visibility = Visibility.Visible;
    }

    private static Button Link(string text, Action onClick)
    {
        var button = UiHelpers.LinkButton(text, onClick);
        button.Margin = new Thickness(0, 0, 16, 0);
        return button;
    }
}
