using System.IO;
using System.Windows;
using System.Windows.Controls;
using DesignatedDriver.Core;
using DesignatedDriver.Helpers;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;
using Microsoft.Win32;

namespace DesignatedDriver.Windows;

/// <summary>Adds a clone to the list, or edits one repository's folder and overrides.</summary>
public partial class SettingsWindow : Window
{
    private readonly IShell _shell;
    private readonly RepositorySettings _repository;
    private readonly bool _adding;
    private CancellationTokenSource? _probe;

    public SettingsWindow(IShell shell, RepositorySettings? repository)
    {
        _shell = shell;
        _adding = repository is null;
        _repository = repository?.Clone() ?? new RepositorySettings();
        InitializeComponent();

        if (_adding) { Title = Strings.Settings_AddTitle; TitleText.Text = Strings.Settings_AddTitle; }
        var s = _repository;
        FolderBox.Text = s.Folder;
        OwnerRepoBox.Text = s.OwnerAndName;
        ProjectOwnerBox.Text = s.BoardOwner;
        ProjectNumberBox.Text = s.BoardNumber > 0 ? s.BoardNumber.ToString() : "";
        DefaultBranchBox.Text = s.DefaultBranch;
        SelfTestBox.Text = s.SelfTestCommand;
        RequiredCheckBox.Text = s.RequiredCheck;
        StoredAtText.Text = string.Format(Strings.Settings_StoredAt, Settings.FilePath);

        // While the manifest names a board, the override boxes don't apply.
        if (!_adding && s.HasFolder && Manifest.Load(s.Folder)?.Board is { Number: > 0 } fromManifest)
        {
            ProjectHelp.Text = string.Format(Strings.Settings_BoardFromManifest, fromManifest.Owner, fromManifest.Number);
            ProjectOwnerBox.IsEnabled = ProjectNumberBox.IsEnabled = false;
        }
        Closed += (_, _) => _probe?.Cancel();
    }

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

            var known = _shell.Workspace.Settings.Find(OwnerRepoBox.Text);
            if (_adding && known is not null)
            {
                KnownNote.Text = string.Format(Strings.Settings_AlreadyKnown, OwnerRepoBox.Text);
                KnownNote.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) { }
    }

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

        _shell.Workspace.SaveRepository(s);
        DialogResult = true;
    }
}
