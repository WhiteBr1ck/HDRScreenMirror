using System.Drawing;
using Vortice.DXGI;

namespace HDRScreenMirror;

internal sealed record DisplayTarget(
    int GlobalIndex,
    int AdapterIndex,
    int OutputIndex,
    string AdapterName,
    string DeviceName,
    Rectangle Bounds,
    ModeRotation Rotation,
    bool AttachedToDesktop,
    ColorSpaceType ColorSpace,
    uint BitsPerColor,
    float MinLuminance,
    float MaxLuminance,
    float MaxFullFrameLuminance)
{
    public bool IsHdrActive => ColorSpace == ColorSpaceType.RgbFullG2084NoneP2020;

    public override string ToString()
    {
        string hdr = Localization.T(IsHdrActive ? "HdrOn" : "HdrOff");
        return $"{GlobalIndex}: {DeviceName}  {Bounds.Width}×{Bounds.Height}  {hdr}  {AdapterName}";
    }

    public string ToDiagnosticString()
    {
        return $"[{GlobalIndex}] adapter={AdapterIndex}, output={OutputIndex}, device={DeviceName}, " +
               $"bounds={Bounds.Left},{Bounds.Top},{Bounds.Width}x{Bounds.Height}, rotation={Rotation}, " +
               $"colorSpace={ColorSpace}, bits={BitsPerColor}, min={MinLuminance:F4}, " +
               $"max={MaxLuminance:F1}, maxFFL={MaxFullFrameLuminance:F1}, gpu={AdapterName}";
    }
}
