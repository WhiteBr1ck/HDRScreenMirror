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

control = 'src/HDRScreenMirror/UI/ControlForm.cs'
replace_once(control,
    '    private readonly System.Windows.Forms.Timer _displayChangeTimer = new() { Interval = 750 };\n',
    '''    private readonly System.Windows.Forms.Timer _displayChangeTimer = new() { Interval = 750 };\n    private readonly System.Windows.Forms.Timer _telemetryUiTimer = new() { Interval = 50 };\n''')
replace_once(control,
    '    private string? _displayChangePresentId;\n',
    '''    private string? _displayChangePresentId;\n    private MirrorTelemetry? _pendingMirrorTelemetry;\n    private LuminanceTelemetry? _pendingLuminanceTelemetry;\n    private GamutTelemetry? _pendingGamutTelemetry;\n''')
replace_once(control,
    '        _displayChangeTimer.Tick += (_, _) => ApplyDisplayChange();\n',
    '        _displayChangeTimer.Tick += (_, _) => ApplyDisplayChange();\n        _telemetryUiTimer.Tick += (_, _) => FlushSessionTelemetry();\n')
old_session = '''            _session = new MirrorSession(\n                capture,\n                bindings,\n                (float)_paperWhite.Value,\n                GetFrameRateMode(),\n                decimal.ToInt32(_frameRateLimit.Value),\n                analysisOnly || _renderCursor.Checked,\n                falseColorMode == FalseColorMode.Luminance,\n                true);\n            _session.SetFalseColorMode(falseColorMode);\n            _session.StatusChanged += OnSessionStatusChanged;\n            _session.TelemetryChanged += OnSessionTelemetryChanged;\n            _session.LuminanceChanged += OnSessionLuminanceChanged;\n            _session.GamutChanged += OnSessionGamutChanged;\n            _session.Failed += OnSessionFailed;\n            _session.Stopped += OnSessionStopped;\n            _session.SetGamutAnalysis(_showCieAnalysis.Checked);\n            _session.SetAblProfile(activeAblProfile);\n            _session.Start();\n'''
new_session = '''            MirrorSession session = new(\n                capture,\n                bindings,\n                (float)_paperWhite.Value,\n                GetFrameRateMode(),\n                decimal.ToInt32(_frameRateLimit.Value),\n                analysisOnly || _renderCursor.Checked,\n                falseColorMode == FalseColorMode.Luminance,\n                true);\n            _session = session;\n            session.SetFalseColorMode(falseColorMode);\n            session.StatusChanged += text => OnSessionStatusChanged(session, text);\n            session.TelemetryChanged += telemetry => OnSessionTelemetryChanged(session, telemetry);\n            session.LuminanceChanged += telemetry => OnSessionLuminanceChanged(session, telemetry);\n            session.GamutChanged += telemetry => OnSessionGamutChanged(session, telemetry);\n            session.Failed += exception => OnSessionFailed(session, exception);\n            session.Stopped += () => OnSessionStopped(session);\n            session.SetGamutAnalysis(_showCieAnalysis.Checked);\n            session.SetLuminanceAnalysis(ShouldAnalyzeLuminance());\n            session.SetAblProfile(activeAblProfile);\n            session.Start();\n            _telemetryUiTimer.Start();\n'''
replace_once(control, old_session, new_session)
old_stop = '''    private void StopMirror(bool restoreControlWindow = true)\n    {\n        bool wasSingleDisplayAnalysis = _singleDisplayAnalysis;\n        MirrorSession? session = _session;\n        _session = null;\n\n        if (session is not null)\n        {\n            session.StatusChanged -= OnSessionStatusChanged;\n            session.TelemetryChanged -= OnSessionTelemetryChanged;\n            session.LuminanceChanged -= OnSessionLuminanceChanged;\n            session.GamutChanged -= OnSessionGamutChanged;\n            session.Failed -= OnSessionFailed;\n            session.Stopped -= OnSessionStopped;\n            session.Stop();\n            session.Dispose();\n        }\n'''
new_stop = '''    private void StopMirror(bool restoreControlWindow = true, bool preserveDisplayRestart = false)\n    {\n        if (!preserveDisplayRestart)\n            CancelPendingDisplayRestart();\n\n        bool wasSingleDisplayAnalysis = _singleDisplayAnalysis;\n        _telemetryUiTimer.Stop();\n        Interlocked.Exchange(ref _pendingMirrorTelemetry, null);\n        Interlocked.Exchange(ref _pendingLuminanceTelemetry, null);\n        Interlocked.Exchange(ref _pendingGamutTelemetry, null);\n        MirrorSession? session = _session;\n        _session = null;\n\n        if (session is not null)\n        {\n            session.Stop();\n            session.Dispose();\n        }\n'''
replace_once(control, old_stop, new_stop)
replace_once(control,
    '        EnsureSingleDisplayAnalysisOverlaysOnTop();\n    }\n\n    private void UpdatePointerLuminanceVisibility()\n',
    '        _session?.SetLuminanceAnalysis(ShouldAnalyzeLuminance());\n        EnsureSingleDisplayAnalysisOverlaysOnTop();\n    }\n\n    private void UpdatePointerLuminanceVisibility()\n')
