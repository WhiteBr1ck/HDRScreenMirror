from pathlib import Path


def read(path):
    return Path(path).read_text(encoding='utf-8')


def write(path, text):
    Path(path).write_text(text, encoding='utf-8', newline='\n')


def replace_once(path, old, new):
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{path}: expected one match, found {count}: {old[:100]!r}')
    write(path, text.replace(old, new, 1))


def replace_between(path, start, end, replacement):
    text = read(path)
    i = text.find(start)
    if i < 0:
        raise RuntimeError(f'{path}: start marker not found: {start!r}')
    j = text.find(end, i)
    if j < 0:
        raise RuntimeError(f'{path}: end marker not found: {end!r}')
    write(path, text[:i] + replacement + text[j:])


mirror = 'src/HDRScreenMirror/DirectX/MirrorSession.cs'
replace_once(mirror,
    '    private const int DxgiErrorWaitTimeout = unchecked((int)0x887A0027);\n',
    '    private const int DxgiErrorWaitTimeout = unchecked((int)0x887A0027);\n    private const int DxgiErrorWasStillDrawing = unchecked((int)0x887A000A);\n')
replace_once(mirror,
    '    private int _gamutAnalysisEnabled;\n',
    '    private int _gamutAnalysisEnabled;\n    private int _luminanceAnalysisEnabled;\n')
replace_once(mirror,
    '    private ID3D11Buffer? _gamutReadback;\n',
    '''    private ID3D11Buffer? _gamutReadback;\n    private bool _frameStatsReadbackPending;\n    private AblProfile? _pendingFrameAblProfile;\n    private int _pendingFrameAblProfileVersion;\n    private bool _pointerStatsReadbackPending;\n    private bool _gamutReadbackPending;\n    private bool _lastPointerInCaptureArea;\n    private double _lastPointerRegionNits;\n    private double? _lastPointerScaledNits;\n''')
replace_once(mirror,
    '        _analyzeLuminance = analyzeLuminance;\n',
    '        _analyzeLuminance = analyzeLuminance;\n        _luminanceAnalysisEnabled = analyzeLuminance ? 1 : 0;\n')
replace_once(mirror,
    '    public void SetGamutAnalysis(bool enabled) =>\n        Volatile.Write(ref _gamutAnalysisEnabled, enabled ? 1 : 0);\n\n',
    '''    public void SetGamutAnalysis(bool enabled) =>\n        Volatile.Write(ref _gamutAnalysisEnabled, enabled ? 1 : 0);\n\n    public void SetLuminanceAnalysis(bool enabled) =>\n        Volatile.Write(ref _luminanceAnalysisEnabled, enabled ? 1 : 0);\n\n''')
replace_once(mirror,
    '        Stopwatch frameLuminanceClock = Stopwatch.StartNew();\n',
    '        Stopwatch frameLuminanceClock = Stopwatch.StartNew();\n        Stopwatch gamutClock = Stopwatch.StartNew();\n')
