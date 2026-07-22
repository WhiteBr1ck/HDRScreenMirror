namespace HDRScreenMirror;

internal sealed record GamutTelemetry(
    int HistogramWidth,
    int HistogramHeight,
    uint[] Histogram,
    double SrgbPercentage,
    double DciP3Percentage,
    double Bt2020Percentage,
    double OutsidePercentage,
    ulong AnalyzedPixels);
