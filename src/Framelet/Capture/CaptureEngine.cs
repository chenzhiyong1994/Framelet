using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Framelet;

public enum CaptureMode
{
    ActiveWindow,
    WindowObject,
    Rectangle,
    Freehand,
    FullScreen,
    Scrolling,
    FixedRegion,
    RepeatLast
}

public static class CaptureEngine
{
    public static Rectangle LastRegion { get; private set; }

    public static Bitmap CaptureScreen(Rectangle bounds, bool includeCursor = false)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds), "截图区域必须具有非零宽度和高度。");
        if (bounds.Width > 64000 || bounds.Height > 64000 || (long)bounds.Width * bounds.Height > 80_000_000)
            throw new ArgumentOutOfRangeException(nameof(bounds), "截图区域过大，请缩小到 8000 万像素以内。");
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            if (includeCursor) NativeCapture.DrawCursor(graphics, bounds.Location);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    public static Bitmap? CaptureActiveWindow(IntPtr handle, bool includeCursor = false)
    {
        if (handle == IntPtr.Zero || !NativeCapture.IsWindow(handle) || NativeCapture.IsIconic(handle)) return null;
        var bounds = Rectangle.Intersect(NativeCapture.WindowBounds(handle), SystemInformation.VirtualScreen);
        if (bounds.IsEmpty) return null;
        LastRegion = bounds;
        return CaptureScreen(bounds, includeCursor);
    }

    public static Bitmap? SelectCapture(CaptureMode mode, Size fixedSize, bool includeCursor = false)
    {
        if (mode == CaptureMode.RepeatLast && !LastRegion.IsEmpty)
        {
            var bounds = Rectangle.Intersect(LastRegion, SystemInformation.VirtualScreen);
            return bounds.IsEmpty ? null : CaptureScreen(bounds, includeCursor);
        }

        var selectionMode = mode is CaptureMode.Scrolling or CaptureMode.RepeatLast ? CaptureMode.Rectangle : mode;
        using var overlay = new CaptureSelection(selectionMode, fixedSize, includeCursor);
        if (overlay.ShowDialog() != DialogResult.OK) return null;
        LastRegion = overlay.SelectedBounds;
        if (mode == CaptureMode.Scrolling)
        {
            using var scroller = new ScrollingCaptureWindow(LastRegion, includeCursor);
            return scroller.ShowDialog() == DialogResult.OK ? scroller.TakeResult() : null;
        }
        return overlay.CreateSelectionBitmap();
    }

    public static Rectangle? SelectScreenBounds()
    {
        using var overlay = new CaptureSelection(CaptureMode.FullScreen, Size.Empty, false);
        return overlay.ShowDialog() == DialogResult.OK ? overlay.SelectedBounds : null;
    }
}

internal sealed record CaptureTarget(Rectangle Bounds, string Label);

internal static class CaptureTargets
{
    // Targets are ordered front to back for windows. Gaps between monitors are not targets.
    internal static CaptureTarget? AtPoint(IReadOnlyList<CaptureTarget> targets, Point point)
        => targets.FirstOrDefault(target => target.Bounds.Contains(point));

    internal static CaptureTarget[] Screens() => Screen.AllScreens.Select((screen, index) =>
        new CaptureTarget(screen.Bounds, $"屏幕 {index + 1}{(screen.Primary ? " · 主屏幕" : "")} · {screen.Bounds.Width} × {screen.Bounds.Height}")).ToArray();
}

internal sealed class CaptureSelection : Form
{
    private readonly CaptureMode mode;
    private readonly Size fixedSize;
    private readonly Rectangle desktop;
    private readonly Bitmap background;
    private readonly CaptureTarget[] screens;
    private readonly CaptureTarget[] windows;
    private string? targetLabel;
    private readonly List<Point> points = [];
    private Point anchor;
    private Point current;
    private bool anchored;
    private bool secondClick;
    private bool mouseDown;
    private bool showHelp = true;
    private bool adjustAnchor;
    private Rectangle selection;
    private string? selectionHint;
    private readonly Font infoFont = new("Microsoft YaHei UI", 9f);
    public Rectangle SelectedBounds => selection.IsEmpty ? Rectangle.Empty : new(selection.X + desktop.X, selection.Y + desktop.Y, selection.Width, selection.Height);

    public CaptureSelection(CaptureMode mode, Size fixedSize, bool includeCursor)
        : this(mode, fixedSize, SystemInformation.VirtualScreen, null, CaptureTargets.Screens(),
            mode == CaptureMode.ActiveWindow ? NativeCapture.SnapshotWindows() : [], includeCursor)
    {
    }

