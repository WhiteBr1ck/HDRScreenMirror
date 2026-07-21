using System.Runtime.InteropServices;
using System.Text;

namespace HDRScreenMirror.UI;

internal static class WindowRelocator
{
    private const uint MonitorDefaultToNull = 0;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpAsyncWindowPos = 0x4000;
    private const int DwmwaCloaked = 14;

    private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "MultitaskingViewFrame",
        "ApplicationManager_ImmersiveShellWindow"
    };

    public static int MoveWindowsToCapture(
        IReadOnlyList<DisplayTarget> outputDisplays,
        Rectangle captureWorkArea)
    {
        Dictionary<nint, Rectangle> outputBoundsByMonitor = [];
        foreach (DisplayTarget output in outputDisplays)
        {
            NativePoint center = new(
                output.Bounds.Left + output.Bounds.Width / 2,
                output.Bounds.Top + output.Bounds.Height / 2);
            nint monitor = MonitorFromPoint(center, MonitorDefaultToNull);
            if (monitor != 0)
                outputBoundsByMonitor[monitor] = output.Bounds;
        }

        if (outputBoundsByMonitor.Count == 0)
            return 0;

        int moved = 0;
        uint ownProcessId = (uint)Environment.ProcessId;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || IsIconic(window) || GetWindowTextLength(window) == 0)
                return true;

            GetWindowThreadProcessId(window, out uint processId);
            if (processId == ownProcessId)
                return true;

            StringBuilder className = new(256);
            GetClassName(window, className, className.Capacity);
            if (ShellWindowClasses.Contains(className.ToString()))
                return true;

            if (DwmGetWindowAttribute(window, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            nint monitor = MonitorFromWindow(window, MonitorDefaultToNull);
            if (!outputBoundsByMonitor.TryGetValue(monitor, out Rectangle outputBounds))
                return true;
            if (!GetWindowRect(window, out NativeRect nativeBounds))
                return true;

            Rectangle windowBounds = Rectangle.FromLTRB(
                nativeBounds.Left,
                nativeBounds.Top,
                nativeBounds.Right,
                nativeBounds.Bottom);
            if (windowBounds.Width <= 0 || windowBounds.Height <= 0)
                return true;

            Point destination = MapWindowPosition(windowBounds, outputBounds, captureWorkArea);
            const uint flags = SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder | SwpAsyncWindowPos;
            if (SetWindowPos(window, 0, destination.X, destination.Y, 0, 0, flags))
                moved++;
            return true;
        }, 0);

        return moved;
    }

    private static Point MapWindowPosition(
        Rectangle windowBounds,
        Rectangle sourceBounds,
        Rectangle destinationBounds)
    {
        int sourceHorizontalRange = Math.Max(1, sourceBounds.Width - windowBounds.Width);
        int sourceVerticalRange = Math.Max(1, sourceBounds.Height - windowBounds.Height);
        double horizontalRatio = Math.Clamp(
            (windowBounds.Left - sourceBounds.Left) / (double)sourceHorizontalRange,
            0d,
            1d);
        double verticalRatio = Math.Clamp(
            (windowBounds.Top - sourceBounds.Top) / (double)sourceVerticalRange,
            0d,
            1d);

        int destinationHorizontalRange = Math.Max(0, destinationBounds.Width - windowBounds.Width);
        int destinationVerticalRange = Math.Max(0, destinationBounds.Height - windowBounds.Height);
        return new Point(
            destinationBounds.Left + (int)Math.Round(horizontalRatio * destinationHorizontalRange),
            destinationBounds.Top + (int)Math.Round(verticalRatio * destinationVerticalRange));
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        int attribute,
        out int value,
        int valueSize);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRect(int Left, int Top, int Right, int Bottom);
}
