using System.Drawing.Drawing2D;

namespace Framelet;

internal sealed class RecordingRegionPicker : Form
{
    private readonly Rectangle desktopBounds;
    private readonly Bitmap desktop;
    private readonly RecordingSelectionGuidance guidance;
    private readonly Label guidanceText;
    private readonly FlowLayoutPanel actions;
    private readonly Button confirm;
    private Point anchor;
    private Point pointer;
    private Rectangle selection;
    private Rectangle dragStart;
    private RecordingSelectionDragMode dragMode;
    private bool selecting;
    private bool regionTooSmall;

    public Rectangle SelectedBounds => RecordingSelectionGeometry.ToDesktop(selection, desktopBounds.Location);

    public RecordingRegionPicker()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        BrandTheme.Apply(this);
        desktopBounds = SystemInformation.VirtualScreen;
        desktop = new Bitmap(desktopBounds.Width, desktopBounds.Height);
        try
        {
            using var graphics = Graphics.FromImage(desktop);
            graphics.CopyFromScreen(desktopBounds.Location, Point.Empty, desktopBounds.Size);
        }
        catch
        {
            desktop.Dispose();
            throw;
        }

        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        Text = BrandTheme.Name + " · 选择录像区域";
        ShowInTaskbar = true;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        Cursor = Cursors.Cross;

        guidance = new RecordingSelectionGuidance();
        guidanceText = guidance.MessageLabel;
        ForwardSelectionInput(guidance);
        ForwardSelectionInput(guidanceText);
        actions = new FlowLayoutPanel
        {
            Name = "RecordingSelectionActions", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = SystemColors.Control, ForeColor = SystemColors.ControlText,
            Padding = new Padding(6), Margin = Padding.Empty, WrapContents = true, Visible = false,
            Cursor = Cursors.Default
        };
        confirm = SelectionButton("确认区域", "确认录像区域（Enter 或空格）");
        var redraw = SelectionButton("重新选择", "清除选区并重新框选");
        var cancel = SelectionButton("取消", "取消选择（Esc）");
        confirm.Click += (_, _) => ConfirmSelection();
        redraw.Click += (_, _) => ResetSelection();
        cancel.Click += (_, _) => CancelSelection();
        actions.Controls.AddRange([confirm, redraw, cancel]);
        Controls.AddRange([guidance, actions]);
        ResumeLayout(true);

