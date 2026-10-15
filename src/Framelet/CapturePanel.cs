using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;

namespace Framelet;

public sealed class CapturePanel : Form
{
    private readonly AppSettings settings;
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer intervalTimer = new();
    private readonly ToolStrip strip;
    private readonly ToolStrip launchStrip;
    private readonly Bitmap brandMark = BrandTheme.CreateMark(36);
    private readonly ToolStripDropDownButton output;
    private EditorForm? editor;
    private RecorderForm? recorder;
    private bool restoreAfterRecorder;
    private bool busy;
    private readonly List<int> hotkeys = [];
    private readonly List<string> conflicts = [];
    private int intervalCount;
    private Point dragOrigin;
    private Point dragPosition;
    private bool dragging;
    private bool dragMoved;
    private bool collapsed;
    private PanelEdge dockEdge;
    private Point expandedLocation;
    private long? idleSince;
    private long? hoverSince;
    private readonly System.Windows.Forms.Timer dockTimer = new() { Interval = 100 };
    private readonly ToolTip panelTip = new();
    private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96d);
    private Size ExpandedSize => new(Px(408), Px(68));
    public static readonly (CaptureMode Mode, string Name, string Icon, string Shortcut, uint Modifiers)[] Captures =
    [
        (CaptureMode.ActiveWindow, "点选窗口截图", "active", "Alt+PrtSc", 1),
        (CaptureMode.WindowObject, "捕捉窗口或对象", "window", "Shift+PrtSc", 4),
        (CaptureMode.Rectangle, "捕捉矩形区域", "rect", "Ctrl+PrtSc", 2),
        (CaptureMode.Freehand, "捕捉手绘区域", "free", "Ctrl+Shift+PrtSc", 6),
        (CaptureMode.FullScreen, "点选屏幕截图", "full", "PrtSc", 0),
        (CaptureMode.Scrolling, "捕捉滚动窗口", "scroll", "Ctrl+Alt+PrtSc", 3),
        (CaptureMode.FixedRegion, "捕捉固定大小区域", "fixed", "Ctrl+Alt+Shift+PrtSc", 7),
        (CaptureMode.RepeatLast, "重复上次捕捉", "repeat", "", 0)
    ];

    public CapturePanel(AppSettings settings)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        this.settings = settings;
        BrandTheme.Apply(this);
        Text = BrandTheme.Name;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;
        TopMost = settings.TopMost;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(408, 68);
        DoubleBuffered = true;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        launchStrip = CreateToolbar(new Point(5, 31), new Size(282, 30));
        var open = CreateIconMenu("打开图像", "open", "打开图像、从剪贴板打开或新建图像");
        open.DropDownOpening += (_, _) =>
        {
            ClearMenu(open.DropDownItems);
            open.DropDownItems.Add(new ToolStripMenuItem("打开图像…", PanelIcons.Create("open"), (_, _) => OpenFiles()));
            open.DropDownItems.Add(new ToolStripMenuItem("从剪贴板打开", PanelIcons.Create("clipboard"), (_, _) => PasteImage()));
            open.DropDownItems.Add(new ToolStripMenuItem("新建图像…", null, (_, _) => OpenBlank()));
        };
        launchStrip.Items.Add(open);
        launchStrip.Items.Add(new ToolStripSeparator { AutoSize = false, Size = new Size(6, 30), Margin = Padding.Empty });
        foreach (var entry in Captures)
        {
            var mode = entry.Mode;
            AddButton(launchStrip, entry.Name, entry.Icon, async () => await CaptureAsync(mode), CaptureShortcut(mode)).Tag = mode;
        }
        Controls.Add(launchStrip);
        strip = CreateToolbar(new Point(291, 31), new Size(108, 30));
        var recordButton = AddButton(strip, "屏幕录像机", "record", OpenRecorder, settings.EnableHotkeys ? "Ctrl+Shift+F9" : "", BrandTheme.Coral);
        recordButton.Tag = "recorder";
        strip.Items.Add(new ToolStripSeparator { AutoSize = false, Size = new Size(6, 30), Margin = Padding.Empty });
        output = CreateIconMenu("输出目标", "editor", "截图输出位置与捕捉选项");
        output.DropDownOpening += (_, _) => BuildOutputMenu();
        strip.Items.Add(output);
        var options = CreateIconMenu("设置和辅助工具", "settings", "设置、图像与辅助工具");
        options.DropDownOpening += (_, _) => { ClearMenu(options.DropDownItems); foreach (var item in BuildOptionsItems()) options.DropDownItems.Add(item); };
        strip.Items.Add(options);
        Controls.Add(strip);
        foreach (var entry in new[] { ("−", "收起为图标", 54, (Action)CollapsePanel), ("×", "退出帧简", 30, (Action)RequestExit) })
        {
            var button = new Button { Text = entry.Item1, AccessibleName = entry.Item2, Bounds = new Rectangle(Width - entry.Item3, 3, 22, 22), TabStop = false, FlatStyle = FlatStyle.Flat, BackColor = BrandTheme.Background, ForeColor = BrandTheme.Muted, Cursor = Cursors.Hand };
            button.FlatAppearance.BorderSize = 0; button.FlatAppearance.MouseOverBackColor = BrandTheme.AccentPale; button.Click += (_, _) => entry.Item4(); Controls.Add(button);
            panelTip.SetToolTip(button, entry.Item2);
        }
        tray = new NotifyIcon { Icon = Icon, Text = BrandTheme.Name + " — 双击打开", Visible = true };
        tray.DoubleClick += (_, _) => ShowPanel();
        var trayMenu = new ContextMenuStrip { Renderer = BrandTheme.CreateToolStripRenderer() };
        trayMenu.Opening += (_, _) =>
        {
            ClearMenu(trayMenu.Items); trayMenu.Items.Add("显示捕捉工具栏", null, (_, _) => ShowPanel());
            trayMenu.Items.Add(new ToolStripSeparator());
            foreach (var entry in Captures) { var mode = entry.Mode; trayMenu.Items.Add(new ToolStripMenuItem(entry.Name, PanelIcons.Create(entry.Icon), async (_, _) => await CaptureAsync(mode)) { Tag = mode, ShortcutKeyDisplayString = CaptureShortcut(mode) }); }
            trayMenu.Items.Add(new ToolStripSeparator());
            foreach (var item in BuildOptionsItems()) trayMenu.Items.Add(item);
        };
        tray.ContextMenuStrip = trayMenu;
        intervalTimer.Tick += async (_, _) => { if (!busy) { intervalCount++; await CaptureAsync(CaptureMode.FullScreen, true); } };
        Load += (_, _) => RegisterShortcuts();
        Shown += (_, _) => {
            if (dockEdge != PanelEdge.None) CollapsePanel();
            if (AppSettings.LoadWarning != null) Notify("配置无法读取，已使用默认设置。原文件未删除。", ToolTipIcon.Warning);
        };
        MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) { trayMenu.Show(this, e.Location); return; }
            if (e.Button != MouseButtons.Left || (!collapsed && e.Y >= Px(28))) return;
            // Use the event position: queued input may already have moved the global cursor.
            dragging = true; dragMoved = false; dragOrigin = PointToScreen(e.Location); dragPosition = Location; Capture = true;
        };
        MouseMove += (_, _) => {
            if (!dragging) return;
            Point delta = new(Cursor.Position.X - dragOrigin.X, Cursor.Position.Y - dragOrigin.Y);
            if (!dragMoved && Math.Abs(delta.X) < Px(4) && Math.Abs(delta.Y) < Px(4)) return;
            if (collapsed) { ExpandPanel(); dragPosition = Location; }
            dragMoved = true;
            Location = new Point(dragPosition.X + delta.X, dragPosition.Y + delta.Y);
        };
        MouseUp += (_, e) => {
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false; Capture = false;
            if (dragMoved) SnapToEdge();
            else if (collapsed) ExpandPanel();
        };
        MouseCaptureChanged += (_, _) => { if (dragging && !Capture) { dragging = false; if (dragMoved) SnapToEdge(); } };
        dockTimer.Tick += (_, _) => UpdateDockState();
        VisibleChanged += (_, _) => { idleSince = hoverSince = null; if (Visible) dockTimer.Start(); else dockTimer.Stop(); };
        DpiChanged += (_, _) => BeginInvoke(() => {
            if (IsDisposed || WindowState != FormWindowState.Normal) return;
            ClientSize = collapsed ? new Size(Px(48), Px(48)) : ExpandedSize;
            if (dragging) { dragOrigin = Cursor.Position; dragPosition = Location; }
            else Location = PanelDocking.Anchor(Location, Size, Screen.FromRectangle(Bounds).WorkingArea, dockEdge);
            if (!collapsed) expandedLocation = Location;
            Invalidate();
        });
        AllowDrop = true;
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) foreach (var file in files) OpenFile(file); };
        ResumeLayout(true);
        var workArea = Screen.PrimaryScreen!.WorkingArea;
        Point saved = new(settings.PanelX, settings.PanelY);
        bool hasSaved = saved != new Point(-1, -1) && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(saved, ExpandedSize)));
        if (Enum.TryParse<PanelEdge>(settings.PanelDockEdge, out var edge) && Enum.IsDefined(edge) && hasSaved) dockEdge = edge;
        Location = hasSaved ? PanelDocking.Anchor(saved, ExpandedSize, Screen.FromRectangle(new Rectangle(saved, ExpandedSize)).WorkingArea, dockEdge)
            : new Point(workArea.Right - Width - Px(45), workArea.Top + Px(45));
        expandedLocation = Location;
        panelTip.SetToolTip(this, "拖动标题栏贴边自动收起，移入或点击图标展开");
    }

    private void SnapToEdge()
    {
        var snapped = PanelDocking.Snap(Location, ExpandedSize, Screen.FromRectangle(Bounds).WorkingArea, Px(20));
        dockEdge = snapped.Edge; Location = snapped.Location; expandedLocation = Location;
        idleSince = hoverSince = null; Persist();
    }
    private void CollapsePanel()
    {
        if (collapsed || dragging || busy || WindowState != FormWindowState.Normal) return;
        expandedLocation = Location;
        var area = Screen.FromRectangle(Bounds).WorkingArea;
        collapsed = true;
        SuspendLayout();
        foreach (Control child in Controls) child.Visible = false;
        ClientSize = new Size(Px(48), Px(48));
        Location = PanelDocking.Anchor(expandedLocation, Size, area, dockEdge);
        ResumeLayout(true);
        AccessibleName = "展开帧简工具栏";
        idleSince = hoverSince = null;
        Invalidate();
    }
    private void ExpandPanel()
    {
        if (!collapsed) return;
        var area = Screen.FromRectangle(Bounds).WorkingArea;
        collapsed = false;
        SuspendLayout();
        ClientSize = ExpandedSize;
        Location = PanelDocking.Anchor(expandedLocation, Size, area, dockEdge);
        foreach (Control child in Controls) child.Visible = true;
        ResumeLayout(true);
        expandedLocation = Location; AccessibleName = "帧简截图与录屏工具栏";
        idleSince = hoverSince = null; Invalidate();
    }
    private void UpdateDockState()
    {
        if (!Visible || !Enabled || WindowState != FormWindowState.Normal || dragging || busy || IsDisposed) { idleSince = hoverSince = null; return; }
        bool inside = Bounds.Contains(Cursor.Position);
        long now = Environment.TickCount64;
        if (collapsed)
        {
            if (!inside) { hoverSince = null; return; }
            hoverSince ??= now;
            if (now - hoverSince.Value >= 250) ExpandPanel();
            return;
        }
        bool menuOpen = tray.ContextMenuStrip?.Visible == true || AnyDropDownOpen(strip) || AnyDropDownOpen(launchStrip);
        if (dockEdge == PanelEdge.None || inside || menuOpen) { idleSince = null; return; }
        idleSince ??= now;
        if (now - idleSince.Value >= 700) CollapsePanel();
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }
        if (m.Msg == 0x312)
        {
            int id = m.WParam.ToInt32();
            if (id >= 100 && id < 108) { _ = CaptureAsync(Captures[id - 100].Mode); return; }
            if (id == 200) { ShowPanel(); return; }
            if (id == 201) { OpenRecorder(); return; }
        }
        base.WndProc(ref m);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BrandTheme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var line = new Pen(BrandTheme.Border);
        using var frame = BrandTheme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Px(collapsed ? 12 : 5));
        e.Graphics.DrawPath(line, frame);
        if (collapsed) { e.Graphics.DrawImage(brandMark, new Rectangle(Px(8), Px(8), Px(32), Px(32))); return; }
        e.Graphics.DrawImage(brandMark, new Rectangle(Px(8), Px(4), Px(20), Px(20)));
        using var title = new Font(Font, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, BrandTheme.Name, title, new Rectangle(Px(34), Px(2), Width - Px(96), Px(24)), BrandTheme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        e.Graphics.DrawLine(line, Px(1), Px(28), Width - Px(2), Px(28));
    }
    private static ToolStrip CreateToolbar(Point location, Size size) => new()
    {
        Dock = DockStyle.None, Location = location, AutoSize = false, Size = size,
        GripStyle = ToolStripGripStyle.Hidden, CanOverflow = false, ImageScalingSize = new Size(20, 20),
        Padding = Padding.Empty, Margin = Padding.Empty, BackColor = BrandTheme.Background,
        Renderer = BrandTheme.CreateToolStripRenderer(), ShowItemToolTips = true
    };
    private static ToolStripDropDownButton CreateIconMenu(string label, string icon, string hint) => new()
    {
        Image = PanelIcons.Create(icon, 20), Text = label, DisplayStyle = ToolStripItemDisplayStyle.Image,
        ToolTipText = hint, AccessibleName = label, AutoToolTip = false, AutoSize = false,
        Size = new Size(36, 30), Padding = Padding.Empty, Margin = Padding.Empty, ForeColor = BrandTheme.Ink
    };
    private static bool AnyDropDownOpen(ToolStrip toolbar) => toolbar.Items.OfType<ToolStripDropDownItem>().Any(item => item.DropDown.Visible);
    private static ToolStripButton AddButton(ToolStrip toolbar, string label, string icon, Action action, string shortcut = "", Color? color = null)
    {
        var button = new ToolStripButton { Image = PanelIcons.Create(icon, 20, color), Text = label, DisplayStyle = ToolStripItemDisplayStyle.Image, AutoSize = false, Size = new Size(30, 30), Padding = Padding.Empty, Margin = Padding.Empty, ForeColor = BrandTheme.Ink, ToolTipText = label + (shortcut.Length > 0 ? " (" + shortcut + ")" : ""), AccessibleName = label, AutoToolTip = false };
        button.Click += (_, _) => action(); toolbar.Items.Add(button); return button;
    }
    private string CaptureShortcut(CaptureMode mode)
    {
        if (!settings.EnableHotkeys || mode == CaptureMode.RepeatLast) return string.Empty;
        int index = Array.FindIndex(Captures, entry => entry.Mode == mode);
        if (index < 0) return string.Empty;
        return settings.AlternateHotkeys ? $"Ctrl+Shift+{index + 1}" : Captures[index].Shortcut;
    }
    private void RefreshShortcutLabels()
    {
        Invalidate();
        foreach (var button in strip.Items.OfType<ToolStripButton>().Concat(launchStrip.Items.OfType<ToolStripButton>()))
        {
            if (button.Tag is string command && command == "recorder")
            {
                button.ToolTipText = "屏幕录像机" + (settings.EnableHotkeys ? " (Ctrl+Shift+F9)" : "");
                continue;
            }
            if (button.Tag is not CaptureMode mode) continue;
            string shortcut = CaptureShortcut(mode);
            button.ToolTipText = button.AccessibleName + (shortcut.Length > 0 ? " (" + shortcut + ")" : "");
        }
        if (tray.ContextMenuStrip == null) return;
        foreach (var item in tray.ContextMenuStrip.Items.OfType<ToolStripMenuItem>())
            if (item.Tag is CaptureMode mode) item.ShortcutKeyDisplayString = CaptureShortcut(mode);
    }
    private IEnumerable<ToolStripItem> BuildOptionsItems()
    {
        yield return new ToolStripMenuItem("打开文件到编辑器…", PanelIcons.Create("open"), (_, _) => OpenFiles());
        yield return new ToolStripMenuItem("从剪贴板打开", PanelIcons.Create("clipboard"), (_, _) => PasteImage());
        yield return new ToolStripMenuItem("新建图像…", null, (_, _) => OpenBlank());
        yield return new ToolStripSeparator();
        yield return new ToolStripMenuItem("屏幕录像机…", PanelIcons.Create("record"), (_, _) => OpenRecorder());
        yield return new ToolStripMenuItem("屏幕取色器", PanelIcons.Create("color"), async (_, _) => await OpenUtility(UtilityMode.ColorPicker));
        yield return new ToolStripMenuItem("屏幕放大镜", PanelIcons.Create("magnifier"), async (_, _) => await OpenUtility(UtilityMode.Magnifier));
        yield return new ToolStripMenuItem("屏幕十字线", PanelIcons.Create("crosshair"), async (_, _) => await OpenUtility(UtilityMode.Crosshair));
        yield return new ToolStripMenuItem("屏幕聚光灯", null, async (_, _) => await OpenUtility(UtilityMode.Focus));
        yield return new ToolStripMenuItem("屏幕标尺", PanelIcons.Create("ruler"), (_, _) => new RulerForm().Show());
        yield return new ToolStripMenuItem(intervalTimer.Enabled ? $"停止自动捕捉（已捕捉 {intervalCount} 次）" : "自动捕捉…", null, (_, _) => ToggleInterval());
        yield return new ToolStripSeparator();
        yield return new ToolStripMenuItem("设置…", PanelIcons.Create("settings"), (_, _) => ShowSettings());
        yield return new ToolStripMenuItem("关于帧简…", null, (_, _) => ShowAbout());
        yield return new ToolStripMenuItem("退出", null, (_, _) => RequestExit());
    }
    private void BuildOutputMenu()
    {
        ClearMenu(output.DropDownItems);
        var names = new[] { "到编辑器", "到剪贴板", "到文件…", "到文件（自动保存）", "到编辑器和剪贴板", "到打印机…" };
        for (int i = 0; i < names.Length; i++)
        {
            var target = (OutputTarget)i;
            output.DropDownItems.Add(new ToolStripMenuItem(names[i], null, (_, _) => { settings.Output = target; Persist(); }) { Checked = settings.Output == target });
        }
        output.DropDownItems.Add(new ToolStripSeparator());
        output.DropDownItems.Add(new ToolStripMenuItem("包含鼠标指针", null, (_, _) => { settings.IncludeCursor = !settings.IncludeCursor; Persist(); }) { Checked = settings.IncludeCursor });
        output.DropDownItems.Add(new ToolStripMenuItem("自动复制到剪贴板", null, (_, _) => { settings.CopyAfterCapture = !settings.CopyAfterCapture; Persist(); }) { Checked = settings.CopyAfterCapture });
        output.DropDownItems.Add(new ToolStripMenuItem("自动添加日期时间戳", null, (_, _) => { settings.Timestamp = !settings.Timestamp; Persist(); }) { Checked = settings.Timestamp });
        var delay = new ToolStripMenuItem("捕捉前延迟");
        foreach (int seconds in new[] { 0, 1, 3, 5, 10 }) delay.DropDownItems.Add(new ToolStripMenuItem(seconds == 0 ? "不延迟" : seconds + " 秒", null, (_, _) => { settings.DelaySeconds = seconds; Persist(); }) { Checked = settings.DelaySeconds == seconds });
        output.DropDownItems.Add(delay);
    }
    public async Task CaptureAsync(CaptureMode mode, bool interval = false)
    {
        if (busy) return;
        busy = true;
        bool visible = Visible;
        bool editorVisible = editor?.Visible == true;
        try
        {
            if (settings.HideOnCapture || mode == CaptureMode.Scrolling) Hide();
            if (editorVisible) editor!.Hide();
            await Task.Delay(Math.Max(200, settings.DelaySeconds * 1000));
            using Bitmap? bitmap = interval
                ? CaptureEngine.CaptureScreen(SystemInformation.VirtualScreen, settings.IncludeCursor)
                : CaptureEngine.SelectCapture(mode, new Size(settings.FixedWidth, settings.FixedHeight), settings.IncludeCursor);
            if (bitmap == null) return;
            if (settings.Timestamp)
            {
                using var graphics = Graphics.FromImage(bitmap);
                using var font = new Font("Segoe UI", 11);
                string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var size = graphics.MeasureString(stamp, font);
                using var fill = new SolidBrush(Color.FromArgb(185, Color.Black));
                var box = new RectangleF(Math.Max(0, bitmap.Width - size.Width - 12), Math.Max(0, bitmap.Height - size.Height - 10), size.Width + 12, size.Height + 10);
                graphics.FillRectangle(fill, box); graphics.DrawString(stamp, font, Brushes.White, box.X + 6, box.Y + 5);
            }
            if (interval) { var path = AutoSave(bitmap); Notify("已保存：" + Path.GetFileName(path)); }
            else Deliver(bitmap);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "捕捉未完成", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally
        {
            busy = false;
            if (editorVisible && editor is { IsDisposed: false }) editor.Show();
            if (visible && editor?.Visible != true) Show();
        }
    }
    private void Deliver(Bitmap image)
    {
        if (settings.CopyAfterCapture || settings.Output is OutputTarget.Clipboard or OutputTarget.EditorAndClipboard) Clipboard.SetImage(image);
        switch (settings.Output)
        {
            case OutputTarget.Editor: case OutputTarget.EditorAndClipboard: OpenImage(image); break;
            case OutputTarget.Clipboard: Notify($"已复制到剪贴板 · {image.Width} × {image.Height}"); break;
            case OutputTarget.AutoSave: Notify("已保存：" + AutoSave(image)); break;
            case OutputTarget.File:
                using (var dialog = new SaveFileDialog { Title = "保存截图", Filter = "PNG 图像|*.png|JPEG 图像|*.jpg|BMP 图像|*.bmp|GIF 图像|*.gif|TIFF 图像|*.tiff", FileName = DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + ".png", AddExtension = true })
                { if (dialog.ShowDialog() == DialogResult.OK) SaveBitmap(image, dialog.FileName); }
                break;
            case OutputTarget.Printer:
                using (var document = new PrintDocument())
                using (var print = new PrintDialog { Document = document, UseEXDialog = true })
                { document.PrintPage += (_, e) => { float scale = Math.Min(e.MarginBounds.Width / (float)image.Width, e.MarginBounds.Height / (float)image.Height); e.Graphics!.DrawImage(image, e.MarginBounds.Left, e.MarginBounds.Top, image.Width * scale, image.Height * scale); }; if (print.ShowDialog() == DialogResult.OK) document.Print(); }
                break;
        }
    }
    public static void SaveBitmap(Bitmap image, string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        var format = ext switch { ".jpg" or ".jpeg" => ImageFormat.Jpeg, ".bmp" => ImageFormat.Bmp, ".gif" => ImageFormat.Gif, ".tif" or ".tiff" => ImageFormat.Tiff, _ => ImageFormat.Png };
        image.Save(path, format);
    }
    private string AutoSave(Bitmap image)
    {
        Directory.CreateDirectory(settings.OutputFolder);
        string stem = DateTime.Now.ToString("yyyy-MM-dd_HHmmss_fff");
        string path = Path.Combine(settings.OutputFolder, stem + "." + settings.OutputFormat);
        for (int n = 1; File.Exists(path); n++) path = Path.Combine(settings.OutputFolder, stem + "_" + n + "." + settings.OutputFormat);
        SaveBitmap(image, path); return path;
    }
    public void OpenImage(Bitmap bitmap, string? path = null)
    {
        if (editor is null || editor.IsDisposed)
        {
            editor = new EditorForm(bitmap, path);
            editor.CaptureRequested += async mode => await CaptureAsync(mode);
            editor.SettingsRequested += ShowSettings;
            editor.RecorderRequested += OpenRecorder;
            editor.FormClosed += (_, _) => { editor = null; if (!IsDisposed) Show(); };
        }
        else editor.AddImage(bitmap, path);
        Hide(); editor.Show(); if (editor.WindowState == FormWindowState.Minimized) editor.WindowState = FormWindowState.Normal; editor.Activate();
    }
    public void OpenFile(string path)
    {
        try { using var source = Image.FromFile(path); using var image = new Bitmap(source); OpenImage(image, path); }
        catch (Exception ex) { AppDiagnostics.Error(ex); MessageBox.Show("无法打开图像：" + ex.Message, "打开文件"); }
    }
    public void OpenBlank() { using var image = new Bitmap(960, 600); using (var g = Graphics.FromImage(image)) g.Clear(Color.White); OpenImage(image); }
    private void OpenFiles() { using var dialog = new OpenFileDialog { Filter = "图像|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*", Multiselect = true }; if (dialog.ShowDialog() == DialogResult.OK) foreach (string file in dialog.FileNames) OpenFile(file); }
    private void PasteImage() { if (!Clipboard.ContainsImage()) { MessageBox.Show("剪贴板中没有图像。", "从剪贴板打开"); return; } using var image = Clipboard.GetImage(); if (image != null) { using var bitmap = new Bitmap(image); OpenImage(bitmap); } }
    public void OpenRecorder()
    {
        restoreAfterRecorder |= Visible;
        if (recorder is null || recorder.IsDisposed)
        {
            recorder = new RecorderForm();
            recorder.FormClosed += (_, _) =>
            {
                recorder = null;
                if (restoreAfterRecorder && !IsDisposed && !Disposing) Show();
                restoreAfterRecorder = false;
            };
        }
        Hide(); recorder.Show();
        if (recorder.WindowState == FormWindowState.Minimized) recorder.WindowState = FormWindowState.Normal;
        recorder.Activate();
    }
    private async Task OpenUtility(UtilityMode mode)
    { if (busy) return; busy = true; bool wasVisible = Visible; Hide(); try { await Task.Delay(160); using var utility = new ScreenUtilityForm(mode); utility.ShowDialog(); } catch (Exception ex) { MessageBox.Show(ex.Message, "辅助工具"); } finally { busy = false; if (wasVisible && !IsDisposed) Show(); } }
    private void ShowPanel()
    {
        if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
        ExpandPanel(); Show(); TopMost = settings.TopMost;
        idleSince = hoverSince = null; Invalidate();
    }
    internal void ReceiveLaunch(LaunchRequest request)
    {
        if (IsDisposed) return;
        if (request.FilePath != null && File.Exists(request.FilePath)) OpenFile(request.FilePath);
        else if (request.Recorder) OpenRecorder();
        else if (request.Editor) OpenBlank();
        else ShowPanel();
    }
    private void ToggleInterval()
    {
        if (intervalTimer.Enabled) { intervalTimer.Stop(); Notify("自动捕捉已停止。"); return; }
        using var dialog = new Form { Text = "自动捕捉", Size = new Size(405, 210), StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var seconds = new NumericUpDown { Minimum = 1, Maximum = 3600, Value = 10, Location = new Point(135, 28), Width = 100 };
        dialog.Controls.Add(new Label { Text = "捕捉间隔（秒）", AutoSize = true, Location = new Point(20, 32) }); dialog.Controls.Add(seconds);
        dialog.Controls.Add(new Label { Text = "全屏截图将自动保存到设置中的输出文件夹。\n再次选择此菜单可停止自动捕捉。", AutoSize = true, Location = new Point(20, 70) });
        var ok = new Button { Text = "开始", Location = new Point(274, 125), DialogResult = DialogResult.OK }; dialog.Controls.Add(ok); dialog.AcceptButton = ok;
        if (dialog.ShowDialog() == DialogResult.OK) { intervalCount = 0; intervalTimer.Interval = (int)seconds.Value * 1000; intervalTimer.Start(); }
    }
    private void ShowSettings()
    {
        bool restorePanel = Visible;
        Hide();
        try { using var dialog = new SettingsForm(settings, conflicts); if (dialog.ShowDialog() == DialogResult.OK) { TopMost = settings.TopMost; Persist(); RegisterShortcuts(); } }
        finally { if (restorePanel && !IsDisposed) Show(); }
    }
    private void ShowAbout() => MessageBox.Show(BrandTheme.Name + " " + typeof(CapturePanel).Assembly.GetName().Version?.ToString(3) + "\n\n让画面，表达得更简单。\n\n快速截图、清晰标注、轻松录屏。\n为日常沟通而做的轻量桌面工具。\n\n使用说明和功能范围可在程序目录的 README 与 docs 中查看。", "关于帧简", MessageBoxButtons.OK, MessageBoxIcon.Information);
    private void RegisterShortcuts()
    {
        foreach (int id in hotkeys) Native.UnregisterHotKey(Handle, id); hotkeys.Clear(); conflicts.Clear();
        RefreshShortcutLabels();
        if (!settings.EnableHotkeys) return;
        for (int i = 0; i < 7; i++)
        {
            uint mods = settings.AlternateHotkeys ? 6u : Captures[i].Modifiers;
            uint key = settings.AlternateHotkeys ? (uint)(Keys.D1 + i) : (uint)Keys.PrintScreen;
            if (Native.RegisterHotKey(Handle, 100 + i, mods | 0x4000, key)) hotkeys.Add(100 + i); else conflicts.Add(Captures[i].Name);
        }
        if (Native.RegisterHotKey(Handle, 200, 6 | 0x4000, (uint)Keys.F12)) hotkeys.Add(200);
        if (Native.RegisterHotKey(Handle, 201, 6 | 0x4000, (uint)Keys.F9)) hotkeys.Add(201);
        if (conflicts.Count > 0) Notify($"{conflicts.Count} 个捕捉热键已被占用。工具栏可正常使用；设置中可切换 Ctrl+Shift+数字。", ToolTipIcon.Warning);
    }
    private void Notify(string message, ToolTipIcon kind = ToolTipIcon.Info) => tray.ShowBalloonTip(3500, BrandTheme.Name, message, kind);
    private void Persist()
    { Point location = collapsed ? expandedLocation : WindowState == FormWindowState.Normal ? Location : RestoreBounds.Location; settings.PanelX = location.X; settings.PanelY = location.Y; settings.PanelDockEdge = dockEdge.ToString(); try { settings.Save(); } catch (Exception ex) { Notify("设置无法保存：" + ex.Message, ToolTipIcon.Warning); } }
    private void RequestExit() { Close(); }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (busy) { e.Cancel = true; return; }
        if (editor is { IsDisposed: false } openEditor) { openEditor.Close(); if (!openEditor.IsDisposed) { e.Cancel = true; return; } }
        if (recorder is { IsDisposed: false } openRecorder) { openRecorder.Close(); if (!openRecorder.IsDisposed) { e.Cancel = true; return; } }
        Persist(); base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { foreach (int id in hotkeys) Native.UnregisterHotKey(Handle, id); intervalTimer.Dispose(); dockTimer.Dispose(); panelTip.Dispose(); if (tray.ContextMenuStrip != null) { ClearMenu(tray.ContextMenuStrip.Items); tray.ContextMenuStrip.Dispose(); } tray.Dispose(); ClearMenu(strip.Items); ClearMenu(launchStrip.Items); brandMark.Dispose(); }
        base.Dispose(disposing);
    }
    private static void ClearMenu(ToolStripItemCollection items)
    {
        while (items.Count > 0) { var item = items[0]; items.RemoveAt(0); if (item is ToolStripDropDownItem child) ClearMenu(child.DropDownItems); item.Image?.Dispose(); item.Dispose(); }
    }
}
