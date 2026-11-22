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
    internal const uint ModShift = 0x0004;
    internal const uint ModNoRepeat = 0x4000;
    internal const uint VirtualKeyF8 = 0x77;
    internal const uint VirtualKeyF9 = 0x78;
    internal const uint VirtualKeyF10 = 0x79;
    internal const uint VirtualKeyF11 = 0x7A;
    internal const uint VirtualKeyF12 = 0x7B;
    internal const uint WaitObject0 = 0x00000000;
    internal const uint WaitTimeout = 0x00000102;
    internal const uint WaitFailed = 0xFFFFFFFF;

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
    private const uint SwpShowWindow = 0x0040;
    private const uint DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const uint DwmwaUseImmersiveDarkMode = 20;
    private const uint DwmwaWindowCornerPreference = 33;
    private const int DwmwcpDefault = 0;
    private const int DwmwcpRound = 2;
    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const uint Srccopy = 0x00CC0020;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(
        nint window,
        uint attribute,
        ref int value,
        uint valueSize);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(nint window, ref Margins margins);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowCompositionAttribute(
        nint window,
        ref WindowCompositionAttributeData data);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint hdc, nint hObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint hObject);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(
        nint hdc,
        ref BitmapInfoHeader pbmi,
        uint usage,
        out nint ppvBits,
        nint hSection,
        uint offset);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(
        nint hdcDest,
        int xDest,
        int yDest,
        int width,
        int height,
        nint hdcSrc,
        int xSrc,
        int ySrc,
        uint rop);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

    internal static void ShowTopMostNoActivate(nint window, Rectangle bounds)
    {
        if (window == nint.Zero)
            return;

        SetWindowPos(
            window,
            HwndTopMost,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SwpNoActivate | SwpNoOwnerZOrder | SwpShowWindow);
    }

    internal static void HideWindow(nint window)
    {
        if (window != nint.Zero)
            ShowWindow(window, 0);
    }

    internal static void ApplyImmersiveDarkMode(nint window, bool dark)
    {
        if (window == nint.Zero)
            return;

        try
        {
            int enabled = dark ? 1 : 0;
            if (DwmSetWindowAttribute(window, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(window, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        }
        catch
        {
        }
    }

    internal static bool TryApplyAcrylicGlass(nint window, bool enabled, Color tintColor, double opacity)
    {
        if (window == nint.Zero)
            return false;

        try
        {
            int corner = enabled ? DwmwcpRound : DwmwcpDefault;
            DwmSetWindowAttribute(window, DwmwaWindowCornerPreference, ref corner, sizeof(int));

            Margins margins = enabled
                ? new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 }
                : default;
            DwmExtendFrameIntoClientArea(window, ref margins);

            byte alpha = (byte)Math.Clamp((int)Math.Round(opacity * 218.0), 42, 240);
            uint gradientColor = ((uint)alpha << 24) | ((uint)tintColor.B << 16) | ((uint)tintColor.G << 8) | tintColor.R;
            AccentPolicy policy = enabled
                ? new AccentPolicy
                {
                    AccentState = AccentEnableAcrylicBlurBehind,
                    AccentFlags = 2,
                    GradientColor = gradientColor
                }
                : new AccentPolicy { AccentState = AccentDisabled };

            unsafe
            {
                WindowCompositionAttributeData data = new()
                {
                    Attribute = WcaAccentPolicy,
                    Data = (nint)(&policy),
                    SizeOfData = sizeof(AccentPolicy)
                };
                return SetWindowCompositionAttribute(window, ref data);
            }
        }
        catch
        {
            return false;
        }
    }

    internal static unsafe void BlitPremultipliedBitmap(Graphics destination, Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        if (width <= 0 || height <= 0)
            return;

        System.Drawing.Imaging.BitmapData sourceData = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        nint destHdc = nint.Zero;
        nint memDc = nint.Zero;
        nint dib = nint.Zero;
        nint oldObj = nint.Zero;
        try
        {
            destHdc = destination.GetHdc();
            memDc = CreateCompatibleDC(destHdc);
            BitmapInfoHeader header = new()
            {
                Size = (uint)sizeof(BitmapInfoHeader),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = 0
            };
            dib = CreateDIBSection(memDc, ref header, 0, out nint dibBits, nint.Zero, 0);
            if (dib == nint.Zero || dibBits == nint.Zero)
                return;

            int rowBytes = width * 4;
            byte* srcPtr = (byte*)sourceData.Scan0;
            byte* dstPtr = (byte*)dibBits;
            for (int y = 0; y < height; y++)
            {
                Buffer.MemoryCopy(
                    srcPtr + y * sourceData.Stride,
                    dstPtr + y * rowBytes,
                    rowBytes,
                    rowBytes);
            }

            oldObj = SelectObject(memDc, dib);
            BitBlt(destHdc, 0, 0, width, height, memDc, 0, 0, Srccopy);
        }
        finally
        {
            if (oldObj != nint.Zero && memDc != nint.Zero)
                SelectObject(memDc, oldObj);
            if (dib != nint.Zero)
                DeleteObject(dib);
            if (memDc != nint.Zero)
                DeleteDC(memDc);
            if (destHdc != nint.Zero)
                destination.ReleaseHdc(destHdc);
            bitmap.UnlockBits(sourceData);
        }
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        internal int Left;
        internal int Right;
        internal int Top;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        internal int AccentState;
        internal int AccentFlags;
        internal uint GradientColor;
        internal int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        internal int Attribute;
        internal nint Data;
        internal int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        internal uint Size;
        internal int Width;
        internal int Height;
        internal ushort Planes;
        internal ushort BitCount;
        internal uint Compression;
        internal uint SizeImage;
        internal int XPelsPerMeter;
        internal int YPelsPerMeter;
        internal uint ClrUsed;
        internal uint ClrImportant;
    }
}
