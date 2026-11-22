using System.Windows.Controls;
using DesignatedDriver.Helpers;
using DesignatedDriver.Services;

namespace DesignatedDriver.Screens;

/// <summary>
/// A screen in the main window. Refreshed each time it is shown, after the settings change, and when the
/// window comes back to the front after a while, so what it shows is what GitHub says now.
/// </summary>
public abstract class ScreenBase : UserControl
{
    protected ScreenBase(IShell shell)
    {
        Shell = shell;
        Focusable = false;
    }

    protected IShell Shell { get; }
    protected Workspace Workspace => Shell.Workspace;
    /// <summary>The current repository's settings. An empty one, with no folder, when none is chosen.</summary>
    protected RepositorySettings Repo => Shell.Workspace.Current;

    public bool NeedsRefresh { get; protected set; } = true;

    /// <summary>False for a screen whose content is the result of an action the user started, such as a scan.</summary>
    public virtual bool ReloadsOnReturn => true;

    /// <summary>When the screen last finished loading; MinValue until it has.</summary>
    public DateTime LastLoaded { get; private set; } = DateTime.MinValue;

    /// <summary>When the last load that finished began; MinValue until one has. Data older than this is not shown.</summary>
    public DateTime LastLoadBegan { get; private set; } = DateTime.MinValue;

    public bool IsOlderThan(TimeSpan age) => DateTime.UtcNow - LastLoaded > age;

    public void Invalidate() => NeedsRefresh = true;

    public async Task RefreshAsync(CancellationToken ct)
    {
        var began = DateTime.UtcNow;
        NeedsRefresh = false;
        await LoadAsync(ct);
        LastLoaded = DateTime.UtcNow;
        LastLoadBegan = began;
    }

    protected abstract Task LoadAsync(CancellationToken ct);
}
