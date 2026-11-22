using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using DesignatedDriver.Core;

namespace DesignatedDriver.Helpers;

/// <summary>A monitor's work area in its own WPF units, with the scale (DPI divided by 96) that produced them.</summary>
public readonly record struct MonitorArea(WindowRect WorkArea, double Scale);

/// <summary>
/// The monitor under the mouse, read through Win32 because WPF only exposes the primary monitor's work area.
/// Monitors can run at different scales, and a window's own DPI is that of the monitor it was created on, so the
/// work area is converted with the target monitor's DPI, not the window's.
/// </summary>
public static class MonitorHelper
{
    private const uint MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect Work; public uint Flags; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    /// <summary>
    /// The work area (the monitor less the taskbar) of the monitor under the mouse, in that monitor's WPF units,
    /// or null when Windows will not say.
    /// </summary>
    public static MonitorArea? AreaUnderMouse()
    {
        if (!GetCursorPos(out var point)) return null;
        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return null;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return null;
        if (GetDpiForMonitor(monitor, EffectiveDpi, out uint dpiX, out _) != 0 || dpiX == 0) return null;

        double scale = dpiX / 96.0;
        var work = info.Work;
        var area = new WindowRect(work.Left / scale, work.Top / scale, (work.Right - work.Left) / scale, (work.Bottom - work.Top) / scale);
        return new MonitorArea(area, scale);
    }

    /// <summary>The scale of the monitor the window is on now: what WPF multiplies Left, Top, Width and Height by.</summary>
    public static double ScaleOf(Window window) => VisualTreeHelper.GetDpi(window).DpiScaleX;
}