    internal CaptureSelection(CaptureMode mode, Size fixedSize, Rectangle desktop, Bitmap? frozen,
        CaptureTarget[] screens, CaptureTarget[] windows, bool includeCursor = false)
    {
        this.mode = mode;
        this.desktop = desktop;
        this.screens = screens;
        this.windows = windows;
        this.fixedSize = new Size(Math.Clamp(fixedSize.Width, 1, desktop.Width), Math.Clamp(fixedSize.Height, 1, desktop.Height));
        background = frozen == null ? CaptureEngine.CaptureScreen(desktop, includeCursor) : new Bitmap(frozen);
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = desktop;
        TopMost = true;
        Text = BrandTheme.Name + " · 截图选区";
        ShowInTaskbar = true;
        DoubleBuffered = true;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        current = new Point(Cursor.Position.X - desktop.X, Cursor.Position.Y - desktop.Y);
        UpdateSelection();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        Focus();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // Selection and the frozen bitmap share physical desktop pixels. Do not
        // let a monitor DPI transition rescale the virtual-desktop overlay.
        e.Cancel = true;
        base.OnDpiChanged(e);
    }

    private Point Constrain(Point point) => new(Math.Clamp(point.X, 0, desktop.Width - 1), Math.Clamp(point.Y, 0, desktop.Height - 1));

    private static Rectangle Between(Point first, Point second) => Rectangle.FromLTRB(
        Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
        Math.Max(first.X, second.X) + 1, Math.Max(first.Y, second.Y) + 1);

