using System.Runtime.InteropServices;

namespace Framelet;

/// <summary>Windows compositor exclusion keeps recording controls out of captured frames.</summary>
internal static class RecordingOverlayProtection
{
    internal static void Restore(Form form)
    {
        if (form.IsHandleCreated && !form.IsDisposed) SetWindowDisplayAffinity(form.Handle, 0);
    }

    internal static void Exclude(Form form)
    {
        if (!SetWindowDisplayAffinity(form.Handle, 0x11 /* WDA_EXCLUDEFROMCAPTURE */))
            throw new InvalidOperationException("Windows 未能将录制控件从视频画面中排除，本次录制尚未开始。请在支持窗口捕获排除的桌面会话中重试。");
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
}

internal sealed class RecordingControlBar : Form
{
    private readonly Label time = new() { AutoSize = true, Text = "准备录制", Margin = new Padding(6, 7, 12, 0) };
    private readonly Button pause = ActionButton("暂停");
    private readonly Button save = ActionButton("结束并保存");
    private readonly Button discard = ActionButton("丢弃");
    private readonly Label hint = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(5, 4, 5, 1) };
    private readonly Rectangle recordingBounds;
    private Point dragOrigin;
    private bool dragging;
    public event Action? PauseRequested;
    public event Action? SaveRequested;
    public event Action? DiscardRequested;

    public RecordingControlBar(Rectangle bounds)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        BrandTheme.Apply(this);
        Name = "RecordingControlBar";
        Text = BrandTheme.Name + " · 录制控制条";
        recordingBounds = bounds;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = SystemColors.Control;
        ForeColor = SystemColors.ControlText;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(5);
        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, ColumnCount = 1, Margin = Padding.Empty };
        var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, Margin = Padding.Empty, Dock = DockStyle.Fill };
        var grip = new Label { Text = "⠿", AutoSize = true, Margin = new Padding(0, 6, 0, 0),
            Cursor = Cursors.SizeAll, AccessibleName = "拖动录制控制条" };
        grip.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { dragOrigin = Cursor.Position; dragging = true; grip.Capture = true; } };
        grip.MouseMove += (_, _) =>
        {
            if (!dragging) return;
            var current = Cursor.Position;
            Location = new Point(Left + current.X - dragOrigin.X, Top + current.Y - dragOrigin.Y);
            dragOrigin = current;
        };
        grip.MouseUp += (_, _) => { dragging = false; grip.Capture = false; KeepOnScreen(); };
        actions.Controls.AddRange([grip, time, pause, save, discard]);
        layout.Controls.Add(actions);
        layout.Controls.Add(hint);
        Controls.Add(layout);
        SizeChanged += (_, _) => { if (Visible) KeepOnScreen(); };
        pause.Click += (_, _) => PauseRequested?.Invoke();
        save.Click += (_, _) => SaveRequested?.Invoke();
        discard.Click += (_, _) => DiscardRequested?.Invoke();
        Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, ClientRectangle, BrandTheme.Accent, ButtonBorderStyle.Solid);
        // Closing the control bar means stop/save, never silently leave an invisible recording running.
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; SaveRequested?.Invoke(); } };
        ResumeLayout(true);
        UpdateState(RecordingState.Ready, TimeSpan.Zero, true, true, false, false);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var work = Screen.FromRectangle(recordingBounds).WorkingArea;
        int x = Math.Clamp(recordingBounds.Left + (recordingBounds.Width - Width) / 2, work.Left, Math.Max(work.Left, work.Right - Width));
        int y = recordingBounds.Bottom + 8;
        if (y + Height > work.Bottom) y = recordingBounds.Top - Height - 8;
        if (y < work.Top) y = work.Top + 8;
        Location = new Point(x, Math.Min(y, Math.Max(work.Top, work.Bottom - Height)));
    }

    private void KeepOnScreen()
    {
        var work = Screen.FromRectangle(Bounds).WorkingArea;
        Location = RecordingSelectionGeometry.KeepVisible(Location, Size, work);
    }

    public void UpdateState(RecordingState state, TimeSpan duration, bool busy, bool canResume, bool pauseHotkey, bool stopHotkey)
    {
        bool paused = state == RecordingState.Paused;
        time.Text = $"{(paused ? "Ⅱ 已暂停" : state == RecordingState.Recording ? "● 录制中" : "准备中")}  {(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        time.ForeColor = paused ? BrandTheme.Accent : Color.FromArgb(185, 49, 43);
        pause.Text = paused ? "继续" : "暂停";
        save.Text = paused ? "保存" : "结束并保存";
        discard.Visible = paused;
        pause.Enabled = !busy && (state == RecordingState.Recording || (paused && canResume));
        save.Enabled = discard.Enabled = !busy && (state == RecordingState.Recording || paused);
        hint.Text = pauseHotkey && stopHotkey ? "Framelet  ·  Ctrl+F10 暂停/继续  ·  Ctrl+F11 结束" : "Framelet  ·  使用上方按钮暂停、结束并保存";
    }

    private static Button ActionButton(string text) => new()
    {
        Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(64, 30), Padding = new Padding(6, 0, 6, 0),
        Margin = new Padding(2, 0, 2, 0), FlatStyle = FlatStyle.System
    };
}

