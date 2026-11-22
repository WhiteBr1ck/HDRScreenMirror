using System.Text.Json;
using System.IO.Compression;
using System.Xml.Linq;
using HDRScreenMirror;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

Near(EotfEditorUnits.ToNits(0.2, true), 2.429263297, 1e-8, "Workbook PQ 0.2");
Near(EotfEditorUnits.ToNits(0.4, true), 32.4479178538, 1e-8, "Workbook PQ 0.4");
Near(EotfEditorUnits.ToNits(0.5, true), 92.2457089941, 1e-7, "PQ 0.5 is not half the window peak");
Near(EotfEditorUnits.ToNits(1, true), 10000, 1e-6, "PQ 1 maps to 10000 nits");
foreach (double nits in new[] { 0, 0.0001, 0.01, 1, 100, 800, 1000, 10000 })
{
    double code = EotfEditorUnits.ToDisplay(nits, true);
    Near(EotfEditorUnits.ToNits(code, true), nits, 1e-6, "Linked PQ/nits round trip");
    Near(EotfEditorUnits.ToDisplay(nits, false), nits, 0, "Absolute view preserves output");
}
if (args.Length == 1)
{
    using var workbook = ZipFile.OpenRead(args[0]);
    using var sheet = workbook.GetEntry("xl/worksheets/sheet1.xml")!.Open();
    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    int samples = 0;
    foreach (var row in XDocument.Load(sheet).Descendants(ns + "row").Skip(1))
    {
        double[] values = row.Elements(ns + "c").Select(cell => (double)cell.Element(ns + "v")!).ToArray();
        Near(EotfEditorUnits.ToNits(values[0], true), values[1], 1e-5, "Workbook PQ reference row");
        if (values[1] > 0)
            Near(EotfEditorUnits.ToDisplay(values[1], true), values[0], 1e-8, "Workbook inverse mapping");
        samples++;
    }
    Check(samples == 256, "Workbook sample count");
    Console.WriteLine($"PASS: {samples} workbook PQ/nits pairs in both directions.");
}

AblProfile legacy = JsonSerializer.Deserialize<AblProfile>("""
    {"Name":"Legacy","Points":[{"AplPercent":1,"PeakNits":1000},
    {"AplPercent":10,"PeakNits":800},{"AplPercent":25,"PeakNits":500},
    {"AplPercent":100,"PeakNits":200}]}
    """)!;
legacy.Normalize();
Check(legacy.IsValid && !legacy.HasAplEotfCurves, "Legacy JSON remains valid without node curves");
var legacyEstimate = AblEstimator.Calculate(legacy, 100, 1000, 0)!;
Near(legacyEstimate.EquivalentAplPercent, 10, 1e-9, "Legacy APL");
Near(legacyEstimate.AverageNits, 80, 1e-9, "Legacy ABL output");
var budgetInput = new AblLuminanceEstimate("test", "test", 50, 800, 0.8, 400, 800, 40, true);
var budgetOutput = AblEstimator.ApplyAverageLimit(legacy, budgetInput);
Near(budgetOutput.AverageNits, 200, 0, "Full-screen average budget");
Near(budgetOutput.MaximumNits, 400, 0, "Highlights scale uniformly and may exceed mean limit");
Near(budgetOutput.MinimumNits, 20, 0, "Minimum shares budget factor");
Near(budgetOutput.AverageLimitScale, 0.5, 0, "Custom pointer uses budget factor once");
Near(budgetOutput.ScaleFactor, 0.4, 1e-9, "Total scale includes budget");
Check(AblEstimator.ApplyAverageLimit(legacy, budgetOutput) == budgetOutput, "Budget is idempotent");
Check(AblEstimator.ApplyAverageLimit(legacy, budgetInput with { AverageNits = 0 }).AverageLimitScale == 1,
    "Black does not divide by zero");
Check(AblEstimator.ApplyAverageLimit(legacy, legacyEstimate) == legacyEstimate, "Below-budget frames unchanged");
AblProfile highLoad = legacy.Clone();
highLoad.Points.Add(new() { AplPercent = 50, PeakNits = 800 });
var highLoadEstimate = AblEstimator.Calculate(highLoad, 500, 1000, 100)!;
Near(highLoadEstimate.AverageNits, 200, 0, "Legacy path also obeys average budget");
Near(highLoadEstimate.MaximumNits, 400, 1e-9, "Legacy budget applied once");
AblProfile powerLimited = legacy.Clone();
powerLimited.Points = [new() { AplPercent = 1, PeakNits = 1000 },
    new() { AplPercent = 25, PeakNits = 500 }, new() { AplPercent = 100, PeakNits = 125 }];
