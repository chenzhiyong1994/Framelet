using System.Runtime.InteropServices;

namespace Framelet;

internal sealed class ScrollingCaptureWindow : Form
{
    private readonly Rectangle region;
    private readonly bool includeCursor;
    private readonly List<Bitmap> frames = [];
    private readonly Label status;
    private readonly Button captureButton;
    private readonly Button finishButton;
    private readonly System.Windows.Forms.Timer captureTimer = new() { Interval = 180 };
    private Bitmap? result;
    private bool captureRegistered;
    private bool finishRegistered;
    protected override bool ShowWithoutActivation => true;

    public ScrollingCaptureWindow(Rectangle region, bool includeCursor)
    {
        this.region = region;
        this.includeCursor = includeCursor;
        BrandTheme.Apply(this);
        Text = BrandTheme.Name + " · 滚动捕捉";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;
        TopMost = true;
        ClientSize = new Size(404, 197);
        Font = new Font("Microsoft YaHei UI", 9f);
        var area = Screen.FromRectangle(region).WorkingArea;
        Location = new Point(Math.Clamp(region.Right + 12, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(region.Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        Controls.Add(new Label { Text = "在目标窗口向下滚动后按 F8 采集下一屏。", AutoSize = false, Bounds = new Rectangle(12, 12, 380, 23) });
        Controls.Add(new Label { Text = "每次保留至少 1/4 重叠区域；完成后按 F9。\n请选择纯滚动内容，避开固定标题栏和悬浮元素。", AutoSize = false, Bounds = new Rectangle(12, 39, 380, 42), ForeColor = Color.DimGray });
        status = new Label { Text = "正在采集第一屏…", Bounds = new Rectangle(12, 88, 380, 46) };
        captureButton = new Button { Text = "采集下一屏 (F8)", Bounds = new Rectangle(12, 147, 143, 32) };
        finishButton = new Button { Text = "完成 (F9)", Bounds = new Rectangle(165, 147, 113, 32) };
        var cancelButton = new Button { Text = "取消", Bounds = new Rectangle(287, 147, 104, 32), DialogResult = DialogResult.Cancel };
        BrandTheme.StyleButton(captureButton);
        BrandTheme.StyleButton(finishButton, true);
        BrandTheme.StyleButton(cancelButton);
        Controls.AddRange([status, captureButton, finishButton, cancelButton]);
        CancelButton = cancelButton;
        captureButton.Click += (_, _) => QueueCapture();
        finishButton.Click += (_, _) => Finish();
        captureTimer.Tick += (_, _) => CaptureFrame();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        captureRegistered = RegisterHotKey(Handle, 1801, 0x4000, (uint)Keys.F8);
        finishRegistered = RegisterHotKey(Handle, 1802, 0x4000, (uint)Keys.F9);
        QueueCapture();
    }

    private void QueueCapture()
    {
        if (captureTimer.Enabled) return;
        captureButton.Enabled = false;
        finishButton.Enabled = false;
        Hide();
        captureTimer.Start();
    }

    private void CaptureFrame()
    {
        captureTimer.Stop();
        try
        {
            if (frames.Count >= 50) throw new InvalidOperationException("单次最多采集 50 屏，请先完成当前拼接。");
            if ((long)region.Width * region.Height * (frames.Count + 1) > 80_000_000)
                throw new InvalidOperationException("本次采集已达到内存保护上限，请完成当前拼接或缩小区域。");
            frames.Add(CaptureEngine.CaptureScreen(region, includeCursor));
            string conflict = string.Join(" / ", new[] { captureRegistered ? "" : "F8", finishRegistered ? "" : "F9" }.Where(text => text.Length > 0));
            status.Text = $"已采集 {frames.Count} 屏 · {region.Width} × {region.Height}" +
                (conflict.Length > 0 ? $"\n{conflict} 已被占用，请使用下方按钮。" : "\n滚动目标窗口后可继续采集。");
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "滚动捕捉", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            Show();
            captureButton.Enabled = true;
            finishButton.Enabled = frames.Count > 0;
        }
    }

    private void Finish()
    {
        if (captureTimer.Enabled || frames.Count == 0) return;
        try
        {
            result = ImageStitcher.Stitch(frames);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "拼接失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public Bitmap? TakeResult()
    {
        var bitmap = result;
        result = null;
        return bitmap;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312)
        {
            if (message.WParam.ToInt32() == 1801) QueueCapture();
            else if (message.WParam.ToInt32() == 1802) Finish();
        }
        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            captureTimer.Dispose();
            if (IsHandleCreated && captureRegistered) UnregisterHotKey(Handle, 1801);
            if (IsHandleCreated && finishRegistered) UnregisterHotKey(Handle, 1802);
            foreach (var frame in frames) frame.Dispose();
            result?.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr handle, int id);
}
