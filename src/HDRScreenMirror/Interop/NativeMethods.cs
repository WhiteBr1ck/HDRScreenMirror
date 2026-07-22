using System.Runtime.InteropServices;

namespace HDRScreenMirror.Interop;

internal static partial class NativeMethods
{
    internal const uint WdaNone = 0x00000000;
    internal const uint WdaExcludeFromCapture = 0x00000011;
    internal const uint EsContinuous = 0x80000000;
    internal const uint EsSystemRequired = 0x00000001;
    internal const uint EsDisplayRequired = 0x00000002;
    internal const int WmHotKey = 0x0312;
    internal const uint ModControl = 0x0002;
    internal const uint ModNoRepeat = 0x4000;
    internal const uint VirtualKeyF8 = 0x77;
    internal const uint VirtualKeyF9 = 0x78;
    internal const uint VirtualKeyF10 = 0x79;
    internal const uint VirtualKeyF11 = 0x7A;
    internal const uint VirtualKeyF12 = 0x7B;

    private static readonly nint DpiAwarenessContextPerMonitorAwareV2 = new(-4);

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