replace_once(control,
    '''        EnsureSingleDisplayAnalysisOverlaysOnTop();\n    }\n\n    private void UpdateCieAnalysisVisibility()\n''',
    '''        _session?.SetLuminanceAnalysis(ShouldAnalyzeLuminance());\n        EnsureSingleDisplayAnalysisOverlaysOnTop();\n    }\n\n    private bool ShouldAnalyzeLuminance() =>\n        _showStatusOverlay.Checked || _showLuminanceMarkers.Checked;\n\n    private void UpdateCieAnalysisVisibility()\n''')
replace_once(control,
    '''                for (int i = 0; i < bitmaps.Length; i++)\n                {\n                    CompositeScreenshotOverlays(bitmaps[i], i);\n                    bitmaps[i].Save(paths[i], ImageFormat.Png);\n                }\n''',
    '''                for (int i = 0; i < bitmaps.Length; i++)\n                    CompositeScreenshotOverlays(bitmaps[i], i);\n                await Task.Run(() =>\n                {\n                    for (int i = 0; i < bitmaps.Length; i++)\n                        bitmaps[i].Save(paths[i], ImageFormat.Png);\n                });\n''')
new_events = r'''    private void OnSessionStatusChanged(MirrorSession session, string text) => PostToUi(() =>
    {
        if (ReferenceEquals(_session, session))
            _statusLabel.Text = text;
    });

    private void OnSessionTelemetryChanged(MirrorSession session, MirrorTelemetry telemetry)
    {
        if (ReferenceEquals(_session, session))
            Volatile.Write(ref _pendingMirrorTelemetry, telemetry);
    }

    private void OnSessionLuminanceChanged(MirrorSession session, LuminanceTelemetry telemetry)
    {
        if (ReferenceEquals(_session, session))
            Volatile.Write(ref _pendingLuminanceTelemetry, telemetry);
    }

    private void OnSessionGamutChanged(MirrorSession session, GamutTelemetry telemetry)
    {
        if (ReferenceEquals(_session, session))
            Volatile.Write(ref _pendingGamutTelemetry, telemetry);
    }

    private void FlushSessionTelemetry()
    {
        if (_session is null)
            return;

        MirrorTelemetry? mirrorTelemetry = Interlocked.Exchange(ref _pendingMirrorTelemetry, null);
        if (mirrorTelemetry is not null)
        {
            foreach (StatusOverlayForm overlay in _statusOverlays)
                overlay.UpdateTelemetry(mirrorTelemetry);
            foreach (AnalysisOverlayForm overlay in _analysisOverlays)
                overlay.UpdateMirrorTelemetry(mirrorTelemetry);
            EnsureSingleDisplayAnalysisOverlaysOnTop();
        }

        LuminanceTelemetry? luminanceTelemetry = Interlocked.Exchange(ref _pendingLuminanceTelemetry, null);
        if (luminanceTelemetry is not null)
        {
            foreach (StatusOverlayForm overlay in _statusOverlays)
                overlay.UpdateLuminance(luminanceTelemetry);
            foreach (AnalysisOverlayForm overlay in _analysisOverlays)
                overlay.UpdateLuminance(luminanceTelemetry);
        }

        GamutTelemetry? gamutTelemetry = Interlocked.Exchange(ref _pendingGamutTelemetry, null);
        if (gamutTelemetry is not null)
        {
            foreach (AnalysisOverlayForm overlay in _analysisOverlays)
                overlay.UpdateGamut(gamutTelemetry);
        }
    }

'''
replace_between(control, '    private void OnSessionStatusChanged(string text) =>', '    private void EnsureSingleDisplayAnalysisOverlaysOnTop()\n', new_events)
old_after_ensure = '''    private void OnSessionLuminanceChanged(LuminanceTelemetry telemetry) => PostToUi(() =>\n    {\n        foreach (StatusOverlayForm overlay in _statusOverlays)\n            overlay.UpdateLuminance(telemetry);\n        foreach (AnalysisOverlayForm overlay in _analysisOverlays)\n            overlay.UpdateLuminance(telemetry);\n    });\n\n    private void OnSessionGamutChanged(GamutTelemetry telemetry) => PostToUi(() =>\n    {\n        foreach (AnalysisOverlayForm overlay in _analysisOverlays)\n            overlay.UpdateGamut(telemetry);\n    });\n\n    private void OnSessionFailed(Exception exception) => PostToUi(() =>\n    {\n        if (_restartMirrorAfterDisplayChange || _displayChangeTimer.Enabled)\n        {\n            StopMirror();\n            return;\n        }\n\n        bool analysisOnly = _singleDisplayAnalysis;\n        StopMirror();\n        MessageBox.Show(\n            this,\n            exception.ToString(),\n            Localization.T(analysisOnly ? "RuntimeAnalysisFailed" : "RuntimeFailed"),\n            MessageBoxButtons.OK,\n            MessageBoxIcon.Error);\n    });\n\n    private void OnSessionStopped() => PostToUi(() =>\n    {\n        if (_session is not null)\n            StopMirror();\n    });\n'''
new_after_ensure = '''    private void OnSessionFailed(MirrorSession session, Exception exception) => PostToUi(() =>\n    {\n        if (!ReferenceEquals(_session, session))\n            return;\n\n        if (_restartMirrorAfterDisplayChange || _displayChangeTimer.Enabled)\n        {\n            StopMirror(preserveDisplayRestart: true);\n            return;\n        }\n\n        bool analysisOnly = _singleDisplayAnalysis;\n        StopMirror();\n        MessageBox.Show(\n            this,\n            exception.ToString(),\n            Localization.T(analysisOnly ? "RuntimeAnalysisFailed" : "RuntimeFailed"),\n            MessageBoxButtons.OK,\n            MessageBoxIcon.Error);\n    });\n\n    private void OnSessionStopped(MirrorSession session) => PostToUi(() =>\n    {\n        if (ReferenceEquals(_session, session))\n            StopMirror();\n    });\n'''
replace_once(control, old_after_ensure, new_after_ensure)
replace_once(control,
    '''        _exitRequested = true;\n        UnregisterConfiguredHotKeys();\n        _displayChangeTimer.Stop();\n        _displayChangeTimer.Dispose();\n        StopMirror();\n''',
    '''        _exitRequested = true;\n        UnregisterConfiguredHotKeys();\n        StopMirror();\n        _displayChangeTimer.Stop();\n        _displayChangeTimer.Dispose();\n        _telemetryUiTimer.Stop();\n        _telemetryUiTimer.Dispose();\n''')