Near(powerLimited.InterpolatePeakNits(50), 250, 1e-8, "Constant APL-times-peak budget interpolates exactly");
Near(legacy.InterpolatePeakNits(50), Math.Sqrt(500 * 200), 1e-8, "High APL power-law interpolation");
foreach (var point in legacy.Points)
    Near(legacy.InterpolatePeakNits(point.AplPercent), point.PeakNits, 1e-8, "Measured peaks preserved");
for (int apl = 26; apl <= 100; apl++)
    Check(legacy.InterpolatePeakNits(apl) <= legacy.InterpolatePeakNits(apl - 1), "Decreasing high-APL peaks stay monotonic");

AblProfile profile = legacy.Clone();
foreach (int apl in new[] { 1, 10, 25 })
{
    AblProfile editor = profile.GetAplEotfEditorProfile(apl);
    double peak = editor.ReferencePeakNits;
    editor.SetCustomEotf(Enumerable.Range(0, 11).Select(i => peak * Math.Pow(i / 10.0, apl == 10 ? 3 : 2)));
    editor.SetEotfClipPqPercent(95);
    profile.SetAplEotfCurve(apl, editor);
}
profile.Normalize();
Check(profile.IsValid, "Three node curves valid");
Check(AblProfile.SupportedEotfAplPercentages.SequenceEqual(new[] { 1, 10, 25 }), "Only three APL EOTF nodes");
AblProfile migrated = profile.Clone();
migrated.AplEotfCurves.Add(new AplEotfCurve { AplPercent = 50 });
migrated.Normalize();
Check(migrated.AplEotfCurves.Count == 3 && migrated.IsValid, "Obsolete EOTF nodes removed on normalization");
Check(migrated.Points.Count == profile.Points.Count, "Window luminance measurements retained");
foreach (int apl in new[] { 1, 10, 25 })
    EqualLuts(profile.BuildOutputEotfLut(apl), profile.GetAplEotfEditorProfile(apl).BuildEotfLut(), $"Exact {apl}% node");

float[] one = profile.BuildOutputEotfLut(1), ten = profile.BuildOutputEotfLut(10);
float[] middle = profile.BuildOutputEotfLut(Math.Sqrt(10));
for (int i = 0; i < middle.Length; i++)
    Near(middle[i], (one[i] + ten[i]) / 2.0,
        0.0001, "Interpolate absolute output without peak-normalization gain");
EqualLuts(profile.BuildOutputEotfLut(0), one, "Below 1% clamps to first node");
float[] hundred = profile.BuildOutputEotfLut(100);
float[] twentyFive = profile.BuildOutputEotfLut(25);
float[] betweenTenAndTwentyFive = profile.BuildOutputEotfLut(Math.Sqrt(10 * 25));
float[] fifty = profile.BuildOutputEotfLut(50);
for (int i = 0; i < hundred.Length; i++)
{
    Near(betweenTenAndTwentyFive[i], (ten[i] + twentyFive[i]) / 2.0, 0.0001,
        "10%-25% interpolates custom curves");
    Near(fifty[i], twentyFive[i] / 500.0 * profile.InterpolatePeakNits(50), 0.0001,
        "50% retains last measured EOTF shape");
}
float[] baseline = legacy.BuildEotfLut();
for (int i = 0; i < hundred.Length; i++)
    Near(hundred[i], twentyFive[i] * 0.4, 0.0001, "100% uses 25% shape and full-screen peak");
