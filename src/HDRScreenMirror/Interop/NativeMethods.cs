using System.Runtime.InteropServices;

namespace HDRScreenMirror.Interop;

internal static partial class NativeMethods
{
    internal const uint WdaNone = 0x00000000;
    internal const uint WdaExcludeFromCapture = 0x00000011;
    internal const uint EsContinuous = 0x80000000;
    internal const uint EsSystemRequired = 0x00000001;
    internal const uint EsDisplayRequired = 0x00000002;
    internal const int WmDisplayChange = 0x007E;
    internal const int WmHotKey = 0x0312;
    internal const uint ModControl = 0x0002;
    internal const uint ModNoRepeat = 0x4000;
    internal const uint VirtualKeyF8 = 0x77;
    internal const uint VirtualKeyF9 = 0x78;
    internal const uint VirtualKeyF10 = 0x79;
    internal const uint VirtualKeyF11 = 0x7A;
    internal const uint VirtualKeyF12 = 0x7B;
    internal const uint WaitObject0 = 0x00000000;
    internal const uint WaitTimeout = 0x00000102;
    internal const uint WaitFailed = 0xFFFFFFFF;

    private static readonly nint DpiAwarenessContextPerMonitorAwareV2 = new(-4);
    private static readonly nint HwndTopMost = new(-1);
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint TimerAllAccess = 0x001F0003;
    private const uint GwHwndPrev = 3;
    private const uint DwmwaCloaked = 14;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpNoSendChanging = 0x0400;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwarenessContext(nint value);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowDisplayAffinity(nint window, uint affinity);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmFlush();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint window, int id);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint window, uint command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out NativeRect rectangle);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(
        nint window,
        uint attribute,
        out int value,
        uint valueSize);

    [LibraryImport("kernel32.dll")]
    internal static partial uint SetThreadExecutionState(uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "CreateWaitableTimerExW",
        StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    private static partial nint CreateWaitableTimerEx(
        nint timerAttributes,
        string? timerName,
        uint flags,
        uint desiredAccess);

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "CreateWaitableTimerW",
        StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    private static partial nint CreateWaitableTimer(
        nint timerAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        string? timerName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWaitableTimer(
        nint timer,
        in long dueTime,
        int period,
        nint completionRoutine,
        nint completionArgument,
        [MarshalAs(UnmanagedType.Bool)] bool resume);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint processId);

    internal static void TryEnablePerMonitorDpiAwareness()
    {
        try
        {
            SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    internal static nint CreateFrameRateTimer()
    {
        try
        {
            nint timer = CreateWaitableTimerEx(
                nint.Zero,
                null,
                CreateWaitableTimerHighResolution,
                TimerAllAccess);
            if (timer != nint.Zero)
                return timer;
        }
        catch (EntryPointNotFoundException)
        {
        }

        return CreateWaitableTimer(nint.Zero, false, null);
    }

    internal static bool SetFrameRateTimer(nint timer, long relativeHundredNanoseconds)
    {
        long dueTime = -Math.Max(1, relativeHundredNanoseconds);
        return SetWaitableTimer(timer, in dueTime, 0, nint.Zero, nint.Zero, false);
    }

    internal static void EnsureOverlayAboveOccludingWindows(nint overlayWindow)
    {
        if (overlayWindow == nint.Zero ||
            !GetWindowRect(overlayWindow, out NativeRect overlayBounds))
        {
            return;
        }

        nint window = overlayWindow;
        for (int i = 0; i < 2048; i++)
        {
            window = GetWindow(window, GwHwndPrev);
            if (window == nint.Zero)
                return;

            GetWindowThreadProcessId(window, out uint processId);
            if (processId == (uint)Environment.ProcessId ||
                !IsWindowVisible(window) ||
                IsWindowCloaked(window) ||
                !GetWindowRect(window, out NativeRect windowBounds) ||
                !overlayBounds.Intersects(windowBounds))
            {
                continue;
            }

            SetWindowPos(
                overlayWindow,
                HwndTopMost,
                0,
                0,
                0,
                0,
                SwpNoSize | SwpNoMove | SwpNoActivate | SwpNoOwnerZOrder | SwpNoSendChanging);
            return;
        }
    }

    private static bool IsWindowCloaked(nint window)
    {
        int result = DwmGetWindowAttribute(window, DwmwaCloaked, out int cloaked, sizeof(int));
        return result == 0 && cloaked != 0;
    }

    internal static void AttachToParentConsole()
    {
        const uint attachParentProcess = 0xFFFFFFFF;
        AttachConsole(attachParentProcess);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly bool Intersects(NativeRect other) =>
            Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
    }

}
