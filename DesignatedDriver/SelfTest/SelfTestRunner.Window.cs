using DesignatedDriver.Core;

namespace DesignatedDriver.SelfTest;

/// <summary>Window placement checks: the main window is centred on the monitor's work area and never larger than it.</summary>
public static partial class SelfTestRunner
{
    private static void WindowPlacementChecks(Action<bool, string> check)
    {
        // A second monitor to the right of a 1920-wide primary, with a 40-unit taskbar at the bottom.
        var wide = new WindowRect(1920, 0, 2560, 1400);
        var fits = WindowPlacement.CentreAndFit(1180, 820, wide);
        check(fits == new WindowRect(2610, 290, 1180, 820), "Placement: a window that fits keeps its size and is centred on that monitor");

        // A laptop monitor at 150% scaling: 1280 by 720 units, 48 of them taskbar.
        var small = new WindowRect(0, 0, 1280, 672);
        var shrunk = WindowPlacement.CentreAndFit(1180, 820, small);
        check(shrunk == new WindowRect(50, 0, 1180, 672), "Placement: a window taller than the work area is shrunk to it and stays centred across");

        // A portrait monitor to the left of the primary, narrower than the window on both counts.
        var portrait = new WindowRect(-1080, -200, 1080, 1880);
        var both = WindowPlacement.CentreAndFit(1180, 820, portrait);
        check(both == new WindowRect(-1080, 330, 1080, 820), "Placement: a window wider than the work area takes its full width");

        foreach (var (area, result) in new[] { (wide, fits), (small, shrunk), (portrait, both) })
        {
            check(result.Left >= area.Left && result.Top >= area.Top && result.Right <= area.Right && result.Bottom <= area.Bottom,
                $"Placement: the result lies inside the work area at {area.Left},{area.Top}");
        }
    }
}
