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
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint TimerAllAccess = 0x001F0003;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwarenessContext(nint value);

    [LibraryImport("user32.dll")]
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
}
