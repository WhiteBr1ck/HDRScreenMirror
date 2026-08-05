using System.Text.Json.Serialization;

namespace HDRScreenMirror;

internal sealed class AblMeasurementPoint
{
    public double AplPercent { get; set; }
    public double PeakNits { get; set; }

    public AblMeasurementPoint Clone() => new()
    {
        AplPercent = AplPercent,
        PeakNits = PeakNits
    };
}

internal sealed class AblProfile
{
    public static readonly int[] SupportedAplPercentages = [1, 3, 5, 10, 20, 25, 50, 75, 100];
    public const int EotfControlPointCount = 11;
    public const int EotfLutSize = 64;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<AblMeasurementPoint> Points { get; set; } = [];
    public List<double> EotfOutputNits { get; set; } = [];
    public double? EotfClipPqPercent { get; set; }

    [JsonIgnore]
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Name) &&
        TryGetPeakNits(1, out _) &&
        TryGetPeakNits(100, out _) &&
        Points.All(point => point.PeakNits is > 0 and <= 10000) &&
        HasValidCustomEotf;

    [JsonIgnore]
    public double ReferencePeakNits =>
        TryGetPeakNits(1, out double peakNits) ? peakNits : 0;

    [JsonIgnore]
    public bool UsesStandardPq => EotfOutputNits.Count == 0 && EotfClipPqPercent is null;

    [JsonIgnore]
    public bool HasCustomEotfCurve => EotfOutputNits.Count == EotfControlPointCount;

    [JsonIgnore]
    public double EffectiveEotfClipPqPercent => EotfClipPqPercent ??
        GetStandardClipPqPercent(ReferencePeakNits);

    [JsonIgnore]
    private bool HasValidCustomEotf =>
        (EotfOutputNits.Count == 0 ||
         EotfOutputNits.Count == EotfControlPointCount &&
         EotfOutputNits.All(value => double.IsFinite(value) && value is >= 0 and <= 10000)) &&
        (EotfClipPqPercent is null ||
         double.IsFinite(EotfClipPqPercent.Value) && EotfClipPqPercent.Value is >= 1 and <= 100);

    public AblProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        Points = Points.Select(point => point.Clone()).ToList(),
        EotfOutputNits = EotfOutputNits.ToList(),
        EotfClipPqPercent = EotfClipPqPercent
    };

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        Name = Name?.Trim() ?? string.Empty;
        Name = MigrateLegacyDefaultName(Name);
        Points ??= [];
        Points = Points
            .Where(point =>
                SupportedAplPercentages.Contains((int)Math.Round(point.AplPercent)) &&
                point.PeakNits is > 0 and <= 10000)
            .GroupBy(point => (int)Math.Round(point.AplPercent))
            .Select(group => new AblMeasurementPoint
            {
                AplPercent = group.Key,
                PeakNits = group.Last().PeakNits
            })
            .OrderBy(point => point.AplPercent)
            .ToList();

        NormalizeEotfCurve();
    }

    public IReadOnlyList<double> GetEotfEditorValues()
    {
        if (HasValidCustomEotf && EotfOutputNits.Count == EotfControlPointCount)
            return EotfOutputNits.ToArray();

        return CreateStandardEotfControlValues(ReferencePeakNits, EffectiveEotfClipPqPercent);
    }

    public void SetCustomEotf(IEnumerable<double> outputNits)
    {
        EotfOutputNits = outputNits.Take(EotfControlPointCount).ToList();
        NormalizeEotfCurve();
    }

    public void SetEotfClipPqPercent(double pqPercent)
    {
        EotfClipPqPercent = Math.Clamp(pqPercent, 1, 100);
        NormalizeEotfCurve();
    }

    public void RestoreStandardPq()
    {
        EotfOutputNits.Clear();
        EotfClipPqPercent = null;
    }

    public float[] BuildEotfLut()
    {
        double referencePeakNits = Math.Clamp(ReferencePeakNits, 0.001, 10000);
        bool custom = EotfOutputNits.Count == EotfControlPointCount && HasValidCustomEotf;
        double clipPq = Math.Clamp(EffectiveEotfClipPqPercent / 100.0, 0.01, 1);
        double standardClipPq = Math.Clamp(GetStandardClipPqPercent(referencePeakNits) / 100.0, 0.000001, 1);
        float[] lut = new float[EotfLutSize];
        for (int index = 0; index < lut.Length; index++)
        {
            double pq = index / (double)(lut.Length - 1);
            double mapped = custom
                ? EvaluateCustomEotf(pq, EotfOutputNits, clipPq, referencePeakNits)
                : EotfClipPqPercent is not null
                    ? Math.Min(PqEotf(Math.Clamp(pq * standardClipPq / clipPq, 0, 1)), referencePeakNits)
                    : Math.Min(PqEotf(pq), referencePeakNits);
            lut[index] = (float)Math.Clamp(mapped, 0, referencePeakNits);
        }

        lut[0] = custom ? (float)Math.Clamp(EotfOutputNits[0], 0, referencePeakNits) : 0;
        for (int index = 1; index < lut.Length; index++)
            lut[index] = Math.Max(lut[index], lut[index - 1]);
        return lut;
    }

    public static double[] CreateStandardEotfControlValues(
        double referencePeakNits,
        double? clipPqPercent = null)
    {
        referencePeakNits = Math.Clamp(referencePeakNits, 0, 10000);
        double standardClipPq = Math.Clamp(GetStandardClipPqPercent(referencePeakNits) / 100.0, 0.000001, 1);
        double effectiveClipPq = Math.Clamp((clipPqPercent ?? standardClipPq * 100.0) / 100.0, 0.01, 1);
        double[] values = new double[EotfControlPointCount];
        for (int index = 0; index < values.Length; index++)
        {
            double pq = index / (double)(values.Length - 1);
            double remappedPq = Math.Clamp(pq * standardClipPq / effectiveClipPq, 0, 1);
            values[index] = Math.Min(PqEotf(remappedPq), referencePeakNits);
        }
        return values;
    }

    public static double GetStandardClipPqPercent(double referencePeakNits) =>
        PqOetf(Math.Clamp(referencePeakNits, 0, 10000)) * 100.0;

    public static double PqEotf(double signal)
    {
        const double m1 = 2610.0 / 16384.0;
        const double m2 = 2523.0 / 32.0;
        const double c1 = 3424.0 / 4096.0;
        const double c2 = 2413.0 / 128.0;
        const double c3 = 2392.0 / 128.0;
        signal = Math.Clamp(signal, 0, 1);
        double power = Math.Pow(signal, 1.0 / m2);
        double numerator = Math.Max(power - c1, 0);
        double denominator = Math.Max(c2 - c3 * power, 1e-12);
        return 10000.0 * Math.Pow(numerator / denominator, 1.0 / m1);
    }

    public static double PqOetf(double luminanceNits)
    {
        const double m1 = 2610.0 / 16384.0;
        const double m2 = 2523.0 / 32.0;
        const double c1 = 3424.0 / 4096.0;
        const double c2 = 2413.0 / 128.0;
        const double c3 = 2392.0 / 128.0;
        double normalized = Math.Clamp(luminanceNits / 10000.0, 0, 1);
        double power = Math.Pow(normalized, m1);
        return Math.Pow((c1 + c2 * power) / (1 + c3 * power), m2);
    }

    private void NormalizeEotfCurve()
    {
        EotfOutputNits ??= [];
        if (EotfClipPqPercent is double clip &&
            (!double.IsFinite(clip) || clip is < 1 or > 100))
        {
            EotfClipPqPercent = null;
        }
        if (EotfOutputNits.Count == 0)
            return;

        double referencePeakNits = ReferencePeakNits;
        if (EotfOutputNits.Count != EotfControlPointCount ||
            referencePeakNits <= 0 ||
            EotfOutputNits.Any(value => !double.IsFinite(value)))
        {
            EotfOutputNits.Clear();
            return;
        }

        EotfOutputNits[0] = 0;
        double previous = 0;
        for (int index = 1; index < EotfOutputNits.Count - 1; index++)
        {
            double value = Math.Clamp(EotfOutputNits[index], previous, referencePeakNits);
            EotfOutputNits[index] = value;
            previous = value;
        }
        EotfOutputNits[^1] = referencePeakNits;
    }

    private static double EvaluateCustomEotf(
        double pq,
        IReadOnlyList<double> values,
        double clipPq,
        double referencePeakNits)
    {
        pq = Math.Clamp(pq, 0, 1);
        if (pq >= clipPq)
            return referencePeakNits;

        double position = Math.Clamp(pq, 0, 1) * (EotfControlPointCount - 1);
        int lowerIndex = Math.Min((int)Math.Floor(position), EotfControlPointCount - 2);
        int upperIndex = lowerIndex + 1;
        double lowerPq = lowerIndex / (double)(EotfControlPointCount - 1);
        double upperPq = upperIndex / (double)(EotfControlPointCount - 1);
        double upperValue = values[upperIndex];
        if (upperPq >= clipPq)
        {
            upperPq = clipPq;
            upperValue = referencePeakNits;
        }
        double amount = upperPq > lowerPq ? (pq - lowerPq) / (upperPq - lowerPq) : 1;
        double lower = Math.Log(1 + Math.Clamp(values[lowerIndex], 0, referencePeakNits));
        double upper = Math.Log(1 + Math.Clamp(upperValue, 0, referencePeakNits));
        return Math.Exp(lower + (upper - lower) * amount) - 1;
    }

    private static string MigrateLegacyDefaultName(string name)
    {
        const string ChinesePrefix = "OLED 配置 ";
        if (name.StartsWith(ChinesePrefix, StringComparison.Ordinal) &&
            int.TryParse(name[ChinesePrefix.Length..], out int chineseIndex) &&
            chineseIndex > 0)
        {
            return $"配置 {chineseIndex}";
        }

        const string EnglishPrefix = "OLED profile ";
        if (name.StartsWith(EnglishPrefix, StringComparison.Ordinal) &&
            int.TryParse(name[EnglishPrefix.Length..], out int englishIndex) &&
            englishIndex > 0)
        {
            return $"Profile {englishIndex}";
        }

        return name;
    }

    public bool TryGetPeakNits(int aplPercent, out double peakNits)
    {
        AblMeasurementPoint? point = Points.FirstOrDefault(candidate =>
            Math.Abs(candidate.AplPercent - aplPercent) < 0.001);
        peakNits = point?.PeakNits ?? 0;
        return point is not null && peakNits is > 0 and <= 10000;
    }

    public double InterpolatePeakNits(double aplPercent)
    {
        AblMeasurementPoint[] points = Points
            .Where(point => point.PeakNits is > 0 and <= 10000)
            .OrderBy(point => point.AplPercent)
            .ToArray();
        if (points.Length == 0)
            return 0;

        aplPercent = Math.Clamp(aplPercent, points[0].AplPercent, points[^1].AplPercent);
        if (aplPercent <= points[0].AplPercent)
            return points[0].PeakNits;
        if (aplPercent >= points[^1].AplPercent)
            return points[^1].PeakNits;

        for (int i = 1; i < points.Length; i++)
        {
            if (aplPercent > points[i].AplPercent)
                continue;

            AblMeasurementPoint lower = points[i - 1];
            AblMeasurementPoint upper = points[i];
            double lowerLog = Math.Log(lower.AplPercent);
            double upperLog = Math.Log(upper.AplPercent);
            double position = (Math.Log(aplPercent) - lowerLog) / (upperLog - lowerLog);
            return lower.PeakNits + (upper.PeakNits - lower.PeakNits) * position;
        }

        return points[^1].PeakNits;
    }
}

internal static class AblEstimator
{
    public static AblLuminanceEstimate? Calculate(
        AblProfile? profile,
        double clippedAverageNits,
        double clippedMaximumNits,
        double clippedMinimumNits)
    {
        if (profile is null || !profile.IsValid || profile.ReferencePeakNits <= 0)
            return null;

        double referencePeakNits = profile.ReferencePeakNits;
        double equivalentAplPercent = Math.Clamp(
            clippedAverageNits / referencePeakNits * 100.0,
            0,
            100);
        double aplPeakNits = profile.InterpolatePeakNits(equivalentAplPercent);
        double scaleFactor = Math.Clamp(aplPeakNits / referencePeakNits, 0, 1);

        return new AblLuminanceEstimate(
            profile.Id,
            profile.Name,
            equivalentAplPercent,
            aplPeakNits,
            scaleFactor,
            clippedAverageNits * scaleFactor,
            clippedMaximumNits * scaleFactor,
            clippedMinimumNits * scaleFactor);
    }
}
