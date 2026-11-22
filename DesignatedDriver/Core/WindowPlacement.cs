namespace DesignatedDriver.Core;

/// <summary>A rectangle in WPF units: where a window is, or the part of a monitor that windows may use.</summary>
public readonly record struct WindowRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

/// <summary>
/// Where the main window opens. WPF's CenterScreen centres the window on the monitor under the mouse without
/// asking whether it fits, so on a small monitor the window overhangs onto the next one. This centres it on the
/// monitor's work area and shrinks it first where it has to. Pure, so the self-test covers it.
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// The window, no larger than the work area and centred in it. A window that fits keeps its size; one that
    /// does not is shrunk to the work area on that side. The result always lies wholly inside the work area.
    /// </summary>
    public static WindowRect CentreAndFit(double width, double height, WindowRect workArea)
    {
        double fitWidth = Math.Min(width, workArea.Width);
        double fitHeight = Math.Min(height, workArea.Height);
        double left = workArea.Left + (workArea.Width - fitWidth) / 2;
        double top = workArea.Top + (workArea.Height - fitHeight) / 2;
        return new WindowRect(Math.Floor(left), Math.Floor(top), fitWidth, fitHeight);
    }
}