replace_once(control,
    '    private void ScheduleDisplayChange()\n    {\n',
    '''    private void CancelPendingDisplayRestart()\n    {\n        _displayChangeTimer.Stop();\n        _restartMirrorAfterDisplayChange = false;\n        _displayChangeAnalysisOnly = null;\n        _displayChangeCaptureId = null;\n        _displayChangePresentId = null;\n    }\n\n    private void ScheduleDisplayChange()\n    {\n''')

analysis = 'src/HDRScreenMirror/UI/AnalysisOverlayForm.cs'
start = '    private static Bitmap CreateHeatmap(GamutTelemetry telemetry)\n'
end = '    private static Color UvToSrgb(float u, float v)\n'
new_heatmap = r'''    private static unsafe Bitmap CreateHeatmap(GamutTelemetry telemetry)
    {
        Bitmap bitmap = new(telemetry.HistogramWidth, telemetry.HistogramHeight, PixelFormat.Format32bppArgb);
        uint maximum = telemetry.Histogram.Length == 0 ? 0 : telemetry.Histogram.Max();
        if (maximum == 0)
            return bitmap;

        BitmapData? data = null;
        try
        {
            data = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);
            double maximumLog = Math.Log(1.0 + maximum);
            byte* basePointer = (byte*)data.Scan0;
            for (int y = 0; y < telemetry.HistogramHeight; y++)
            {
                byte* row = basePointer + (telemetry.HistogramHeight - 1 - y) * data.Stride;
                for (int x = 0; x < telemetry.HistogramWidth; x++)
                {
                    uint count = telemetry.Histogram[y * telemetry.HistogramWidth + x];
                    if (count == 0)
                        continue;

                    double intensity = Math.Log(1.0 + count) / maximumLog;
                    float u = (x + 0.5f) / telemetry.HistogramWidth * DiagramMaximum;
                    float v = (y + 0.5f) / telemetry.HistogramHeight * DiagramMaximum;
                    Color chromaticity = UvToSrgb(u, v);
                    byte* pixel = row + x * 4;
                    pixel[0] = (byte)Math.Clamp(chromaticity.B * (0.35 + 0.65 * intensity), 0, 255);
                    pixel[1] = (byte)Math.Clamp(chromaticity.G * (0.35 + 0.65 * intensity), 0, 255);
                    pixel[2] = (byte)Math.Clamp(chromaticity.R * (0.35 + 0.65 * intensity), 0, 255);
                    pixel[3] = (byte)Math.Clamp(45 + 210 * Math.Sqrt(intensity), 0, 255);
                }
            }

            bitmap.UnlockBits(data);
            data = null;
            return bitmap;
        }
        catch
        {
            if (data is not null)
                bitmap.UnlockBits(data);
            bitmap.Dispose();
            throw;
        }
    }

'''
replace_between(analysis, start, end, new_heatmap)

print('UI performance fixes applied.')
