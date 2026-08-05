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
    double PointerRegionNits,
    double? PointerScaledNits,
    AblLuminanceEstimate? AblEstimate);

internal sealed record AblLuminanceEstimate(
    string ProfileId,
    string ProfileName,
    double EquivalentAplPercent,
    double AplPeakNits,
    double ScaleFactor,
    double AverageNits,
    double MaximumNits,
    double MinimumNits);
