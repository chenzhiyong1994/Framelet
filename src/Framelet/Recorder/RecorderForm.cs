using System.Runtime.InteropServices;
using System.Text.Json;

namespace Framelet;

public sealed class RecorderForm : Form
{
    private readonly RadioButton fullScreen = new() { Text = "全屏", Checked = true, AutoSize = true };
    private readonly RadioButton region = new() { Text = "矩形区域", AutoSize = true };
    private readonly Button selectArea = new() { Text = "选择区域…", Width = 100 };
    private readonly Label areaLabel = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    private readonly NumericUpDown frameRate = new() { Minimum = 1, Maximum = 60, Value = 15, Width = 58 };
    private readonly CheckBox includeCursor = new() { Text = "录制鼠标指针", Checked = true, AutoSize = true };
    private readonly CheckBox minimize = new() { Text = "开始时最小化录像机", Checked = true, AutoSize = true };
    private readonly ComboBox audioDevice = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button refreshAudio = new() { Text = "刷新", Width = 56 };
    private readonly Button ffmpegButton = new() { Text = "引擎设置…", Width = 120 };
    private readonly Button startButton = new() { Text = "●  录制", Width = 104, Height = 30 };
    private readonly Button pauseButton = new() { Text = "Ⅱ  暂停", Enabled = false, Width = 104, Height = 30 };
    private readonly Button stopButton = new() { Text = "■  结束", Enabled = false, Width = 104, Height = 30 };
    private readonly Button closeButton = new() { Text = "关闭", Width = 74, Height = 30 };
    private readonly Button editButton = new() { Text = "视频剪辑…", Width = 110, Height = 28 };
    private readonly Label status = new() { Text = "就绪", AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label elapsed = new() { Text = "00:00:00", TextAlign = ContentAlignment.MiddleRight };
    private readonly Label hints = new() { Text = "保存为 MP4 / WMV。Ctrl+F10 暂停/继续，Ctrl+F11 结束。", AutoSize = false };
    private readonly Panel captureGroup = new();
    private readonly Panel optionsGroup = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly ToolTip toolTip = new();
    private Image? brandMark;
    private Rectangle? selectedBounds;
    private ScreenRecordingSession? session;
    private string? lastSavedVideo;
    private RecorderPreferences preferences;
    private bool busy;
    private bool checkingEngine;
    private string? resolvedEngine;
    private bool canClose;
    private bool hotkeyPause;
    private bool hotkeyStop;
    private const int PauseHotkeyId = 0x7901, StopHotkeyId = 0x7902;
    private const int WmHotkey = 0x0312;

    public RecorderForm() : this(null) { }

    public RecorderForm(Rectangle? initialBounds)
    {
        BrandTheme.Apply(this);
        Text = BrandTheme.Name + " · 屏幕录像";
        ClientSize = new Size(620, 582);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        preferences = RecorderPreferences.Load();
        frameRate.Value = Math.Clamp(preferences.FramesPerSecond, 1, 60);
        includeCursor.Checked = preferences.IncludeCursor;
        minimize.Checked = preferences.MinimizeOnStart;
        selectedBounds = initialBounds;
        if (initialBounds is not null) region.Checked = true;

        brandMark = BrandTheme.CreateMark(42);
        var mark = new PictureBox { Image = brandMark, SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(22, 28, 42, 42) };
        var wordmark = new Label { Text = BrandTheme.Wordmark, Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = BrandTheme.Accent, Bounds = new Rectangle(78, 20, 180, 23) };
        var title = new Label { Text = "屏幕录像", Font = new Font(Font.FontFamily, 19F, FontStyle.Bold), ForeColor = BrandTheme.Ink, Bounds = new Rectangle(76, 44, 260, 36) };
        editButton.SetBounds(466, 35, 134, 36);
        captureGroup.SetBounds(20, 100, 580, 112);
        StyleCard(captureGroup);
        var captureTitle = SectionLabel("录制范围", 18, 13);
        fullScreen.Text = "整个主屏幕";
        fullScreen.Location = new Point(18, 48);
        region.Location = new Point(168, 48);
        selectArea.SetBounds(432, 40, 130, 34);
        areaLabel.SetBounds(18, 80, 544, 23);
        areaLabel.ForeColor = BrandTheme.Muted;
        areaLabel.Font = new Font(Font.FontFamily, 8.5F);
        areaLabel.AutoEllipsis = true;
        captureGroup.Controls.AddRange([captureTitle, fullScreen, region, selectArea, areaLabel]);
        optionsGroup.SetBounds(20, 224, 580, 174);
        StyleCard(optionsGroup);
        var optionsTitle = SectionLabel("录制选项", 18, 13);
        var fpsLabel = new Label { Text = "帧率", Bounds = new Rectangle(18, 53, 50, 25), TextAlign = ContentAlignment.MiddleLeft };
        frameRate.SetBounds(80, 49, 70, 30);
        frameRate.BorderStyle = BorderStyle.FixedSingle;
        var fpsUnit = new Label { Text = "帧 / 秒", ForeColor = BrandTheme.Muted, Bounds = new Rectangle(162, 53, 70, 25) };
        includeCursor.Location = new Point(286, 53);
        var audioLabel = new Label { Text = "声音", Bounds = new Rectangle(18, 94, 50, 25), TextAlign = ContentAlignment.MiddleLeft };
        audioDevice.SetBounds(80, 89, 352, 30);
        audioDevice.FlatStyle = FlatStyle.Flat;
        audioDevice.BackColor = BrandTheme.Background;
        audioDevice.Items.Add("不录制声音");
        audioDevice.SelectedIndex = 0;
        refreshAudio.SetBounds(444, 87, 118, 32);
        minimize.Location = new Point(18, 136);
        ffmpegButton.SetBounds(424, 129, 138, 32);
        optionsGroup.Controls.AddRange([optionsTitle, fpsLabel, frameRate, fpsUnit, includeCursor, audioLabel, audioDevice, refreshAudio, minimize, ffmpegButton]);
        var recordingStatus = new Panel { Bounds = new Rectangle(20, 414, 580, 62), BackColor = BrandTheme.AccentPale };
        status.SetBounds(16, 18, 386, 26);
        status.ForeColor = BrandTheme.Accent;
        elapsed.SetBounds(430, 13, 132, 34);
        elapsed.ForeColor = BrandTheme.Ink;
        elapsed.Font = new Font("Consolas", 18F, FontStyle.Bold);
        recordingStatus.Controls.AddRange([status, elapsed]);
        startButton.Text = "●  开始录制";
        startButton.SetBounds(20, 492, 146, 40);
        pauseButton.SetBounds(176, 492, 112, 40);
        stopButton.SetBounds(298, 492, 112, 40);
        closeButton.SetBounds(504, 492, 96, 40);
        foreach (var button in new[] { selectArea, refreshAudio, ffmpegButton, editButton, pauseButton, stopButton, closeButton }) BrandTheme.StyleButton(button);
        BrandTheme.StyleButton(startButton, true);
        startButton.BackColor = BrandTheme.Coral;
        startButton.ForeColor = BrandTheme.Ink;
        startButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(233, 142, 123);
        hints.Text = "Ctrl+F10 暂停/继续  ·  Ctrl+F11 结束  ·  保存为 MP4 / WMV";
        hints.SetBounds(20, 544, 580, 32);
        hints.Font = new Font(Font.FontFamily, 8.5F);
        hints.ForeColor = BrandTheme.Muted;
        Controls.AddRange([mark, wordmark, title, editButton, captureGroup, optionsGroup, recordingStatus, startButton, pauseButton, stopButton, closeButton, hints]);
        toolTip.SetToolTip(audioDevice, "仅支持本机 DirectShow 音频设备；系统声音需要可用的回环/立体声混音设备。本版本不支持摄像头叠加。");
        toolTip.SetToolTip(minimize, "可从任务栏恢复录像机；也可用 Ctrl+F10 暂停/继续、Ctrl+F11 结束。");
        toolTip.SetToolTip(ffmpegButton, "通常会自动识别已安装的录屏引擎；仅在缺失或需要更换时设置。");
        fullScreen.CheckedChanged += (_, _) => RefreshArea();
        region.CheckedChanged += (_, _) => RefreshArea();
        selectArea.Click += (_, _) => SelectRegion();
        refreshAudio.Click += async (_, _) => await RefreshAudioAsync();
        ffmpegButton.Click += async (_, _) => await ConfigureEngineAsync();
        startButton.Click += async (_, _) => await StartRecordingAsync();
        pauseButton.Click += async (_, _) => await TogglePauseAsync();
        stopButton.Click += async (_, _) => await FinishRecordingAsync();
        closeButton.Click += (_, _) => Close();
        editButton.Click += async (_, _) =>
        {
            using var editor = new VideoTrimForm(lastSavedVideo, preferences);
            editor.ShowDialog(this);
            await RefreshAudioAsync();
        };
        timer.Tick += async (_, _) => await OnTimerAsync();
        FormClosing += OnClosing;
        Shown += async (_, _) => await RefreshAudioAsync();
        RefreshArea();
    }

    private Label SectionLabel(string text, int x, int y) => new()
    {
        Text = text, Bounds = new Rectangle(x, y, 220, 26), ForeColor = BrandTheme.Ink,
        Font = new Font(Font.FontFamily, 10F, FontStyle.Bold)
    };

    private static void StyleCard(Panel panel)
    {
        panel.BackColor = BrandTheme.Surface;
        panel.Paint += (_, e) => { using var pen = new Pen(BrandTheme.Border); e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1); };
    }