AblProfile intermediatePeak = profile.Clone();
intermediatePeak.Points.Add(new() { AplPercent = 20, PeakNits = 550 });
intermediatePeak.Points.Add(new() { AplPercent = 50, PeakNits = 230 });
intermediatePeak.Normalize();
Near(intermediatePeak.BuildOutputEotfLut(20)[^1], 550, 0.0001, "Honor intermediate 20% peak measurement");
Near(intermediatePeak.BuildOutputEotfLut(50)[^1], 230, 0.0001, "Honor high-APL 50% peak measurement");
foreach (int node in new[] { 1, 10, 25 })
{
    var before = profile.BuildOutputEotfLut(node - 0.000001);
    var after = profile.BuildOutputEotfLut(node + 0.000001);
    for (int i = 0; i < before.Length; i++)
        Near(before[i], after[i], 0.001, "Continuity at EOTF boundaries");
}
for (int apl = 0; apl <= 100; apl++)
{
    float[] lut = profile.BuildOutputEotfLut(apl);
    Near(lut[0], 0, 0, "Black remains black");
    for (int i = 1; i < lut.Length; i++)
        Check(float.IsFinite(lut[i]) && lut[i] >= lut[i - 1] && lut[i] <= 1000, "Finite monotonic output LUT");
}
AblProfile restored = JsonSerializer.Deserialize<AblProfile>(JsonSerializer.Serialize(profile))!;
restored.Normalize();
EqualLuts(restored.BuildOutputEotfLut(17), profile.BuildOutputEotfLut(17), "Save/load preserves node curves");
AblProfile clone = profile.Clone();
clone.AplEotfCurves[0].OutputNits[1] = 999;
Check(profile.AplEotfCurves[0].OutputNits[1] != 999, "Clone isolates editing");
EqualLuts(profile.BuildEotfLut(), legacy.BuildEotfLut(), "Output curve edits leave equivalent APL reference unchanged");

// Exercise the shipped bytecode on D3D11 WARP: the second pass must map each
// original pixel, including the pointer probe, rather than map an average.
RunGpu(ten);
AblProfile pqTracking = legacy.Clone();
foreach (int apl in new[] { 1, 10, 25 })
    pqTracking.SetAplEotfCurve(apl, pqTracking.GetAplEotfEditorProfile(apl));
foreach (double apl in new[] { 1, 5, 10, 16.1, 20, 25, 50, 100 })
{
    float[] lut = pqTracking.BuildOutputEotfLut(apl);
    for (int sample = 0; sample <= 1000; sample++)
    {
        double input = AblProfile.PqEotf(sample / 1000.0);
        Check(Map(input, lut) <= input + 0.001, "PQ tracking must not amplify input at any APL");
    }
}
// A uniform 161-nit frame with a 1000-nit reference is exactly 16.1% APL.
RunGpu(pqTracking.BuildOutputEotfLut(16.1), true);
Near(AblEstimator.CalculateEffectiveArea(0, 0, 256), 0, 0, "Black area is well defined");
double previousWindowOutput = 0;
foreach (double input in new[] { 0.01, 1, 10, 50, 100, 161, 250, 500, 1000, 4000, 10000 })
{
    double area = AblEstimator.CalculateEffectiveArea(64 * input, 64 * input * input, 256);
    Near(area, 25, 1e-10, "25% geometry is independent of input brightness");
    var seed = AblEstimator.Calculate(pqTracking, Math.Min(input, 1000) * 0.25,
        Math.Min(input, 1000), 0, area)!;
    double mapped = Map(input, pqTracking.BuildOutputEotfLut(seed.EffectiveAreaPercent!.Value));
    var output = AblEstimator.ApplyAverageLimit(pqTracking, seed with
    {
        AverageNits = mapped * 0.25, MaximumNits = mapped, MinimumNits = 0,
        AverageLimitScale = 1, UsesAplEotf = true
    });
    Check(output.MaximumNits + 0.001 >= previousWindowOutput, "Fixed 25% window brightens monotonically");
    previousWindowOutput = output.MaximumNits;
}
Near(previousWindowOutput, 500, 0.001, "25% window reaches its measured plateau");
foreach (float input in new[] { 10f, 161f, 500f, 1000f, 4000f })
    RunGpu(pqTracking.BuildOutputEotfLut(25), windowInput: input);
Console.WriteLine("PASS: legacy compatibility, node interpolation, persistence, cloning, LUT bounds, GPU frame and pointer mapping.");

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
}
static void Near(double actual, double expected, double tolerance, string name) =>
    Check(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance, $"{name}: {actual} != {expected}");
