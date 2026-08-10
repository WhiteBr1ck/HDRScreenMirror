using System.Runtime.InteropServices;

namespace HDRScreenMirror.Interop;

internal sealed record DisplayIdentity(string FriendlyName, string StableId);

internal static class DisplayIdentityResolver
{
    private const uint QueryOnlyActivePaths = 0x00000002;
    private const uint GetSourceName = 1;
    private const uint GetTargetName = 2;
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    public static IReadOnlyDictionary<string, DisplayIdentity> GetActiveDisplays()
    {
        Dictionary<string, DisplayIdentity> identities = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int result = GetDisplayConfigBufferSizes(
                    QueryOnlyActivePaths,
                    out uint pathCount,
                    out uint modeCount);
                if (result != ErrorSuccess)
                    return identities;

                DisplayConfigPathInfo[] paths = new DisplayConfigPathInfo[pathCount];
                DisplayConfigModeInfo[] modes = new DisplayConfigModeInfo[modeCount];
                result = QueryDisplayConfig(
                    QueryOnlyActivePaths,
                    ref pathCount,
                    paths,
                    ref modeCount,
                    modes,
                    nint.Zero);
                if (result == ErrorInsufficientBuffer)
                    continue;
                if (result != ErrorSuccess)
                    return identities;

                for (int index = 0; index < pathCount; index++)
                {
                    DisplayConfigPathInfo path = paths[index];
                    DisplayConfigSourceDeviceName source = new()
                    {
                        Header = new DisplayConfigDeviceInfoHeader
                        {
                            Type = GetSourceName,
                            Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                            AdapterId = path.SourceInfo.AdapterId,
                            Id = path.SourceInfo.Id
                        }
                    };
                    DisplayConfigTargetDeviceName target = new()
                    {
                        Header = new DisplayConfigDeviceInfoHeader
                        {
                            Type = GetTargetName,
                            Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                            AdapterId = path.TargetInfo.AdapterId,
                            Id = path.TargetInfo.Id
                        }
                    };

                    if (GetSourceDeviceInfo(ref source) != ErrorSuccess ||
                        GetTargetDeviceInfo(ref target) != ErrorSuccess ||
                        string.IsNullOrWhiteSpace(source.ViewGdiDeviceName))
                    {
                        continue;
                    }

                    string deviceName = source.ViewGdiDeviceName.TrimEnd('\0');
                    string friendlyName = target.MonitorFriendlyDeviceName?.TrimEnd('\0', ' ') ?? string.Empty;
                    string stableId = target.MonitorDevicePath?.TrimEnd('\0') ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(stableId))
                        stableId = deviceName;
                    if (string.IsNullOrWhiteSpace(friendlyName))
                        friendlyName = TrimDisplayPrefix(deviceName);

                    identities[deviceName] = new DisplayIdentity(friendlyName, stableId);
                }

                return identities;
            }
        }
        catch (EntryPointNotFoundException)
        {
        }
        catch (DllNotFoundException)
        {
        }

        return identities;
    }

    private static string TrimDisplayPrefix(string deviceName) =>
        deviceName.StartsWith(@"\\.\", StringComparison.Ordinal) ? deviceName[4..] : deviceName;

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numberOfPaths,
        out uint numberOfModes);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numberOfPaths,
        [In, Out] DisplayConfigPathInfo[] pathInfoArray,
        ref uint numberOfModes,
        [In, Out] DisplayConfigModeInfo[] modeInfoArray,
        nint currentTopologyId);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", ExactSpelling = true)]
    private static extern int GetSourceDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", ExactSpelling = true)]
    private static extern int GetTargetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DisplayConfigRational RefreshRate;
        public uint ScanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfig2DRegion
    {
        public uint Width;
        public uint Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectL
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigVideoSignalInfo
    {
        public ulong PixelRate;
        public DisplayConfigRational HorizontalSyncFrequency;
        public DisplayConfigRational VerticalSyncFrequency;
        public DisplayConfig2DRegion ActiveSize;
        public DisplayConfig2DRegion TotalSize;
        public uint VideoStandard;
        public uint ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigTargetMode
    {
        public DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigSourceMode
    {
        public uint Width;
        public uint Height;
        public uint PixelFormat;
        public PointL Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDesktopImageInfo
    {
        public PointL PathSourceSize;
        public RectL DesktopImageRegion;
        public RectL DesktopImageClip;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DisplayConfigModeInfoUnion
    {
        [FieldOffset(0)] public DisplayConfigTargetMode TargetMode;
        [FieldOffset(0)] public DisplayConfigSourceMode SourceMode;
        [FieldOffset(0)] public DisplayConfigDesktopImageInfo DesktopImageInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
        public DisplayConfigModeInfoUnion ModeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string? MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? MonitorDevicePath;
    }
}
