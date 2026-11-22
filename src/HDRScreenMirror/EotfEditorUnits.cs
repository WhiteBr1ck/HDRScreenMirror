namespace HDRScreenMirror;

// Both views store nits. Normalized output is a PQ code, not a peak ratio.
internal static class EotfEditorUnits
{
    public static double ToDisplay(double nits, bool normalized) => normalized
        ? nits <= 0 ? 0 : AblProfile.PqOetf(nits)
        : nits;

    public static double ToNits(double value, bool normalized) => normalized
        ? AblProfile.PqEotf(value)
        : value;
}
