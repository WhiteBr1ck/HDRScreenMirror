using System.Drawing;
using HDRScreenMirror.Interop;
using Vortice.DXGI;
using static Vortice.DXGI.DXGI;

namespace HDRScreenMirror.DirectX;

internal static class DxgiDisplayEnumerator
{
    public static IReadOnlyList<DisplayTarget> GetDisplays()
    {
        List<DisplayTarget> displays = [];
        IReadOnlyDictionary<string, DisplayIdentity> displayIdentities =
            DisplayIdentityResolver.GetActiveDisplays();
        using IDXGIFactory2 factory = CreateDXGIFactory1<IDXGIFactory2>();

        int globalIndex = 0;
        for (uint adapterIndex = 0; factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1? adapter).Success; adapterIndex++)
        {
            if (adapter is null)
                continue;

            using (adapter)
            {
                string adapterName = adapter.Description1.Description.TrimEnd('\0', ' ');

                for (uint outputIndex = 0; adapter.EnumOutputs(outputIndex, out IDXGIOutput? output).Success; outputIndex++)
                {
                    if (output is null)
                        continue;

                    using (output)
                    {
                        OutputDescription description = output.Description;
                        Rectangle bounds = Rectangle.FromLTRB(
                            description.DesktopCoordinates.Left,
                            description.DesktopCoordinates.Top,
                            description.DesktopCoordinates.Right,
                            description.DesktopCoordinates.Bottom);

                        ColorSpaceType colorSpace = (ColorSpaceType)(-1);
                        uint bitsPerColor = 0;
                        float minLuminance = 0;
                        float maxLuminance = 0;
                        float maxFullFrameLuminance = 0;

                        using IDXGIOutput6? output6 = output.QueryInterfaceOrNull<IDXGIOutput6>();
                        if (output6 is not null)
                        {
                            OutputDescription1 description1 = output6.Description1;
                            colorSpace = description1.ColorSpace;
                            bitsPerColor = description1.BitsPerColor;
                            minLuminance = description1.MinLuminance;
                            maxLuminance = description1.MaxLuminance;
                            maxFullFrameLuminance = description1.MaxFullFrameLuminance;
                        }

                        string deviceName = description.DeviceName.TrimEnd('\0');
                        DisplayIdentity identity = displayIdentities.TryGetValue(deviceName, out DisplayIdentity? resolved)
                            ? resolved
                            : new DisplayIdentity(
                                deviceName.StartsWith(@"\\.\", StringComparison.Ordinal)
                                    ? deviceName[4..]
                                    : deviceName,
                                deviceName);

                        displays.Add(new DisplayTarget(
                            globalIndex++,
                            (int)adapterIndex,
                            (int)outputIndex,
                            adapterName,
                            deviceName,
                            identity.FriendlyName,
                            identity.StableId,
                            bounds,
                            description.Rotation,
                            description.AttachedToDesktop,
                            colorSpace,
                            bitsPerColor,
                            minLuminance,
                            maxLuminance,
                            maxFullFrameLuminance));
                    }
                }
            }
        }

        return displays;
    }
}
