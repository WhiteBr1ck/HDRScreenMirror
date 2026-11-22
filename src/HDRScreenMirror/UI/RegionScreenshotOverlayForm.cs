using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace HDRScreenMirror.UI;

internal enum RegionScreenshotAction
{
    Copy,
    Save
}

internal sealed record RegionScreenshotSelection(
    int OutputIndex,
    Rectangle PixelBounds,
    RegionScreenshotAction Action);

internal sealed class RegionScreenshotOverlayForm : Form
{
    private const float BaselineDpi = 144f;
    private const float DesignFontScale = BaselineDpi / 96f;

    private readonly int _outputIndex;
    private readonly Bitmap _frozenBitmap;
    private readonly Font _badgeFont = new("Segoe UI", 12f * DesignFontScale, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font _hintFont = new("Microsoft YaHei UI", 12f * DesignFontScale, FontStyle.Regular, GraphicsUnit.Pixel);

    private bool _selecting;
    private bool _moving;
    private Point _dragAnchor;
    private Point _moveOffset;
    private Rectangle _selection;
    private RectangleF _copyButtonBounds;
    private RectangleF _saveButtonBounds;
    private RectangleF _cancelButtonBounds;
    private int _hoveredButton = -1;
    private float _dpiScale = 1f;

    public RegionScreenshotOverlayForm(int outputIndex, Rectangle outputBounds, Bitmap frozenBitmap)
    {
        _outputIndex = outputIndex;
        _frozenBitmap = frozenBitmap;

        Text = "HDRScreenMirror Region Capture";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = outputBounds;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        BackColor = Color.Black;
    }

    public event Action<RegionScreenshotSelection>? SelectionConfirmed;
    public event Action? SelectionCanceled;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _dpiScale = Math.Max(0.5f, DeviceDpi / BaselineDpi);
        Activate();
        BringToFront();
        Invalidate();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        _dpiScale = Math.Max(0.5f, e.DeviceDpiNew / BaselineDpi);
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            SelectionCanceled?.Invoke();
            return;
        }

        if (_selection.Width < 2 || _selection.Height < 2)
            return;

        if (e.KeyCode == Keys.Enter || (e.Control && e.KeyCode == Keys.C))
        {
            e.Handled = true;
            Confirm(RegionScreenshotAction.Copy);
        }
        else if (e.KeyCode == Keys.S)
        {
            e.Handled = true;
            Confirm(RegionScreenshotAction.Save);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right)
        {
            if (_selection.Width > 0 && _selection.Height > 0)
            {
                _selection = Rectangle.Empty;
                _hoveredButton = -1;
                Invalidate();
            }
            else
            {
                SelectionCanceled?.Invoke();
            }
            return;
        }

        if (e.Button != MouseButtons.Left)
            return;

        if (HasValidSelection)
        {
            if (_copyButtonBounds.Contains(e.Location))
            {
                Confirm(RegionScreenshotAction.Copy);
                return;
            }
            if (_saveButtonBounds.Contains(e.Location))
            {
                Confirm(RegionScreenshotAction.Save);
                return;
            }
            if (_cancelButtonBounds.Contains(e.Location))
            {
                SelectionCanceled?.Invoke();
                return;
            }
            if (_selection.Contains(e.Location))
            {
                _moving = true;
                _moveOffset = new Point(e.X - _selection.X, e.Y - _selection.Y);
                Cursor = Cursors.SizeAll;
                return;
            }
        }

