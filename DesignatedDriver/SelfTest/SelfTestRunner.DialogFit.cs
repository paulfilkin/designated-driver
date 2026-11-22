using DesignatedDriver.Core;

namespace DesignatedDriver.SelfTest;

/// <summary>A dialog that sizes itself to its content fits the screen it opens on, and its content scrolls instead.</summary>
public static partial class SelfTestRunner
{
    private static void DialogFitChecks(Action<bool, string> check)
    {
        // A laptop's work area at 150 %: 1707 by 1067 less a 48 px taskbar.
        var laptop = new WindowRect(0, 0, 1707, 1019);
        check(DialogFit.MaxHeight(laptop.Height) == laptop.Height - 2 * DialogFit.ScreenMargin && DialogFit.MaxHeight(200) == DialogFit.SmallestHeight,
            "Dialog fit: the window is capped at the work area less a margin, and never below the smallest height");
        double height = DialogFit.MaxHeight(laptop.Height);
        check(DialogFit.Top(-200, height, laptop) == 0 && DialogFit.Top(400, height, laptop) == laptop.Height - height && DialogFit.Top(20, height, laptop) == 20,
            "Dialog fit: a window placed off the top or bottom is moved inside the work area, and one already inside stays put");
        var second = new WindowRect(1707, -300, 1920, 1040);
        check(DialogFit.Top(-500, 600, second) == -300 && DialogFit.Top(900, 600, second) == -300 + 1040 - 600,
            "Dialog fit: on a second monitor above the first, the work area's own top and bottom are used");
    }
}
