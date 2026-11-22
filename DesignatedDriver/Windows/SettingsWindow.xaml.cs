using System.IO;
using System.Windows;
using System.Windows.Controls;
using DesignatedDriver.Core;
using DesignatedDriver.Helpers;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;
using DesignatedDriver.Services;
using Microsoft.Win32;

namespace DesignatedDriver.Windows;

/// <summary>Adds a clone to the list, or edits one repository's folder and overrides.</summary>
public partial class SettingsWindow : Window
{
    private readonly IShell _shell;
    private readonly RepositorySettings _repository;
    private readonly bool _adding;
    private CancellationTokenSource? _probe;
    private CancellationTokenSource? _read;

    /// <param name="readFromGitHub">Read what the repository already uses from GitHub as the window opens, for a repository just added.</param>
    public SettingsWindow(IShell shell, RepositorySettings? repository, bool readFromGitHub = false)
    {
        _shell = shell;
        _adding = repository is null;
        _repository = repository?.Clone() ?? new RepositorySettings();
        InitializeComponent();

        // Fit the screen it opens on, which is the one under the mouse that opened it: no taller than its work area,
        // with the content scrolling and Save and Cancel below it, and moved to stay inside it (issue 59).
        var area = MonitorHelper.AreaUnderMouse();
        MaxHeight = DialogFit.MaxHeight(area?.WorkArea.Height ?? SystemParameters.WorkArea.Height);
        if (area is { } a) Loaded += (_, _) => Top = DialogFit.Top(Top, ActualHeight, a.WorkArea);

        if (_adding) { Title = Strings.Settings_AddTitle; TitleText.Text = Strings.Settings_AddTitle; }
        var s = _repository;
        FolderBox.Text = s.Folder;
        OwnerRepoBox.Text = s.OwnerAndName;
        ProjectOwnerBox.Text = s.BoardOwner;
        ProjectNumberBox.Text = s.BoardNumber > 0 ? s.BoardNumber.ToString() : "";
        DefaultBranchBox.Text = s.DefaultBranch;
        SelfTestBox.Text = s.SelfTestCommand;
        RequiredCheckBox.Text = s.RequiredCheck;
        ReadyColumnBox.Text = s.ReadyColumn;
        InProgressColumnBox.Text = s.InProgressColumn;
        InReviewColumnBox.Text = s.InReviewColumn;
        KeepsOwnBox.IsChecked = s.KeepsOwnConventions;
        StoredAtText.Text = string.Format(Strings.Settings_StoredAt, Settings.FilePath);

        // While the manifest names a board, the override boxes don't apply, unless the repository keeps its own conventions.
        if (!_adding && s.HasFolder && !s.KeepsOwnConventions && Manifest.Load(s.Folder)?.Board is { Number: > 0 } fromManifest)
        {
            ProjectHelp.Text = string.Format(Strings.Settings_BoardFromManifest, fromManifest.Owner, fromManifest.Number);
            ProjectOwnerBox.IsEnabled = ProjectNumberBox.IsEnabled = false;
        }
        UpdateReadPanel();
        if (readFromGitHub && CanRead) Loaded += (_, _) => _ = ReadAsync(firstRead: true);
        Closed += (_, _) => { _probe?.Cancel(); _read?.Cancel(); };
    }

    /// <summary>A repository with a manifest takes its values from there, so only one without can be read from GitHub.</summary>
    private bool CanRead => OwnerRepoBox.Text.Contains('/') && !(Directory.Exists(FolderBox.Text) && Manifest.Load(FolderBox.Text) is not null);

    private void UpdateReadPanel() => ReadPanel.Visibility = CanRead ? Visibility.Visible : Visibility.Collapsed;

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        // The .NET folder picker, where a path can be typed or pasted; not the legacy tree-only dialog.
        var dialog = new OpenFolderDialog
        {
            Title = Strings.Settings_BrowseTitle,
            Multiselect = false,
            InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }

    private void FolderBox_TextChanged(object sender, TextChangedEventArgs e) => _ = ProbeFolderAsync(FolderBox.Text);