old_loop = '''                if (_analyzeLuminance && luminanceClock.Elapsed >= PointerAnalysisInterval)\n                {\n                    bool refreshFrameLuminance =\n                        !_hasFrameLuminance ||\n                        frameLuminanceClock.Elapsed >= FrameAnalysisInterval ||\n                        _lastAnalyzedAblProfileVersion != Volatile.Read(ref _ablProfileVersion);\n                    LuminanceTelemetry? luminance = AnalyzeLuminance(refreshFrameLuminance);\n                    if (luminance is not null)\n                        LuminanceChanged?.Invoke(luminance);\n                    if (refreshFrameLuminance && Volatile.Read(ref _gamutAnalysisEnabled) != 0)\n                    {\n                        GamutTelemetry? gamut = AnalyzeGamut();\n                        if (gamut is not null)\n                            GamutChanged?.Invoke(gamut);\n                    }\n                    if (refreshFrameLuminance)\n                        frameLuminanceClock.Restart();\n                    luminanceClock.Restart();\n                }\n'''
new_loop = '''                if (_analyzeLuminance &&\n                    Volatile.Read(ref _luminanceAnalysisEnabled) != 0 &&\n                    luminanceClock.Elapsed >= PointerAnalysisInterval)\n                {\n                    bool refreshFrameLuminance =\n                        !_hasFrameLuminance ||\n                        frameLuminanceClock.Elapsed >= FrameAnalysisInterval ||\n                        _lastAnalyzedAblProfileVersion != Volatile.Read(ref _ablProfileVersion);\n                    LuminanceTelemetry? luminance = AnalyzeLuminance(refreshFrameLuminance);\n                    if (luminance is not null)\n                        LuminanceChanged?.Invoke(luminance);\n                    if (refreshFrameLuminance)\n                        frameLuminanceClock.Restart();\n                    luminanceClock.Restart();\n                }\n\n                if (_analyzeLuminance &&\n                    Volatile.Read(ref _gamutAnalysisEnabled) != 0 &&\n                    gamutClock.Elapsed >= FrameAnalysisInterval)\n                {\n                    GamutTelemetry? gamut = AnalyzeGamut();\n                    if (gamut is not null)\n                        GamutChanged?.Invoke(gamut);\n                    gamutClock.Restart();\n                }\n'''
replace_once(mirror, old_loop, new_loop)