        // The frozen image and selection use physical desktop pixels. Only the instruction controls autoscale.
        // Assign this after ResumeLayout so the virtual desktop is never enlarged by the form's initial DPI scale.
        Bounds = desktopBounds;
        pointer = ClampPoint(PointToLocal(Cursor.Position));
        RefreshGuidance();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = desktopBounds;
        pointer = ClampPoint(PointToLocal(Cursor.Position));
        RefreshGuidance();
        Activate();
        Focus();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (guidance is null) return;
        Bounds = desktopBounds;
        RefreshGuidance();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right) { CancelSelection(); return; }
        if (e.Button != MouseButtons.Left) return;
        anchor = pointer = ClampPoint(e.Location);
        dragMode = RecordingSelectionGeometry.HitTest(selection, pointer, LogicalToDeviceUnits(7));
        dragStart = selection;
        if (dragMode == RecordingSelectionDragMode.Create) selection = Rectangle.Empty;
        selecting = true;
        regionTooSmall = false;
        actions.Visible = false;
        Cursor = CursorFor(dragMode);
        Capture = true;
        RefreshGuidance();
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        pointer = ClampPoint(e.Location);
        if (selecting)
        {
            selection = ApplyDrag(e.Location);
            RefreshGuidance();
        }
        else
        {
            Cursor = CursorFor(RecordingSelectionGeometry.HitTest(selection, pointer, LogicalToDeviceUnits(7)));
            if (selection.IsEmpty) RefreshGuidance();
        }
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right) { CancelSelection(); return; }
        if (e.Button != MouseButtons.Left || !selecting) return;
        pointer = ClampPoint(e.Location);
        selection = ApplyDrag(e.Location);
        selecting = false;
        Capture = false;
        regionTooSmall = !RecordingSelectionGeometry.IsUsable(selection);
        if (regionTooSmall) selection = Rectangle.Empty;
        Cursor = CursorFor(RecordingSelectionGeometry.HitTest(selection, pointer, LogicalToDeviceUnits(7)));
        RefreshGuidance();
        Invalidate();
        Focus();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!selecting || Capture) return;
        selecting = false;
        regionTooSmall = !RecordingSelectionGeometry.IsUsable(selection);
        if (regionTooSmall) selection = Rectangle.Empty;
        Cursor = CursorFor(RecordingSelectionGeometry.HitTest(selection, pointer, LogicalToDeviceUnits(7)));
        RefreshGuidance();
        Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { CancelSelection(); return true; }
        if (keyData is Keys.Enter or Keys.Space)
        {
            if (!selecting) ConfirmSelection();
            return true;
        }
        if (!selecting && RecordingSelectionGeometry.IsUsable(selection))
        {
            var key = keyData & Keys.KeyCode;
            var modifiers = keyData & Keys.Modifiers;
            if ((modifiers is Keys.None or Keys.Shift) && (key is Keys.Left or Keys.Right or Keys.Up or Keys.Down))
            {
                int x = key == Keys.Left ? -1 : key == Keys.Right ? 1 : 0;
                int y = key == Keys.Up ? -1 : key == Keys.Down ? 1 : 0;
                selection = modifiers == Keys.Shift
                    ? RecordingSelectionGeometry.Resize(selection, x, y, desktopBounds.Size)
                    : RecordingSelectionGeometry.Move(selection, x, y, desktopBounds.Size);
                Cursor = CursorFor(RecordingSelectionGeometry.HitTest(selection, pointer, LogicalToDeviceUnits(7)));
                RefreshGuidance();
                Invalidate();
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ConfirmSelection()
    {
        if (selecting || !RecordingSelectionGeometry.IsUsable(selection)) return;
        DialogResult = DialogResult.OK;
    }

    private void CancelSelection()
    {
        selecting = false;
        Capture = false;
        DialogResult = DialogResult.Cancel;
    }

    private void ResetSelection()
    {
        selection = Rectangle.Empty;
        selecting = false;
        regionTooSmall = false;
        Capture = false;
        pointer = ClampPoint(PointToLocal(Cursor.Position));
        Cursor = Cursors.Cross;
        RefreshGuidance();
        Invalidate();
        Focus();
    }

    private void RefreshGuidance()
    {
        bool ready = !selecting && RecordingSelectionGeometry.IsUsable(selection);
        var reference = selection.IsEmpty ? pointer : new Point(selection.X + selection.Width / 2, selection.Y + selection.Height / 2);
        var monitor = Screen.FromPoint(PointToDesktop(reference)).WorkingArea;
        monitor.Offset(-desktopBounds.X, -desktopBounds.Y);
        monitor = Rectangle.Intersect(monitor, new Rectangle(Point.Empty, desktopBounds.Size));
        if (monitor.Width == 0 || monitor.Height == 0) monitor = new Rectangle(Point.Empty, desktopBounds.Size);
        int gap = LogicalToDeviceUnits(10);
        int availableWidth = Math.Max(1, monitor.Width - gap * 2);
        string instruction = ready
            ? $"已选择 {selection.Width} × {selection.Height} 像素 · 拖动选区或角点调整\nEnter / 空格确认 · 方向键移动 · Shift + 方向键调整大小"
            : selecting
                ? $"{(dragMode == RecordingSelectionDragMode.Create ? "正在选择" : "正在调整")} {selection.Width} × {selection.Height} 像素\n松开鼠标后确认区域 · Esc / 右键取消"
                : regionTooSmall
                    ? "区域过小，请重新框选（至少 2 × 2 像素）\n按住鼠标左键拖动 · Esc / 右键取消"
                    : "按住鼠标左键，拖动框选录制区域\n松开后可确认或重新选择 · Esc / 右键取消";
        guidance.SetMessage(instruction, availableWidth);
        guidance.Location = RecordingSelectionGeometry.KeepVisible(
            new Point(monitor.Left + (monitor.Width - guidance.Width) / 2, monitor.Top + gap), guidance.Size, monitor);

        actions.Visible = ready;
        confirm.Enabled = ready;
        if (ready)
        {
            actions.MaximumSize = new Size(availableWidth, 0);
            actions.PerformLayout();
            actions.Size = actions.GetPreferredSize(new Size(availableWidth, 0));
            int top = selection.Bottom + gap;
            if (top + actions.Height > monitor.Bottom - gap) top = selection.Top - actions.Height - gap;
            if (top < monitor.Top + gap) top = selection.Bottom - actions.Height - gap;
            var proposed = new Point(selection.Right - actions.Width, top);
            actions.Location = RecordingSelectionGeometry.KeepVisible(proposed, actions.Size, monitor);
            if (actions.Bounds.IntersectsWith(guidance.Bounds))
                guidance.Location = RecordingSelectionGeometry.KeepVisible(
                    new Point(guidance.Left, monitor.Bottom - guidance.Height - gap), guidance.Size, monitor);
            actions.BringToFront();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(desktop, 0, 0);
        using var shade = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
        e.Graphics.FillRectangle(shade, ClientRectangle);
        if (selection.Width > 0 && selection.Height > 0)
        {
            e.Graphics.DrawImage(desktop, selection, selection, GraphicsUnit.Pixel);
            var outline = new Rectangle(selection.X, selection.Y, Math.Max(0, selection.Width - 1), Math.Max(0, selection.Height - 1));
            using var white = new Pen(Color.White, 1);
            using var accent = new Pen(BrandTheme.Accent, 1) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawRectangle(white, outline);
            e.Graphics.DrawRectangle(accent, outline);
            if (!selecting || dragMode != RecordingSelectionDragMode.Create)
            {
                int handle = LogicalToDeviceUnits(4);
                using var fill = new SolidBrush(BrandTheme.Accent);
                foreach (var corner in new[] { outline.Location, new Point(outline.Right, outline.Top), new Point(outline.Left, outline.Bottom), new Point(outline.Right, outline.Bottom) })
                    e.Graphics.FillRectangle(fill, corner.X - handle / 2, corner.Y - handle / 2, handle, handle);
            }
        }
        if (selecting || selection.IsEmpty)
        {
            using var crosshair = new Pen(Color.FromArgb(185, Color.White)) { DashStyle = DashStyle.Dot };
            e.Graphics.DrawLine(crosshair, pointer.X, 0, pointer.X, ClientSize.Height);
            e.Graphics.DrawLine(crosshair, 0, pointer.Y, ClientSize.Width, pointer.Y);
        }
    }

    private static Button SelectionButton(string text, string accessibleName) => new()
    {
        Text = text, AccessibleName = accessibleName, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(84, 30), Padding = new Padding(8, 2, 8, 2), Margin = new Padding(3),
        FlatStyle = FlatStyle.System, UseVisualStyleBackColor = true, Cursor = Cursors.Default
    };

    private Rectangle ApplyDrag(Point location)
    {
        var delta = new Point(location.X - anchor.X, location.Y - anchor.Y);
        return dragMode switch
        {
            RecordingSelectionDragMode.Create => RecordingSelectionGeometry.FromDrag(anchor, location, desktopBounds.Size),
            RecordingSelectionDragMode.Move => RecordingSelectionGeometry.Move(dragStart, delta.X, delta.Y, desktopBounds.Size),
            _ => RecordingSelectionGeometry.ResizeFromCorner(dragStart, delta, dragMode, desktopBounds.Size)
        };
    }

    private static Cursor CursorFor(RecordingSelectionDragMode mode) => mode switch
    {
        RecordingSelectionDragMode.Move => Cursors.SizeAll,
        RecordingSelectionDragMode.TopLeft or RecordingSelectionDragMode.BottomRight => Cursors.SizeNWSE,
        RecordingSelectionDragMode.TopRight or RecordingSelectionDragMode.BottomLeft => Cursors.SizeNESW,
        _ => Cursors.Cross
    };

    private void ForwardSelectionInput(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            var point = PointToClient(control.PointToScreen(e.Location));
            OnMouseDown(new MouseEventArgs(e.Button, e.Clicks, point.X, point.Y, e.Delta));
        };
        control.MouseMove += (_, e) =>
        {
            var point = PointToClient(control.PointToScreen(e.Location));
            OnMouseMove(new MouseEventArgs(e.Button, e.Clicks, point.X, point.Y, e.Delta));
        };
    }

    private Point PointToLocal(Point point) => new(point.X - desktopBounds.X, point.Y - desktopBounds.Y);
    private Point PointToDesktop(Point point) => new(point.X + desktopBounds.X, point.Y + desktopBounds.Y);
    private Point ClampPoint(Point point) => new(Math.Clamp(point.X, 0, Math.Max(0, desktopBounds.Width - 1)), Math.Clamp(point.Y, 0, Math.Max(0, desktopBounds.Height - 1)));

    protected override void Dispose(bool disposing)
    {
        if (disposing) desktop.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>The real selection instruction layout can be checked without taking a desktop snapshot.</summary>
internal sealed class RecordingSelectionGuidance : FlowLayoutPanel
{
    internal Label MessageLabel { get; }

    internal RecordingSelectionGuidance()
    {
        Name = "RecordingSelectionGuidance";
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        BackColor = Color.FromArgb(35, 39, 42);
        ForeColor = Color.White;
        Padding = new Padding(12, 9, 12, 9);
        Margin = Padding.Empty;
        MessageLabel = new Label
        {
            Name = "RecordingSelectionGuidanceText", AutoSize = true, ForeColor = Color.White,
            BackColor = Color.Transparent, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(MessageLabel);
    }

    internal void SetMessage(string message, int availableWidth)
    {
        SuspendLayout();
        int width = Math.Max(1, availableWidth);
        MaximumSize = new Size(width, 0);
        MessageLabel.MaximumSize = new Size(Math.Max(1, width - Padding.Horizontal), 0);
        MessageLabel.Text = message;
        // Docked children do not contribute their preferred width to Panel autosizing. A flow child does,
        // and measuring its wrapping constraint also makes the layout usable before a HWND exists.
        MessageLabel.Size = MessageLabel.GetPreferredSize(MessageLabel.MaximumSize);
        ResumeLayout(true);
        Size = GetPreferredSize(new Size(width, 0));
    }
}

internal enum RecordingSelectionDragMode { Create, Move, TopLeft, TopRight, BottomLeft, BottomRight }

internal static class RecordingSelectionGeometry
{
    internal static bool IsUsable(Rectangle rectangle) => rectangle.Width >= 2 && rectangle.Height >= 2;

    internal static Rectangle FromDrag(Point anchor, Point pointer, Size canvas)
    {
        int x1 = Math.Clamp(anchor.X, 0, canvas.Width);
        int y1 = Math.Clamp(anchor.Y, 0, canvas.Height);
        int x2 = Math.Clamp(pointer.X, 0, canvas.Width);
        int y2 = Math.Clamp(pointer.Y, 0, canvas.Height);
        return Rectangle.FromLTRB(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
    }

    internal static Rectangle Move(Rectangle selection, int dx, int dy, Size canvas) => new(
        Math.Clamp(selection.X + dx, 0, Math.Max(0, canvas.Width - selection.Width)),
        Math.Clamp(selection.Y + dy, 0, Math.Max(0, canvas.Height - selection.Height)), selection.Width, selection.Height);

    internal static Rectangle Resize(Rectangle selection, int dx, int dy, Size canvas) => new(
        selection.Location, new Size(Math.Clamp(selection.Width + dx, 2, Math.Max(2, canvas.Width - selection.X)),
            Math.Clamp(selection.Height + dy, 2, Math.Max(2, canvas.Height - selection.Y))));

    internal static RecordingSelectionDragMode HitTest(Rectangle selection, Point pointer, int cornerRadius)
    {
        if (!IsUsable(selection)) return RecordingSelectionDragMode.Create;
        var corners = new (Point Point, RecordingSelectionDragMode Mode)[]
        {
            (selection.Location, RecordingSelectionDragMode.TopLeft),
            (new Point(selection.Right - 1, selection.Top), RecordingSelectionDragMode.TopRight),
            (new Point(selection.Left, selection.Bottom - 1), RecordingSelectionDragMode.BottomLeft),
            (new Point(selection.Right - 1, selection.Bottom - 1), RecordingSelectionDragMode.BottomRight)
        };
        long nearest = long.MaxValue;
        var hit = RecordingSelectionDragMode.Create;
        foreach (var corner in corners)
        {
            long dx = pointer.X - (long)corner.Point.X, dy = pointer.Y - (long)corner.Point.Y;
            if (Math.Abs(dx) > cornerRadius || Math.Abs(dy) > cornerRadius) continue;
            long distance = dx * dx + dy * dy;
            if (distance >= nearest) continue;
            nearest = distance;
            hit = corner.Mode;
        }
        return hit != RecordingSelectionDragMode.Create ? hit
            : selection.Contains(pointer) ? RecordingSelectionDragMode.Move : RecordingSelectionDragMode.Create;
    }

    internal static Rectangle ResizeFromCorner(Rectangle selection, Point delta, RecordingSelectionDragMode corner, Size canvas)
    {
        int left = selection.Left, top = selection.Top, right = selection.Right, bottom = selection.Bottom;
        switch (corner)
        {
            case RecordingSelectionDragMode.TopLeft:
                left = Math.Clamp(left + delta.X, 0, right - 2);
                top = Math.Clamp(top + delta.Y, 0, bottom - 2);
                break;
            case RecordingSelectionDragMode.TopRight:
                right = Math.Clamp(right + delta.X, left + 2, canvas.Width);
                top = Math.Clamp(top + delta.Y, 0, bottom - 2);
                break;
            case RecordingSelectionDragMode.BottomLeft:
                left = Math.Clamp(left + delta.X, 0, right - 2);
                bottom = Math.Clamp(bottom + delta.Y, top + 2, canvas.Height);
                break;
            case RecordingSelectionDragMode.BottomRight:
                right = Math.Clamp(right + delta.X, left + 2, canvas.Width);
                bottom = Math.Clamp(bottom + delta.Y, top + 2, canvas.Height);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(corner));
        }
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    internal static Rectangle ToDesktop(Rectangle selection, Point origin) => new(selection.X + origin.X, selection.Y + origin.Y, selection.Width, selection.Height);

    internal static Point KeepVisible(Point location, Size size, Rectangle area) => new(
        Math.Clamp(location.X, area.Left, Math.Max(area.Left, area.Right - size.Width)),
        Math.Clamp(location.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
}
