using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;

namespace CaptureReplica;

public sealed class CapturePanel : Form
{
    private readonly AppSettings settings;
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer foregroundTimer = new() { Interval = 150 };
    private readonly System.Windows.Forms.Timer intervalTimer = new();
    private readonly ToolStrip strip;
    private readonly ToolStrip launchStrip;
    private readonly Bitmap brandMark = BrandTheme.CreateMark(36);
    private readonly ToolStripDropDownButton output;
    private EditorForm? editor;
    private RecorderForm? recorder;
    private bool restoreAfterRecorder;
    private IntPtr previousWindow;
    private bool busy;
    private readonly List<int> hotkeys = [];
    private readonly List<string> conflicts = [];
    private int intervalCount;
    private Point dragOrigin;
    private Point dragPosition;
    private bool dragging;
    public static readonly (CaptureMode Mode, string Name, string Icon, string Shortcut, uint Modifiers)[] Captures =
    [
        (CaptureMode.ActiveWindow, "捕捉活动窗口", "active", "Alt+PrtSc", 1),
        (CaptureMode.WindowObject, "捕捉窗口或对象", "window", "Shift+PrtSc", 4),
        (CaptureMode.Rectangle, "捕捉矩形区域", "rect", "Ctrl+PrtSc", 2),
        (CaptureMode.Freehand, "捕捉手绘区域", "free", "Ctrl+Shift+PrtSc", 6),
        (CaptureMode.FullScreen, "捕捉整个屏幕", "full", "PrtSc", 0),
        (CaptureMode.Scrolling, "捕捉滚动窗口", "scroll", "Ctrl+Alt+PrtSc", 3),
        (CaptureMode.FixedRegion, "捕捉固定大小区域", "fixed", "Ctrl+Alt+Shift+PrtSc", 7),
        (CaptureMode.RepeatLast, "重复上次捕捉", "repeat", "", 0)
    ];

