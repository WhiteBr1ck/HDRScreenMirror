namespace HDRScreenMirror;

internal sealed record LuminanceTelemetry(
    double AverageNits,
    double MaximumNits,
    double MinimumNits,
    uint MaximumX,
    uint MaximumY,
    uint MinimumX,
    uint MinimumY,
    bool PointerInCaptureArea,
    double PointerRegionNits);
