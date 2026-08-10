using System.Drawing;
using Vortice.DXGI;

namespace HDRScreenMirror;

internal sealed record DisplayTarget(
    int GlobalIndex,
    int AdapterIndex,
    int OutputIndex,
    string AdapterName,
    string DeviceName,
    string FriendlyName,
    string StableId,
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
    public string ShortDeviceName =>
        DeviceName.StartsWith(@"\\.\", StringComparison.Ordinal) ? DeviceName[4..] : DeviceName;
    public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName) ? ShortDeviceName : FriendlyName;

    public override string ToString()
    {
        string hdr = Localization.T(IsHdrActive ? "HdrOn" : "HdrOff");
        return $"{GlobalIndex}: {DisplayName} [{ShortDeviceName}]  {Bounds.Width}×{Bounds.Height}  {hdr}  {AdapterName}";
    }

    public string ToDiagnosticString()
    {
        return $"[{GlobalIndex}] adapter={AdapterIndex}, output={OutputIndex}, device={DeviceName}, " +
               $"name={DisplayName}, stableId={StableId}, " +
               $"bounds={Bounds.Left},{Bounds.Top},{Bounds.Width}x{Bounds.Height}, rotation={Rotation}, " +
               $"colorSpace={ColorSpace}, bits={BitsPerColor}, min={MinLuminance:F4}, " +
               $"max={MaxLuminance:F1}, maxFFL={MaxFullFrameLuminance:F1}, gpu={AdapterName}";
    }
}