    /// <summary>Checks the folder is a Git repository and reads owner and name from its origin remote.</summary>
    private async Task ProbeFolderAsync(string folder)
    {
        _probe?.Cancel();
        var cts = _probe = new CancellationTokenSource();
        OwnerRepoBox.Text = "";
        KnownNote.Visibility = Visibility.Collapsed;
        UpdateReadPanel();
        if (!Directory.Exists(folder))
        {
            FolderNote.Text = folder.Length == 0 ? "" : Strings.Settings_FolderMissing;
            FolderNote.Foreground = UiHelpers.Brush("DangerBrush");
            return;
        }
        try
        {
            await Task.Delay(300, cts.Token);
            var git = _shell.Workspace.Git;
            var root = await git.RepositoryRootAsync(folder, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (root is null)
            {
                FolderNote.Text = Strings.Settings_NotARepository;
                FolderNote.Foreground = UiHelpers.Brush("DangerBrush");
                return;
            }
            var url = await git.OriginUrlInAsync(folder, cts.Token);
            if (cts.IsCancellationRequested) return;
            var parsed = RemoteUrl.ParseOwnerAndRepository(url);
            if (parsed is null || !ScanFacts.IsGitHubUrl(url))
            {
                FolderNote.Text = Strings.Settings_NoOrigin;
                FolderNote.Foreground = UiHelpers.Brush("DangerBrush");
                return;
            }
            OwnerRepoBox.Text = $"{parsed.Value.Owner}/{parsed.Value.Repository}";
            FolderNote.Text = string.Format(Strings.Settings_FolderOk, root);
            FolderNote.Foreground = UiHelpers.Brush("SuccessBrush");
            UpdateReadPanel();

            var known = _shell.Workspace.Settings.Find(OwnerRepoBox.Text);
            if (_adding && known is not null)
            {
                KnownNote.Text = string.Format(Strings.Settings_AlreadyKnown, OwnerRepoBox.Text);
                KnownNote.Visibility = Visibility.Visible;
            }
            // A clone being added that has no manifest has what it already uses read from GitHub, to confirm.
            else if (_adding && CanRead)
            {
                await ReadAsync(firstRead: true);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Read_Click(object sender, RoutedEventArgs e) => _ = ReadAsync(firstRead: false);

    /// <summary>
    /// Scans the repository and fills the boxes from what GitHub says it uses. Only reads run, and nothing is saved
    /// until Save. On the first read every value GitHub has fills its box; after that an empty box is filled and a
    /// value that differs from GitHub's is replaced only if the user says so.
    /// </summary>
    private async Task ReadAsync(bool firstRead)
    {
        if (!CanRead) return;
        _read?.Cancel();
        var cts = _read = new CancellationTokenSource();
        var name = OwnerRepoBox.Text.Trim();
        var folder = Directory.Exists(FolderBox.Text) ? FolderBox.Text.Trim() : null;
        ReadButton.IsEnabled = false;
        ShowReadNote(string.Format(Strings.Settings_Reading, name));
        try
        {
            var facts = await new ScanService(_shell.Workspace).ScanAsync(folder, name, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (!facts.RemoteFound) { ShowReadNote(string.Format(Strings.Settings_ReadNotFound, name)); return; }

            var profile = RepositoryProfile.From(facts);
            var (fill, ask) = ProfileComparison.Compare(CurrentValues(), profile, firstRead);
            foreach (var change in fill) Apply(change.Field, profile);

            var kept = new List<ProfileValue>();
            if (ask.Count > 0)
            {
                var lines = string.Join("\n", ask.Select(a => string.Format(Strings.Settings_ReadAskLine, Label(a.Field), a.Current, a.FromGitHub)));
                var answer = MessageBox.Show(this, string.Format(Strings.Settings_ReadAsk, lines), Strings.Settings_ReadAskTitle, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (answer == MessageBoxResult.Yes) foreach (var change in ask) Apply(change.Field, profile);
                else kept = ask;
            }

            var filled = fill.Concat(ask.Except(kept)).Select(c => Label(c.Field)).ToList();
            var note = filled.Count > 0 ? string.Format(Strings.Settings_ReadFilled, string.Join(", ", filled)) : string.Format(Strings.Settings_ReadNothing, name);
            if (kept.Count > 0) note += " " + string.Format(Strings.Settings_ReadKept, string.Join(", ", kept.Select(c => Label(c.Field))));
            ShowReadNote(note);
        }
        catch (OperationCanceledException) { }
        catch (CommandFailedException ex) { ShowReadNote(string.Format(Strings.Settings_ReadFailed, name, ex.Message)); }
        finally { if (_read == cts) ReadButton.IsEnabled = true; }
    }

    private void ShowReadNote(string text)
    {
        ReadNote.Text = text;
        ReadNote.Visibility = Visibility.Visible;
    }

    private Dictionary<ProfileField, string> CurrentValues() => new()
    {
        [ProfileField.DefaultBranch] = DefaultBranchBox.Text,
        [ProfileField.RequiredCheck] = RequiredCheckBox.Text,
        [ProfileField.Board] = ProfileComparison.BoardText(ProjectOwnerBox.Text, int.TryParse(ProjectNumberBox.Text.Trim(), out var n) ? n : 0),
        [ProfileField.ReadyColumn] = ReadyColumnBox.Text,
        [ProfileField.InProgressColumn] = InProgressColumnBox.Text,
        [ProfileField.InReviewColumn] = InReviewColumnBox.Text,
        [ProfileField.SelfTestCommand] = SelfTestBox.Text,
    };

    private void Apply(ProfileField field, RepositoryProfile p)
    {
        switch (field)
        {
            case ProfileField.DefaultBranch: DefaultBranchBox.Text = p.DefaultBranch; break;
            case ProfileField.RequiredCheck: RequiredCheckBox.Text = p.RequiredCheck; break;
            case ProfileField.Board:
                if (!ProjectOwnerBox.IsEnabled || p.Board is null) break;
                ProjectOwnerBox.Text = p.Board.Owner;
                ProjectNumberBox.Text = p.Board.Number.ToString();
                break;
            case ProfileField.ReadyColumn: ReadyColumnBox.Text = p.ReadyColumn; break;
            case ProfileField.InProgressColumn: InProgressColumnBox.Text = p.InProgressColumn; break;
            case ProfileField.InReviewColumn: InReviewColumnBox.Text = p.InReviewColumn; break;
            case ProfileField.SelfTestCommand: SelfTestBox.Text = p.SelfTestCommand; break;
        }
    }

    private static string Label(ProfileField field) => field switch
    {
        ProfileField.DefaultBranch => Strings.Settings_DefaultBranch,
        ProfileField.RequiredCheck => Strings.Settings_RequiredCheck,
        ProfileField.Board => Strings.Settings_Board,
        ProfileField.ReadyColumn => Strings.Settings_ReadyColumn,
        ProfileField.InProgressColumn => Strings.Settings_InProgressColumn,
        ProfileField.InReviewColumn => Strings.Settings_InReviewColumn,
        _ => Strings.Settings_SelfTestCommand,
    };

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(FolderBox.Text))
        {
            MessageBox.Show(this, Strings.Settings_FolderMissing, Strings.Settings_Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var parsed = RemoteUrl.ParseOwnerAndRepository(OwnerRepoBox.Text.Contains('/') ? "https://github.com/" + OwnerRepoBox.Text : "");
        if (parsed is null)
        {
            MessageBox.Show(this, Strings.Settings_NoOrigin, Strings.Settings_Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        int number = 0;
        var numberText = ProjectNumberBox.Text.Trim();
        if (numberText.Length > 0 && (!int.TryParse(numberText, out number) || number <= 0))
        {
            MessageBox.Show(this, Strings.Settings_ProjectNumberInvalid, Strings.Settings_Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var s = _repository;
        s.Folder = FolderBox.Text.Trim().TrimEnd('\\');
        s.Owner = parsed.Value.Owner;
        s.Name = parsed.Value.Repository;
        s.BoardOwner = ProjectOwnerBox.Text.Trim();
        s.BoardNumber = s.BoardOwner.Length > 0 ? number : 0;
        s.DefaultBranch = string.IsNullOrWhiteSpace(DefaultBranchBox.Text) ? RepositorySettings.DefaultDefaultBranch : DefaultBranchBox.Text.Trim();
        s.SelfTestCommand = SelfTestBox.Text.Trim();
        s.RequiredCheck = string.IsNullOrWhiteSpace(RequiredCheckBox.Text) ? RepositorySettings.DefaultRequiredCheck : RequiredCheckBox.Text.Trim();
        s.ReadyColumn = ReadyColumnBox.Text.Trim();
        s.InProgressColumn = InProgressColumnBox.Text.Trim();
        s.InReviewColumn = InReviewColumnBox.Text.Trim();
        s.KeepsOwnConventions = KeepsOwnBox.IsChecked == true;

        _shell.Workspace.SaveRepository(s);
        DialogResult = true;
    }
}