new_analysis = r'''    private GamutTelemetry? AnalyzeGamut()
    {
        if (_frameView is null || _constantBuffer is null ||
            _gamutBuffer is null || _gamutView is null || _gamutReadback is null ||
            _gamutClearComputeShader is null || _gamutAnalysisComputeShader is null)
        {
            return null;
        }

        GamutTelemetry? telemetry = TryConsumeGamutReadback();
        if (_gamutReadbackPending)
            return telemetry;

        _context!.UpdateSubresource(CreateMirrorConstants(), _constantBuffer);
        _context.CSSetConstantBuffer(0, _constantBuffer);
        _context.CSSetUnorderedAccessView(0, _gamutView);
        _context.CSSetShader(_gamutClearComputeShader);
        _context.Dispatch((uint)((GamutElementCount + 255) / 256), 1, 1);
        _context.CSSetUnorderedAccessView(0, null!);

        _context.CSSetShaderResource(0, _frameView);
        _context.CSSetUnorderedAccessView(0, _gamutView);
        _context.CSSetShader(_gamutAnalysisComputeShader);
        _context.Dispatch((_frameWidth + 31) / 32, (_frameHeight + 31) / 32, 1);
        _context.CSSetUnorderedAccessView(0, null!);
        _context.CSSetShaderResource(0, null!);
        _context.CSSetShader(null!);
        _context.CopyResource(_gamutReadback, _gamutBuffer);
        _gamutReadbackPending = true;
        return telemetry;
    }

    private GamutTelemetry? TryConsumeGamutReadback()
    {
        if (!_gamutReadbackPending || _gamutReadback is null ||
            !TryMapReadback(_gamutReadback, out MappedSubresource map))
        {
            return null;
        }

        _gamutReadbackPending = false;
        uint[] histogram = new uint[GamutHistogramCount];
        ulong[] counters = new ulong[GamutCounterCount];
        try
        {
            unsafe
            {
                uint* values = (uint*)map.DataPointer;
                for (int slice = 0; slice < GamutSliceCount; slice++)
                {
                    int sliceOffset = slice * GamutSliceStride;
                    for (int i = 0; i < GamutHistogramCount; i++)
                        histogram[i] += values[sliceOffset + i];
                    for (int i = 0; i < GamutCounterCount; i++)
                        counters[i] += values[sliceOffset + GamutHistogramCount + i];
                }
            }
        }
        finally
        {
            _context!.Unmap(_gamutReadback, 0);
        }

        ulong analyzedPixels = counters[4];
        if (analyzedPixels == 0)
            return new GamutTelemetry(
                GamutHistogramWidth,
                GamutHistogramHeight,
                histogram,
                0,
                0,
                0,
                0,
                0);

        double scale = 100.0 / analyzedPixels;
        return new GamutTelemetry(
            GamutHistogramWidth,
            GamutHistogramHeight,
            histogram,
            counters[0] * scale,
            counters[1] * scale,
            counters[2] * scale,
            counters[3] * scale,
            analyzedPixels);
    }

    private LuminanceTelemetry? AnalyzeLuminance(bool refreshFrameLuminance)
    {
        if (_frameView is null || _constantBuffer is null ||
            _frameStatsBuffer is null || _frameStatsView is null || _frameStatsReadback is null ||
            _pointerStatsBuffer is null || _pointerStatsView is null || _pointerStatsReadback is null)
            return null;

        TryConsumeFrameLuminanceReadback();
        TryConsumePointerReadback();
        UpdateProbePointerPosition();

        bool pointerInCaptureArea = IsPointerInCaptureArea();
        if (!pointerInCaptureArea)
        {
            _lastPointerInCaptureArea = false;
            _lastPointerRegionNits = 0;
            _lastPointerScaledNits = null;
        }

        AblProfile? ablProfile = Volatile.Read(ref _ablProfile);
        int ablProfileVersion = Volatile.Read(ref _ablProfileVersion);
        float ablReferencePeakNits = (float)(ablProfile?.ReferencePeakNits ?? 10000);
        _context!.UpdateSubresource(CreateMirrorConstants(ablReferencePeakNits), _constantBuffer);
        _context.CSSetConstantBuffer(0, _constantBuffer);
        _context.CSSetShaderResource(0, _frameView);

        if (refreshFrameLuminance && !_frameStatsReadbackPending)
        {
            _context.CSSetShader(_luminanceComputeShader);
            _context.CSSetUnorderedAccessView(0, _frameStatsView);
            _context.Dispatch((_frameWidth + 15) / 16, (_frameHeight + 15) / 16, 1);
            _context.CSSetUnorderedAccessView(0, null!);
            _context.CopyResource(_frameStatsReadback, _frameStatsBuffer);
            _frameStatsReadbackPending = true;
            _pendingFrameAblProfile = ablProfile;
            _pendingFrameAblProfileVersion = ablProfileVersion;
        }

        if (pointerInCaptureArea && !_pointerStatsReadbackPending)
        {
            _context.CSSetShader(_pointerProbeComputeShader);
            _context.CSSetUnorderedAccessView(0, _pointerStatsView);
            _context.Dispatch(1, 1, 1);
            _context.CSSetUnorderedAccessView(0, null!);
            _context.CopyResource(_pointerStatsReadback, _pointerStatsBuffer);
            _pointerStatsReadbackPending = true;
        }

        _context.CSSetShaderResource(0, null!);
        _context.CSSetShader(null!);

        if (!_hasFrameLuminance)
            return null;

        return new LuminanceTelemetry(
            _lastFrameAverageNits,
            _lastFrameMaximumNits,
            _lastFrameMinimumNits,
            _lastFrameMaximumX,
            _lastFrameMaximumY,
            _lastFrameMinimumX,
            _lastFrameMinimumY,
            _lastPointerInCaptureArea,
            _lastPointerRegionNits,
            _lastPointerScaledNits,
            _lastAblEstimate);
    }

    private bool TryConsumeFrameLuminanceReadback()
    {
        if (!_frameStatsReadbackPending || _frameStatsReadback is null ||
            !TryMapReadback(_frameStatsReadback, out MappedSubresource frameMap))
        {
            return false;
        }

        _frameStatsReadbackPending = false;
        AblProfile? analyzedProfile = _pendingFrameAblProfile;
        int analyzedProfileVersion = _pendingFrameAblProfileVersion;
        _pendingFrameAblProfile = null;

        double sum = 0;
        double minimum = double.PositiveInfinity;
        double maximum = 0;
        double clippedSum = 0;
        double clippedMinimum = double.PositiveInfinity;
        double clippedMaximum = 0;
        ulong count = 0;
        uint maximumX = 0;
        uint maximumY = 0;
        uint minimumX = 0;
        uint minimumY = 0;
        try
        {
            unsafe
            {
                LuminanceGroupResult* results = (LuminanceGroupResult*)frameMap.DataPointer;
                for (int i = 0; i < _frameStatsCount; i++)
                {
                    LuminanceGroupResult result = results[i];
                    if (result.Count == 0)
                        continue;

                    sum += result.Sum;
                    if (result.Minimum < minimum)
                    {
                        minimum = result.Minimum;
                        minimumX = result.MinimumX;
                        minimumY = result.MinimumY;
                    }
                    if (result.Maximum > maximum)
                    {
                        maximum = result.Maximum;
                        maximumX = result.MaximumX;
                        maximumY = result.MaximumY;
                    }
                    count += result.Count;
                    clippedSum += result.ClippedSum;
                    clippedMinimum = Math.Min(clippedMinimum, result.ClippedMinimum);
                    clippedMaximum = Math.Max(clippedMaximum, result.ClippedMaximum);
                }
            }
        }
        finally
        {
            _context!.Unmap(_frameStatsReadback, 0);
        }

        if (count == 0)
            return false;

        _lastFrameAverageNits = sum / count;
        _lastFrameMaximumNits = maximum;
        _lastFrameMinimumNits = double.IsPositiveInfinity(minimum) ? 0 : minimum;
        _lastFrameMaximumX = maximumX;
        _lastFrameMaximumY = maximumY;
        _lastFrameMinimumX = minimumX;
        _lastFrameMinimumY = minimumY;
        double clippedAverage = clippedSum / count;
        _lastAblEstimate = AblEstimator.Calculate(
            analyzedProfile,
            clippedAverage,
            clippedMaximum,
            double.IsPositiveInfinity(clippedMinimum) ? 0 : clippedMinimum);
        _lastAnalyzedAblProfileVersion = analyzedProfileVersion;
        _hasFrameLuminance = true;
        return true;
    }

    private bool TryConsumePointerReadback()
    {
        if (!_pointerStatsReadbackPending || _pointerStatsReadback is null ||
            !TryMapReadback(_pointerStatsReadback, out MappedSubresource pointerMap))
        {
            return false;
        }

        _pointerStatsReadbackPending = false;
        try
        {
            unsafe
            {
                LuminanceGroupResult result = *(LuminanceGroupResult*)pointerMap.DataPointer;
                if (result.Count == 0)
                {
                    _lastPointerInCaptureArea = false;
                    _lastPointerRegionNits = 0;
                    _lastPointerScaledNits = null;
                    return true;
                }

                _lastPointerInCaptureArea = true;
                _lastPointerRegionNits = result.Sum / result.Count;
                if (_lastAblEstimate is AblLuminanceEstimate estimate)
                {
                    double clippedPointerNits = result.ClippedSum / result.Count;
                    _lastPointerScaledNits = clippedPointerNits * estimate.ScaleFactor;
                }
                else
                {
                    _lastPointerScaledNits = null;
                }
            }
        }
        finally
        {
            _context!.Unmap(_pointerStatsReadback, 0);
        }

        return true;
    }

    private bool TryMapReadback(ID3D11Buffer buffer, out MappedSubresource mapped)
    {
        try
        {
            mapped = _context!.Map(
                buffer,
                MapMode.Read,
                Vortice.Direct3D11.MapFlags.DoNotWait);
            return true;
        }
        catch (SharpGenException exception) when (exception.HResult == DxgiErrorWasStillDrawing)
        {
            mapped = default;
            return false;
        }
    }

'''
replace_between(mirror, '    private GamutTelemetry? AnalyzeGamut()\n', '    private void UpdateProbePointerPosition()\n', new_analysis)
replace_once(mirror,
    '''        _gamutReadback = null;\n        _frameStatsCount = 0;\n        _hasFrameLuminance = false;\n        _lastAblEstimate = null;\n''',
    '''        _gamutReadback = null;\n        _frameStatsReadbackPending = false;\n        _pendingFrameAblProfile = null;\n        _pointerStatsReadbackPending = false;\n        _gamutReadbackPending = false;\n        _lastPointerInCaptureArea = false;\n        _lastPointerRegionNits = 0;\n        _lastPointerScaledNits = null;\n        _frameStatsCount = 0;\n        _hasFrameLuminance = false;\n        _lastAblEstimate = null;\n''')

print('GPU analysis fixes applied.')