internal sealed class RecordingBoundary : Form
{
    private readonly System.Windows.Forms.Timer blink = new() { Interval = 600 };
    private bool alternate;
    public RecordingBoundary(Rectangle bounds)
    {
        AutoScaleMode = AutoScaleMode.None; // Capture coordinates are physical pixels, including on mixed-DPI desktops.
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        Bounds = Rectangle.Intersect(Rectangle.Inflate(bounds, 3, 3), SystemInformation.VirtualScreen);
        BackColor = BrandTheme.Coral;
        var ring = new Region(ClientRectangle);
        ring.Exclude(Rectangle.Inflate(ClientRectangle, -3, -3));
        Region = ring;
        blink.Tick += (_, _) => { alternate = !alternate; BackColor = alternate ? BrandTheme.Coral : BrandTheme.Accent; };
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var value = base.CreateParams; value.ExStyle |= 0x080000A0; return value; } // no-activate, tool-window, click-through
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        blink.Start();
    }
    public void SetPaused(bool paused)
    {
        if (paused) { blink.Stop(); BackColor = BrandTheme.Accent; }
        else blink.Start();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { blink.Dispose(); Region?.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class RecordingCountdown : Form
{
    private readonly System.Windows.Forms.Timer tick = new() { Interval = 1000 };
    private readonly Label title = new() { AutoSize = true, Text = "准备录制 · 3", Margin = new Padding(0, 0, 0, 10) };
    private int seconds = 3;
    public RecordingCountdown(Rectangle bounds, int fps, bool cursor, bool audio)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        BrandTheme.Apply(this);
        Text = BrandTheme.Name + " · 即将录制";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(20);
        var layout = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, Margin = Padding.Empty };
        title.Font = new Font(Font.FontFamily, 20, FontStyle.Bold);
        layout.Controls.Add(title);
        layout.Controls.Add(new Label { AutoSize = true, Text = $"区域 {bounds.Width} × {bounds.Height}  ·  {fps} 帧/秒\n鼠标指针{(cursor ? "开启" : "关闭")}  ·  {(audio ? "录制所选声音" : "不录制声音")}", Margin = new Padding(0, 0, 0, 12) });
        layout.Controls.Add(new Label { AutoSize = true, Text = "录制时会显示边框和暂停 / 结束控制条。\n按 Esc 取消本次录制。", ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 12) });
        var cancel = new Button { Text = "取消", AutoSize = true, MinimumSize = new Size(82, 30), DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.System };
        layout.Controls.Add(cancel);
        Controls.Add(layout);
        CancelButton = cancel;
        tick.Tick += (_, _) =>
        {
            seconds--;
            if (seconds == 0) { tick.Stop(); DialogResult = DialogResult.OK; }
            else title.Text = "准备录制 · " + seconds;
        };
        Shown += (_, _) => tick.Start();
        ResumeLayout(true);
    }
    protected override void Dispose(bool disposing) { if (disposing) tick.Dispose(); base.Dispose(disposing); }
}