    private void RefreshArea()
    {
        var bounds = region.Checked ? selectedBounds : (Screen.PrimaryScreen?.Bounds ?? SystemInformation.VirtualScreen);
        areaLabel.Text = bounds is { } rectangle
            ? $"{(region.Checked ? "矩形区域" : "主显示器")}：{rectangle.Width} × {rectangle.Height} 像素  ({rectangle.X}, {rectangle.Y})"
            : "请点击“选择区域”，拖动鼠标选择需要录制的范围。";
        selectArea.Enabled = region.Checked;
    }

    private void SelectRegion()
    {
        Hide();
        try
        {
            using var picker = new RecordingRegionPicker();
            if (picker.ShowDialog() == DialogResult.OK)
            {
                selectedBounds = picker.SelectedBounds;
                region.Checked = true;
            }
        }
        finally { Show(); Activate(); RefreshArea(); }
    }

    private async Task RefreshAudioAsync()
    {
        if (checkingEngine) return;
        checkingEngine = true;
        resolvedEngine = null;
        refreshAudio.Enabled = false;
        SetButtons();
        try
        {
            var previous = audioDevice.SelectedItem?.ToString();
            status.Text = "正在自动检测录屏引擎…";
            resolvedEngine = await FFmpegLocator.CheckAvailableAsync(preferences.FFmpegPath);
            if (IsDisposed) return;
            audioDevice.Items.Clear();
            audioDevice.Items.Add("不录制声音");
            audioDevice.SelectedIndex = 0;
            status.Text = "录屏引擎就绪 · " + (string.IsNullOrWhiteSpace(preferences.FFmpegPath) ? "自动识别" : "已记住设置");
            toolTip.SetToolTip(status, "可以直接开始录制。引擎位置：\n" + resolvedEngine);
            try
            {
                var devices = await FFmpegLocator.GetAudioDevicesAsync(resolvedEngine);
                if (IsDisposed) return;
                foreach (var device in devices) audioDevice.Items.Add(device);
                audioDevice.SelectedIndex = Math.Max(0, audioDevice.Items.IndexOf(previous ?? ""));
                toolTip.SetToolTip(audioDevice, devices.Count == 0
                    ? "当前可录制无声视频。录制声音需要本机可用的 DirectShow 音频设备。"
                    : "声音默认关闭，选中设备后才会采集声音。系统声音需要回环/立体声混音设备。");
            }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                status.Text = "录屏引擎就绪 · 暂可录制无声视频";
                toolTip.SetToolTip(audioDevice, "声音设备检测失败，可点击刷新重试。\n" + ex.Message);
            }
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            resolvedEngine = null;
            status.Text = "录屏引擎待设置 · 点击“引擎设置”";
            toolTip.SetToolTip(status, ex.Message);
        }
        finally
        {
            checkingEngine = false;
            if (!IsDisposed) { refreshAudio.Enabled = true; SetButtons(); }
        }
    }

    private async Task ConfigureEngineAsync()
    {
        using var dialog = new RecordingEngineForm(preferences);
        dialog.ShowDialog(this);
        await RefreshAudioAsync();
    }

    private async Task StartRecordingAsync()
    {
        if (busy || checkingEngine) return;
        if (resolvedEngine is null)
        {
            await ConfigureEngineAsync();
            if (resolvedEngine is null) return;
        }
        if (region.Checked && selectedBounds is null)
        {
            SelectRegion();
            if (selectedBounds is null) return;
        }
        busy = true;
        SetButtons();
        try
        {
            if (session is not null) await session.DisposeAsync();
            var bounds = region.Checked ? selectedBounds!.Value : (Screen.PrimaryScreen?.Bounds ?? SystemInformation.VirtualScreen);
            preferences.FramesPerSecond = (int)frameRate.Value;
            preferences.IncludeCursor = includeCursor.Checked;
            preferences.MinimizeOnStart = minimize.Checked;
            preferences.Save();
            session = new ScreenRecordingSession(new RecordingOptions(bounds, (int)frameRate.Value,
                includeCursor.Checked, audioDevice.SelectedIndex > 0 ? audioDevice.SelectedItem?.ToString() : null, resolvedEngine));
            status.Text = "正在准备录像…";
            RegisterRecordingHotkeys();
            if (minimize.Checked) WindowState = FormWindowState.Minimized;
            await session.StartAsync();
            status.Text = "●  正在录像";
            timer.Start();
        }
        catch (Exception ex) { await ReportFailureAsync(ex); }
        finally { busy = false; SetButtons(); }
    }

    private async Task TogglePauseAsync()
    {
        if (busy || session is null) return;
        busy = true;
        SetButtons();
        try
        {
            if (session.State == RecordingState.Recording)
            {
                status.Text = "正在暂停…";
                await session.PauseAsync();
                status.Text = "Ⅱ  已暂停";
            }
            else if (session.State == RecordingState.Paused)
            {
                status.Text = "正在继续…";
                await session.ResumeAsync();
                status.Text = "●  正在录像";
            }
        }
        catch (Exception ex) { await ReportFailureAsync(ex); }
        finally { busy = false; SetButtons(); }
    }

    private async Task FinishRecordingAsync()
    {
        if (busy || session is null || session.State is not (RecordingState.Recording or RecordingState.Paused)) return;
        busy = true;
        SetButtons();
        try
        {
            if (session.State == RecordingState.Recording) await session.PauseAsync();
            WindowState = FormWindowState.Normal;
            Activate();
            status.Text = "Ⅱ  已暂停 · 请选择保存位置";
            using var dialog = new SaveFileDialog
            {
                Title = "保存录像", Filter = "MP4 视频 (*.mp4)|*.mp4|Windows Media 视频 (*.wmv)|*.wmv",
                DefaultExt = "mp4", AddExtension = true, OverwritePrompt = true,
                InitialDirectory = Directory.Exists(preferences.LastDirectory) ? preferences.LastDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                FileName = "录像_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + ".mp4"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                status.Text = "Ⅱ  已暂停 · 可继续录像，或再次点击“结束”保存";
                return;
            }
            status.Text = "正在保存视频，请稍候…";
            UnregisterRecordingHotkeys();
            var output = await session.FinishAsync(dialog.FileName);
            lastSavedVideo = output;
            preferences.LastDirectory = Path.GetDirectoryName(output);
            preferences.Save();
            timer.Stop();
            status.Text = "已保存：" + Path.GetFileName(output);
            toolTip.SetToolTip(status, output);
            MessageBox.Show(this, "录像已保存：\n" + output, "屏幕录像机", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { await ReportFailureAsync(ex); }
        finally { busy = false; SetButtons(); }
    }

    private async Task OnTimerAsync()
    {
        if (session is null) return;
        var duration = session.RecordedTime;
        elapsed.Text = $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        if (!busy && session.EncoderExited)
        {
            busy = true;
            try { await session.PauseAsync(); }
            catch (Exception ex) { await ReportFailureAsync(ex); }
            finally { busy = false; SetButtons(); }
        }
    }

    private async Task ReportFailureAsync(Exception exception)
    {
        timer.Stop();
        UnregisterRecordingHotkeys();
        WindowState = FormWindowState.Normal;
        Activate();
        status.Text = "录像失败 · " + exception.Message.Split('\n')[0];
        var message = exception.Message;
        if (session is not null)
        {
            await session.DisposeAsync();
            if (Directory.Exists(session.RecoveryDirectory) && Directory.EnumerateFiles(session.RecoveryDirectory).Any())
                message += "\n\n已保留录像片段，可用于恢复：\n" + session.RecoveryDirectory;
        }
        MessageBox.Show(this, message, "录像未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void SetButtons()
    {
        if (IsDisposed) return;
        var active = session?.State is RecordingState.Recording or RecordingState.Paused or RecordingState.Saving;
        captureGroup.Enabled = optionsGroup.Enabled = !busy && !checkingEngine && !active;
        startButton.Enabled = !busy && !checkingEngine && !active;
        editButton.Enabled = !busy && !checkingEngine && !active;
        pauseButton.Enabled = !busy && session?.State is RecordingState.Recording or RecordingState.Paused;
        stopButton.Enabled = pauseButton.Enabled;
        closeButton.Enabled = !busy && !checkingEngine;
        pauseButton.Text = session?.State == RecordingState.Paused ? "▶  继续" : "Ⅱ  暂停";
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (canClose) return;
        if (busy || checkingEngine) { e.Cancel = true; return; }
        if (session?.State is RecordingState.Recording or RecordingState.Paused)
        {
            e.Cancel = true;
            await FinishRecordingAsync();
            if (session.State != RecordingState.Finished) return;
        }
        timer.Stop();
        UnregisterRecordingHotkeys();
        if (session is not null)
        {
            e.Cancel = true;
            await session.DisposeAsync();
            canClose = true;
            BeginInvoke(Close);
        }
    }

    private void RegisterRecordingHotkeys()
    {
        hotkeyPause = RegisterHotKey(Handle, PauseHotkeyId, 0x4002, (int)Keys.F10);
        hotkeyStop = RegisterHotKey(Handle, StopHotkeyId, 0x4002, (int)Keys.F11);
        hints.Text = hotkeyPause && hotkeyStop ? "Ctrl+F10 暂停/继续，Ctrl+F11 结束。可从任务栏恢复窗口。" : "快捷键被其他程序占用；请从任务栏恢复窗口操作。";
    }
    private void UnregisterRecordingHotkeys()
    {
        if (hotkeyPause) UnregisterHotKey(Handle, PauseHotkeyId);
        if (hotkeyStop) UnregisterHotKey(Handle, StopHotkeyId);
        hotkeyPause = hotkeyStop = false;
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey)
        {
            if (m.WParam.ToInt32() == PauseHotkeyId) _ = TogglePauseAsync();
            if (m.WParam.ToInt32() == StopHotkeyId) _ = FinishRecordingAsync();
        }
        base.WndProc(ref m);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); toolTip.Dispose(); brandMark?.Dispose(); }
        base.Dispose(disposing);
    }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, int key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
}

