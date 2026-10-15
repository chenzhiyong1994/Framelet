using System.Runtime.InteropServices;

namespace Framelet;

internal sealed class ScrollingCaptureWindow : Form
{
    private readonly Func<Bitmap> capture;
    private readonly Func<string?> prepare;
    private readonly Func<string?> scroll;
    private readonly IDisposable? target;
    private readonly ScrollCaptureSession session = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Label status;
    private readonly Label shortcuts;
    private readonly Button pauseButton;
    private readonly Button finishButton;
    private readonly int sampleDelay;
    private readonly bool nativeControls;
    private readonly Point scrollPoint;
    private readonly Rectangle region;
    private Bitmap? result;
    private bool running;
    private bool pauseRequested;
    private bool finishRequested;
    private bool captureExcluded;
    private bool canShowDuringCapture;
    private readonly List<int> registeredHotkeys = [];
    protected override bool ShowWithoutActivation => true;
    internal bool IsRunning => running;
    internal int AcceptedFrames => session.FrameCount;
    internal string StatusText => status.Text;

    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ExStyle |= 0x08000000; return parameters; }
    }

    public ScrollingCaptureWindow(Rectangle region, bool includeCursor, Point? targetPoint = null)
        : this(region, ScrollTarget.FromRegion(region, targetPoint)) { }

    private ScrollingCaptureWindow(Rectangle region, ScrollTarget target)
        : this(region, () => CaptureEngine.CaptureScreen(region),
            () => target.Prepare(out var error) ? null : error,
            () => target.ScrollDown(out var error) ? null : error, 180, true, target.Position)
    {
        this.target = target;
    }

    internal ScrollingCaptureWindow(Rectangle region, Func<Bitmap> capture, Func<string?> prepare,
        Func<string?> scroll, int sampleDelay = 180, bool nativeControls = false, Point? targetPoint = null)
    {
        this.capture = capture;
        this.region = region;
        this.prepare = prepare;
        this.scroll = scroll;
        this.sampleDelay = sampleDelay;
        this.nativeControls = nativeControls;
        scrollPoint = targetPoint ?? new Point(region.Left + region.Width / 2, region.Top + region.Height / 2);
        BrandTheme.Apply(this);
        Text = BrandTheme.Name + " · 自动滚动捕捉";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(12) };
        layout.Controls.Add(new Label
        {
            Text = "正在自动向下滚动，抵达底部后完成。",
            AutoSize = true, Margin = new Padding(0, 0, 0, 8)
        });
        status = new Label { Text = "准备采集第一屏…", AutoSize = true, MinimumSize = new Size(370, 52), MaximumSize = new Size(370, 0) };
        shortcuts = new Label { Text = "F8 暂停 / 继续 · F9 完成 · Esc 取消", AutoSize = true, MaximumSize = new Size(370, 0), ForeColor = BrandTheme.Muted };
        layout.Controls.Add(status);
        layout.Controls.Add(shortcuts);
        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        pauseButton = new Button { Text = "暂停 (F8)", AutoSize = true, MinimumSize = new Size(116, 34) };
        finishButton = new Button { Text = "完成 (F9)", AutoSize = true, MinimumSize = new Size(116, 34), Enabled = false };
        var cancelButton = new Button { Text = "取消", AutoSize = true, MinimumSize = new Size(116, 34), DialogResult = DialogResult.Cancel };
        BrandTheme.StyleButton(pauseButton);
        BrandTheme.StyleButton(finishButton, true);
        BrandTheme.StyleButton(cancelButton);
        actions.Controls.AddRange([pauseButton, finishButton, cancelButton]);
        layout.Controls.Add(actions);
        Controls.Add(layout);
        CancelButton = cancelButton;
        pauseButton.Click += (_, _) => TogglePause();
        finishButton.Click += (_, _) => RequestFinish();
        Shown += (_, _) =>
        {
            var area = Screen.FromRectangle(region).WorkingArea;
            Point[] positions = [new(area.Right - Width, area.Top), new(area.Left, area.Top),
                new(area.Right - Width, area.Bottom - Height), new(area.Left, area.Bottom - Height)];
            Location = positions.OrderBy(point => new Rectangle(point, Size).Contains(scrollPoint))
                .ThenBy(point => IntersectionArea(new Rectangle(point, Size), region)).First();
        };
    }

    private static long IntersectionArea(Rectangle first, Rectangle second)
    {
        var intersection = Rectangle.Intersect(first, second);
        return (long)intersection.Width * intersection.Height;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (nativeControls)
        {
            RegisterShortcut(1801, Keys.F8);
            RegisterShortcut(1802, Keys.F9);
            RegisterShortcut(1803, Keys.Escape);
            captureExcluded = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
                && SetWindowDisplayAffinity(Handle, 0x11)
                && GetWindowDisplayAffinity(Handle, out uint affinity) && affinity == 0x11;
        }
        canShowDuringCapture = (captureExcluded || !Bounds.IntersectsWith(region)) && !Bounds.Contains(scrollPoint);
        BeginInvoke(StartCapture);
    }

    private void RegisterShortcut(int id, Keys key)
    {
        if (RegisterHotKey(Handle, id, 0x4000, (uint)key)) registeredHotkeys.Add(id);
        else shortcuts.Text += $"\n{key} 被占用，请使用窗口按钮。";
    }

    internal void TogglePause()
    {
        if (running)
        {
            pauseRequested = true;
            pauseButton.Enabled = false;
            status.Text = "正在保存当前画面，随后暂停…";
        }
        else StartCapture();
    }

    private async void StartCapture()
    {
        if (running || lifetime.IsCancellationRequested) return;
        running = true;
        pauseRequested = false;
        finishRequested = false;
        pauseButton.Text = "暂停 (F8)";
        pauseButton.Enabled = true;
        try
        {
            Opacity = 0;
            await Task.Delay(sampleDelay, lifetime.Token);
            if (nativeControls)
            {
                while (Control.MouseButtons != MouseButtons.None || Control.ModifierKeys != Keys.None)
                    await Task.Delay(50, lifetime.Token);
            }
            string? error = prepare();
            if (error != null) { status.Text = error; return; }
            if (canShowDuringCapture) Opacity = 1;
            using (var initial = await CaptureStableFrame())
            {
                var match = session.Append(initial);
                if (match.Status is ScrollMatchStatus.Unmatched or ScrollMatchStatus.Upward)
                {
                    status.Text = "画面与暂停前不连续，请滚回上一次位置后继续，或完成已采集部分。";
                    return;
                }
            }
            UpdateProgress();
            int unchanged = 0;
            while (!lifetime.IsCancellationRequested)
            {
                if (nativeControls && !canShowDuringCapture) await Task.Delay(400, lifetime.Token);
                if (finishRequested) { Complete(); return; }
                if (pauseRequested) { status.Text = ProgressText() + "\n已暂停，可继续或完成。"; return; }
                error = scroll();
                if (error != null) { status.Text = ProgressText() + "\n" + error; return; }
                using var next = await CaptureStableFrame();
                var match = session.Append(next);
                if (match.Status == ScrollMatchStatus.Matched)
                {
                    unchanged = 0;
                    UpdateProgress();
                }
                else if (match.Status == ScrollMatchStatus.Duplicate)
                {
                    unchanged++;
                    if (unchanged >= 3)
                    {
                        if (session.FrameCount > 1) { Complete(); return; }
                        status.Text = "目标没有响应滚动，或已在底部。\n请确认选区内是可滚动内容；可重试或完成单屏截图。";
                        return;
                    }
                }
                else
                {
                    status.Text = ProgressText() + (match.Status == ScrollMatchStatus.Upward
                        ? "\n检测到向上滚动，已暂停。请回到最后采集位置后继续。"
                        : "\n画面无法可靠对齐，已暂停。请滚回上一位置，或完成已采集部分。");
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!IsDisposed) status.Text = ProgressText() + "\n" + error.Message; }
        finally
        {
            running = false;
            if (!IsDisposed && !lifetime.IsCancellationRequested)
            {
                Opacity = 1;
                pauseButton.Text = "继续 (F8)";
                pauseButton.Enabled = true;
                finishButton.Enabled = session.FrameCount > 0;
            }
        }
    }

    private async Task<Bitmap> CaptureStableFrame()
    {
        if (!canShowDuringCapture) Opacity = 0;
        await Task.Delay(sampleDelay * 2, lifetime.Token);
        Bitmap? previous = null;
        try
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                await Task.Delay(sampleDelay, lifetime.Token);
                var current = capture();
                if (previous != null && ImageStitcher.AreEquivalent(previous, current)) return current;
                previous?.Dispose();
                previous = current;
            }
            throw new InvalidOperationException("画面持续变化，已暂停。请停止动画或等待内容加载后继续。");
        }
        finally
        {
            previous?.Dispose();
            if (!canShowDuringCapture && !IsDisposed && !lifetime.IsCancellationRequested) Opacity = 1;
        }
    }

    private string ProgressText() => $"已采集 {session.FrameCount} 屏 · {session.Width} × {session.Height}";
    private void UpdateProgress()
    {
        status.Text = ProgressText() + "\n请保持目标窗口位置不变。";
        finishButton.Enabled = true;
    }

    internal void RequestFinish()
    {
        if (running) { finishRequested = true; status.Text = "正在采集最后一屏…"; }
        else if (session.FrameCount > 0) Complete();
    }

    private void Complete()
    {
        result = session.ToBitmap();
        DialogResult = DialogResult.OK;
        Close();
    }

    public Bitmap? TakeResult()
    {
        var bitmap = result;
        result = null;
        return bitmap;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0021) { message.Result = (IntPtr)3; return; }
        if (message.Msg == 0x0312)
        {
            if (message.WParam.ToInt32() == 1801) TogglePause();
            else if (message.WParam.ToInt32() == 1802) RequestFinish();
            else if (message.WParam.ToInt32() == 1803) { DialogResult = DialogResult.Cancel; Close(); }
        }
        base.WndProc(ref message);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        lifetime.Cancel();
        base.OnFormClosing(e);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        foreach (int hotkey in registeredHotkeys) UnregisterHotKey(Handle, hotkey);
        registeredHotkeys.Clear();
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lifetime.Cancel();
            target?.Dispose();
            session.Dispose();
            result?.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr handle, int id);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr handle, uint affinity);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr handle, out uint affinity);
}
