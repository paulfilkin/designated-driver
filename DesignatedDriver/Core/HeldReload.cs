namespace DesignatedDriver.Core;

/// <summary>Whether a running reload stops for another screen's.</summary>
public static class HeldReload
{
    /// <summary>
    /// True when the running work is a reload of a screen other than the one now asked for: the user has left that
    /// screen, so its load is stopped and the new screen's starts. A user action never gives way, and nor does a
    /// reload of the same screen.
    /// </summary>
    public static bool GivesWay<T>(bool runningIsReload, T? runningView, T requested) where T : class
        => runningIsReload && runningView is not null && !ReferenceEquals(runningView, requested);
}

/// <summary>
/// A screen reload asked for while other work was running, kept until that work ends. Only the latest request is
/// kept. It is handed back only if its screen is still the one shown and no load that began after the request has
/// finished, so a screen the user has left, or one that an action has already reloaded, is not loaded again.
/// Generic and free of WPF, so the self-test covers the rule with plain objects.
/// </summary>
public sealed class HeldReload<T> where T : class
{
    private T? _view;
    private DateTime _requested;

    public bool HasHeld => _view is not null;

    /// <summary>Keeps a reload for this view, replacing any held before.</summary>
    public void Hold(T view, DateTime requested)
    {
        _view = view;
        _requested = requested;
    }

    public void Drop() => _view = null;

    /// <summary>
    /// Clears what is held and returns it if it should still run: the held view is the one shown, and its last
    /// finished load began before the request.
    /// </summary>
    public T? Take(T? shown, Func<T, DateTime> lastLoadBegan)
    {
        var view = _view;
        var requested = _requested;
        _view = null;
        if (view is null || !ReferenceEquals(view, shown)) return null;
        return lastLoadBegan(view) < requested ? view : null;
    }
}
