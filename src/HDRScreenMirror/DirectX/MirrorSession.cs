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
    private const int DxgiErrorAccessLost = unchecked((int)0x887A0026);
    private const int DxgiErrorWaitTimeout = unchecked((int)0x887A0027);

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
    private readonly bool _vsync;
    private readonly bool _renderCursor;
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
    private ID3D11SamplerState? _sampler;
    private ID3D11BlendState? _opaqueBlendState;
    private ID3D11BlendState? _cursorBlendState;
    private ID3D11Buffer? _constantBuffer;
    private ID3D11Buffer? _cursorConstantBuffer;
    private ID3D11Texture2D? _cursorTexture;
    private ID3D11ShaderResourceView? _cursorView;
    private Format _inputFormat = Format.Unknown;
    private uint _frameWidth;
    private uint _frameHeight;
    private int _rotationCode;
    private bool _cursorVisible;
    private int _cursorX;
    private int _cursorY;
    private uint _cursorWidth;
    private uint _cursorHeight;
    private int _cursorHotSpotX;
    private int _cursorHotSpotY;

    public MirrorSession(
        DisplayTarget capture,
        DisplayTarget present,
        nint outputWindow,
        float paperWhiteNits,
        bool vsync,
        bool renderCursor = true)
        : this(
            capture,
            [new MirrorOutputBinding(present, outputWindow)],
            paperWhiteNits,
            vsync,
            renderCursor)
    {
    }

    public MirrorSession(
        DisplayTarget capture,
        IReadOnlyList<MirrorOutputBinding> outputs,
        float paperWhiteNits,
        bool vsync,
        bool renderCursor = true)
    {
        if (outputs.Count == 0)
            throw new ArgumentException(Localization.T("NeedOutput"), nameof(outputs));

        _capture = capture;
        _outputs = outputs;
        _paperWhiteNits = paperWhiteNits;
        _vsync = vsync;
        _renderCursor = renderCursor;
    }

    public event Action<string>? StatusChanged;
    public event Action<MirrorTelemetry>? TelemetryChanged;
    public event Action<Exception>? Failed;
    public event Action? Stopped;

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

        CreateDuplication();
        CreatePresenters();
        CreatePipeline();
        StatusChanged?.Invoke(Localization.T("D3DReady"));
    }

    private void CreateDuplication()
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
            catch (SharpGenException)
            {
                _duplication = null;
            }
        }

        if (_duplication is null)
        {
            using IDXGIOutput1 output1 = _captureOutput!.QueryInterface<IDXGIOutput1>();
            _duplication = output1.DuplicateOutput(_device!);
        }
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
                Width = (uint)output.Display.Bounds.Width,
                Height = (uint)output.Display.Bounds.Height,
                Format = Format.R16G16B16A16_Float,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = SampleDescription.Default,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = AlphaMode.Ignore,
                Flags = SwapChainFlags.None
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
            ID3D11Texture2D backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
            ID3D11RenderTargetView renderTarget = _device!.CreateRenderTargetView(backBuffer);
            _presenters.Add(new OutputPresenter(output.Display, swapChain, backBuffer, renderTarget));
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
        long frames = 0;
        long intervalFrames = 0;
        long timeouts = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            bool rendered = AcquireAndRender(ref timeouts);
            if (rendered)
            {
                frames++;
                intervalFrames++;
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
                StatusChanged?.Invoke(Localization.F(
                    "RunningStatus",
                    fps,
                    _inputFormat,
                    hdr,
                    _frameWidth,
                    _frameHeight,
                    cursorState,
                    frames,
                    timeouts));
                TelemetryChanged?.Invoke(new MirrorTelemetry(
                    Localization.T("Running"),
                    fps,
                    _inputFormat.ToString(),
                    hdr,
                    _frameWidth,
                    _frameHeight,
                    frames,
                    timeouts,
                    _cursorVisible));
                reportingClock.Restart();
                intervalFrames = 0;
            }
        }
    }

    private bool AcquireAndRender(ref long timeouts)
    {
        Result result = _duplication!.AcquireNextFrame(16, out OutduplFrameInfo frameInfo, out IDXGIResource? desktopResource);
        if (result.Code == DxgiErrorWaitTimeout)
        {
            timeouts++;
            return false;
        }

        if (result.Code == DxgiErrorAccessLost)
        {
            Thread.Sleep(100);
            CreateDuplication();
            return false;
        }

        result.CheckError();
        if (desktopResource is null)
        {
            UpdatePointer(frameInfo);
            _duplication.ReleaseFrame();
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
            _duplication.ReleaseFrame();
        }

        DrawFrame();
        return true;
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

        uint inputMode = _inputFormat switch
        {
            Format.R16G16B16A16_Float => 0,
            Format.R10G10B10A2_UNorm => 2,
            Format.B8G8R8A8_UNorm => 1,
            _ => throw new NotSupportedException(Localization.F("UnsupportedFormat", _inputFormat))
        };

        MirrorConstants constants = new(_paperWhiteNits, inputMode, (uint)_rotationCode, 0);
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
    }

    private void DrawFrame()
    {
        for (int i = 0; i < _presenters.Count; i++)
        {
            OutputPresenter presenter = _presenters[i];
            _context!.OMSetRenderTargets(presenter.RenderTarget);
            _context.ClearRenderTargetView(presenter.RenderTarget, new Color4(0, 0, 0, 1));
            _context.RSSetViewport(CalculateViewport(presenter.Display));
            _context.OMSetBlendState(_opaqueBlendState!);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _context.VSSetShader(_vertexShader);
            _context.PSSetShader(_pixelShader);
            _context.PSSetShaderResource(0, _frameView!);
            _context.PSSetSampler(0, _sampler);
            _context.PSSetConstantBuffer(0, _constantBuffer);
            _context.Draw(3, 0);
            _context.PSSetShaderResource(0, null!);

            DrawCursor(presenter.Display);

            uint syncInterval = _vsync && i == _presenters.Count - 1 ? 1u : 0u;
            Result result = presenter.SwapChain.Present(syncInterval, PresentFlags.None);
            result.CheckError();
        }
    }

    private Viewport CalculateViewport(DisplayTarget present)
    {
        float sourceWidth = _frameWidth;
        float sourceHeight = _frameHeight;
        if (_rotationCode is 1 or 3)
            (sourceWidth, sourceHeight) = (sourceHeight, sourceWidth);

        float outputWidth = present.Bounds.Width;
        float outputHeight = present.Bounds.Height;
        float scale = Math.Min(outputWidth / sourceWidth, outputHeight / sourceHeight);
        float width = sourceWidth * scale;
        float height = sourceHeight * scale;
        float x = (outputWidth - width) * 0.5f;
        float y = (outputHeight - height) * 0.5f;
        return new Viewport(x, y, width, height, 0, 1);
    }

    private void UpdatePointer(OutduplFrameInfo frameInfo)
    {
        if (!_renderCursor)
        {
            _cursorVisible = false;
            return;
        }

        if (frameInfo.LastMouseUpdateTime != 0)
        {
            _cursorVisible = frameInfo.PointerPosition.Visible;
            _cursorX = frameInfo.PointerPosition.Position.X;
            _cursorY = frameInfo.PointerPosition.Position.Y;
        }

        if (frameInfo.PointerShapeBufferSize == 0)
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

    private void DrawCursor(DisplayTarget present)
    {
        if (!_renderCursor || !_cursorVisible || _cursorView is null || _cursorWidth == 0 || _cursorHeight == 0)
            return;

        Viewport imageViewport = CalculateViewport(present);
        float sourceWidth = _rotationCode is 1 or 3 ? _frameHeight : _frameWidth;
        float scale = imageViewport.Width / sourceWidth;
        float x = imageViewport.X + (_cursorX - _cursorHotSpotX) * scale;
        float y = imageViewport.Y + (_cursorY - _cursorHotSpotY) * scale;
        CursorConstants constants = new(
            x,
            y,
            _cursorWidth * scale,
            _cursorHeight * scale,
            present.Bounds.Width,
            present.Bounds.Height,
            0,
            0);
        _context!.UpdateSubresource(constants, _cursorConstantBuffer!);

        _context.RSSetViewport(new Viewport(0, 0, present.Bounds.Width, present.Bounds.Height));
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
        foreach (OutputPresenter presenter in _presenters)
            presenter.Dispose();
        _presenters.Clear();
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
        float Padding);

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
            DisplayTarget display,
            IDXGISwapChain1 swapChain,
            ID3D11Texture2D backBuffer,
            ID3D11RenderTargetView renderTarget)
        {
            Display = display;
            SwapChain = swapChain;
            BackBuffer = backBuffer;
            RenderTarget = renderTarget;
        }

        public DisplayTarget Display { get; }
        public IDXGISwapChain1 SwapChain { get; }
        public ID3D11Texture2D BackBuffer { get; }
        public ID3D11RenderTargetView RenderTarget { get; }

        public void Dispose()
        {
            RenderTarget.Dispose();
            BackBuffer.Dispose();
            SwapChain.Dispose();
        }
    }
}
