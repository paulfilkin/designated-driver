using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DesignatedDriver.Core;
using DesignatedDriver.Helpers;
using DesignatedDriver.Resources;
using DesignatedDriver.Screens;
using DesignatedDriver.Services;
using DesignatedDriver.Windows;

namespace DesignatedDriver;

public partial class MainWindow : Window, IShell
{
    private readonly Dictionary<Screen, ScreenBase> _screens = [];
    private readonly Dictionary<Screen, RadioButton> _navButtons = [];
    private readonly ActivityBuffer _activity = new();
    private readonly DispatcherTimer _busyTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly System.Diagnostics.Stopwatch _busySince = new();
    private int _flushQueued;
    private string _runningCommand = "";
    private CancellationTokenSource? _cts;
    private Screen _current = Screen.Repositories;
    private bool _firstShow = true;
    private bool _fillingPicker;

    public MainWindow(Workspace workspace)
    {
        Workspace = workspace;
        InitializeComponent();

        AccessibilityHelper.SetupLiveRegion(ActivityText);
        Workspace.Runner.Activity += OnActivity;
        _busyTimer.Tick += BusyTimer_Tick;
        Workspace.IdentityChanged += (_, _) => Dispatcher.BeginInvoke(RefreshIdentityDisplay);
        Workspace.SettingsChanged += (_, _) => Dispatcher.BeginInvoke(OnSettingsChanged);
        Workspace.StarredChanged += (_, _) => Dispatcher.BeginInvoke(FillRepositoryPicker);

        BuildNavigation();
        FillRepositoryPicker();
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Activated += MainWindow_Activated;
        Deactivated += MainWindow_Deactivated;
        Closing += (_, _) => { _heldReload.Drop(); _cts?.Cancel(); };
    }

    public Workspace Workspace { get; }
    public Window Window => this;
    public bool IsBusy => _cts is not null;

    /// <summary>
    /// Places the window on the monitor under the mouse, wholly inside its work area. The startup location is
    /// Manual because WPF's CenterScreen does not check that the window fits, and on a small monitor it overhangs
    /// onto the next one. The handle exists here and the window is not yet shown, so there is no flicker.
    /// </summary>
    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var target = MonitorHelper.AreaUnderMouse();
        if (target is null) return;
        var placed = WindowPlacement.CentreAndFit(Width, Height, target.Value.WorkArea);
        // A monitor smaller than the minimum size still gets a window that fits.
        double minWidth = Math.Min(MinWidth, placed.Width);
        double minHeight = Math.Min(MinHeight, placed.Height);

        // The handle was created on whichever monitor Windows chose, and WPF converts Left, Top, Width and Height
        // with that monitor's DPI. The window is first given its final size and place in device pixels, expressed
        // in that monitor's units, so that after the move it lies wholly on the target and Windows switches its
        // DPI to the target's. The same values in the target's units then land exactly, whether or not the DPI
        // changed. The minimum size is lifted for the move, since the interim units can be smaller than it.
        double ratio = target.Value.Scale / MonitorHelper.ScaleOf(this);
        MinWidth = 0;
        MinHeight = 0;
        Width = placed.Width * ratio;
        Height = placed.Height * ratio;
        Left = placed.Left * ratio;
        Top = placed.Top * ratio;

