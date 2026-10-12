using System.Runtime.InteropServices;
using System.Text.Json;

namespace Framelet;

public sealed partial class RecorderForm : Form
{
    private readonly RadioButton fullScreen = new() { Text = "全屏", Checked = true, AutoSize = true };
    private readonly RadioButton region = new() { Text = "矩形区域", AutoSize = true };
    private readonly Button selectArea = new() { Text = "选择区域…", Width = 100 };
    private readonly Label areaLabel = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    private readonly NumericUpDown frameRate = new() { Minimum = 1, Maximum = 60, Value = 15, Width = 58 };
    private readonly CheckBox includeCursor = new() { Text = "录制鼠标指针", Checked = true, AutoSize = true };
    private readonly CheckBox minimize = new() { Text = "开始时最小化录像机", Checked = true, AutoSize = true };
    private readonly ComboBox audioDevice = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button extensionButton = new() { Text = "扩展录音…", Width = 120 };
    private readonly Button startButton = new() { Text = "●  录制", Width = 104, Height = 30 };
    private readonly Button pauseButton = new() { Text = "Ⅱ  暂停", Enabled = false, Width = 104, Height = 30 };
    private readonly Button stopButton = new() { Text = "■  结束", Enabled = false, Width = 104, Height = 30 };
    private readonly Button closeButton = new() { Text = "关闭", Width = 74, Height = 30 };
    private readonly Button editButton = new() { Text = "扩展剪辑…", Width = 110, Height = 28 };
    private readonly Label status = new() { Text = "就绪 · 可直接录制", AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label elapsed = new() { Text = "00:00:00", TextAlign = ContentAlignment.MiddleRight };
    private readonly Label hints = new() { Text = "保存为 MP4。Ctrl+F10 暂停/继续，Ctrl+F11 结束。", AutoSize = false };
    private readonly GroupBox captureGroup = new();
    private readonly GroupBox optionsGroup = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly ToolTip toolTip = new();
    private RecordingControlBar? controlBar;
    private RecordingBoundary? boundary;
    private Rectangle? selectedBounds;
    private ScreenRecordingSession? session;
    private string? lastSavedVideo;
    private RecorderPreferences preferences;
    private bool busy;
    private bool checkingEngine;
    private bool selectingRegion;
    private string? resolvedEngine;
    private bool canClose;
    private bool hotkeyPause;
    private bool hotkeyStop;
    private const int PauseHotkeyId = 0x7901, StopHotkeyId = 0x7902;
    private const int WmHotkey = 0x0312;

    public RecorderForm() : this(null) { }

    public RecorderForm(Rectangle? initialBounds)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        BrandTheme.Apply(this);
        preferences = RecorderPreferences.Load();
        frameRate.Value = Math.Clamp(preferences.FramesPerSecond, 1, 60);
        includeCursor.Checked = preferences.IncludeCursor;
        minimize.Checked = preferences.MinimizeOnStart;
        selectedBounds = initialBounds;
        if (initialBounds is not null) region.Checked = true;

        BuildRecorderLayout();
        fullScreen.CheckedChanged += (_, _) => { if (fullScreen.Checked) region.Checked = false; RefreshArea(); };
        region.CheckedChanged += (_, _) => { if (region.Checked) fullScreen.Checked = false; RefreshArea(); };
        fullScreen.Checked = !region.Checked;
        selectArea.Click += async (_, _) => await SelectRegionAsync();
        extensionButton.Click += async (_, _) => await ConfigureAudioAsync();
        audioDevice.SelectedIndexChanged += (_, _) => RefreshReadyStatus();
        startButton.Click += async (_, _) => await StartRecordingAsync();
        pauseButton.Click += async (_, _) => await TogglePauseAsync();
        stopButton.Click += async (_, _) => await FinishRecordingAsync();
        closeButton.Click += (_, _) => Close();
        editButton.Click += (_, _) =>
        {
            using var editor = new VideoTrimForm(lastSavedVideo, preferences);
            editor.ShowDialog(this);
        };
        timer.Tick += async (_, _) => await OnTimerAsync();
        FormClosing += OnClosing;
        RefreshArea();
        SetButtons();
        ResumeLayout(true);
        Size = GetPreferredSize(Size.Empty);
    }

    private void RefreshArea()
    {
        var bounds = region.Checked ? selectedBounds : (Screen.PrimaryScreen?.Bounds ?? SystemInformation.VirtualScreen);
        areaLabel.Text = bounds is { } rectangle
            ? $"{(region.Checked ? "矩形区域" : "主显示器")}：{rectangle.Width} × {rectangle.Height} 像素  ({rectangle.X}, {rectangle.Y})"
            : "请点击“选择区域”，拖动鼠标选择需要录制的范围。";
        selectArea.Enabled = region.Checked;
    }