internal sealed class RecorderPreferences
{
    public string? FFmpegPath { get; set; }
    public string? LastDirectory { get; set; }
    public int FramesPerSecond { get; set; } = 15;
    public bool IncludeCursor { get; set; } = true;
    public bool MinimizeOnStart { get; set; } = true;
    private static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Framelet", "recorder.json");
    public static RecorderPreferences Load()
    {
        try { return JsonSerializer.Deserialize<RecorderPreferences>(File.ReadAllText(ConfigPath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Recording is still usable if settings cannot be saved. */ }
    }
}

internal sealed class RecordingRegionPicker : Form
{
    private readonly Bitmap desktop;
    private Point anchor;
    private Rectangle selection;
    private bool selecting;
    public Rectangle SelectedBounds => new(selection.X + Bounds.X, selection.Y + Bounds.Y, selection.Width, selection.Height);

    public RecordingRegionPicker()
    {
        Bounds = SystemInformation.VirtualScreen;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        Text = BrandTheme.Name + " · 选择录像区域";
        ShowInTaskbar = true;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        desktop = new Bitmap(Bounds.Width, Bounds.Height);
        using (var graphics = Graphics.FromImage(desktop)) graphics.CopyFromScreen(Bounds.Location, Point.Empty, Bounds.Size);
        MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { anchor = e.Location; selecting = true; Capture = true; } };
        MouseMove += (_, e) =>
        {
            if (!selecting) return;
            var x = Math.Clamp(e.X, 0, ClientSize.Width);
            var y = Math.Clamp(e.Y, 0, ClientSize.Height);
            selection = Rectangle.FromLTRB(Math.Min(anchor.X, x), Math.Min(anchor.Y, y), Math.Max(anchor.X, x), Math.Max(anchor.Y, y));
            Invalidate();
        };
        MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; return; }
            if (!selecting) return;
            selecting = false;
            Capture = false;
            if (selection.Width >= 2 && selection.Height >= 2) DialogResult = DialogResult.OK;
        };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel; };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(desktop, 0, 0);
        using var shade = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
        e.Graphics.FillRectangle(shade, ClientRectangle);
        if (!selection.IsEmpty)
        {
            e.Graphics.DrawImage(desktop, selection, selection, GraphicsUnit.Pixel);
            using var border = new Pen(BrandTheme.Accent, 2);
            e.Graphics.DrawRectangle(border, selection);
        }
        var label = selection.IsEmpty ? "拖动鼠标选择录像区域 · Esc / 右键取消" : $"{selection.Width} × {selection.Height} · 松开鼠标确定 · Esc 取消";
        var size = TextRenderer.MeasureText(label, Font);
        var location = selection.IsEmpty ? new Point(24, 24) : new Point(Math.Clamp(selection.X, 0, Math.Max(0, ClientSize.Width - size.Width - 14)), Math.Max(0, selection.Y - 30));
        using var labelBackground = new SolidBrush(BrandTheme.Ink);
        e.Graphics.FillRectangle(labelBackground, new Rectangle(location, new Size(size.Width + 12, 28)));
        TextRenderer.DrawText(e.Graphics, label, Font, new Point(location.X + 6, location.Y + 5), Color.White);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) desktop.Dispose();
        base.Dispose(disposing);
    }
}
