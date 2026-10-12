using System.Diagnostics;

namespace Framelet;

internal sealed class RecordingEngineForm : Form
{
    private readonly RecorderPreferences preferences;
    private readonly Label state = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = BrandTheme.Muted, Margin = new Padding(0, 8, 0, 8) };
    private readonly TextBox location = new() { ReadOnly = true, Multiline = true, Height = 56, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    private readonly Button detect = new() { Text = "重新自动检测", AutoSize = true };
    private readonly Button choose = new() { Text = "选择已安装的引擎…", AutoSize = true };
    private readonly Button download = new() { Text = "打开 FFmpeg 下载页", AutoSize = true };
    private readonly Button done = new() { Text = "关闭", AutoSize = true };
    private bool checking;
    public bool EngineReady { get; private set; }
    public string? ResolvedEnginePath { get; private set; }

    public RecordingEngineForm(RecorderPreferences preferences)
    {
        this.preferences = preferences;
        BrandTheme.Apply(this);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = BrandTheme.Name + " · 录屏引擎";
        ClientSize = new Size(600, 350);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 4; row++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var explanation = new Label
        {
            Text = "Framelet 使用 FFmpeg 将屏幕画面保存为视频，并完成视频剪辑。\n已安装的引擎会自动识别，无需每次选择；当前便携包未附带引擎。",
            AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8)
        };
        root.Controls.Add(explanation, 0, 0);
        root.Controls.Add(state, 0, 1);
        root.Controls.Add(location, 0, 2);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 12, 0, 0) };
        foreach (var button in new[] { detect, choose, download })
        {
            BrandTheme.StyleButton(button);
            button.MinimumSize = new Size(0, 34);
            button.Margin = new Padding(0, 0, 8, 8);
            actions.Controls.Add(button);
        }
        root.Controls.Add(actions, 0, 3);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 10, 0, 0) };
        BrandTheme.StyleButton(done, true);
        done.MinimumSize = new Size(96, 34);
        done.Margin = Padding.Empty;
        footer.Controls.Add(done);
        root.Controls.Add(footer, 0, 4);
        Controls.Add(root);
        detect.Click += async (_, _) => await DetectAsync(null, true);
        choose.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "选择 FFmpeg 引擎", Filter = "FFmpeg 引擎 (ffmpeg.exe)|ffmpeg.exe", CheckFileExists = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) await DetectAsync(dialog.FileName, true);
        };
        download.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://ffmpeg.org/download.html#build-windows") { UseShellExecute = true }); }
            catch (Exception ex) { state.Text = "无法打开浏览器：" + ex.Message; }
        };
        done.Click += (_, _) => { DialogResult = EngineReady ? DialogResult.OK : DialogResult.Cancel; Close(); };
        Shown += async (_, _) => await DetectAsync(preferences.FFmpegPath, false);
        FormClosing += (_, e) => { if (checking) e.Cancel = true; };
    }

    private async Task DetectAsync(string? configuredPath, bool saveChoice)
    {
        if (checking) return;
        checking = true;
        EngineReady = false;
        ResolvedEnginePath = null;
        detect.Enabled = choose.Enabled = done.Enabled = false;
        state.Text = "正在检测录屏引擎…";
        try
        {
            var resolved = await FFmpegLocator.CheckAvailableAsync(configuredPath);
            if (IsDisposed) return;
            if (saveChoice)
            {
                preferences.FFmpegPath = configuredPath;
                preferences.Save();
            }
            EngineReady = true;
            ResolvedEnginePath = resolved;
            state.Text = "引擎已就绪 · " + (string.IsNullOrWhiteSpace(configuredPath) ? "自动识别，无需手动选择" : "已记住选定的引擎");
            state.ForeColor = BrandTheme.Accent;
            location.Text = resolved;
            done.Text = "完成";
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            EngineReady = false;
            state.Text = ex.Message;
            state.ForeColor = BrandTheme.Coral;
            location.Text = "可从下载页获取 Windows 版本并解压，再选择其中的 ffmpeg.exe。";
            done.Text = "关闭";
        }
        finally
        {
            checking = false;
            if (!IsDisposed) detect.Enabled = choose.Enabled = done.Enabled = true;
        }
    }
}
