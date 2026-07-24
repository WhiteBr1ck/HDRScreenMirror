using System.Diagnostics;
using System.Runtime.InteropServices;
using HDRScreenMirror.Interop;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace HDRScreenMirror.DirectX;

internal sealed class MirrorSession : IDisposable
{
    private const int GamutHistogramWidth = 128;
    private const int GamutHistogramHeight = 128;
    private const int GamutHistogramCount = GamutHistogramWidth * GamutHistogramHeight;
    private const int GamutCounterCount = 5;
    private const int GamutSliceCount = 8;
    private const int GamutSliceStride = GamutHistogramCount + GamutCounterCount;
    private const int GamutElementCount = GamutSliceCount * GamutSliceStride;
    private const int ErrorAccessDenied = unchecked((int)0x80070005);
    private const int DxgiErrorAccessLost = unchecked((int)0x887A0026);
    private const int DxgiErrorWaitTimeout = unchecked((int)0x887A0027);
    private static readonly TimeSpan PointerAnalysisInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan FrameAnalysisInterval = TimeSpan.FromMilliseconds(250);

    private static readonly FeatureLevel[] FeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0
    ];

    private readonly DisplayTarget _capture;
    private readonly IReadOnlyList<MirrorOutputBinding> _outputs;
    private readonly float _paperWhiteNits;
    private readonly FrameRateMode _frameRateMode;
    private readonly int _frameRateLimit;
    private readonly bool _renderCursor;
    private int _falseColorEnabled;
    private int _gamutAnalysisEnabled;
    private readonly bool _analyzeLuminance;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Thread? _renderThread;
    private bool _disposed;

    private IDXGIFactory2? _factory;
    private IDXGIAdapter1? _adapter;
    private IDXGIOutput? _captureOutput;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private readonly List<OutputPresenter> _presenters = [];
    private ID3D11Texture2D? _frameTexture;
    private ID3D11ShaderResourceView? _frameView;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11VertexShader? _cursorVertexShader;
    private ID3D11PixelShader? _cursorPixelShader;
    private ID3D11ComputeShader? _luminanceComputeShader;
    private ID3D11ComputeShader? _pointerProbeComputeShader;
    private ID3D11ComputeShader? _gamutClearComputeShader;
    private ID3D11ComputeShader? _gamutAnalysisComputeShader;
    private ID3D11SamplerState? _sampler;
    private ID3D11BlendState? _opaqueBlendState;
    private ID3D11BlendState? _cursorBlendState;
    private ID3D11Buffer? _constantBuffer;
    private ID3D11Buffer? _cursorConstantBuffer;
    private ID3D11Texture2D? _cursorTexture;
    private ID3D11ShaderResourceView? _cursorView;
    private ID3D11Buffer? _frameStatsBuffer;
    private ID3D11UnorderedAccessView? _frameStatsView;
    private ID3D11Buffer? _frameStatsReadback;
    private ID3D11Buffer? _pointerStatsBuffer;
    private ID3D11UnorderedAccessView? _pointerStatsView;
    private ID3D11Buffer? _pointerStatsReadback;
    private ID3D11Buffer? _gamutBuffer;
    private ID3D11UnorderedAccessView? _gamutView;
    private ID3D11Buffer? _gamutReadback;
    private Format _inputFormat = Format.Unknown;
    private uint _frameWidth;
    private uint _frameHeight;
    private uint _inputMode;
    private int _frameStatsCount;
    private bool _hasFrameLuminance;
    private double _lastFrameAverageNits;
    private double _lastFrameMaximumNits;
    private double _lastFrameMinimumNits;
    private uint _lastFrameMaximumX;
    private uint _lastFrameMaximumY;
    private uint _lastFrameMinimumX;
    private uint _lastFrameMinimumY;
    private int _rotationCode;
    private bool _cursorVisible;
    private int _cursorX;
    private int _cursorY;
    private uint _cursorWidth;
    private uint _cursorHeight;
    private int _cursorHotSpotX;
    private int _cursorHotSpotY;
    private int _probePointerX;
    private int _probePointerY;
    private bool _probePointerInCaptureArea;
    private bool _captureAccessPaused;
    private long _nextFixedFrameTimestamp;
    private nint _fixedFrameTimer;
    private bool _waitForOutputBeforeNextFrame;

    public MirrorSession(
        DisplayTarget capture,
        DisplayTarget present,
        nint outputWindow,
        float paperWhiteNits,
        FrameRateMode frameRateMode,
        int frameRateLimit,
        bool renderCursor = true,
        bool falseColor = false,
        bool analyzeLuminance = true)
        : this(
            capture,
            [new MirrorOutputBinding(present, outputWindow)],
            paperWhiteNits,
            frameRateMode,
            frameRateLimit,
            renderCursor,
            falseColor,
            analyzeLuminance)
    {
    }

    public MirrorSession(
        DisplayTarget capture,
        IReadOnlyList<MirrorOutputBinding> outputs,
        float paperWhiteNits,
        FrameRateMode frameRateMode,
        int frameRateLimit,
        bool renderCursor = true,
        bool falseColor = false,
        bool analyzeLuminance = true)
    {
        if (outputs.Count == 0)
            throw new ArgumentException(Localization.T("NeedOutput"), nameof(outputs));

        _capture = capture;
        _outputs = outputs;
        _paperWhiteNits = paperWhiteNits;
        _frameRateMode = frameRateMode;
        _frameRateLimit = Math.Clamp(frameRateLimit, 24, 500);
        _renderCursor = renderCursor;
        _falseColorEnabled = falseColor ? 1 : 0;
        _analyzeLuminance = analyzeLuminance;
    }

    public event Action<string>? StatusChanged;
    public event Action<MirrorTelemetry>? TelemetryChanged;
    public event Action<LuminanceTelemetry>? LuminanceChanged;
    public event Action<GamutTelemetry>? GamutChanged;
    public event Action<Exception>? Failed;
    public event Action? Stopped;

    public void SetFalseColor(bool enabled) =>
        Volatile.Write(ref _falseColorEnabled, enabled ? 1 : 0);

    public void SetGamutAnalysis(bool enabled) =>
        Volatile.Write(ref _gamutAnalysisEnabled, enabled ? 1 : 0);

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_renderThread is not null)
            throw new InvalidOperationException(Localization.T("SessionStarted"));

        _renderThread = new Thread(RenderThreadMain)
        {
            IsBackground = true,
            Name = "HDRScreenMirror Render"
        };
        _renderThread.Start();

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        _initialized.Task.WaitAsync(timeout.Token).GetAwaiter().GetResult();
    }

    public void Stop()
    {
        _cancellation.Cancel();
        Thread? thread = _renderThread;
        if (thread is not null && thread != Thread.CurrentThread)
            thread.Join(TimeSpan.FromSeconds(5));
    }

    private void RenderThreadMain()
    {
        try
        {
            InitializeDirectX();
            NativeMethods.SetThreadExecutionState(
                NativeMethods.EsContinuous | NativeMethods.EsDisplayRequired | NativeMethods.EsSystemRequired);
            _initialized.TrySetResult();
            RenderLoop(_cancellation.Token);
        }
        catch (Exception exception)
        {
            _initialized.TrySetException(exception);
            if (!_cancellation.IsCancellationRequested)
                Failed?.Invoke(exception);
        }
        finally
        {
            DisposeDirectX();
            NativeMethods.SetThreadExecutionState(NativeMethods.EsContinuous);
            Stopped?.Invoke();
        }
    }

    private void InitializeDirectX()
    {
        _factory = CreateDXGIFactory1<IDXGIFactory2>();

        _factory.EnumAdapters1((uint)_capture.AdapterIndex, out IDXGIAdapter1? adapter).CheckError();
        _adapter = adapter ?? throw new InvalidOperationException(Localization.T("OpenAdapterFailed"));

        _adapter.EnumOutputs((uint)_capture.OutputIndex, out IDXGIOutput? captureOutput).CheckError();
        _captureOutput = captureOutput ?? throw new InvalidOperationException(Localization.T("OpenCaptureFailed"));

        Result createDevice = D3D11CreateDevice(
            _adapter,
            DriverType.Unknown,
            DeviceCreationFlags.BgraSupport,
            FeatureLevels,
            out ID3D11Device device,
            out _,
            out ID3D11DeviceContext context);
        createDevice.CheckError();
        _device = device;
        _context = context;

        if (_frameRateMode == FrameRateMode.Fixed)
        {
            _fixedFrameTimer = NativeMethods.CreateFrameRateTimer();
            if (_fixedFrameTimer == nint.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        bool duplicationReady = TryCreateDuplication();
        CreatePresenters();
        CreatePipeline();
        StatusChanged?.Invoke(Localization.T(duplicationReady ? "D3DReady" : "SecureDesktopWaiting"));
    }

    private bool TryCreateDuplication()
    {
        _duplication?.Dispose();
        _duplication = null;

        using IDXGIOutput5? output5 = _captureOutput!.QueryInterfaceOrNull<IDXGIOutput5>();
        if (output5 is not null)
        {
            Format[] preferredFormats =
            [
                Format.R16G16B16A16_Float,
                Format.R10G10B10A2_UNorm,
                Format.B8G8R8A8_UNorm
            ];

            try
            {
                _duplication = output5.DuplicateOutput1(_device!, preferredFormats);
            }
            catch (SharpGenException exception) when (exception.HResult == ErrorAccessDenied)
            {
                SetCaptureAccessPaused(true);
                return false;
            }
            catch (SharpGenException)
            {
                _duplication = null;
            }
        }

        if (_duplication is null)
        {
            using IDXGIOutput1 output1 = _captureOutput!.QueryInterface<IDXGIOutput1>();
            try
            {
                _duplication = output1.DuplicateOutput(_device!);
            }
            catch (SharpGenException exception) when (exception.HResult == ErrorAccessDenied)
            {
                SetCaptureAccessPaused(true);
                return false;
            }
        }

        SetCaptureAccessPaused(false);
        return true;
    }

    private void CreatePresenters()
    {
        foreach (MirrorOutputBinding output in _outputs)
        {
            if (output.Display.AdapterIndex != _capture.AdapterIndex)
            {
                throw new InvalidOperationException(Localization.F("OutputCrossGpu", output.Display.DeviceName));
            }

            _adapter!.EnumOutputs((uint)output.Display.OutputIndex, out IDXGIOutput? presentOutput).CheckError();
            using IDXGIOutput actualOutput =
                presentOutput ?? throw new InvalidOperationException(
                    Localization.F("OpenOutputFailed", output.Display.DeviceName));

            SwapChainDescription1 description = new()
            {
                Width = 0,
                Height = 0,
                Format = Format.R16G16B16A16_Float,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = SampleDescription.Default,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = AlphaMode.Ignore,
                Flags = _frameRateMode == FrameRateMode.Unlimited
                    ? SwapChainFlags.None
                    : SwapChainFlags.FrameLatencyWaitableObject
            };

            SwapChainFullscreenDescription fullscreenDescription = new() { Windowed = true };
            IDXGISwapChain1 swapChain = _factory!.CreateSwapChainForHwnd(
                _device!,
                output.WindowHandle,
                description,
                fullscreenDescription,
                actualOutput);
            _factory.MakeWindowAssociation(output.WindowHandle, WindowAssociationFlags.IgnoreAltEnter);

            using IDXGISwapChain3 swapChain3 = swapChain.QueryInterface<IDXGISwapChain3>();
            SwapChainColorSpaceSupportFlags support =
                swapChain3.CheckColorSpaceSupport(ColorSpaceType.RgbFullG10NoneP709);
            if ((support & SwapChainColorSpaceSupportFlags.Present) == 0)
            {
                swapChain.Dispose();
                throw new InvalidOperationException(Localization.F("Fp16Unsupported", output.Display.DeviceName));
            }

            swapChain3.SetColorSpace1(ColorSpaceType.RgbFullG10NoneP709);
            nint frameLatencyWaitableObject = nint.Zero;
            if (_frameRateMode != FrameRateMode.Unlimited)
            {
                using IDXGISwapChain2 swapChain2 = swapChain.QueryInterface<IDXGISwapChain2>();
                swapChain2.MaximumFrameLatency = 1;
                frameLatencyWaitableObject = swapChain2.FrameLatencyWaitableObject;
                if (frameLatencyWaitableObject == nint.Zero)
                {
                    swapChain.Dispose();
                    throw new InvalidOperationException(
                        Localization.F("FrameLatencyUnavailable", output.Display.DeviceName));
                }
            }

            ID3D11Texture2D backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
            ID3D11RenderTargetView renderTarget = _device!.CreateRenderTargetView(backBuffer);
            _presenters.Add(new OutputPresenter(
                swapChain,
                backBuffer,
                renderTarget,
                frameLatencyWaitableObject));
        }
    }

    private void CreatePipeline()
    {
        string shaderDirectory = Path.Combine(AppContext.BaseDirectory, "Shaders");
        byte[] vertexBytecode = File.ReadAllBytes(Path.Combine(shaderDirectory, "MirrorVS.cso"));
        byte[] pixelBytecode = File.ReadAllBytes(Path.Combine(shaderDirectory, "MirrorPS.cso"));
        byte[] cursorVertexBytecode = File.ReadAllBytes(Path.Combine(shaderDirectory, "CursorVS.cso"));
        byte[] cursorPixelBytecode = File.ReadAllBytes(Path.Combine(shaderDirectory, "CursorPS.cso"));
        _vertexShader = _device!.CreateVertexShader(vertexBytecode);
        _pixelShader = _device.CreatePixelShader(pixelBytecode);
        _cursorVertexShader = _device.CreateVertexShader(cursorVertexBytecode);
        _cursorPixelShader = _device.CreatePixelShader(cursorPixelBytecode);
        if (_analyzeLuminance)
        {
            byte[] luminanceBytecode = File.ReadAllBytes(Path.Combine(shaderDirectory, "LuminanceCS.cso"));
            byte[] pointerProbeBytecode = File.ReadAllBytes(Path.Combine(shaderDirectory, "PointerProbeCS.cso"));
            byte[] gamutClearBytecode = File.ReadAllBytes(Path.Combine(shaderDirectory, "GamutClearCS.cso"));
            byte[] gamutAnalysisBytecode = File.ReadAllBytes(Path.Combine(shaderDirectory, "GamutAnalysisCS.cso"));
            _luminanceComputeShader = _device.CreateComputeShader(luminanceBytecode);
            _pointerProbeComputeShader = _device.CreateComputeShader(pointerProbeBytecode);
            _gamutClearComputeShader = _device.CreateComputeShader(gamutClearBytecode);
            _gamutAnalysisComputeShader = _device.CreateComputeShader(gamutAnalysisBytecode);
        }
        _sampler = _device.CreateSamplerState(SamplerDescription.LinearClamp);
        _opaqueBlendState = _device.CreateBlendState(BlendDescription.Opaque);
        _cursorBlendState = _device.CreateBlendState(BlendDescription.NonPremultiplied);

        BufferDescription cursorConstantDescription = new()
        {
            ByteWidth = (uint)Marshal.SizeOf<CursorConstants>(),
            BindFlags = BindFlags.ConstantBuffer,
            Usage = ResourceUsage.Default,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
            StructureByteStride = 0
        };
        _cursorConstantBuffer = _device.CreateBuffer(cursorConstantDescription);
    }

    private void RenderLoop(CancellationToken cancellationToken)
    {
        Stopwatch reportingClock = Stopwatch.StartNew();
        Stopwatch luminanceClock = Stopwatch.StartNew();
        Stopwatch frameLuminanceClock = Stopwatch.StartNew();
        long frames = 0;
        long intervalFrames = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            WaitForFramePacing(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                break;

            bool rendered = AcquireAndRender(cancellationToken);
            if (rendered)
            {
                frames++;
                intervalFrames++;

                if (_analyzeLuminance && luminanceClock.Elapsed >= PointerAnalysisInterval)
                {
                    bool refreshFrameLuminance =
                        !_hasFrameLuminance || frameLuminanceClock.Elapsed >= FrameAnalysisInterval;
                    LuminanceTelemetry? luminance = AnalyzeLuminance(refreshFrameLuminance);
                    if (luminance is not null)
                        LuminanceChanged?.Invoke(luminance);
                    if (refreshFrameLuminance && Volatile.Read(ref _gamutAnalysisEnabled) != 0)
                    {
                        GamutTelemetry? gamut = AnalyzeGamut();
                        if (gamut is not null)
                            GamutChanged?.Invoke(gamut);
                    }
                    if (refreshFrameLuminance)
                        frameLuminanceClock.Restart();
                    luminanceClock.Restart();
                }
            }

            if (reportingClock.Elapsed >= TimeSpan.FromSeconds(1))
            {
                double fps = intervalFrames / reportingClock.Elapsed.TotalSeconds;
                string hdr = Localization.T(
                    _inputFormat == Format.R16G16B16A16_Float ||
                    _inputFormat == Format.R10G10B10A2_UNorm
                        ? "Hdr"
                        : "SdrFallback");
                string cursorState = !_renderCursor
                    ? Localization.T("CursorDisabled")
                    : !_cursorVisible
                    ? Localization.T("CursorHidden")
                    : _cursorView is not null
                        ? Localization.T("CursorRendered")
                        : Localization.T("CursorWaiting");
                if (_captureAccessPaused)
                    StatusChanged?.Invoke(Localization.T("SecureDesktopWaiting"));
                else
                    StatusChanged?.Invoke(Localization.F(
                        "RunningStatus",
                        fps,
                        _inputFormat,
                        hdr,
                        _frameWidth,
                        _frameHeight,
                        cursorState,
                        frames));
                TelemetryChanged?.Invoke(new MirrorTelemetry(
                    Localization.T(_captureAccessPaused ? "SecureDesktopPaused" : "Running"),
                    fps,
                    _inputFormat.ToString(),
                    hdr,
                    _frameWidth,
                    _frameHeight,
                    frames,
                    _renderCursor && _cursorVisible));
                reportingClock.Restart();
                intervalFrames = 0;
            }
        }
    }

    private void WaitForFramePacing(CancellationToken cancellationToken)
    {
        if (_frameRateMode == FrameRateMode.Fixed)
            WaitForFixedFrameRate(cancellationToken);

        if (_waitForOutputBeforeNextFrame && !cancellationToken.IsCancellationRequested)
        {
            WaitForOutputAvailability(cancellationToken);
            _waitForOutputBeforeNextFrame = false;
        }
    }

    private void WaitForFixedFrameRate(CancellationToken cancellationToken)
    {
        long interval = Math.Max(1, Stopwatch.Frequency / _frameRateLimit);
        long now = Stopwatch.GetTimestamp();
        if (_nextFixedFrameTimestamp == 0)
        {
            _nextFixedFrameTimestamp = now;
            return;
        }

        long deadline = _nextFixedFrameTimestamp + interval;
        if (now > deadline + interval)
            deadline = now;

        now = Stopwatch.GetTimestamp();
        long remaining = deadline - now;
        if (remaining > 0)
        {
            long relativeHundredNanoseconds = Math.Max(
                1,
                (long)Math.Ceiling(remaining * 10_000_000.0 / Stopwatch.Frequency));
            if (!NativeMethods.SetFrameRateTimer(_fixedFrameTimer, relativeHundredNanoseconds))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            while (!cancellationToken.IsCancellationRequested)
            {
                uint result = NativeMethods.WaitForSingleObject(_fixedFrameTimer, 50);
                if (result == NativeMethods.WaitObject0)
                    break;
                if (result == NativeMethods.WaitTimeout)
                    continue;
                if (result == NativeMethods.WaitFailed)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                throw new InvalidOperationException(Localization.T("FrameRateWaitFailed"));
            }
        }

        _nextFixedFrameTimestamp = deadline;
    }

    private void WaitForOutputAvailability(CancellationToken cancellationToken)
    {
        if (_presenters.Count == 0)
            return;

        nint waitableObject = _presenters[^1].FrameLatencyWaitableObject;
        while (!cancellationToken.IsCancellationRequested)
        {
            uint result = NativeMethods.WaitForSingleObject(waitableObject, 50);
            if (result == NativeMethods.WaitObject0)
                return;
            if (result == NativeMethods.WaitTimeout)
                continue;
            if (result == NativeMethods.WaitFailed)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            throw new InvalidOperationException(Localization.T("FrameLatencyWaitFailed"));
        }
    }

    private bool AcquireAndRender(CancellationToken cancellationToken)
    {
        if (_duplication is null)
        {
            if (!TryCreateDuplication())
            {
                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(250));
                return false;
            }
        }

        IDXGIOutputDuplication duplication = _duplication!;
        Result result = duplication.AcquireNextFrame(
            16,
            out OutduplFrameInfo frameInfo,
            out IDXGIResource? desktopResource);
        if (result.Code == DxgiErrorWaitTimeout)
            return false;

        if (result.Code == DxgiErrorAccessLost || result.Code == ErrorAccessDenied)
        {
            duplication.Dispose();
            _duplication = null;
            if (result.Code == ErrorAccessDenied)
                SetCaptureAccessPaused(true);
            cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(100));
            return false;
        }

        result.CheckError();
        if (desktopResource is null)
        {
            UpdatePointer(frameInfo);
            duplication.ReleaseFrame();
            return false;
        }

        try
        {
            using (desktopResource)
            using (ID3D11Texture2D sourceTexture = desktopResource.QueryInterface<ID3D11Texture2D>())
            {
                EnsureFrameResources(sourceTexture.Description);
                _context!.CopyResource(_frameTexture!, sourceTexture);
            }

            UpdatePointer(frameInfo);
        }
        finally
        {
            duplication.ReleaseFrame();
        }

        DrawFrame();
        return true;
    }

    private void SetCaptureAccessPaused(bool paused)
    {
        if (_captureAccessPaused == paused)
            return;

        _captureAccessPaused = paused;
        StatusChanged?.Invoke(Localization.T(paused ? "SecureDesktopWaiting" : "CaptureResumed"));
    }

    private void EnsureFrameResources(Texture2DDescription sourceDescription)
    {
        if (_frameTexture is not null &&
            _inputFormat == sourceDescription.Format &&
            _frameWidth == sourceDescription.Width &&
            _frameHeight == sourceDescription.Height)
            return;

        _frameView?.Dispose();
        _frameTexture?.Dispose();
        _constantBuffer?.Dispose();
        DisposeAnalysisResources();

        _inputFormat = sourceDescription.Format;
        _frameWidth = sourceDescription.Width;
        _frameHeight = sourceDescription.Height;
        _rotationCode = MapRotation(_capture.Rotation);

        Texture2DDescription frameDescription = new(
            _inputFormat,
            _frameWidth,
            _frameHeight,
            1,
            1,
            BindFlags.ShaderResource,
            ResourceUsage.Default,
            CpuAccessFlags.None,
            1,
            0,
            ResourceOptionFlags.None);
        _frameTexture = _device!.CreateTexture2D(frameDescription);
        _frameView = _device.CreateShaderResourceView(_frameTexture);

        _inputMode = _inputFormat switch
        {
            Format.R16G16B16A16_Float => 0,
            Format.R10G10B10A2_UNorm => 2,
            Format.B8G8R8A8_UNorm => 1,
            _ => throw new NotSupportedException(Localization.F("UnsupportedFormat", _inputFormat))
        };

        MirrorConstants constants = CreateMirrorConstants();
        BufferDescription constantDescription = new()
        {
            ByteWidth = (uint)Marshal.SizeOf<MirrorConstants>(),
            BindFlags = BindFlags.ConstantBuffer,
            Usage = ResourceUsage.Default,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
            StructureByteStride = 0
        };
        _constantBuffer = _device.CreateBuffer(constants, constantDescription);
        if (_analyzeLuminance)
            CreateAnalysisResources();
    }

    private void DrawFrame()
    {
        _context!.UpdateSubresource(CreateMirrorConstants(), _constantBuffer!);
        for (int i = 0; i < _presenters.Count; i++)
        {
            OutputPresenter presenter = _presenters[i];
            _context!.OMSetRenderTargets(presenter.RenderTarget);
            _context.ClearRenderTargetView(presenter.RenderTarget, new Color4(0, 0, 0, 1));
            _context.RSSetViewport(CalculateViewport(presenter));
            _context.OMSetBlendState(_opaqueBlendState!);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _context.VSSetShader(_vertexShader);
            _context.PSSetShader(_pixelShader);
            _context.PSSetShaderResource(0, _frameView!);
            _context.PSSetSampler(0, _sampler);
            _context.PSSetConstantBuffer(0, _constantBuffer);
            _context.Draw(3, 0);
            _context.PSSetShaderResource(0, null!);

            DrawCursor(presenter);

            uint syncInterval =
                _frameRateMode != FrameRateMode.Unlimited && i == _presenters.Count - 1 ? 1u : 0u;
            Result result = presenter.SwapChain.Present(syncInterval, PresentFlags.None);
            result.CheckError();
        }

        _waitForOutputBeforeNextFrame = _frameRateMode != FrameRateMode.Unlimited;
    }

    private Viewport CalculateViewport(OutputPresenter presenter)
    {
        float sourceWidth = _frameWidth;
        float sourceHeight = _frameHeight;
        if (_rotationCode is 1 or 3)
            (sourceWidth, sourceHeight) = (sourceHeight, sourceWidth);

        float outputWidth = presenter.Width;
        float outputHeight = presenter.Height;
        float scale = Math.Min(outputWidth / sourceWidth, outputHeight / sourceHeight);
        float width = sourceWidth * scale;
        float height = sourceHeight * scale;
        float x = (outputWidth - width) * 0.5f;
        float y = (outputHeight - height) * 0.5f;
        return new Viewport(x, y, width, height, 0, 1);
    }

    private void UpdatePointer(OutduplFrameInfo frameInfo)
    {
        if (frameInfo.LastMouseUpdateTime != 0)
        {
            _cursorVisible = frameInfo.PointerPosition.Visible;
            _cursorX = frameInfo.PointerPosition.Position.X;
            _cursorY = frameInfo.PointerPosition.Position.Y;
        }

        if (!_renderCursor || frameInfo.PointerShapeBufferSize == 0)
            return;

        byte[] shapeBuffer = new byte[frameInfo.PointerShapeBufferSize];
        OutduplPointerShapeInfo shapeInfo;
        uint requiredSize;
        unsafe
        {
            fixed (byte* bufferPointer = shapeBuffer)
            {
                Result result = _duplication!.GetFramePointerShape(
                    (uint)shapeBuffer.Length,
                    (nint)bufferPointer,
                    out requiredSize,
                    out shapeInfo);
                result.CheckError();
            }
        }

        if (requiredSize > shapeBuffer.Length)
            throw new InvalidOperationException(Localization.T("InvalidPointerBuffer"));

        UploadPointerShape(shapeBuffer, shapeInfo);
    }

    private void UploadPointerShape(byte[] source, OutduplPointerShapeInfo shapeInfo)
    {
        const uint monochrome = 1;
        const uint color = 2;
        const uint maskedColor = 4;

        uint displayHeight = shapeInfo.Type == monochrome ? shapeInfo.Height / 2 : shapeInfo.Height;
        if (shapeInfo.Width == 0 || displayHeight == 0)
            return;

        byte[] rgba = new byte[checked((int)(shapeInfo.Width * displayHeight * 4))];
        if (shapeInfo.Type is color or maskedColor)
        {
            for (uint y = 0; y < displayHeight; y++)
            {
                for (uint x = 0; x < shapeInfo.Width; x++)
                {
                    int sourceIndex = checked((int)(y * shapeInfo.Pitch + x * 4));
                    int destinationIndex = checked((int)((y * shapeInfo.Width + x) * 4));
                    byte blue = source[sourceIndex];
                    byte green = source[sourceIndex + 1];
                    byte red = source[sourceIndex + 2];
                    byte alpha = source[sourceIndex + 3];

                    rgba[destinationIndex] = red;
                    rgba[destinationIndex + 1] = green;
                    rgba[destinationIndex + 2] = blue;
                    rgba[destinationIndex + 3] = shapeInfo.Type == maskedColor && alpha == 0
                        ? (byte)220
                        : alpha;
                }
            }
        }
        else if (shapeInfo.Type == monochrome)
        {
            int xorOffset = checked((int)(displayHeight * shapeInfo.Pitch));
            for (uint y = 0; y < displayHeight; y++)
            {
                for (uint x = 0; x < shapeInfo.Width; x++)
                {
                    int byteIndex = checked((int)(y * shapeInfo.Pitch + x / 8));
                    int bitMask = 0x80 >> (int)(x % 8);
                    bool andBit = (source[byteIndex] & bitMask) != 0;
                    bool xorBit = (source[xorOffset + byteIndex] & bitMask) != 0;
                    int destinationIndex = checked((int)((y * shapeInfo.Width + x) * 4));

                    byte value = xorBit ? (byte)255 : (byte)0;
                    byte alpha = andBit && !xorBit ? (byte)0 : (byte)255;
                    rgba[destinationIndex] = value;
                    rgba[destinationIndex + 1] = value;
                    rgba[destinationIndex + 2] = value;
                    rgba[destinationIndex + 3] = alpha;
                }
            }
        }
        else
        {
            return;
        }

        _cursorView?.Dispose();
        _cursorTexture?.Dispose();
        _cursorTexture = _device!.CreateTexture2D(
            rgba,
            Format.R8G8B8A8_UNorm,
            shapeInfo.Width,
            displayHeight,
            bindFlags: BindFlags.ShaderResource);
        _cursorView = _device.CreateShaderResourceView(_cursorTexture);
        _cursorWidth = shapeInfo.Width;
        _cursorHeight = displayHeight;
        _cursorHotSpotX = shapeInfo.HotSpot.X;
        _cursorHotSpotY = shapeInfo.HotSpot.Y;
    }

    private void DrawCursor(OutputPresenter presenter)
    {
        if (!_renderCursor || !_cursorVisible || _cursorView is null || _cursorWidth == 0 || _cursorHeight == 0)
            return;

        Viewport imageViewport = CalculateViewport(presenter);
        float sourceWidth = _rotationCode is 1 or 3 ? _frameHeight : _frameWidth;
        float scale = imageViewport.Width / sourceWidth;
        float x = imageViewport.X + (_cursorX - _cursorHotSpotX) * scale;
        float y = imageViewport.Y + (_cursorY - _cursorHotSpotY) * scale;
        CursorConstants constants = new(
            x,
            y,
            _cursorWidth * scale,
            _cursorHeight * scale,
            presenter.Width,
            presenter.Height,
            0,
            0);
        _context!.UpdateSubresource(constants, _cursorConstantBuffer!);

        _context.RSSetViewport(new Viewport(0, 0, presenter.Width, presenter.Height));
        _context.OMSetBlendState(_cursorBlendState!);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        _context.VSSetShader(_cursorVertexShader);
        _context.VSSetConstantBuffer(1, _cursorConstantBuffer);
        _context.PSSetShader(_cursorPixelShader);
        _context.PSSetConstantBuffer(0, _constantBuffer);
        _context.PSSetShaderResource(1, _cursorView);
        _context.PSSetSampler(0, _sampler);
        _context.Draw(4, 0);
        _context.PSSetShaderResource(1, null!);
    }

    private MirrorConstants CreateMirrorConstants() => new(
        _paperWhiteNits,
        _inputMode,
        (uint)_rotationCode,
        Volatile.Read(ref _falseColorEnabled) != 0 ? 1u : 0u,
        _frameWidth,
        _frameHeight,
        _probePointerX,
        _probePointerY);

    private void CreateAnalysisResources()
    {
        uint groupCountX = (_frameWidth + 15) / 16;
        uint groupCountY = (_frameHeight + 15) / 16;
        _frameStatsCount = checked((int)(groupCountX * groupCountY));
        uint resultStride = (uint)Marshal.SizeOf<LuminanceGroupResult>();
        uint frameBufferSize = checked((uint)_frameStatsCount * resultStride);

        BufferDescription frameDescription = new(
            frameBufferSize,
            BindFlags.UnorderedAccess,
            ResourceUsage.Default,
            CpuAccessFlags.None,
            ResourceOptionFlags.BufferStructured,
            resultStride);
        _frameStatsBuffer = _device!.CreateBuffer(frameDescription);
        _frameStatsView = _device.CreateUnorderedAccessView(_frameStatsBuffer);

        BufferDescription frameReadbackDescription = new(
            frameBufferSize,
            BindFlags.None,
            ResourceUsage.Staging,
            CpuAccessFlags.Read,
            ResourceOptionFlags.BufferStructured,
            resultStride);
        _frameStatsReadback = _device.CreateBuffer(frameReadbackDescription);

        BufferDescription pointerDescription = new(
            resultStride,
            BindFlags.UnorderedAccess,
            ResourceUsage.Default,
            CpuAccessFlags.None,
            ResourceOptionFlags.BufferStructured,
            resultStride);
        _pointerStatsBuffer = _device.CreateBuffer(pointerDescription);
        _pointerStatsView = _device.CreateUnorderedAccessView(_pointerStatsBuffer);

        BufferDescription pointerReadbackDescription = new(
            resultStride,
            BindFlags.None,
            ResourceUsage.Staging,
            CpuAccessFlags.Read,
            ResourceOptionFlags.BufferStructured,
            resultStride);
        _pointerStatsReadback = _device.CreateBuffer(pointerReadbackDescription);

        uint gamutBufferSize = checked((uint)GamutElementCount * sizeof(uint));
        BufferDescription gamutDescription = new(
            gamutBufferSize,
            BindFlags.UnorderedAccess,
            ResourceUsage.Default,
            CpuAccessFlags.None,
            ResourceOptionFlags.BufferStructured,
            sizeof(uint));
        _gamutBuffer = _device.CreateBuffer(gamutDescription);
        _gamutView = _device.CreateUnorderedAccessView(_gamutBuffer);

        BufferDescription gamutReadbackDescription = new(
            gamutBufferSize,
            BindFlags.None,
            ResourceUsage.Staging,
            CpuAccessFlags.Read,
            ResourceOptionFlags.BufferStructured,
            sizeof(uint));
        _gamutReadback = _device.CreateBuffer(gamutReadbackDescription);
    }

    private GamutTelemetry? AnalyzeGamut()
    {
        if (_frameView is null || _constantBuffer is null ||
            _gamutBuffer is null || _gamutView is null || _gamutReadback is null ||
            _gamutClearComputeShader is null || _gamutAnalysisComputeShader is null)
        {
            return null;
        }

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

        uint[] histogram = new uint[GamutHistogramCount];
        ulong[] counters = new ulong[GamutCounterCount];
        MappedSubresource map = _context.Map(
            _gamutReadback,
            MapMode.Read,
            Vortice.Direct3D11.MapFlags.None);
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
            _context.Unmap(_gamutReadback, 0);
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

        UpdateProbePointerPosition();
        _context!.UpdateSubresource(CreateMirrorConstants(), _constantBuffer);
        _context.CSSetConstantBuffer(0, _constantBuffer);
        _context.CSSetShaderResource(0, _frameView);

        if (refreshFrameLuminance)
        {
            _context.CSSetShader(_luminanceComputeShader);
            _context.CSSetUnorderedAccessView(0, _frameStatsView);
            _context.Dispatch((_frameWidth + 15) / 16, (_frameHeight + 15) / 16, 1);
            _context.CSSetUnorderedAccessView(0, null!);
            _context.CopyResource(_frameStatsReadback, _frameStatsBuffer);
        }

        bool pointerInCaptureArea = IsPointerInCaptureArea();
        if (pointerInCaptureArea)
        {
            _context.CSSetShader(_pointerProbeComputeShader);
            _context.CSSetUnorderedAccessView(0, _pointerStatsView);
            _context.Dispatch(1, 1, 1);
            _context.CSSetUnorderedAccessView(0, null!);
            _context.CopyResource(_pointerStatsReadback, _pointerStatsBuffer);
        }

        _context.CSSetShaderResource(0, null!);
        _context.CSSetShader(null!);

        if (refreshFrameLuminance)
        {
            double sum = 0;
            double minimum = double.PositiveInfinity;
            double maximum = 0;
            ulong count = 0;
            uint maximumX = 0;
            uint maximumY = 0;
            uint minimumX = 0;
            uint minimumY = 0;
            MappedSubresource frameMap = _context.Map(
                _frameStatsReadback,
                MapMode.Read,
                Vortice.Direct3D11.MapFlags.None);
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
                    }
                }
            }
            finally
            {
                _context.Unmap(_frameStatsReadback, 0);
            }

            if (count == 0)
                return null;

            _lastFrameAverageNits = sum / count;
            _lastFrameMaximumNits = maximum;
            _lastFrameMinimumNits = double.IsPositiveInfinity(minimum) ? 0 : minimum;
            _lastFrameMaximumX = maximumX;
            _lastFrameMaximumY = maximumY;
            _lastFrameMinimumX = minimumX;
            _lastFrameMinimumY = minimumY;
            _hasFrameLuminance = true;
        }

        double pointerRegionNits = 0;
        if (pointerInCaptureArea)
        {
            MappedSubresource pointerMap = _context.Map(
                _pointerStatsReadback,
                MapMode.Read,
                Vortice.Direct3D11.MapFlags.None);
            try
            {
                unsafe
                {
                    LuminanceGroupResult result = *(LuminanceGroupResult*)pointerMap.DataPointer;
                    if (result.Count > 0)
                        pointerRegionNits = result.Sum / result.Count;
                    else
                        pointerInCaptureArea = false;
                }
            }
            finally
            {
                _context.Unmap(_pointerStatsReadback, 0);
            }
        }

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
            pointerInCaptureArea,
            pointerRegionNits);
    }

    private void UpdateProbePointerPosition()
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.NativePoint point))
            return;

        int localX = point.X - _capture.Bounds.Left;
        int localY = point.Y - _capture.Bounds.Top;
        if (localX < 0 || localY < 0 ||
            localX >= _capture.Bounds.Width || localY >= _capture.Bounds.Height)
        {
            _probePointerInCaptureArea = false;
            return;
        }

        (_probePointerX, _probePointerY) = _rotationCode switch
        {
            1 => (localY, checked((int)_frameHeight - 1 - localX)),
            2 => (checked((int)_frameWidth - 1 - localX), checked((int)_frameHeight - 1 - localY)),
            3 => (checked((int)_frameWidth - 1 - localY), localX),
            _ => (localX, localY)
        };
        _probePointerInCaptureArea =
            _probePointerX >= 0 && _probePointerY >= 0 &&
            _probePointerX < _frameWidth && _probePointerY < _frameHeight;
    }

    private bool IsPointerInCaptureArea() => _probePointerInCaptureArea;

    private void DisposeAnalysisResources()
    {
        _frameStatsView?.Dispose();
        _frameStatsBuffer?.Dispose();
        _frameStatsReadback?.Dispose();
        _pointerStatsView?.Dispose();
        _pointerStatsBuffer?.Dispose();
        _pointerStatsReadback?.Dispose();
        _gamutView?.Dispose();
        _gamutBuffer?.Dispose();
        _gamutReadback?.Dispose();
        _frameStatsView = null;
        _frameStatsBuffer = null;
        _frameStatsReadback = null;
        _pointerStatsView = null;
        _pointerStatsBuffer = null;
        _pointerStatsReadback = null;
        _gamutView = null;
        _gamutBuffer = null;
        _gamutReadback = null;
        _frameStatsCount = 0;
        _hasFrameLuminance = false;
    }

    private static int MapRotation(ModeRotation rotation) => rotation switch
    {
        ModeRotation.Rotate90 => 1,
        ModeRotation.Rotate180 => 2,
        ModeRotation.Rotate270 => 3,
        _ => 0
    };

    private void DisposeDirectX()
    {
        try
        {
            _context?.ClearState();
            _context?.Flush();
        }
        catch
        {
        }

        _constantBuffer?.Dispose();
        DisposeAnalysisResources();
        _cursorConstantBuffer?.Dispose();
        _cursorView?.Dispose();
        _cursorTexture?.Dispose();
        _cursorBlendState?.Dispose();
        _opaqueBlendState?.Dispose();
        _frameView?.Dispose();
        _frameTexture?.Dispose();
        _sampler?.Dispose();
        _pixelShader?.Dispose();
        _vertexShader?.Dispose();
        _cursorPixelShader?.Dispose();
        _cursorVertexShader?.Dispose();
        _luminanceComputeShader?.Dispose();
        _pointerProbeComputeShader?.Dispose();
        _gamutClearComputeShader?.Dispose();
        _gamutAnalysisComputeShader?.Dispose();
        foreach (OutputPresenter presenter in _presenters)
            presenter.Dispose();
        _presenters.Clear();
        if (_fixedFrameTimer != nint.Zero)
        {
            NativeMethods.CloseHandle(_fixedFrameTimer);
            _fixedFrameTimer = nint.Zero;
        }
        _duplication?.Dispose();
        _captureOutput?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _adapter?.Dispose();
        _factory?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        _cancellation.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct MirrorConstants(
        float PaperWhiteNits,
        uint InputMode,
        uint Rotation,
        uint FalseColorEnabled,
        uint FrameWidth,
        uint FrameHeight,
        int PointerX,
        int PointerY);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct LuminanceGroupResult(
        float Sum,
        float Minimum,
        float Maximum,
        uint Count,
        uint MinimumX,
        uint MinimumY,
        uint MaximumX,
        uint MaximumY);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CursorConstants(
        float X,
        float Y,
        float Width,
        float Height,
        float OutputWidth,
        float OutputHeight,
        float Padding0,
        float Padding1);

    private sealed class OutputPresenter : IDisposable
    {
        public OutputPresenter(
            IDXGISwapChain1 swapChain,
            ID3D11Texture2D backBuffer,
            ID3D11RenderTargetView renderTarget,
            nint frameLatencyWaitableObject)
        {
            SwapChain = swapChain;
            BackBuffer = backBuffer;
            RenderTarget = renderTarget;
            Texture2DDescription description = backBuffer.Description;
            Width = description.Width;
            Height = description.Height;
            FrameLatencyWaitableObject = frameLatencyWaitableObject;
        }

        public IDXGISwapChain1 SwapChain { get; }
        public ID3D11Texture2D BackBuffer { get; }
        public ID3D11RenderTargetView RenderTarget { get; }
        public uint Width { get; }
        public uint Height { get; }
        public nint FrameLatencyWaitableObject { get; }

        public void Dispose()
        {
            RenderTarget.Dispose();
            BackBuffer.Dispose();
            SwapChain.Dispose();
        }
    }
}
