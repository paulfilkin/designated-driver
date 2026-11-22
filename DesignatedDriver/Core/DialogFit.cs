namespace DesignatedDriver.Core;

/// <summary>
/// Keeps a dialog that sizes itself to its content on the screen it opens on: no taller than the work area less a
/// margin, so its content scrolls instead, and moved up or down to stay inside it. Pure, so the self-test covers it.
/// </summary>
public static class DialogFit
{
    /// <summary>Space left above and below, so the window's frame and shadow stay clear of the taskbar and the top edge.</summary>
    public const double ScreenMargin = 24;

    /// <summary>The least a dialog is allowed, so a tiny or misreported work area doesn't squash it to nothing.</summary>
    public const double SmallestHeight = 360;

    /// <summary>The tallest the dialog may be on a work area of this height.</summary>
    public static double MaxHeight(double workAreaHeight) => Math.Max(SmallestHeight, workAreaHeight - 2 * ScreenMargin);

    /// <summary>The top that keeps a dialog of this height inside the work area, as near as can be to where it was put.</summary>
    public static double Top(double top, double height, WindowRect workArea)
        => Math.Max(workArea.Top, Math.Min(top, workArea.Top + workArea.Height - height));
}
