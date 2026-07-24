using System.Drawing;
using HDRScreenMirror.Interop;

namespace HDRScreenMirror.UI;

internal sealed class MirrorForm : Form
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private readonly bool _mouseThrough;
    private readonly bool _excludeFromCapture;
    private readonly Rectangle _outputBounds;

    public MirrorForm(Rectangle bounds, bool mouseThrough, bool excludeFromCapture = true)
    {
        _mouseThrough = mouseThrough;
        _excludeFromCapture = excludeFromCapture;
        _outputBounds = bounds;
        Text = "HDRScreenMirror Output";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = bounds;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        DoubleBuffered = false;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
            if (_mouseThrough)
                parameters.ExStyle |= WsExTransparent;
            return parameters;
        }
    }

    protected override void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        Bounds = _outputBounds;
        SetCaptureExclusion(true);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs eventArgs)
    {
        base.OnDpiChanged(eventArgs);
        Bounds = _outputBounds;
    }

    internal void SetCaptureExclusion(bool excluded)
    {
        if (!_excludeFromCapture || !IsHandleCreated)
            return;

        NativeMethods.SetWindowDisplayAffinity(
            Handle,
            excluded ? NativeMethods.WdaExcludeFromCapture : NativeMethods.WdaNone);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
    }
}