        MinWidth = minWidth;
        MinHeight = minHeight;
        Width = placed.Width;
        Height = placed.Height;
        Left = placed.Left;
        Top = placed.Top;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_firstShow) return;
        _firstShow = false;

        if (Workspace.Settings.Repositories.Count == 0)
        {
            ShowInfo(Strings.Settings_FirstRun);
            Navigate(Screen.Repositories);
        }
        else
        {
            Navigate(Workspace.HasRepository ? Screen.Status : Screen.Repositories);
        }
        await Task.CompletedTask;
    }

    /// <summary>A repository was added, removed or switched: every screen reloads against the new one.</summary>
    private void OnSettingsChanged()
    {
        foreach (var s in _screens.Values) s.Invalidate();
        FillRepositoryPicker();
        if (ScreenCatalogue.NeedsRepository(_current) && !Workspace.HasRepository) Navigate(Screen.Repositories);
        else Navigate(_current);
    }

    // Repository switcher

    private void FillRepositoryPicker()
    {
        _fillingPicker = true;
        try
        {
            RepositoryPicker.Items.Clear();
            foreach (var repo in Workspace.Settings.Repositories)
            {
                bool starred = Workspace.Starred is { } set && RepositoryFilter.IsStarred(repo, set);
                var item = new ComboBoxItem { Content = (starred ? "★ " : "") + repo.OwnerAndName, Tag = repo.OwnerAndName };
                AutomationProperties.SetName(item, repo.OwnerAndName);
                RepositoryPicker.Items.Add(item);
                if (repo.Matches(Workspace.Settings.Current)) RepositoryPicker.SelectedItem = item;
            }
            if (RepositoryPicker.Items.Count == 0)
            {
                var none = new ComboBoxItem { Content = Strings.Header_NoRepository, IsEnabled = false };
                RepositoryPicker.Items.Add(none);
                RepositoryPicker.SelectedItem = none;
            }
        }
        finally
        {
            _fillingPicker = false;
        }
    }

    private void RepositoryPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingPicker) return;
        if (RepositoryPicker.SelectedItem is not ComboBoxItem { Tag: string key }) return;
        if (IsBusy) { ShowInfo(Strings.Busy_TryAgain); FillRepositoryPicker(); return; }
        Workspace.SelectRepository(key);
    }

    // Navigation

    private void BuildNavigation()
    {
        NavPanel.Children.Clear();
        _navButtons.Clear();
        foreach (var screen in ScreenCatalogue.ScreensFor(Workspace.IsAdmin).Where(ScreenCatalogue.IsBuilt))
        {
            var button = new RadioButton
            {
                Content = ScreenCatalogue.Title(screen),
                Style = (Style)FindResource("NavButton"),
                GroupName = "Nav",
                Tag = screen,
                IsChecked = screen == _current,
            };
            AutomationProperties.SetName(button, ScreenCatalogue.Title(screen));
            button.Checked += (_, _) => Navigate(screen);
            NavPanel.Children.Add(button);
            _navButtons[screen] = button;
        }
    }

    public void Navigate(Screen screen, int? pullRequest = null)
    {
        if (!ScreenCatalogue.IsBuilt(screen)) return;
        _current = screen;
        if (_navButtons.TryGetValue(screen, out var button) && button.IsChecked != true) button.IsChecked = true;

        if (!_screens.TryGetValue(screen, out var view))
        {
            view = screen switch
            {
                Screen.Repositories => new RepositoriesScreen(this),
                Screen.Scan => new ScanScreen(this),
                Screen.NewRepository => new NewRepositoryScreen(this),
                Screen.Status => new StatusScreen(this),
                Screen.Check => new CheckScreen(this),
                Screen.StartWork => new StartWorkScreen(this),
                Screen.SaveAndShare => new SaveShareScreen(this),
                Screen.Submit => new SubmitScreen(this),
                Screen.Review => new ReviewScreen(this),
                Screen.TidyUp => new TidyUpScreen(this),
                Screen.FixProblem => new FixProblemScreen(this),
                _ => throw new InvalidOperationException(screen.ToString()),
            };
            _screens[screen] = view;
        }
        if (pullRequest is not null && view is ReviewScreen review) review.SelectOnLoad(pullRequest.Value);

        // Every arrival reloads the screen: pull requests, checks and reviews change while the tool is open.
        bool arriving = !ReferenceEquals(ScreenHost.Content, view);
        if (arriving)
        {
            ScreenHost.Content = view;
            AutomationProperties.SetName(ScreenHost, ScreenCatalogue.Title(screen));
        }
        bool canLoad = !ScreenCatalogue.NeedsRepository(screen) || Workspace.HasRepository;
        if ((arriving || view.NeedsRefresh || pullRequest is not null) && canLoad)
            Reload(view);
    }

    /// <summary>
    /// Reloads a screen now, or holds the reload until the running work ends. A running reload of a screen the user
    /// has left is stopped, so the held one starts straight away; a user action is never stopped.
    /// </summary>
    private void Reload(ScreenBase view)
    {
        if (!IsBusy) { _ = RunAsync(view.RefreshAsync, WorkKind.Reload, view); return; }
        _heldReload.Hold(view, DateTime.UtcNow);
        if (HeldReload.GivesWay(_runningKind is WorkKind.Reload or WorkKind.Pick, _runningView, view) && !_cts!.IsCancellationRequested)
        {
            Workspace.Runner.Note(string.Format(Strings.Activity_ReloadLeft, ScreenTitleOf(_runningView!)));
            _stoppedForLeave = true;
            _cts.Cancel();
        }
    }

    private string ScreenTitleOf(ScreenBase view)
        => _screens.FirstOrDefault(pair => ReferenceEquals(pair.Value, view)) is { Value: not null } found ? ScreenCatalogue.Title(found.Key) : "";

    /// <summary>Set up a particular repository: shows the wizard with that target chosen.</summary>
    public void NavigateToSetup(string? folder, string ownerAndName, ScanFacts? facts = null)
    {
        Navigate(Screen.NewRepository);
        if (_screens[Screen.NewRepository] is NewRepositoryScreen wizard) wizard.Prefill(folder, ownerAndName, facts);
    }

    public bool ShowDialog(Window dialog)
    {
        dialog.Owner = this;
        return Dialog(dialog.ShowDialog) == true;
    }

    /// <summary>Scan a particular repository: shows the Scan screen with that target chosen.</summary>
    public void NavigateToScan(string? folder, string ownerAndName)
    {
        Navigate(Screen.Scan);
        if (_screens[Screen.Scan] is ScanScreen scan) scan.ScanTarget(folder, ownerAndName);
    }

    /// <summary>Clone a particular repository as it is: shows Repositories with the clone card open for it.</summary>
    public void NavigateToClone(string ownerAndName, bool keepsOwnConventions = false)
    {
        Navigate(Screen.Repositories);
        if (_screens[Screen.Repositories] is RepositoriesScreen repositories) repositories.ShowClone(ownerAndName, keepsOwnConventions);
    }

    /// <summary>
    /// Back in front after a while away: reload the screen on show, unless work is already running. Coming back
    /// from one of the tool's own dialogs doesn't count: a confirmation closing would otherwise start a refresh
    /// before the confirmed action, and the action would be dropped as a click while busy.
    /// </summary>
    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        bool fromOwnDialog = _deactivatedForOwnDialog || _dialogShownSinceActivation;
        _deactivatedForOwnDialog = false;
        _dialogShownSinceActivation = false;
        if (fromOwnDialog || IsBusy || _firstShow) return;
        if (ScreenCatalogue.NeedsRepository(_current) && !Workspace.HasRepository) return;
        if (ScreenHost.Content is ScreenBase view && view.ReloadsOnReturn && view.IsOlderThan(StaleAfter))
        {
            Workspace.Runner.Note(Strings.Activity_ReloadOnReturn);
            _ = RunAsync(view.RefreshAsync, WorkKind.Reload, view);
        }
    }

    /// <summary>
    /// Decided at the moment focus leaves, which is the reliable moment: a message box or an owned window of ours
    /// is open, or the window taking focus belongs to this process. Otherwise the user went to another application.
    /// </summary>
    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        _deactivatedForOwnDialog = _dialogDepth > 0 || OwnedWindows.Count > 0 || ForegroundWindowIsOurs();
    }

    private static bool ForegroundWindowIsOurs()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(foreground, out var pid);
        return pid == Environment.ProcessId;
    }

    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);
    private int _dialogDepth;
    private bool _deactivatedForOwnDialog;
    private bool _dialogShownSinceActivation;
    /// <summary>What the running work is; null when nothing runs.</summary>
    private WorkKind? _runningKind;
    /// <summary>The screen the running reload or pick belongs to; null for a user action.</summary>
    private ScreenBase? _runningView;
    /// <summary>The running work was stopped because its screen was left, or for a newer pick; no "Cancelled" note.</summary>
    private bool _stoppedForLeave;
    /// <summary>What is waiting for the running work to stop, and the ticket that lets only the latest of those start.</summary>
    private WorkKind? _waitingKind;
    private int _ticket;
    private TaskCompletionSource _runningDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HeldReload<ScreenBase> _heldReload = new();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private void RefreshIdentityDisplay()
    {
        if (Workspace.IdentityKnown)
        {
            UserText.Text = Workspace.Login;
            RoleText.Text = Workspace.IsAdmin ? Strings.Role_Admin : Strings.Role_Contributor;
            UserChip.Visibility = Visibility.Visible;
        }
        else
        {
            UserChip.Visibility = Visibility.Collapsed;
        }
        var shown = _navButtons.Keys.ToHashSet();
        var wanted = ScreenCatalogue.ScreensFor(Workspace.IsAdmin).Where(ScreenCatalogue.IsBuilt).ToHashSet();
        if (!shown.SetEquals(wanted)) BuildNavigation();
    }

    // Busy work

    public Task RunAsync(Func<CancellationToken, Task> work) => RunAsync(work, WorkKind.Action, null);

    public Task RunPickAsync(Func<CancellationToken, Task> work) => RunAsync(work, WorkKind.Pick, ScreenHost.Content as ScreenBase);

    /// <summary>
    /// Runs work with the busy strip shown, following <see cref="BusyRules"/>. A reload the tool started by itself, or
    /// a pick that only loads what was picked, gives way to a pick or a user action: it is cancelled and the new work
    /// runs, so a click is never lost to a load and the latest pick wins. A pick or an action while an action runs is
    /// not queued; the user is told. A screen reload asked for while busy is held by <see cref="Reload"/> and runs when
    /// the work ends, after anything waiting to start. A reload or pick names its screen, so leaving that screen can stop it.
    /// </summary>
    private async Task RunAsync(Func<CancellationToken, Task> work, WorkKind kind, ScreenBase? view)
    {
        if (_cts is not null)
        {
            // What runs next is what is waiting, if anything is; otherwise what is running now.
            switch (BusyRules.OnRequest(_waitingKind ?? _runningKind, kind))
            {
                case WorkStart.Drop:
                    return;
                case WorkStart.Refuse:
                    ShowInfo(Strings.Busy_TryAgain);
                    return;
                default:
                    int ticket = ++_ticket;
                    _waitingKind = kind;
                    // A newer pick replaces the one loading without a "Cancelled" line each time.
                    if (kind == WorkKind.Pick && _runningKind == WorkKind.Pick) _stoppedForLeave = true;
                    _cts.Cancel();
                    while (_cts is not null) await _runningDone.Task;
                    if (ticket != _ticket) return;
                    _waitingKind = null;
                    break;
            }
        }
        _cts = new CancellationTokenSource();
        _runningKind = kind;
        _runningView = kind == WorkKind.Action ? null : view;
        _stoppedForLeave = false;
        _runningDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _runningCommand = "";
        _busySince.Restart();
        BusyText.Text = Strings.Busy_Working;
        BusyStrip.Visibility = Visibility.Visible;
        _busyTimer.Start();
        Mouse.OverrideCursor = Cursors.AppStarting;
        try
        {
            await work(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_stoppedForLeave) Workspace.Runner.Note(Strings.Activity_Cancelled);
        }
        catch (CommandFailedException ex)
        {
            ShowError(string.Format(Strings.Error_CommandFailed, ex.Result.CommandLine, ex.Message));
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _runningKind = null;
            _runningView = null;
            _runningDone.TrySetResult();
            _busyTimer.Stop();
            _busySince.Stop();
            Mouse.OverrideCursor = null;
            // Hidden keeps the strip's space, so the page doesn't move up under the pointer.
            BusyStrip.Visibility = Visibility.Hidden;
        }

        // A pick or action that cancelled this work starts next; the held reload then waits for it to end.
        if (_waitingKind is null && _heldReload.Take(ScreenHost.Content as ScreenBase, v => v.LastLoadBegan) is { } next)
            _ = RunAsync(next.RefreshAsync, WorkKind.Reload, next);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // Dialogs and links

    public bool Confirm(string message, string title)
        => Dialog(() => MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)) == MessageBoxResult.Yes;

    public void ShowError(string message) => Dialog(() => MessageBox.Show(this, message, Strings.Common_Error, MessageBoxButton.OK, MessageBoxImage.Error));

    public void ShowInfo(string message) => Dialog(() => MessageBox.Show(this, message, Strings.App_Name, MessageBoxButton.OK, MessageBoxImage.Information));

    /// <summary>Runs a modal dialog, counted so that losing focus to it isn't taken for leaving the application.</summary>
    public T Dialog<T>(Func<T> show)
    {
        _dialogDepth++;
        _dialogShownSinceActivation = true;
        try { return show(); }
        finally { _dialogDepth--; }
    }

    public void OpenUrl(string url)
    {
        Workspace.Runner.Note(string.Format(Strings.Activity_OpeningUrl, url));
        UiHelpers.OpenUrl(url);
    }

    public void Announce(string message)
    {
        Workspace.Runner.Note(message);
        AccessibilityHelper.Announce(ActivityText, message);
    }

    // Activity panel

    /// <summary>
    /// Called on whatever thread the command runner is on. The entry goes into the buffer and one flush is
    /// queued at background priority, behind input and rendering, so a burst of output lines reaches the panel
    /// as a single append and the window stays responsive while a command prints.
    /// </summary>
    private void OnActivity(object? sender, ActivityEntry entry)
    {
        _activity.Add(entry);
        if (entry.Kind == ActivityKind.Command) _runningCommand = ActivityBuffer.ShortCommand(entry.Text);
        if (Interlocked.Exchange(ref _flushQueued, 1) == 0)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, FlushActivity);
    }

    private void FlushActivity()
    {
        Interlocked.Exchange(ref _flushQueued, 0);
        var flush = _activity.Flush();
        if (flush.Trimmed) ActivityText.Text = flush.Whole;
        else if (flush.Added.Length > 0) ActivityText.AppendText(flush.Added);
        ActivityText.ScrollToEnd();
        UpdateBusyText();
    }

    private void CopyActivity_Click(object sender, RoutedEventArgs e) => UiHelpers.CopyToClipboard(ActivityText.Text);

    private void ClearActivity_Click(object sender, RoutedEventArgs e)
    {
        _activity.Clear();
        ActivityText.Clear();
    }

    // Busy strip

    /// <summary>The strip names the command that is running and how long the run has been going, once a second.</summary>
    private void UpdateBusyText()
    {
        if (_cts is null) return;
        var seconds = (int)_busySince.Elapsed.TotalSeconds;
        BusyText.Text = _runningCommand.Length == 0 ? Strings.Busy_Working : string.Format(Strings.Busy_Running, seconds, _runningCommand);
    }

    private void BusyTimer_Tick(object? sender, EventArgs e) => UpdateBusyText();

    // Header buttons

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Workspace.HasRepository && Workspace.Settings.CurrentRepository is null) { Navigate(Screen.Repositories); return; }
        EditRepository(Workspace.Current);
    }

    /// <summary>Opens the settings window for a known repository, or for a new one when null.</summary>
    public void EditRepository(RepositorySettings? repository, bool readFromGitHub = false)
    {
        var dialog = new SettingsWindow(this, repository, readFromGitHub) { Owner = this };
        Dialog(dialog.ShowDialog);
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        Dialog(new AboutWindow(Workspace) { Owner = this }.ShowDialog);
    }
}