    private void UpdateSelection()
    {
        if (mode is CaptureMode.FullScreen or CaptureMode.ActiveWindow)
        {
            var target = CaptureTargets.AtPoint(mode == CaptureMode.FullScreen ? screens : windows,
                new Point(current.X + desktop.X, current.Y + desktop.Y));
            targetLabel = target?.Label;
            selection = target == null ? Rectangle.Empty : Rectangle.Intersect(target.Bounds, desktop);
            if (!selection.IsEmpty) selection.Offset(-desktop.X, -desktop.Y);
        }
        else if (mode == CaptureMode.FixedRegion)
        {
            selection = new Rectangle(
                Math.Clamp(current.X - fixedSize.Width / 2, 0, desktop.Width - fixedSize.Width),
                Math.Clamp(current.Y - fixedSize.Height / 2, 0, desktop.Height - fixedSize.Height),
                fixedSize.Width, fixedSize.Height);
        }
        else if (mode == CaptureMode.WindowObject)
        {
            var native = NativeCapture.FindUnderlyingWindow(new Point(current.X + desktop.X, current.Y + desktop.Y), Handle);
            selection = Rectangle.Intersect(native, desktop);
            selection.Offset(-desktop.X, -desktop.Y);
        }
        else if (mode == CaptureMode.Freehand && points.Count > 0)
        {
            selection = Rectangle.FromLTRB(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X) + 1, points.Max(p => p.Y) + 1);
        }
        else if (anchored)
        {
            selection = Between(anchor, current);
        }
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        current = Constrain(e.Location);
        selectionHint = null;
        if (mode == CaptureMode.Freehand && mouseDown &&
            (points.Count == 0 || Distance(points[^1], current) > 3)) points.Add(current);
        UpdateSelection();
    }

    private static int Distance(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right) { CancelSelection(); return; }
        if (e.Button != MouseButtons.Left) return;
        current = Constrain(e.Location);
        if (mode is CaptureMode.FixedRegion or CaptureMode.WindowObject or CaptureMode.FullScreen or CaptureMode.ActiveWindow)
        {
            UpdateSelection();
            AcceptSelection();
            return;
        }
        mouseDown = true;
        if (mode == CaptureMode.Freehand)
        {
            if (points.Count == 0 || points[^1] != current) points.Add(current);
            UpdateSelection();
            return;
        }
        secondClick = anchored;
        if (!anchored) { anchor = current; anchored = true; }
        UpdateSelection();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        mouseDown = false;
        if (mode == CaptureMode.Freehand) return;
        if (anchored && (secondClick || Distance(anchor, current) > 4)) AcceptSelection();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (mode == CaptureMode.Freehand && e.Button == MouseButtons.Left) AcceptSelection();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key == Keys.Escape) { CancelSelection(); return true; }
        if (key == Keys.F1) { showHelp = !showHelp; Invalidate(); return true; }
        if (key == Keys.Tab)
        {
            if (mode == CaptureMode.FullScreen && screens.Length > 0)
            {
                int index = Array.FindIndex(screens, screen => screen.Bounds == SelectedBounds);
                int direction = (keyData & Keys.Shift) != 0 ? -1 : 1;
                var next = screens[(index + direction + screens.Length) % screens.Length].Bounds;
                current = new Point(next.Left + next.Width / 2 - desktop.Left, next.Top + next.Height / 2 - desktop.Top);
                UpdateSelection();
            }
            else { adjustAnchor = !adjustAnchor; Invalidate(); }
            return true;
        }
        if (key is Keys.Enter or Keys.Space)
        {
            if (mode == CaptureMode.Freehand)
            {
                if (key == Keys.Space) { points.Add(current); UpdateSelection(); }
                else AcceptSelection();
            }
            else if (!anchored && mode == CaptureMode.Rectangle)
            {
                anchor = current;
                anchored = true;
                UpdateSelection();
            }
            else AcceptSelection();
            return true;
        }
        if (key is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
        {
            int step = (keyData & Keys.Shift) != 0 ? 10 : 1;
            var delta = key switch
            {
                Keys.Left => new Point(-step, 0), Keys.Right => new Point(step, 0),
                Keys.Up => new Point(0, -step), _ => new Point(0, step)
            };
            if (anchored && adjustAnchor) anchor = Constrain(new Point(anchor.X + delta.X, anchor.Y + delta.Y));
            else current = Constrain(new Point(current.X + delta.X, current.Y + delta.Y));
            UpdateSelection();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void AcceptSelection()
    {
        if (selection.Width <= 0 || selection.Height <= 0)
        {
            selectionHint = "请先选择截图区域；Esc 可随时取消。";
            Invalidate();
            return;
        }
        if (mode == CaptureMode.Freehand && !CaptureGeometry.HasArea(points))
        {
            selectionHint = "请画出有面积的封闭区域，至少需要 3 个不共线的点。";
            showHelp = true;
            Invalidate();
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CancelSelection()
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    public Bitmap CreateSelectionBitmap()
        => CaptureGeometry.CropSelection(background, selection, mode == CaptureMode.Freehand ? points : null);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(background, 0, 0);
        using var shade = new SolidBrush(Color.FromArgb(96, 0, 0, 0));
        g.FillRectangle(shade, ClientRectangle);
        if (!selection.IsEmpty)
        {
            if (mode == CaptureMode.Freehand && points.Count >= 3)
            {
                using var path = new GraphicsPath();
                path.AddPolygon(points.ToArray());
                var state = g.Save();
                g.SetClip(path);
                g.DrawImageUnscaled(background, 0, 0);
                g.Restore(state);
                using var pen = new Pen(BrandTheme.Accent, 2);
                g.DrawPath(pen, path);
            }
            else
            {
                g.DrawImage(background, selection, selection, GraphicsUnit.Pixel);
                using var border = new Pen(Color.Red, 1);
                using var dash = new Pen(Color.White, 1) { DashStyle = DashStyle.Dash };
                var outline = selection;
                outline.Width = Math.Max(0, outline.Width - 1);
                outline.Height = Math.Max(0, outline.Height - 1);
                g.DrawRectangle(border, outline);
                g.DrawRectangle(dash, outline);
                foreach (var point in new[] { selection.Location, new Point(selection.Right - 1, selection.Top),
                    new Point(selection.Left, selection.Bottom - 1), new Point(selection.Right - 1, selection.Bottom - 1) })
                    g.FillRectangle(Brushes.White, point.X - 2, point.Y - 2, 5, 5);
            }
        }
        if (mode == CaptureMode.Freehand && points.Count > 1)
        {
            using var pen = new Pen(Color.Coral, 2);
            g.DrawLines(pen, points.ToArray());
        }
        if (mode is not (CaptureMode.FullScreen or CaptureMode.ActiveWindow)) DrawMagnifier(g);
        if (showHelp) DrawHelp(g);
    }

    private void DrawMagnifier(Graphics g)
    {
        const int boxWidth = 174, boxHeight = 128;
        int x = current.X + 25, y = current.Y + 24;
        if (x + boxWidth > ClientSize.Width) x = current.X - boxWidth - 25;
        if (y + boxHeight > ClientSize.Height) y = current.Y - boxHeight - 24;
        x = Math.Clamp(x, 0, Math.Max(0, ClientSize.Width - boxWidth));
        y = Math.Clamp(y, 0, Math.Max(0, ClientSize.Height - boxHeight));
        var box = new Rectangle(x, y, boxWidth, boxHeight);
        using var dark = new SolidBrush(BrandTheme.Ink);
        g.FillRectangle(dark, box);
        var src = new Rectangle(Math.Clamp(current.X - 14, 0, Math.Max(0, background.Width - 29)),
            Math.Clamp(current.Y - 8, 0, Math.Max(0, background.Height - 17)), Math.Min(29, background.Width), Math.Min(17, background.Height));
        var dest = new Rectangle(x + 2, y + 2, boxWidth - 4, 94);
        var state = g.Save();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(background, dest, src, GraphicsUnit.Pixel);
        g.Restore(state);
        using var pen = new Pen(BrandTheme.Accent);
        float crossX = dest.X + (current.X - src.X + 0.5f) * dest.Width / src.Width;
        float crossY = dest.Y + (current.Y - src.Y + 0.5f) * dest.Height / src.Height;
        g.DrawLine(pen, crossX, dest.Top, crossX, dest.Bottom);
        g.DrawLine(pen, dest.Left, crossY, dest.Right, crossY);
        var caption = selection.IsEmpty
            ? $"{current.X + desktop.X}, {current.Y + desktop.Y}"
            : $"{selection.Width} × {selection.Height}    ({SelectedBounds.X}, {SelectedBounds.Y})";
        TextRenderer.DrawText(g, caption, infoFont, new Rectangle(x + 5, y + 99, boxWidth - 10, 25), Color.White,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        g.DrawRectangle(Pens.White, x, y, boxWidth - 1, boxHeight - 1);
    }

    private void DrawHelp(Graphics g)
    {
        string title = mode switch
        {
            CaptureMode.Freehand => "手绘区域：单击添加点或按住鼠标绘制，Enter / 双击完成",
            CaptureMode.WindowObject => "窗口 / 对象：移动鼠标选择窗口或控件，单击捕捉",
            CaptureMode.ActiveWindow => "窗口截图：移动鼠标高亮完整窗口，单击捕捉",
            CaptureMode.FullScreen => "选择屏幕：移动鼠标高亮显示器，单击确认",
            CaptureMode.FixedRegion => $"固定区域 {fixedSize.Width} × {fixedSize.Height}：单击捕捉",
            _ => "矩形区域：拖动选择，或依次点击两个角点"
        };
        var screen = Screen.FromPoint(new Point(current.X + desktop.X, current.Y + desktop.Y));
        var help = new Rectangle(screen.WorkingArea.Left - desktop.Left + 16, screen.WorkingArea.Top - desktop.Top + 16,
            Math.Min(760, screen.WorkingArea.Width - 32), mode is CaptureMode.FullScreen or CaptureMode.ActiveWindow ? 92 : 64);
        using var brush = new SolidBrush(BrandTheme.Ink);
        g.FillRectangle(brush, help);
        TextRenderer.DrawText(g, title, infoFont, new Rectangle(help.X + 12, help.Y + 8, help.Width - 24, 22), Color.White);
        string detail = selectionHint ?? (mode == CaptureMode.FullScreen
            ? "Tab / Shift+Tab 切换屏幕 · Enter 确认 · Esc / 右键取消"
            : mode == CaptureMode.ActiveWindow
            ? "单击 / Enter 确认 · 仅捕捉当前可见内容 · Esc / 右键取消"
            : mode == CaptureMode.Freehand
            ? "Space 添加点 · Enter / 双击完成 · F1 隐藏提示 · Esc / 右键取消"
            : "方向键微调 · Shift+方向键 10 px · Tab 切换角点 · F1 隐藏提示 · Esc / 右键取消");
        TextRenderer.DrawText(g, detail, infoFont, new Rectangle(help.X + 12, help.Y + 34, help.Width - 24, 22), Color.FromArgb(198, 208, 224));
        if (mode is CaptureMode.FullScreen or CaptureMode.ActiveWindow)
            TextRenderer.DrawText(g, targetLabel ?? "将鼠标移到要捕捉的目标上", infoFont,
                new Rectangle(help.X + 12, help.Y + 62, help.Width - 24, 22), Color.White,
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { background.Dispose(); infoFont.Dispose(); }
        base.Dispose(disposing);
    }
}

internal static class CaptureGeometry
{
    internal static bool HasArea(IReadOnlyList<Point> points)
    {
        if (points.Count < 3) return false;
        // Find a non-collinear triangle; signed polygon area can be zero for
        // a valid self-intersecting freehand shape.
        Point origin = points[0];
        for (int i = 1; i < points.Count - 1; i++)
        {
            long cross = (long)(points[i].X - origin.X) * (points[i + 1].Y - origin.Y)
                - (long)(points[i].Y - origin.Y) * (points[i + 1].X - origin.X);
            if (cross != 0) return true;
        }
        return false;
    }

    internal static Bitmap CropSelection(Bitmap background, Rectangle selection, IReadOnlyList<Point>? polygon)
    {
        if (selection.Width <= 0 || selection.Height <= 0 || !new Rectangle(Point.Empty, background.Size).Contains(selection))
            throw new ArgumentOutOfRangeException(nameof(selection), "选区必须位于图像范围内且具有非零面积。");
        if (polygon != null && !HasArea(polygon)) throw new ArgumentException("手绘路径没有可捕捉的面积。", nameof(polygon));
        var result = new Bitmap(selection.Width, selection.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(result);
            if (polygon != null)
            {
                using var path = new GraphicsPath();
                path.AddPolygon(polygon.Select(p => new Point(p.X - selection.X, p.Y - selection.Y)).ToArray());
                graphics.SetClip(path);
            }
            graphics.DrawImage(background, new Rectangle(Point.Empty, result.Size), selection, GraphicsUnit.Pixel);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}

internal static class NativeCapture
{
    internal delegate bool EnumWindowsProc(IntPtr handle, IntPtr param);
    [StructLayout(LayoutKind.Sequential)] internal struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public int Size, Flags; public IntPtr Handle; public NativePoint Position; }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { [MarshalAs(UnmanagedType.Bool)] public bool Icon; public int HotspotX, HotspotY; public IntPtr Mask, Color; }
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr handle, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out NativeRect rect, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr handle, ref NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr ChildWindowFromPointEx(IntPtr handle, NativePoint point, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, uint step, IntPtr brush, uint flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);

    internal static Rectangle WindowBounds(IntPtr handle)
    {
        if (DwmGetWindowAttribute(handle, 9, out NativeRect bounds, Marshal.SizeOf<NativeRect>()) != 0)
            GetWindowRect(handle, out bounds);
        return Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }

    internal static CaptureTarget[] SnapshotWindows()
    {
        var targets = new List<CaptureTarget>();
        EnumWindows((handle, _) =>
        {
            if (!IsSelectableWindow(handle)) return true;
            var bounds = Rectangle.Intersect(WindowBounds(handle), SystemInformation.VirtualScreen);
            if (bounds.Width <= 0 || bounds.Height <= 0) return true;
            var title = new StringBuilder(512);
            GetWindowText(handle, title, title.Capacity);
            targets.Add(new CaptureTarget(bounds, $"{(title.Length > 0 ? title.ToString() : "窗口")} · {bounds.Width} × {bounds.Height}"));
            return true;
        }, IntPtr.Zero);
        return targets.ToArray();
    }

    private static bool IsSelectableWindow(IntPtr handle)
    {
        if (!IsWindowVisible(handle) || IsIconic(handle)) return false;
        long style = IntPtr.Size == 8 ? GetWindowLongPtr64(handle, -20).ToInt64() : GetWindowLong32(handle, -20);
        // Click-through cursor/recording overlays are not the window the user is pointing at.
        if ((style & 0x20 /* WS_EX_TRANSPARENT */) != 0) return false;
        return DwmGetWindowAttribute(handle, 14, out int cloaked, sizeof(int)) != 0 || cloaked == 0;
    }

    internal static Rectangle FindUnderlyingWindow(Point point, IntPtr overlay)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((handle, _) =>
        {
            if (handle == overlay || !IsSelectableWindow(handle)) return true;
            var bounds = WindowBounds(handle);
            if (!bounds.Contains(point)) return true;
            found = handle;
            return false;
        }, IntPtr.Zero);
        if (found == IntPtr.Zero) return SystemInformation.VirtualScreen;
        for (int depth = 0; depth < 12; depth++)
        {
            var local = new NativePoint { X = point.X, Y = point.Y };
            if (!ScreenToClient(found, ref local)) break;
            var child = ChildWindowFromPointEx(found, local, 1 | 2 | 4);
            if (child == IntPtr.Zero || child == found) break;
            found = child;
        }
        return WindowBounds(found);
    }

    internal static void DrawCursor(Graphics graphics, Point origin)
    {
        var cursor = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref cursor) || (cursor.Flags & 1) == 0 || !GetIconInfo(cursor.Handle, out var icon)) return;
        try
        {
            var dc = graphics.GetHdc();
            try { DrawIconEx(dc, cursor.Position.X - origin.X - icon.HotspotX, cursor.Position.Y - origin.Y - icon.HotspotY, cursor.Handle, 0, 0, 0, IntPtr.Zero, 3); }
            finally { graphics.ReleaseHdc(dc); }
        }
        finally
        {
            if (icon.Mask != IntPtr.Zero) DeleteObject(icon.Mask);
            if (icon.Color != IntPtr.Zero) DeleteObject(icon.Color);
        }
    }
}