        _selecting = true;
        _dragAnchor = e.Location;
        _selection = new Rectangle(e.Location, Size.Empty);
        _hoveredButton = -1;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_selecting)
        {
            int x1 = Math.Clamp(Math.Min(_dragAnchor.X, e.X), 0, ClientSize.Width);
            int y1 = Math.Clamp(Math.Min(_dragAnchor.Y, e.Y), 0, ClientSize.Height);
            int x2 = Math.Clamp(Math.Max(_dragAnchor.X, e.X), 0, ClientSize.Width);
            int y2 = Math.Clamp(Math.Max(_dragAnchor.Y, e.Y), 0, ClientSize.Height);
            _selection = new Rectangle(x1, y1, x2 - x1, y2 - y1);
            Invalidate();
            return;
        }

        if (_moving)
        {
            int nextX = Math.Clamp(e.X - _moveOffset.X, 0, Math.Max(0, ClientSize.Width - _selection.Width));
            int nextY = Math.Clamp(e.Y - _moveOffset.Y, 0, Math.Max(0, ClientSize.Height - _selection.Height));
            _selection = new Rectangle(nextX, nextY, _selection.Width, _selection.Height);
            Invalidate();
            return;
        }

        if (HasValidSelection)
        {
            int hovered = _copyButtonBounds.Contains(e.Location) ? 0
                : _saveButtonBounds.Contains(e.Location) ? 1
                : _cancelButtonBounds.Contains(e.Location) ? 2
                : -1;
            Cursor = hovered >= 0 ? Cursors.Hand : _selection.Contains(e.Location) ? Cursors.SizeAll : Cursors.Cross;
            if (_hoveredButton != hovered)
            {
                _hoveredButton = hovered;
                Invalidate();
            }
        }
        else
        {
            Cursor = Cursors.Cross;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_selecting)
        {
            _selecting = false;
            if (_selection.Width < 4 || _selection.Height < 4)
                _selection = Rectangle.Empty;
            Invalidate();
        }
        else if (_moving)
        {
            _moving = false;
            Cursor = _selection.Contains(e.Location) ? Cursors.SizeAll : Cursors.Cross;
            Invalidate();
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && HasValidSelection && _selection.Contains(e.Location))
            Confirm(RegionScreenshotAction.Copy);
    }

    private bool HasValidSelection => !_selecting && _selection.Width >= 4 && _selection.Height >= 4;

    private Rectangle MapSelectionToPixels()
    {
        float scaleX = _frozenBitmap.Width / (float)Math.Max(ClientSize.Width, 1);
        float scaleY = _frozenBitmap.Height / (float)Math.Max(ClientSize.Height, 1);
        int x = Math.Clamp((int)Math.Round(_selection.X * scaleX), 0, _frozenBitmap.Width - 1);
        int y = Math.Clamp((int)Math.Round(_selection.Y * scaleY), 0, _frozenBitmap.Height - 1);
        int width = Math.Clamp((int)Math.Round(_selection.Width * scaleX), 1, _frozenBitmap.Width - x);
        int height = Math.Clamp((int)Math.Round(_selection.Height * scaleY), 1, _frozenBitmap.Height - y);
        return new Rectangle(x, y, width, height);
    }

    private void Confirm(RegionScreenshotAction action)
    {
        if (_selection.Width < 2 || _selection.Height < 2)
            return;

        SelectionConfirmed?.Invoke(new RegionScreenshotSelection(
            _outputIndex,
            MapSelectionToPixels(),
            action));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;

        graphics.DrawImage(_frozenBitmap, ClientRectangle);

        using SolidBrush dimBrush = new(Color.FromArgb(128, 0, 0, 0));
        if (_selection.Width > 0 && _selection.Height > 0)
        {
            using Region dimRegion = new(ClientRectangle);
            dimRegion.Exclude(_selection);
            graphics.FillRegion(dimBrush, dimRegion);

            using Pen borderPen = new(Color.FromArgb(235, 255, 255, 255), 1f);
            graphics.DrawRectangle(borderPen, _selection.X, _selection.Y, _selection.Width - 1, _selection.Height - 1);

            DrawDimensionBadge(graphics);
            if (HasValidSelection)
                DrawActionToolbar(graphics);
        }
        else
        {
            graphics.FillRectangle(dimBrush, ClientRectangle);
            DrawInitialHint(graphics);
        }
    }

    private void DrawInitialHint(Graphics graphics)
    {
        string hint = Localization.T("RegionCaptureHint");
        using StringFormat typographic = CreateTypographicFormat();
        SizeF textSize = graphics.MeasureString(hint, _hintFont, 600, typographic);
        float designW = textSize.Width + 32f;
        float designH = 38f;
        float scaledW = designW * _dpiScale;
        float scaledH = designH * _dpiScale;
        float x = (ClientSize.Width - scaledW) * 0.5f;
        float y = 28f * _dpiScale;

        GraphicsState state = graphics.Save();
        graphics.TranslateTransform(x, y);
        graphics.ScaleTransform(_dpiScale, _dpiScale);

        RectangleF box = new(0.25f, 0.25f, designW - 0.5f, designH - 0.5f);
        using GraphicsPath path = HextaTheme.CreateRoundedPath(box, 10f);
        using SolidBrush bg = new(Color.FromArgb(235, 17, 19, 24));
        using Pen border = new(Color.FromArgb(46, 255, 255, 255), 0.5f);
        using SolidBrush textBrush = new(Color.FromArgb(243, 245, 248));
        graphics.FillPath(bg, path);
        graphics.DrawPath(border, path);
        graphics.DrawString(hint, _hintFont, textBrush, 16f, (designH - _hintFont.Height) * 0.5f, typographic);
        graphics.Restore(state);
    }

    private void DrawDimensionBadge(Graphics graphics)
    {
        Rectangle pixels = MapSelectionToPixels();
        string label = $"{pixels.Width} × {pixels.Height}";
        using StringFormat typographic = CreateTypographicFormat();
        SizeF textSize = graphics.MeasureString(label, _badgeFont, 240, typographic);
        float designW = textSize.Width + 20f;
        float designH = 28f;
        float scaledW = designW * _dpiScale;
        float scaledH = designH * _dpiScale;
        float x = Math.Clamp(_selection.Left, 8f, Math.Max(8f, ClientSize.Width - scaledW - 8f));
        float y = _selection.Top - scaledH - 8f * _dpiScale;
        if (y < 8f)
            y = _selection.Top + 8f * _dpiScale;

        GraphicsState state = graphics.Save();
        graphics.TranslateTransform(x, y);
        graphics.ScaleTransform(_dpiScale, _dpiScale);

        RectangleF box = new(0.25f, 0.25f, designW - 0.5f, designH - 0.5f);
        using GraphicsPath path = HextaTheme.CreateRoundedPath(box, 6f);
        using SolidBrush bg = new(Color.FromArgb(235, 17, 19, 24));
        using Pen border = new(Color.FromArgb(46, 255, 255, 255), 0.5f);
        using SolidBrush textBrush = new(Color.FromArgb(243, 245, 248));
        graphics.FillPath(bg, path);
        graphics.DrawPath(border, path);
        graphics.DrawString(label, _badgeFont, textBrush, 10f, (designH - _badgeFont.Height) * 0.5f, typographic);
        graphics.Restore(state);
    }

    private void DrawActionToolbar(Graphics graphics)
    {
        string copyText = Localization.T("RegionCaptureCopy");
        string saveText = Localization.T("RegionCaptureSave");
        string cancelText = Localization.T("AblCancel");

        using StringFormat typographic = CreateTypographicFormat();
        float copyW = Math.Max(72f, graphics.MeasureString(copyText, _hintFont, 200, typographic).Width + 24f);
        float saveW = Math.Max(72f, graphics.MeasureString(saveText, _hintFont, 200, typographic).Width + 24f);
        float cancelW = Math.Max(60f, graphics.MeasureString(cancelText, _hintFont, 160, typographic).Width + 20f);

        const float pad = 5f;
        const float gap = 4f;
        const float btnH = 30f;
        float barDesignW = pad * 2f + copyW + saveW + cancelW + gap * 2f;
        float barDesignH = pad * 2f + btnH;

        float scaledW = barDesignW * _dpiScale;
        float scaledH = barDesignH * _dpiScale;
        float barX = Math.Clamp(_selection.Right - scaledW, 8f, Math.Max(8f, ClientSize.Width - scaledW - 8f));
        float barY = _selection.Bottom + 10f * _dpiScale;
        if (barY + scaledH + 8f > ClientSize.Height)
            barY = Math.Max(8f, _selection.Top - scaledH - 10f * _dpiScale);

        _copyButtonBounds = new RectangleF(barX + pad * _dpiScale, barY + pad * _dpiScale, copyW * _dpiScale, btnH * _dpiScale);
        _saveButtonBounds = new RectangleF(_copyButtonBounds.Right + gap * _dpiScale, _copyButtonBounds.Y, saveW * _dpiScale, btnH * _dpiScale);
        _cancelButtonBounds = new RectangleF(_saveButtonBounds.Right + gap * _dpiScale, _copyButtonBounds.Y, cancelW * _dpiScale, btnH * _dpiScale);

        GraphicsState state = graphics.Save();
        graphics.TranslateTransform(barX, barY);
        graphics.ScaleTransform(_dpiScale, _dpiScale);

        RectangleF barBounds = new(0.25f, 0.25f, barDesignW - 0.5f, barDesignH - 0.5f);
        using GraphicsPath barPath = HextaTheme.CreateRoundedPath(barBounds, 9f);
        using SolidBrush barBg = new(Color.FromArgb(242, 17, 19, 24));
        using Pen barBorder = new(Color.FromArgb(52, 255, 255, 255), 0.5f);
        graphics.FillPath(barBg, barPath);
        graphics.DrawPath(barBorder, barPath);

        DrawToolbarButton(graphics, new RectangleF(pad, pad, copyW, btnH), copyText, isPrimary: true, hovered: _hoveredButton == 0);
        DrawToolbarButton(graphics, new RectangleF(pad + copyW + gap, pad, saveW, btnH), saveText, isPrimary: false, hovered: _hoveredButton == 1);
        DrawToolbarButton(graphics, new RectangleF(pad + copyW + saveW + gap * 2f, pad, cancelW, btnH), cancelText, isPrimary: false, hovered: _hoveredButton == 2);

        graphics.Restore(state);
    }

    private void DrawToolbarButton(Graphics graphics, RectangleF bounds, string text, bool isPrimary, bool hovered)
    {
        Color bg = isPrimary
            ? (hovered ? Color.FromArgb(228, 228, 231) : Color.FromArgb(250, 250, 250))
            : (hovered ? Color.FromArgb(46, 255, 255, 255) : Color.Transparent);
        Color fg = isPrimary ? Color.FromArgb(9, 9, 11) : Color.FromArgb(236, 239, 244);

        if (bg != Color.Transparent)
        {
            using GraphicsPath path = HextaTheme.CreateRoundedPath(bounds, 5f);
            using SolidBrush fill = new(bg);
            graphics.FillPath(fill, path);
        }

        using StringFormat centered = new(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        using SolidBrush textBrush = new(fg);
        graphics.DrawString(text, _hintFont, textBrush, bounds, centered);
    }

    private static StringFormat CreateTypographicFormat() => new(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Near,
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _badgeFont.Dispose();
            _hintFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
