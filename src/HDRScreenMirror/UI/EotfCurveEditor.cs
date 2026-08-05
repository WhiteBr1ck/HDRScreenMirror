using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace HDRScreenMirror.UI;

internal sealed class EotfCurveEditor : Control
{
    private static readonly Color GridColor = Color.FromArgb(228, 232, 239);
    private static readonly Color AxisColor = Color.FromArgb(139, 148, 163);
    private static readonly Color ReferenceColor = Color.FromArgb(164, 171, 183);
    private static readonly Color CurveColor = Color.FromArgb(45, 112, 225);
    private static readonly Color DisabledCurveColor = Color.FromArgb(174, 181, 192);
    private static readonly Color TextColor = Color.FromArgb(92, 101, 116);
    private static readonly Color SurfaceColor = Color.White;

    private readonly double[] _values = new double[AblProfile.EotfControlPointCount];
    private double _referencePeakNits;
    private int _dragIndex = -1;
    private int _hoverIndex = -1;
    private bool _draggingClipPoint;
    private bool _hoverClipPoint;
    private bool _hasCustomCurve;
    private bool _usesStandardPq = true;
    private double _clipPq = 1;

    public EotfCurveEditor()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
        BackColor = SurfaceColor;
        MinimumSize = new Size(420, 360);
        TabStop = true;
    }

    public event Action<IReadOnlyList<double>>? CurveChanged;
    public event Action<double>? ClipPointChanged;

    public bool UsesStandardPq => _usesStandardPq;

    public void LoadCurve(
        double referencePeakNits,
        IReadOnlyList<double> values,
        bool usesStandardPq,
        double clipPqPercent,
        bool hasCustomCurve)
    {
        _referencePeakNits = Math.Clamp(referencePeakNits, 0, 10000);
        _clipPq = Math.Clamp(clipPqPercent / 100.0, 0.01, 1);
        double[] standard = AblProfile.CreateStandardEotfControlValues(
            _referencePeakNits,
            _clipPq * 100.0);
        for (int index = 0; index < _values.Length; index++)
        {
            _values[index] = values.Count == _values.Length
                ? Math.Clamp(values[index], 0, _referencePeakNits)
                : standard[index];
        }
        NormalizeValues();
        _usesStandardPq = usesStandardPq;
        _hasCustomCurve = hasCustomCurve;
        _dragIndex = -1;
        _hoverIndex = -1;
        _draggingClipPoint = false;
        _hoverClipPoint = false;
        Cursor = Cursors.Default;
        Invalidate();
    }

    public void RestoreStandardPq()
    {
        _clipPq = Math.Clamp(
            AblProfile.GetStandardClipPqPercent(_referencePeakNits) / 100.0,
            0.01,
            1);
        double[] standard = AblProfile.CreateStandardEotfControlValues(_referencePeakNits);
        Array.Copy(standard, _values, _values.Length);
        _hasCustomCurve = false;
        _usesStandardPq = true;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        base.OnEnabledChanged(eventArgs);
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Enabled ? SurfaceColor : Color.FromArgb(248, 249, 252));

        RectangleF plot = GetPlotBounds();
        if (_referencePeakNits <= 0 || plot.Width <= 1 || plot.Height <= 1)
        {
            TextRenderer.DrawText(
                graphics,
                Localization.T("AblEotfNoPeak"),
                Font,
                ClientRectangle,
                TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        DrawGrid(graphics, plot);
        DrawReferenceCurve(graphics, plot);
        DrawEditableCurve(graphics, plot);
        DrawControlPoints(graphics, plot);
        DrawClipPoint(graphics, plot);
        DrawActiveValue(graphics, plot);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        if (!Enabled || eventArgs.Button != MouseButtons.Left || _referencePeakNits <= 0)
            return;

        if (HitTestClipPoint(eventArgs.Location))
        {
            Focus();
            _draggingClipPoint = true;
            _hoverClipPoint = true;
            Capture = true;
            ApplyClipDrag(eventArgs.X);
            return;
        }

        int index = HitTestControlPoint(eventArgs.Location);
        if (index <= 0 || index >= _values.Length - 1)
            return;

        Focus();
        _dragIndex = index;
        _hoverIndex = index;
        Capture = true;
        ApplyDrag(eventArgs.Y);
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);
        if (_draggingClipPoint)
        {
            ApplyClipDrag(eventArgs.X);
            return;
        }
        if (_dragIndex >= 0)
        {
            ApplyDrag(eventArgs.Y);
            return;
        }

        int oldHover = _hoverIndex;
        bool oldClipHover = _hoverClipPoint;
        _hoverClipPoint = HitTestClipPoint(eventArgs.Location);
        _hoverIndex = HitTestControlPoint(eventArgs.Location);
        bool draggable = _hoverClipPoint ||
            _hoverIndex > 0 && _hoverIndex < _values.Length - 1 && Enabled;
        Cursor = draggable ? Cursors.Hand : Cursors.Default;
        if (oldHover != _hoverIndex || oldClipHover != _hoverClipPoint)
            Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        base.OnMouseUp(eventArgs);
        if (eventArgs.Button != MouseButtons.Left)
            return;

        if (_draggingClipPoint)
        {
            ApplyClipDrag(eventArgs.X);
            _draggingClipPoint = false;
        }
        else if (_dragIndex >= 0)
        {
            ApplyDrag(eventArgs.Y);
            _dragIndex = -1;
        }
        else
        {
            return;
        }
        Capture = false;
        Cursor = _hoverClipPoint || _hoverIndex > 0 && _hoverIndex < _values.Length - 1
            ? Cursors.Hand
            : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        base.OnMouseLeave(eventArgs);
        if (_dragIndex >= 0 || _draggingClipPoint)
            return;

        _hoverIndex = -1;
        _hoverClipPoint = false;
        Cursor = Cursors.Default;
        Invalidate();
    }

    private void DrawGrid(Graphics graphics, RectangleF plot)
    {
        using Pen gridPen = new(GridColor, 1);
        using Pen axisPen = new(AxisColor, 1);
        using SolidBrush textBrush = new(TextColor);
        using StringFormat rightAligned = new()
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        using StringFormat centered = new()
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near,
            FormatFlags = StringFormatFlags.NoWrap
        };

        for (int step = 0; step <= 4; step++)
        {
            float amount = step / 4f;
            float y = plot.Bottom - plot.Height * amount;
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            string label = FormatNits(_referencePeakNits * amount);
            graphics.DrawString(label, Font, textBrush, new RectangleF(0, y - 10, plot.Left - Scale(8), 20), rightAligned);
        }

        for (int step = 0; step <= 5; step++)
        {
            float amount = step / 5f;
            float x = plot.Left + plot.Width * amount;
            graphics.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            graphics.DrawString(
                $"{amount * 100:F0}%",
                Font,
                textBrush,
                new RectangleF(x - Scale(28), plot.Bottom + Scale(7), Scale(56), Scale(22)),
                centered);
        }

        graphics.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
        graphics.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
    }

    private void DrawReferenceCurve(Graphics graphics, RectangleF plot)
    {
        using GraphicsPath path = CreateCurvePath(plot, pq =>
            Math.Min(AblProfile.PqEotf(pq), _referencePeakNits));
        using Pen pen = new(ReferenceColor, Math.Max(1, Scale(1)))
        {
            DashStyle = DashStyle.Dash
        };
        graphics.DrawPath(pen, path);
    }

    private void DrawEditableCurve(Graphics graphics, RectangleF plot)
    {
        using GraphicsPath path = CreateCurvePath(plot, EvaluateCurve);
        using Pen pen = new(Enabled ? CurveColor : DisabledCurveColor, Math.Max(2, Scale(2)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        graphics.DrawPath(pen, path);
    }

    private void DrawControlPoints(Graphics graphics, RectangleF plot)
    {
        float radius = Math.Max(5, Scale(5));
        for (int index = 0; index < _values.Length; index++)
        {
            PointF point = GetControlPoint(plot, index);
            bool endpoint = index == 0 || index == _values.Length - 1;
            bool clipped = index / (double)(_values.Length - 1) >= _clipPq;
            bool active = index == _dragIndex || index == _hoverIndex;
            float currentRadius = active && !endpoint ? radius + Scale(2) : radius;
            RectangleF bounds = new(
                point.X - currentRadius,
                point.Y - currentRadius,
                currentRadius * 2,
                currentRadius * 2);
            using SolidBrush fill = new(endpoint || clipped || !Enabled ? Color.White : CurveColor);
            using Pen outline = new(endpoint || clipped || !Enabled
                ? DisabledCurveColor
                : CurveColor, Math.Max(1, Scale(2)));
            graphics.FillEllipse(fill, bounds);
            graphics.DrawEllipse(outline, bounds);
        }
    }

    private void DrawClipPoint(Graphics graphics, RectangleF plot)
    {
        PointF point = GetClipPoint(plot);
        float radius = Math.Max(7, Scale(_hoverClipPoint || _draggingClipPoint ? 9 : 7));
        PointF[] diamond =
        [
            new(point.X, point.Y - radius),
            new(point.X + radius, point.Y),
            new(point.X, point.Y + radius),
            new(point.X - radius, point.Y)
        ];
        Color color = Enabled ? CurveColor : DisabledCurveColor;
        using SolidBrush fill = new(color);
        using Pen outline = new(Color.White, Math.Max(1, Scale(2)));
        graphics.FillPolygon(fill, diamond);
        graphics.DrawPolygon(outline, diamond);
    }

    private void DrawActiveValue(Graphics graphics, RectangleF plot)
    {
        if (_draggingClipPoint || _hoverClipPoint)
        {
            DrawClipValue(graphics, plot);
            return;
        }

        int index = _dragIndex >= 0 ? _dragIndex : _hoverIndex;
        if (index <= 0 || index >= _values.Length - 1)
            return;

        PointF point = GetControlPoint(plot, index);
        string text = $"PQ {index * 10}%   {FormatNits(_values[index])} nits";
        SizeF measured = graphics.MeasureString(text, Font);
        float width = measured.Width + Scale(18);
        float height = measured.Height + Scale(10);
        float x = Math.Clamp(point.X - width / 2, plot.Left, plot.Right - width);
        float y = Math.Max(plot.Top, point.Y - height - Scale(12));
        RectangleF bounds = new(x, y, width, height);
        using GraphicsPath bubble = CreateRoundedRectangle(bounds, Scale(7));
        using SolidBrush background = new(Color.FromArgb(235, 31, 36, 46));
        using SolidBrush foreground = new(Color.White);
        graphics.FillPath(background, bubble);
        using StringFormat centered = new()
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(text, Font, foreground, bounds, centered);
    }

    private void DrawClipValue(Graphics graphics, RectangleF plot)
    {
        PointF point = GetClipPoint(plot);
        string text = Localization.F(
            "AblEotfClipValue",
            _clipPq * 100.0,
            FormatNits(AblProfile.PqEotf(_clipPq)));
        SizeF measured = graphics.MeasureString(text, Font);
        float width = measured.Width + Scale(18);
        float height = measured.Height + Scale(10);
        float x = Math.Clamp(point.X - width / 2, plot.Left, plot.Right - width);
        RectangleF bounds = new(x, plot.Top + Scale(13), width, height);
        using GraphicsPath bubble = CreateRoundedRectangle(bounds, Scale(7));
        using SolidBrush background = new(Color.FromArgb(235, 31, 36, 46));
        using SolidBrush foreground = new(Color.White);
        graphics.FillPath(background, bubble);
        using StringFormat centered = new()
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(text, Font, foreground, bounds, centered);
    }

    private GraphicsPath CreateCurvePath(RectangleF plot, Func<double, double> evaluate)
    {
        GraphicsPath path = new();
        const int samples = 160;
        PointF[] points = new PointF[samples + 1];
        for (int index = 0; index <= samples; index++)
        {
            double pq = index / (double)samples;
            points[index] = ToPlotPoint(plot, pq, evaluate(pq));
        }
        path.AddLines(points);
        return path;
    }

    private double EvaluateCurve(double pq)
    {
        pq = Math.Clamp(pq, 0, 1);
        if (!_hasCustomCurve)
        {
            double standardClip = Math.Clamp(
                AblProfile.GetStandardClipPqPercent(_referencePeakNits) / 100.0,
                0.000001,
                1);
            return Math.Min(
                AblProfile.PqEotf(Math.Clamp(pq * standardClip / _clipPq, 0, 1)),
                _referencePeakNits);
        }
        if (pq >= _clipPq)
            return _referencePeakNits;

        double position = pq * (_values.Length - 1);
        int lowerIndex = Math.Min((int)Math.Floor(position), _values.Length - 2);
        int upperIndex = lowerIndex + 1;
        double lowerPq = lowerIndex / (double)(_values.Length - 1);
        double upperPq = upperIndex / (double)(_values.Length - 1);
        double upperValue = _values[upperIndex];
        if (upperPq >= _clipPq)
        {
            upperPq = _clipPq;
            upperValue = _referencePeakNits;
        }
        double amount = upperPq > lowerPq ? (pq - lowerPq) / (upperPq - lowerPq) : 1;
        double lower = Math.Log(1 + Math.Clamp(_values[lowerIndex], 0, _referencePeakNits));
        double upper = Math.Log(1 + Math.Clamp(upperValue, 0, _referencePeakNits));
        return Math.Exp(lower + (upper - lower) * amount) - 1;
    }

    private void ApplyDrag(int mouseY)
    {
        if (_dragIndex <= 0 || _dragIndex >= _values.Length - 1)
            return;

        RectangleF plot = GetPlotBounds();
        double amount = Math.Clamp((plot.Bottom - mouseY) / plot.Height, 0, 1);
        double lower = _values[_dragIndex - 1];
        double nextPq = (_dragIndex + 1) / (double)(_values.Length - 1);
        double upper = nextPq >= _clipPq ? _referencePeakNits : _values[_dragIndex + 1];
        _values[_dragIndex] = Math.Clamp(amount * _referencePeakNits, lower, upper);
        _hasCustomCurve = true;
        _usesStandardPq = false;
        CurveChanged?.Invoke(_values.ToArray());
        Invalidate();
    }

    private void ApplyClipDrag(int mouseX)
    {
        RectangleF plot = GetPlotBounds();
        double clipPq = Math.Clamp((mouseX - plot.Left) / plot.Width, 0.01, 1);
        if (Math.Abs(clipPq - _clipPq) < 0.00001)
            return;

        _clipPq = clipPq;
        if (!_hasCustomCurve)
        {
            double[] standard = AblProfile.CreateStandardEotfControlValues(
                _referencePeakNits,
                _clipPq * 100.0);
            Array.Copy(standard, _values, _values.Length);
        }
        _usesStandardPq = false;
        ClipPointChanged?.Invoke(_clipPq * 100.0);
        Invalidate();
    }

    private int HitTestControlPoint(Point location)
    {
        if (_referencePeakNits <= 0)
            return -1;

        RectangleF plot = GetPlotBounds();
        float hitRadius = Math.Max(18, Scale(18));
        float bestDistanceSquared = hitRadius * hitRadius;
        int bestIndex = -1;
        for (int index = 0; index < _values.Length; index++)
        {
            double pointPq = index / (double)(_values.Length - 1);
            if (pointPq >= _clipPq && index != _values.Length - 1)
                continue;

            PointF point = GetControlPoint(plot, index);
            float deltaX = location.X - point.X;
            float deltaY = location.Y - point.Y;
            float distanceSquared = deltaX * deltaX + deltaY * deltaY;
            if (distanceSquared > bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            bestIndex = index;
        }
        return bestIndex;
    }

    private bool HitTestClipPoint(Point location)
    {
        if (_referencePeakNits <= 0)
            return false;

        PointF point = GetClipPoint(GetPlotBounds());
        float hitRadius = Math.Max(20, Scale(20));
        float deltaX = location.X - point.X;
        float deltaY = location.Y - point.Y;
        return deltaX * deltaX + deltaY * deltaY <= hitRadius * hitRadius;
    }

    private RectangleF GetPlotBounds()
    {
        float left = Scale(68);
        float top = Scale(18);
        float right = Scale(18);
        float bottom = Scale(42);
        return new RectangleF(
            left,
            top,
            Math.Max(1, ClientSize.Width - left - right),
            Math.Max(1, ClientSize.Height - top - bottom));
    }

    private PointF GetControlPoint(RectangleF plot, int index) =>
        ToPlotPoint(
            plot,
            index / (double)(_values.Length - 1),
            index / (double)(_values.Length - 1) >= _clipPq
                ? _referencePeakNits
                : _values[index]);

    private PointF GetClipPoint(RectangleF plot) =>
        ToPlotPoint(plot, _clipPq, _referencePeakNits);

    private PointF ToPlotPoint(RectangleF plot, double pq, double outputNits)
    {
        float x = plot.Left + (float)Math.Clamp(pq, 0, 1) * plot.Width;
        float y = plot.Bottom - (float)Math.Clamp(outputNits / _referencePeakNits, 0, 1) * plot.Height;
        return new PointF(x, y);
    }

    private void NormalizeValues()
    {
        _values[0] = 0;
        double previous = 0;
        for (int index = 1; index < _values.Length - 1; index++)
        {
            _values[index] = Math.Clamp(_values[index], previous, _referencePeakNits);
            previous = _values[index];
        }
        _values[^1] = _referencePeakNits;
    }

    private int Scale(int value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96f));

    private static string FormatNits(double value) => value switch
    {
        < 1 => value.ToString("0.###"),
        < 100 => value.ToString("0.0"),
        _ => value.ToString("0")
    };

    private static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
    {
        float diameter = Math.Max(1, radius * 2);
        GraphicsPath path = new();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