    public CapturePanel(AppSettings settings)
    {
        this.settings = settings;
        BrandTheme.Apply(this);
        Text = BrandTheme.Name;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;
        TopMost = settings.TopMost;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(420, 208);
        DoubleBuffered = true;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        launchStrip = new ToolStrip { Dock = DockStyle.None, Location = new Point(14, 66), AutoSize = false, Size = new Size(392, 58), GripStyle = ToolStripGripStyle.Hidden, ImageScalingSize = new Size(24, 24), Padding = Padding.Empty, BackColor = BrandTheme.Background, Renderer = BrandTheme.CreateToolStripRenderer() };
        var captureButton = new ToolStripButton("截取区域", PanelIcons.Create("rect", 24, Color.White)) { AccessibleName = "捕捉矩形区域", Tag = CaptureMode.Rectangle, AutoSize = false, Size = new Size(232, 56), Margin = new Padding(0, 0, 8, 0), TextImageRelation = TextImageRelation.ImageBeforeText, BackColor = BrandTheme.Accent, ForeColor = Color.White, Font = new Font(Font.FontFamily, 11F, FontStyle.Bold) };
        captureButton.Click += async (_, _) => await CaptureAsync(CaptureMode.Rectangle);
        var recordButton = new ToolStripButton("录制屏幕", PanelIcons.Create("record", 24, BrandTheme.Coral)) { AccessibleName = "屏幕录像机", AutoSize = false, Size = new Size(150, 56), Margin = Padding.Empty, TextImageRelation = TextImageRelation.ImageBeforeText, BackColor = BrandTheme.Surface, ForeColor = BrandTheme.Ink, Font = new Font(Font.FontFamily, 10F), ToolTipText = "录制屏幕 (Ctrl+Shift+F9)" };
        recordButton.Click += (_, _) => OpenRecorder();
        launchStrip.Items.AddRange([captureButton, recordButton]);
        Controls.Add(launchStrip);
        strip = new ToolStrip { Dock = DockStyle.None, Location = new Point(14, 132), AutoSize = false, Size = new Size(392, 40), GripStyle = ToolStripGripStyle.Hidden, ImageScalingSize = new Size(18, 18), Padding = Padding.Empty, BackColor = BrandTheme.Background, Renderer = BrandTheme.CreateToolStripRenderer() };
        foreach (var entry in Captures.Where(e => e.Mode is CaptureMode.ActiveWindow or CaptureMode.FullScreen))
        {
            var mode = entry.Mode;
            AddButton(entry.Name, entry.Icon, async () => await CaptureAsync(mode), CaptureShortcut(mode)).Tag = mode;
        }
        var modes = new ToolStripDropDownButton { Text = "更多截图", AutoSize = false, Size = new Size(92, 38), Margin = Padding.Empty, ForeColor = BrandTheme.Ink, AccessibleName = "更多截图方式" };
        modes.DropDownOpening += (_, _) =>
        {
            ClearMenu(modes.DropDownItems);
            foreach (var entry in Captures.Where(e => e.Mode is not (CaptureMode.ActiveWindow or CaptureMode.FullScreen or CaptureMode.Rectangle)))
            { var mode = entry.Mode; modes.DropDownItems.Add(new ToolStripMenuItem(entry.Name.Replace("捕捉", ""), PanelIcons.Create(entry.Icon, 18), async (_, _) => await CaptureAsync(mode)) { ShortcutKeyDisplayString = CaptureShortcut(mode) }); }
        };
        strip.Items.Add(modes);
        output = new ToolStripDropDownButton { Image = PanelIcons.Create("editor", 18), Text = "输出", TextImageRelation = TextImageRelation.ImageBeforeText, ToolTipText = "截图输出位置", AutoSize = false, Size = new Size(78, 38), Margin = Padding.Empty, AccessibleName = "输出目标" };
        output.DropDownOpening += (_, _) => BuildOutputMenu();
        strip.Items.Add(output);
        var options = new ToolStripDropDownButton { Image = PanelIcons.Create("settings", 18), Text = "工具", TextImageRelation = TextImageRelation.ImageBeforeText, AutoSize = false, Size = new Size(78, 38), Margin = Padding.Empty, ToolTipText = "设置、打开图像与辅助工具", AccessibleName = "设置和辅助工具" };
        options.DropDownOpening += (_, _) => { ClearMenu(options.DropDownItems); foreach (var item in BuildOptionsItems()) options.DropDownItems.Add(item); };
        strip.Items.Add(options);
        Controls.Add(strip);
        foreach (var entry in new[] { ("−", "收起工具栏", 70, (Action)Hide), ("×", "退出帧简", 38, (Action)RequestExit) })
        {
            var button = new Button { Text = entry.Item1, AccessibleName = entry.Item2, Bounds = new Rectangle(Width - entry.Item3, 16, 26, 27), TabStop = false, FlatStyle = FlatStyle.Flat, BackColor = BrandTheme.Background, ForeColor = BrandTheme.Muted, Cursor = Cursors.Hand };
            button.FlatAppearance.BorderSize = 0; button.FlatAppearance.MouseOverBackColor = BrandTheme.AccentPale; button.Click += (_, _) => entry.Item4(); Controls.Add(button);
        }
        var workArea = Screen.PrimaryScreen!.WorkingArea;
        Location = !(settings.PanelX == -1 && settings.PanelY == -1) && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(settings.PanelX, settings.PanelY, Width, Height)))
            ? new Point(settings.PanelX, settings.PanelY) : new Point(workArea.Right - Width - 45, workArea.Top + 45);
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
        foregroundTimer.Tick += (_, _) =>
        {
            var hwnd = Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (hwnd != IntPtr.Zero && pid != Environment.ProcessId) previousWindow = hwnd;
        };
        foregroundTimer.Start();
        intervalTimer.Tick += async (_, _) => { if (!busy) { intervalCount++; await CaptureAsync(CaptureMode.FullScreen, true); } };
        Load += (_, _) => RegisterShortcuts();
        Shown += (_, _) => { if (AppSettings.LoadWarning != null) Notify("配置无法读取，已使用默认设置。原文件未删除。", ToolTipIcon.Warning); };
        MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) { trayMenu.Show(this, e.Location); return; }
            if (e.Y >= 62) return;
            dragging = true; dragOrigin = Cursor.Position; dragPosition = Location; Capture = true;
        };
        MouseMove += (_, _) => { if (dragging) Location = new Point(dragPosition.X + Cursor.Position.X - dragOrigin.X, dragPosition.Y + Cursor.Position.Y - dragOrigin.Y); };
        MouseUp += (_, _) => { dragging = false; Capture = false; Persist(); };
        AllowDrop = true;
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) foreach (var file in files) OpenFile(file); };
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
        using var frame = BrandTheme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        e.Graphics.DrawPath(line, frame);
        e.Graphics.DrawImage(brandMark, 18, 15, 36, 36);
        using var title = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        using var wordmark = new Font("Segoe UI", 7.5F, FontStyle.Bold);
        using var small = new Font("Microsoft YaHei UI", 8F);
        TextRenderer.DrawText(e.Graphics, BrandTheme.ShortName, title, new Point(64, 10), BrandTheme.Ink);
        TextRenderer.DrawText(e.Graphics, BrandTheme.Wordmark, wordmark, new Point(66, 34), BrandTheme.Muted);
        TextRenderer.DrawText(e.Graphics, "让画面，表达得更简单。", small, new Rectangle(159, 16, 180, 30), BrandTheme.Muted, TextFormatFlags.VerticalCenter);
        e.Graphics.DrawLine(line, 18, 180, Width - 18, 180);
        TextRenderer.DrawText(e.Graphics, "拖入图片，即可编辑", small, new Rectangle(18, 182, 172, 24), BrandTheme.Muted, TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(e.Graphics, CaptureShortcut(CaptureMode.Rectangle), small, new Rectangle(207, 182, Width - 226, 24), BrandTheme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
    private ToolStripButton AddButton(string label, string icon, Action action, string shortcut = "")
    {
        string shortName = icon switch { "active" => "窗口", "window" => "对象", "rect" => "区域", "free" => "手绘", "full" => "全屏", "scroll" => "长截图", "fixed" => "固定", "repeat" => "重复", "record" => "录屏", _ => label };
        var button = new ToolStripButton { Image = PanelIcons.Create(icon, 18), Text = shortName, TextImageRelation = TextImageRelation.ImageBeforeText, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, AutoSize = false, Size = new Size(70, 38), Margin = Padding.Empty, ForeColor = BrandTheme.Ink, ToolTipText = label + (shortcut.Length > 0 ? " (" + shortcut + ")" : ""), AccessibleName = label };
        button.Click += (_, _) => action(); strip.Items.Add(button); return button;
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
            if (settings.HideOnCapture) Hide();
            if (editorVisible) editor!.Hide();
            await Task.Delay(Math.Max(200, settings.DelaySeconds * 1000));
            using Bitmap? bitmap = mode == CaptureMode.ActiveWindow && previousWindow != IntPtr.Zero
                ? CaptureEngine.CaptureActiveWindow(previousWindow, settings.IncludeCursor)
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
    private void ShowPanel() { Show(); TopMost = settings.TopMost; Invalidate(); }
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
    private void ShowAbout() => MessageBox.Show(BrandTheme.Name + " 0.2\n\n让画面，表达得更简单。\n\n快速截图、清晰标注、轻松录屏。\n为日常沟通而做的轻量桌面工具。\n\n使用说明和功能范围可在程序目录的 README 与 docs 中查看。", "关于帧简", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
    { settings.PanelX = Left; settings.PanelY = Top; try { settings.Save(); } catch (Exception ex) { Notify("设置无法保存：" + ex.Message, ToolTipIcon.Warning); } }
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
        if (disposing) { foreach (int id in hotkeys) Native.UnregisterHotKey(Handle, id); foregroundTimer.Dispose(); intervalTimer.Dispose(); if (tray.ContextMenuStrip != null) { ClearMenu(tray.ContextMenuStrip.Items); tray.ContextMenuStrip.Dispose(); } tray.Dispose(); ClearMenu(strip.Items); ClearMenu(launchStrip.Items); brandMark.Dispose(); }
        base.Dispose(disposing);
    }
    private static void ClearMenu(ToolStripItemCollection items)
    {
        while (items.Count > 0) { var item = items[0]; items.RemoveAt(0); if (item is ToolStripDropDownItem child) ClearMenu(child.DropDownItems); item.Image?.Dispose(); item.Dispose(); }
    }
}