static void EqualLuts(float[] a, float[] b, string name)
{
    for (int i = 0; i < a.Length; i++) Near(a[i], b[i], 0.0001, name);
}
static double Map(double nits, float[] lut)
{
    double position = AblProfile.PqOetf(nits) * 63;
    int lower = Math.Min((int)position, 62);
    double lowNits = AblProfile.PqEotf(lower / 63.0);
    double highNits = AblProfile.PqEotf((lower + 1) / 63.0);
    double weight = Math.Clamp((nits - lowNits) / (highNits - lowNits), 0, 1);
    return lut[lower] + (lut[lower + 1] - lut[lower]) * weight;
}
static unsafe void RunGpu(float[] lut, bool pqIdentity = false, float? windowInput = null)
{
    D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.None,
        new[] { FeatureLevel.Level_11_0 }, out ID3D11Device device, out _, out ID3D11DeviceContext context).CheckError();
    using (device)
    using (context)
    {
        float[] pixels = new float[16 * 16 * 4];
        for (int i = 0; i < 256; i++)
        {
            float nits = pqIdentity ? 161 : i % 16 < 8 ? 100 : 800;
            if (windowInput.HasValue)
                nits = i % 16 < 8 && i / 16 < 8 ? windowInput.Value : 0;
            for (int c = 0; c < 3; c++) pixels[i * 4 + c] = nits / 80;
            pixels[i * 4 + 3] = 1;
        }
        using var texture = device.CreateTexture2D<float>(pixels, Format.R32G32B32A32_Float, 16, 16,
            bindFlags: BindFlags.ShaderResource);
        using var srv = device.CreateShaderResourceView(texture);
        using var output = device.CreateBuffer(new BufferDescription(48, BindFlags.UnorderedAccess,
            ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 48));
        using var uav = device.CreateUnorderedAccessView(output);
        using var staging = device.CreateBuffer(new BufferDescription(48, BindFlags.None,
            ResourceUsage.Staging, CpuAccessFlags.Read, ResourceOptionFlags.BufferStructured, 48));
        byte[] constants = new byte[304];
        using (var writer = new BinaryWriter(new MemoryStream(constants)))
        {
            writer.Write(203f); writer.Write(0u); writer.Write(0u); writer.Write(0u);
            writer.Write(16u); writer.Write(16u); writer.Write(4); writer.Write(4);
            writer.Write(lut[^1]); writer.Write(1u); writer.Write(1f); writer.Write(0f);
            foreach (float value in lut) writer.Write(value);
        }
        using var cb = device.CreateBuffer<byte>(constants, new BufferDescription(304, BindFlags.ConstantBuffer));
        context.CSSetConstantBuffer(0, cb);
        context.CSSetShaderResource(0, srv);
        foreach (bool pointer in new[] { false, true })
        {
            using var shader = device.CreateComputeShader(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
                pointer ? "PointerProbeCS.cso" : "LuminanceCS.cso")));
            context.CSSetShader(shader);
            context.CSSetUnorderedAccessView(0, uav);
            context.Dispatch(1, 1, 1);
            context.CSSetUnorderedAccessView(0, null!);
            context.CopyResource(staging, output);
            var mapped = context.Map(staging, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                float* values = (float*)mapped.DataPointer;
                uint count = ((uint*)mapped.DataPointer)[3];
                Near(count, pointer ? 25 : 256, 0, "GPU sample count");
                double expected = pqIdentity ? Map(161, lut) : pointer ? Map(100, lut) : (Map(100, lut) + Map(800, lut)) / 2;
                if (windowInput.HasValue)
                    expected = Map(windowInput.Value, lut) * (pointer ? 1 : 0.25);
                Near(values[8] / count, expected, 0.15, "GPU nonlinear output mean / pointer");
                if (windowInput.HasValue)
                {
                    if (!pointer)
                        Near(AblEstimator.CalculateEffectiveArea(values[0], values[11], count), 25,
                            0.001, "GPU raw moments recover fixed window area");
                }
                else if (pqIdentity)
                {
                    Near(values[8] / count, 161, 0.03, "16.1% APL PQ frame/pointer must preserve 161 nits");
                    Check(values[8] / count <= 161.03, "No PQ amplification beyond GPU rounding");
                }
                else if (!pointer)
                {
                    Near(values[9], Map(100, lut), 0.15, "GPU output minimum");
                    Near(values[10], Map(800, lut), 0.15, "GPU output maximum");
                    Check(Math.Abs(expected - Map(450, lut)) > 1, "Test distinguishes per-pixel mapping from mapping the mean");
                }
            }
            finally { context.Unmap(staging, 0); }
        }
        context.ClearState();
    }
}
