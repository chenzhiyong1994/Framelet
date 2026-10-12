using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace Framelet;

public static class EditorViewport
{
    public static PointF Origin(Size viewport, Size image, float zoom, Point scroll, bool centerVertically)
    {
        var origin = new PointF(scroll.X + Math.Max(18, (viewport.Width - image.Width * zoom) / 2),
            scroll.Y + (centerVertically ? Math.Max(18, (viewport.Height - image.Height * zoom) / 2) : 18));
        // A physical pixel must map to exactly one source pixel at actual size.
        return zoom == 1 ? new PointF(MathF.Floor(origin.X), MathF.Floor(origin.Y)) : origin;
    }

    public static PointF ToImage(PointF clientPoint, PointF origin, float zoom) =>
        new((clientPoint.X - origin.X) / zoom, (clientPoint.Y - origin.Y) / zoom);
}

internal sealed class ImageCanvas : ScrollableControl
{
    private EditorDocument? document;
    private float zoom = 1;
    private bool dragging;
    private bool panning;
    private Point panStart;
    private Point scrollStart;
    private PointF start;
    private PointF end;
    private readonly List<PointF> stroke = [];
    private Rectangle selection;
    private AnnotationTool tool;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public EditorDocument? Document
    {
        get => document;
        set
        {
            if (document != null) document.Changed -= DocumentChanged;
            document = value;
            if (document != null) document.Changed += DocumentChanged;
            selection = Rectangle.Empty;
            UpdateScrollSize(); Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AnnotationTool Tool
    {
        get => tool;
        set { tool = value; Cursor = value == AnnotationTool.Text ? Cursors.IBeam : Cursors.Cross; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color StrokeColor { get; set; } = Color.FromArgb(232, 67, 49);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float StrokeWidth { get; set; } = 3;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float TextSize { get; set; } = 24;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int NextNumber { get; set; } = 1;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool CenterVertically { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string?>? RequestText { get; set; }
    public event EventHandler? SelectionChanged;
    public event EventHandler? ZoomChanged;
    public event Action<Point>? ImagePointerMoved;
    public event Action<Exception>? OperationFailed;
    public Rectangle Selection => selection;
    public bool HasSelection => selection.Width > 0 && selection.Height > 0;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Zoom
    {
        get => zoom;
        set
        {
            zoom = Math.Clamp(value, .05f, 8f);
            UpdateScrollSize(); Invalidate(); ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ImageCanvas()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        AutoScroll = true; BackColor = BrandTheme.Canvas;
        TabStop = true; Cursor = Cursors.Cross;
    }

    public void Fit()
    {
        if (document == null) return;
        Zoom = Math.Min(1, Math.Min((ClientSize.Width - 40f) / document.Image.Width, (ClientSize.Height - 40f) / document.Image.Height));
        AutoScrollPosition = Point.Empty;
    }

    public void ClearSelection()
    {
        selection = Rectangle.Empty; Invalidate(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectAll()
    {
        if (document == null) return;
        selection = new Rectangle(Point.Empty, document.Image.Size);
        Invalidate(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private PointF ImageOrigin => document == null ? PointF.Empty :
        EditorViewport.Origin(ClientSize, document.Image.Size, zoom, AutoScrollPosition, CenterVertically);

    private PointF Convert(Point point, bool clamp)
    {
        var converted = EditorViewport.ToImage(point, ImageOrigin, zoom);
        if (!clamp || document == null) return converted;
        return new PointF(Math.Clamp(converted.X, 0, document.Image.Width), Math.Clamp(converted.Y, 0, document.Image.Height));
    }

    private void DocumentChanged(object? sender, EventArgs e)
    {
        if (document != null) selection.Intersect(new Rectangle(Point.Empty, document.Image.Size));
        UpdateScrollSize(); Invalidate();
    }

    private void UpdateScrollSize()
    {
        AutoScrollMinSize = document == null ? Size.Empty :
            new Size((int)Math.Ceiling(document.Image.Width * zoom) + 36, (int)Math.Ceiling(document.Image.Height * zoom) + 36);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (document == null) return;
        var image = document.Image;
        var origin = ImageOrigin;
        var display = new RectangleF(origin.X, origin.Y, image.Width * zoom, image.Height * zoom);
        using (var shadow = new SolidBrush(Color.FromArgb(30, 0, 0, 0)))
            e.Graphics.FillRectangle(shadow, display.X + 3, display.Y + 4, display.Width, display.Height);
        using (var checker = new HatchBrush(HatchStyle.LargeCheckerBoard, Color.FromArgb(230, 230, 230), Color.White))
            e.Graphics.FillRectangle(checker, display);
        if (zoom == 1)
            e.Graphics.DrawImageUnscaled(image, (int)origin.X, (int)origin.Y);
        else
        {
            e.Graphics.InterpolationMode = zoom >= 2 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(image, display, new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
        }
        using (var outline = new Pen(Color.FromArgb(140, 140, 140)))
            e.Graphics.DrawRectangle(outline, display.X - 1, display.Y - 1, display.Width + 1, display.Height + 1);

        if (dragging && tool != AnnotationTool.Select && tool != AnnotationTool.Text && tool != AnnotationTool.Number && tool != AnnotationTool.Mosaic)
        {
            var state = e.Graphics.Save();
            e.Graphics.SetClip(display);
            e.Graphics.TranslateTransform(origin.X, origin.Y); e.Graphics.ScaleTransform(zoom, zoom);
            EditorDocument.RenderAnnotation(e.Graphics, tool, start, end, StrokeColor, StrokeWidth, points: stroke, fontSize: TextSize);
            e.Graphics.Restore(state);
        }

        var selected = dragging && (tool == AnnotationTool.Select || tool == AnnotationTool.Mosaic)
            ? Rectangle.Round(EditorDocument.Normalized(start, end)) : selection;
        if (selected.Width > 0 && selected.Height > 0)
        {
            var visual = new RectangleF(origin.X + selected.X * zoom, origin.Y + selected.Y * zoom, selected.Width * zoom, selected.Height * zoom);
            using var white = new Pen(Color.White, 1);
            using var dashed = new Pen(Color.FromArgb(32, 65, 110), 1) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawRectangle(white, visual.X, visual.Y, visual.Width, visual.Height);
            e.Graphics.DrawRectangle(dashed, visual.X, visual.Y, visual.Width, visual.Height);
            string label = $"{selected.Width} × {selected.Height}";
            var size = TextRenderer.MeasureText(label, Font);
            var labelRect = new Rectangle((int)visual.X, Math.Max(1, (int)visual.Y - size.Height - 4), size.Width + 8, size.Height + 4);
            using var labelBrush = new SolidBrush(Color.FromArgb(37, 65, 99));
            e.Graphics.FillRectangle(labelBrush, labelRect);
            TextRenderer.DrawText(e.Graphics, label, Font, labelRect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); Focus();
        if (document == null) return;
        if (e.Button == MouseButtons.Middle)
        {
            panning = true; panStart = e.Location; scrollStart = AutoScrollPosition; Capture = true; Cursor = Cursors.Hand; return;
        }
        if (e.Button != MouseButtons.Left) return;
        var point = Convert(e.Location, false);
        if (point.X < 0 || point.Y < 0 || point.X > document.Image.Width || point.Y > document.Image.Height) return;
        start = end = point; stroke.Clear(); stroke.Add(point);
        if (tool == AnnotationTool.Text)
        {
            string? text = RequestText?.Invoke();
            if (!string.IsNullOrWhiteSpace(text)) TryEdit(() => document.Annotate(tool, start, start, StrokeColor, StrokeWidth, text, fontSize: TextSize));
            return;
        }
        if (tool == AnnotationTool.Number)
        {
            TryEdit(() => document.Annotate(tool, start, start, StrokeColor, StrokeWidth, number: NextNumber++, fontSize: TextSize)); return;
        }
        dragging = true; Capture = true;
        if (tool == AnnotationTool.Select) selection = Rectangle.Empty;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (document == null) return;
        var point = Convert(e.Location, true);
        ImagePointerMoved?.Invoke(Point.Round(point));
        if (panning)
        {
            AutoScrollPosition = new Point(-(scrollStart.X + e.X - panStart.X), -(scrollStart.Y + e.Y - panStart.Y)); Invalidate(); return;
        }
        if (!dragging) return;
        end = point;
        if ((ModifierKeys & Keys.Shift) != 0 && tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse)
        {
            float side = Math.Min(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
            end = new PointF(start.X + Math.Sign(end.X - start.X) * side, start.Y + Math.Sign(end.Y - start.Y) * side);
        }
        if (tool == AnnotationTool.Pen) stroke.Add(end);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (panning) { panning = false; Capture = false; Cursor = Cursors.Cross; return; }
        if (!dragging || document == null) return;
        dragging = false; Capture = false;
        if (tool == AnnotationTool.Select)
        {
            selection = Rectangle.Round(EditorDocument.Normalized(start, end));
            selection.Intersect(new Rectangle(Point.Empty, document.Image.Size));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (tool == AnnotationTool.Pen || Math.Abs(end.X - start.X) + Math.Abs(end.Y - start.Y) >= 1)
            TryEdit(() => document.Annotate(tool, start, end, StrokeColor, StrokeWidth, points: stroke, fontSize: TextSize));
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) != 0) Zoom *= e.Delta > 0 ? 1.2f : 1 / 1.2f;
        else base.OnMouseWheel(e);
        Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { dragging = false; Capture = false; ClearSelection(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }
    private void TryEdit(Action action) { try { action(); } catch (Exception ex) { OperationFailed?.Invoke(ex); } }

    protected override void Dispose(bool disposing)
    {
        if (disposing && document != null) document.Changed -= DocumentChanged;
        base.Dispose(disposing);
    }
}
