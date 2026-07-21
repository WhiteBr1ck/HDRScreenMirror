namespace HDRScreenMirror;

internal sealed record MirrorTelemetry(
    string State,
    double FramesPerSecond,
    string InputFormat,
    string InputKind,
    uint Width,
    uint Height,
    long TotalFrames,
    long Timeouts,
    bool CursorVisible);