    private async Task SelectRegionAsync()
    {
        if (busy || checkingEngine || selectingRegion || IsDisposed || Disposing) return;
        selectingRegion = true;
        SetButtons();
        Exception? failure = null;
        var disableTransitions = 1;
        bool transitionsDisabled = false;
        try
        {
            // Do not photograph the recorder's DWM fade-out animation in the frozen selection preview.
            transitionsDisabled = DwmSetWindowAttribute(Handle, 3 /* DWMWA_TRANSITIONS_FORCEDISABLED */,
                ref disableTransitions, sizeof(int)) == 0;
            Hide();
            await Task.Delay(transitionsDisabled ? 80 : 350);
            if (IsDisposed || Disposing) return;
            DwmFlush();
            using var picker = new RecordingRegionPicker();
            if (picker.ShowDialog() == DialogResult.OK)
            {
                if (IsDisposed || Disposing) return;
                selectedBounds = picker.SelectedBounds;
                region.Checked = true;
            }
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            selectingRegion = false;
            if (!IsDisposed && !Disposing)
            {
                if (transitionsDisabled)
                {
                    disableTransitions = 0;
                    DwmSetWindowAttribute(Handle, 3, ref disableTransitions, sizeof(int));
                }
                Show(); Activate(); RefreshArea(); SetButtons();
            }
        }
        if (failure is not null && !IsDisposed && !Disposing)
            MessageBox.Show(this, "无法选择录像区域：\n" + failure.Message, "选择录像区域", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void RefreshReadyStatus()
    {
        if (busy || checkingEngine || session?.State is RecordingState.Recording or RecordingState.Paused or RecordingState.Saving) return;
        var withAudio = audioDevice.SelectedIndex > 0;
        status.Text = withAudio ? "就绪 · 将同时录制所选声音" : "就绪 · 可直接录制";
        toolTip.SetToolTip(status, withAudio ? "声音设备：" + audioDevice.SelectedItem : "录制无声 MP4 视频，无需配置额外工具。");
        hints.Text = "Ctrl+F10 暂停/继续  ·  Ctrl+F11 结束  ·  保存为 " + (withAudio ? "MP4 / WMV" : "MP4");
    }

    private async Task ConfigureAudioAsync()
    {
        if (checkingEngine) return;
        checkingEngine = true;
        SetButtons();
        try
        {
            using var dialog = new RecordingEngineForm(preferences);
            dialog.ShowDialog(this);
            if (!dialog.EngineReady || dialog.ResolvedEnginePath is null) return;
            resolvedEngine = dialog.ResolvedEnginePath;
            var previous = audioDevice.SelectedItem?.ToString();
            status.Text = "正在查找声音设备…";
            var devices = await FFmpegLocator.GetAudioDevicesAsync(resolvedEngine);
            if (IsDisposed) return;
            audioDevice.Items.Clear();
            audioDevice.Items.Add("不录制声音");
            foreach (var device in devices) audioDevice.Items.Add(device);
            audioDevice.SelectedIndex = Math.Max(0, audioDevice.Items.IndexOf(previous ?? ""));
            toolTip.SetToolTip(audioDevice, devices.Count == 0
                ? "未找到可用的声音设备，仍可直接录制无声视频。"
                : "选择设备后才会采集声音。选择“不录制声音”可恢复无声录屏。");
            if (devices.Count == 0)
                MessageBox.Show(this, "未找到可用的声音设备。你仍然可以直接录制无声视频。", "扩展录音", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            resolvedEngine = null;
            MessageBox.Show(this, "声音设备未准备好。选择“不录制声音”仍可直接录屏。\n\n" + ex.Message,
                "扩展录音", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            checkingEngine = false;
            if (!IsDisposed) { SetButtons(); RefreshReadyStatus(); }
        }
    }

    private async Task StartRecordingAsync()
    {
        if (busy || checkingEngine || selectingRegion || IsDisposed || Disposing) return;
        if (region.Checked && selectedBounds is null)
        {
            await SelectRegionAsync();
            if (selectedBounds is null || IsDisposed || Disposing) return;
        }
        busy = true;
        SetButtons();
        try
        {
            if (session is not null) await session.DisposeAsync();
            session = null;
            var selectedAudio = audioDevice.SelectedIndex > 0 ? audioDevice.SelectedItem?.ToString() : null;
            if (selectedAudio is not null)
                resolvedEngine = await FFmpegLocator.CheckAvailableAsync(preferences.FFmpegPath);
            var bounds = region.Checked ? selectedBounds!.Value : (Screen.PrimaryScreen?.Bounds ?? SystemInformation.VirtualScreen);
            preferences.FramesPerSecond = (int)frameRate.Value;
            preferences.IncludeCursor = includeCursor.Checked;
            preferences.MinimizeOnStart = minimize.Checked;
            preferences.Save();
            Hide();
            using (var countdown = new RecordingCountdown(bounds, (int)frameRate.Value, includeCursor.Checked, selectedAudio is not null))
            {
                if (countdown.ShowDialog() != DialogResult.OK)
                {
                    ShowSettings();
                    return;
                }
            }
            // Wait for the countdown's closing animation before taking the first frame.
            await Task.Delay(250);
            DwmFlush();
            RecordingOverlayProtection.Exclude(this);
            session = new ScreenRecordingSession(new RecordingOptions(bounds, (int)frameRate.Value,
                includeCursor.Checked, selectedAudio, selectedAudio is null ? null : resolvedEngine));
            status.Text = "正在准备录像…";
            RegisterRecordingHotkeys();
            ShowRecordingOverlays(bounds);
            await session.StartAsync();
            status.Text = "●  正在录像";
            timer.Start();
            if (!minimize.Checked) ShowSettings();
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
                RestoreOverlayCapture();
                status.Text = "Ⅱ  已暂停";
            }
            else if (session.State == RecordingState.Paused && session.CanResume)
            {
                status.Text = "正在继续…";
                ProtectOverlayCapture();
                await session.ResumeAsync();
                status.Text = "●  正在录像";
                timer.Start();
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
            RestoreOverlayCapture();
            UpdateRecordingOverlays();
            controlBar?.Hide();
            ShowSettings();
            status.Text = "Ⅱ  已暂停 · 请选择保存位置";
            using var dialog = new SaveFileDialog
            {
                Title = "保存录像", Filter = session.UsesNativeBackend
                    ? "MP4 视频 (*.mp4)|*.mp4"
                    : "MP4 视频 (*.mp4)|*.mp4|Windows Media 视频 (*.wmv)|*.wmv",
                DefaultExt = "mp4", AddExtension = true, OverwritePrompt = true,
                InitialDirectory = Directory.Exists(preferences.LastDirectory) ? preferences.LastDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                FileName = "录像_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + ".mp4"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                status.Text = session.CanResume
                    ? "Ⅱ  已暂停 · 可继续录像，或再次点击“结束”保存"
                    : "录像已保留 · 可重新选择位置保存";
                return;
            }
            status.Text = "正在保存视频，请稍候…";
            UnregisterRecordingHotkeys();
            var output = await session.FinishAsync(dialog.FileName);
            lastSavedVideo = output;
            preferences.LastDirectory = Path.GetDirectoryName(output);
            preferences.Save();
            timer.Stop();
            CloseRecordingOverlays();
            status.Text = "已保存：" + Path.GetFileName(output);
            toolTip.SetToolTip(status, output);
            MessageBox.Show(this, "录像已保存：\n" + output, "屏幕录像机", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { await ReportFailureAsync(ex, retrySaving: true); }
        finally
        {
            busy = false;
            if (session?.State == RecordingState.Paused && controlBar is not null)
            {
                controlBar.Show();
                controlBar.Activate();
            }
            SetButtons();
        }
    }

    private void ShowRecordingOverlays(Rectangle bounds)
    {
        CloseRecordingOverlays();
        boundary = new RecordingBoundary(bounds);
        controlBar = new RecordingControlBar(bounds);
        controlBar.PauseRequested += () => _ = TogglePauseAsync();
        controlBar.SaveRequested += () => _ = FinishRecordingAsync();
        controlBar.DiscardRequested += () => _ = DiscardRecordingAsync();
        // Protect both HWNDs synchronously: Shown is queued and can run after the first capture.
        RecordingOverlayProtection.Exclude(this);
        RecordingOverlayProtection.Exclude(boundary);
        RecordingOverlayProtection.Exclude(controlBar);
        boundary.Show();
        controlBar.Show();
        DwmFlush();
    }

    private void UpdateRecordingOverlays()
    {
        if (session is null) return;
        controlBar?.UpdateState(session.State, session.RecordedTime, busy, session.CanResume, hotkeyPause, hotkeyStop);
        boundary?.SetPaused(session.State != RecordingState.Recording);
    }

    private void CloseRecordingOverlays()
    {
        controlBar?.Dispose();
        boundary?.Dispose();
        controlBar = null;
        boundary = null;
        RecordingOverlayProtection.Restore(this);
    }

    private void ProtectOverlayCapture()
    {
        RecordingOverlayProtection.Exclude(this);
        if (controlBar is not null) RecordingOverlayProtection.Exclude(controlBar);
        if (boundary is not null) RecordingOverlayProtection.Exclude(boundary);
        DwmFlush();
    }

    private void RestoreOverlayCapture()
    {
        RecordingOverlayProtection.Restore(this);
        if (controlBar is not null) RecordingOverlayProtection.Restore(controlBar);
        if (boundary is not null) RecordingOverlayProtection.Restore(boundary);
    }

    private void ShowSettings()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private async Task DiscardRecordingAsync()
    {
        if (busy || session?.State != RecordingState.Paused) return;
        busy = true;
        SetButtons();
        try
        {
            if (MessageBox.Show(controlBar ?? (IWin32Window)this, "丢弃本次录像？尚未保存的画面将被删除。", "丢弃录像",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            await session.DiscardAsync();
            session = null;
            timer.Stop();
            UnregisterRecordingHotkeys();
            CloseRecordingOverlays();
            ShowSettings();
            elapsed.Text = "00:00:00";
            status.Text = "已丢弃本次录像 · 可以重新录制";
        }
        catch (Exception ex) { await ReportFailureAsync(ex); }
        finally { busy = false; SetButtons(); }
    }

    private async Task OnTimerAsync()
    {
        if (session is null) return;
        var duration = session.RecordedTime;
        elapsed.Text = $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        UpdateRecordingOverlays();
        if (!busy && session.EncoderExited)
        {
            busy = true;
            try { await session.PauseAsync(); }
            catch (Exception ex) { await ReportFailureAsync(ex); }
            finally { busy = false; SetButtons(); }
        }
    }

    private async Task ReportFailureAsync(Exception exception, bool retrySaving = false)
    {
        timer.Stop();
        UnregisterRecordingHotkeys();
        ShowSettings();
        var message = exception.Message;
        if (retrySaving && session?.State == RecordingState.Paused && !session.EncoderExited)
        {
            RestoreOverlayCapture();
            status.Text = "录像已保留 · 可重新选择位置保存";
            toolTip.SetToolTip(status, "点击“结束”重新选择保存位置。\n" + exception.Message);
            RegisterRecordingHotkeys();
            if (!session.CanResume) hints.Text = "Ctrl+F11 或点击“结束”重新保存。录像已停止追加画面。";
            MessageBox.Show(this, "未能保存到所选位置，录像仍然保留。请点击“结束”重新选择位置。\n\n" + message,
                "录像尚未保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        status.Text = "录像失败 · " + exception.Message.Split('\n')[0];
        CloseRecordingOverlays();
        if (session is not null)
        {
            await session.DisposeAsync();
            if (Directory.Exists(session.RecoveryDirectory) && Directory.EnumerateFiles(session.RecoveryDirectory).Any())
                message += "\n\n已保留录像片段，可用于恢复：\n" + session.RecoveryDirectory;
            session = null;
        }
        MessageBox.Show(this, message, "录像未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void SetButtons()
    {
        if (IsDisposed) return;
        var active = session?.State is RecordingState.Recording or RecordingState.Paused or RecordingState.Saving;
        captureGroup.Enabled = optionsGroup.Enabled = !busy && !checkingEngine && !selectingRegion && !active;
        startButton.Enabled = !busy && !checkingEngine && !selectingRegion && !active;
        editButton.Enabled = !busy && !checkingEngine && !selectingRegion && !active;
        pauseButton.Enabled = !busy && (session?.State == RecordingState.Recording || (session?.State == RecordingState.Paused && session.CanResume));
        stopButton.Enabled = !busy && session?.State is RecordingState.Recording or RecordingState.Paused;
        closeButton.Enabled = !busy && !checkingEngine && !selectingRegion;
        pauseButton.Text = session?.State == RecordingState.Paused ? "▶  继续" : "Ⅱ  暂停";
        var actions = Controls.Find("RecorderSessionActions", true).FirstOrDefault();
        if (actions is not null) actions.Visible = active;
        UpdateRecordingOverlays();
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (canClose) return;
        if (busy || checkingEngine || selectingRegion) { e.Cancel = true; return; }
        if (session?.State is RecordingState.Recording or RecordingState.Paused)
        {
            e.Cancel = true;
            await FinishRecordingAsync();
            if (session?.State != RecordingState.Finished) return;
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
        hints.Text = hotkeyPause && hotkeyStop ? "Ctrl+F10 暂停/继续 · Ctrl+F11 结束并保存" : "快捷键被其他程序占用；请使用录制控制条上的按钮。";
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
        if (disposing) { timer.Dispose(); toolTip.Dispose(); CloseRecordingOverlays(); }
        base.Dispose(disposing);
    }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, int key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
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
