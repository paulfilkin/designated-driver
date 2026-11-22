using System.Windows;
using DesignatedDriver.Core;
using DesignatedDriver.Services;

namespace DesignatedDriver.Helpers;

/// <summary>What a screen can ask of the main window.</summary>
public interface IShell
{
    Workspace Workspace { get; }
    Window Window { get; }
    bool IsBusy { get; }

    /// <summary>Runs work with the busy strip shown and command failures reported in a dialog. Ignored while busy.</summary>
    Task RunAsync(Func<CancellationToken, Task> work);

    /// <summary>
    /// Runs a pick that only loads what the user picked, such as a pull request's files. A newer pick stops it and
    /// takes its place, and so does a user action; it never interrupts a user action, and is refused while one runs.
    /// </summary>
    Task RunPickAsync(Func<CancellationToken, Task> work);

    /// <summary>Shows a screen and reloads it. With a pull request number, the Review screen selects that pull request.</summary>
    void Navigate(Screen screen, int? pullRequest = null);
    /// <summary>Shows the Scan screen with a folder or an owner/name already chosen.</summary>
    void NavigateToScan(string? folder, string ownerAndName);
    /// <summary>Shows Repositories with the clone card open for an owner/name.</summary>
    void NavigateToClone(string ownerAndName);
    /// <summary>Shows the set-up wizard with a folder or an owner/name already chosen, and what a scan found when there was one.</summary>
    void NavigateToSetup(string? folder, string ownerAndName, ScanFacts? facts = null);
    /// <summary>Shows one of the tool's own windows modally. True when it was accepted.</summary>
    bool ShowDialog(System.Windows.Window dialog);
    /// <summary>
    /// Opens the settings window for a known repository, or for a new one when null. With readFromGitHub, a
    /// repository without a manifest has what it already uses read from GitHub into the boxes, to confirm.
    /// </summary>
    void EditRepository(RepositorySettings? repository, bool readFromGitHub = false);
    bool Confirm(string message, string title);
    void ShowError(string message);
    void ShowInfo(string message);
    void OpenUrl(string url);
    /// <summary>Says something to screen readers through the activity panel's live region.</summary>
    void Announce(string message);
}
