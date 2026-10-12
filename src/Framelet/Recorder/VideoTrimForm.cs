using System.Diagnostics;

namespace Framelet;

public sealed class VideoTrimForm : Form
{
    private readonly TextBox filePath = new() { ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill };
    private readonly Button browse = new() { Text = "打开视频", Width = 100, Height = 30 };
    private readonly PictureBox preview = new() { SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill };
    private readonly TrackBar timeline = new() { Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None, Dock = DockStyle.Fill, Enabled = false };
    private readonly Label cursorTime = new() { Text = "00:00.00 / 00:00.00", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
    private readonly Label information = new() { Text = "打开视频后，拖动时间条预览画面。", AutoSize = false, Dock = DockStyle.Fill };
    private readonly NumericUpDown start = new() { DecimalPlaces = 2, Increment = .1m, Maximum = 999999, Width = 110 };
    private readonly NumericUpDown end = new() { DecimalPlaces = 2, Increment = .1m, Maximum = 999999, Width = 110 };
    private readonly Button startHere = new() { Text = "将此帧设为起点", AutoSize = true };
    private readonly Button endHere = new() { Text = "将此帧设为终点", AutoSize = true };
    private readonly Button reset = new() { Text = "保留完整视频", AutoSize = true };
    private readonly Label selectedDuration = new() { Text = "待选择范围", TextAlign = ContentAlignment.MiddleRight };
    private readonly ComboBox format = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly Button export = new() { Text = "导出视频", Width = 142, Height = 38 };
    private readonly Button cancel = new() { Text = "取消导出", Width = 90, Height = 34, Enabled = false };
    private readonly Button openFolder = new() { Text = "打开导出文件夹", Width = 132, Height = 34, Enabled = false };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1000, Height = 8, Dock = DockStyle.Fill };
    private readonly Label status = new() { Text = "就绪", AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 180 };
    private readonly string? configuredFFmpeg;
    private readonly string? initialPath;
    private CancellationTokenSource? previewCancellation;
    private CancellationTokenSource? operationCancellation;
    private VideoMetadata? metadata;
    private bool operationRunning;
    private bool exporting;
    private bool initializingValues;
    private string? lastOutput;
    private Image? brandMark;

    public VideoTrimForm(string? inputPath = null, string? ffmpegPath = null)
    {
        initialPath = inputPath;
        configuredFFmpeg = ffmpegPath ?? RecorderPreferences.Load().FFmpegPath;
        BrandTheme.Apply(this);
        Text = BrandTheme.Name + " · 视频剪辑";
        ClientSize = new Size(1020, 680);
        MinimumSize = new Size(916, 680);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 14), ColumnCount = 1, RowCount = 9 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        var heading = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        brandMark = BrandTheme.CreateMark(42);
        heading.Controls.Add(new PictureBox { Image = brandMark, SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(2, 12, 42, 42) });
        heading.Controls.Add(new Label { Text = BrandTheme.Wordmark, Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = BrandTheme.Accent, Bounds = new Rectangle(58, 2, 220, 24) });
        heading.Controls.Add(new Label { Text = "留下精彩的那一段", Font = new Font(Font.FontFamily, 19F, FontStyle.Bold), Bounds = new Rectangle(56, 29, 620, 38) });
        root.Controls.Add(heading, 0, 0);

        var inputRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        filePath.Margin = new Padding(0, 7, 12, 8);
        filePath.BackColor = BrandTheme.Surface;
        filePath.PlaceholderText = "打开要剪辑的视频文件";
        browse.Size = new Size(132, 36);
        browse.Margin = new Padding(0, 1, 0, 5);
        BrandTheme.StyleButton(browse);
        inputRow.Controls.Add(filePath, 0, 0);
        inputRow.Controls.Add(browse, 1, 0);
        root.Controls.Add(inputRow, 0, 1);
        preview.Margin = new Padding(0, 2, 0, 4);
        preview.BackColor = BrandTheme.Canvas;
        preview.Paint += (_, e) =>
        {
            if (preview.Image is not null) return;
            TextRenderer.DrawText(e.Graphics, "打开视频，找到要保留的瞬间\n拖动时间条预览，再设置起点与终点", Font, preview.ClientRectangle, BrandTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        };
        root.Controls.Add(preview, 0, 2);

        var timelineRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        timelineRow.RowCount = 1;
        timelineRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        timelineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        timelineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 198));
        timeline.BackColor = BrandTheme.Background;
        timeline.AutoSize = false;
        timeline.Margin = Padding.Empty;
        cursorTime.ForeColor = BrandTheme.Ink;
        cursorTime.Font = new Font("Consolas", 10F);
        timelineRow.Controls.Add(timeline, 0, 0);
        timelineRow.Controls.Add(cursorTime, 1, 0);
        root.Controls.Add(timelineRow, 0, 3);
        information.ForeColor = BrandTheme.Muted;
        root.Controls.Add(information, 0, 4);

        var selectionCard = new Panel { Dock = DockStyle.Fill, Size = new Size(980, 94), Margin = new Padding(0, 0, 0, 8), BackColor = BrandTheme.Surface };
        selectionCard.Paint += (_, e) => { using var pen = new Pen(BrandTheme.Border); e.Graphics.DrawRectangle(pen, 0, 0, selectionCard.Width - 1, selectionCard.Height - 1); };
        selectionCard.Controls.Add(new Label { Text = "保留区间", Font = new Font(Font.FontFamily, 10F, FontStyle.Bold), Bounds = new Rectangle(20, 13, 240, 25) });
        selectedDuration.SetBounds(712, 10, 246, 28);
        selectedDuration.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        selectedDuration.ForeColor = BrandTheme.Accent;
        selectionCard.Controls.Add(selectedDuration);
        var startLabel = new Label { Text = "起点 / 秒", Bounds = new Rectangle(20, 56, 74, 22) };
        var endLabel = new Label { Text = "终点 / 秒", Bounds = new Rectangle(214, 56, 74, 22) };
        start.SetBounds(96, 49, 100, 30);
        end.SetBounds(292, 49, 100, 30);
        start.BorderStyle = end.BorderStyle = BorderStyle.FixedSingle;
        startHere.AutoSize = endHere.AutoSize = reset.AutoSize = false;
        startHere.Text = "当前帧 → 起点"; endHere.Text = "当前帧 → 终点";
        startHere.SetBounds(410, 45, 134, 36);
        endHere.SetBounds(554, 45, 134, 36);
        reset.SetBounds(698, 45, 142, 36);
        foreach (var button in new[] { startHere, endHere, reset }) BrandTheme.StyleButton(button);
        selectionCard.Controls.AddRange([startLabel, start, endLabel, end, startHere, endHere, reset]);
        root.Controls.Add(selectionCard, 0, 5);
        format.Items.AddRange(["MP4 · H.264", "WMV · Windows Media"]);
        format.SelectedIndex = 0;
        format.Width = 194;
        format.FlatStyle = FlatStyle.Flat;
        format.BackColor = BrandTheme.Background;
        var actionCard = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), BackColor = BrandTheme.Surface, Padding = new Padding(20, 14, 14, 10) };
        var formatLabel = new Label { Text = "导出格式", Bounds = new Rectangle(20, 24, 80, 24) };
        format.Location = new Point(110, 19);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 458, WrapContents = false, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
        cancel.Size = new Size(100, 38); openFolder.Size = new Size(156, 38);
        foreach (var button in new[] { cancel, openFolder }) BrandTheme.StyleButton(button);
        BrandTheme.StyleButton(export, true);
        foreach (var button in new[] { openFolder, cancel, export }) { button.Margin = new Padding(6, 0, 0, 0); actions.Controls.Add(button); }
        actionCard.Controls.AddRange([formatLabel, format, actions]);
        root.Controls.Add(actionCard, 0, 6);
        progress.ForeColor = BrandTheme.Accent;
        progress.BackColor = BrandTheme.AccentPale;
        status.ForeColor = BrandTheme.Muted;
        root.Controls.Add(progress, 0, 7);
        root.Controls.Add(status, 0, 8);
        Controls.Add(root);

        browse.Click += async (_, _) => await ChooseInputAsync();
        timeline.ValueChanged += (_, _) =>
        {
            RefreshCursorTime();
            previewTimer.Stop();
            previewTimer.Start();
        };
        previewTimer.Tick += async (_, _) => { previewTimer.Stop(); await RefreshPreviewAsync(); };
        start.ValueChanged += (_, _) => RefreshSelection();
        end.ValueChanged += (_, _) => RefreshSelection();
        startHere.Click += (_, _) => { if (metadata is not null) start.Value = Math.Clamp((decimal)CursorSeconds, start.Minimum, start.Maximum); };
        endHere.Click += (_, _) => { if (metadata is not null) end.Value = Math.Clamp((decimal)CursorSeconds, end.Minimum, end.Maximum); };
        reset.Click += (_, _) => { if (metadata is not null) { start.Value = 0; end.Value = (decimal)metadata.DurationSeconds; } };
        export.Click += async (_, _) => await ExportAsync();
        cancel.Click += (_, _) => { operationCancellation?.Cancel(); cancel.Enabled = false; status.Text = "正在取消导出…"; };
        openFolder.Click += (_, _) =>
        {
            if (lastOutput is null) return;
            try { Process.Start(new ProcessStartInfo(Path.GetDirectoryName(lastOutput)!) { UseShellExecute = true }); }
            catch (Exception ex) { ShowFailure(ex); }
        };
        Shown += async (_, _) => { if (!string.IsNullOrWhiteSpace(initialPath)) await LoadInputAsync(initialPath); };
        FormClosing += (_, e) =>
        {
            if (exporting) { e.Cancel = true; operationCancellation?.Cancel(); status.Text = "正在取消导出，请稍后关闭。"; }
        };
        SetEnabledState();
    }

    private double CursorSeconds => metadata is null ? 0 : timeline.Value / 1000d * metadata.DurationSeconds;
    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds / 10:00}";
    }
    private void RefreshCursorTime() => cursorTime.Text = $"{FormatTime(CursorSeconds)} / {FormatTime(metadata?.DurationSeconds ?? 0)}";
    private void RefreshSelection()
    {
        if (initializingValues) return;
        var duration = (double)(end.Value - start.Value);
        selectedDuration.Text = duration > 0 ? "保留 " + FormatTime(duration) : "起点必须早于终点";
        selectedDuration.ForeColor = duration > 0 ? BrandTheme.Accent : BrandTheme.Coral;
        SetEnabledState();
    }
    private void SetEnabledState()
    {
        var ready = metadata is not null && !operationRunning;
        browse.Enabled = !operationRunning;
        timeline.Enabled = ready;
        start.Enabled = end.Enabled = startHere.Enabled = endHere.Enabled = reset.Enabled = format.Enabled = ready;
        export.Enabled = ready && end.Value > start.Value;
        cancel.Enabled = exporting;
        openFolder.Enabled = lastOutput is not null && !operationRunning;
    }

    private async Task ChooseInputAsync()
    {
        using var dialog = new OpenFileDialog { Title = "打开需要剪辑的视频", Filter = "视频文件|*.mp4;*.wmv;*.mkv;*.mov;*.avi;*.webm;*.m4v;*.ts|所有文件|*.*" };
        if (dialog.ShowDialog(this) == DialogResult.OK) await LoadInputAsync(dialog.FileName);
    }
    private async Task LoadInputAsync(string path)
    {
        operationRunning = true;
        metadata = null;
        operationCancellation?.Cancel();
        operationCancellation?.Dispose();
        operationCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        previewCancellation?.Cancel();
        SetEnabledState();
        status.Text = "正在读取视频信息…";
        try
        {
            var info = await VideoTrimService.InspectAsync(path, configuredFFmpeg, operationCancellation.Token);
            if (IsDisposed) return;
            metadata = info;
            filePath.Text = Path.GetFullPath(path);
            information.Text = $"{info.Width} × {info.Height}  ·  {info.VideoCodec.ToUpperInvariant()}  ·  {FormatTime(info.DurationSeconds)}  ·  {(info.HasAudio ? "包含声音，导出时保留" : "无声视频")}";
            initializingValues = true;
            start.Value = 0;
            end.Value = 0;
            start.Maximum = end.Maximum = (decimal)info.DurationSeconds;
            end.Value = (decimal)info.DurationSeconds;
            initializingValues = false;
            timeline.Value = 0;
            RefreshCursorTime();
            status.Text = "拖动时间条预览画面，再设置起点和终点。";
            await RefreshPreviewAsync();
        }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = "读取视频已取消或超时。"; }
        catch (Exception ex) { if (!IsDisposed) ShowFailure(ex); }
        finally
        {
            operationRunning = false;
            initializingValues = false;
            if (!IsDisposed) RefreshSelection();
        }
    }

    private async Task RefreshPreviewAsync()
    {
        if (metadata is null || exporting || IsDisposed) return;
        previewCancellation?.Cancel();
        previewCancellation?.Dispose();
        var cancellation = previewCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var seconds = Math.Min(CursorSeconds, Math.Max(0, metadata.DurationSeconds - .1));
            var bitmap = await VideoTrimService.GetPreviewAsync(filePath.Text, seconds, configuredFFmpeg, cancellation.Token);
            if (IsDisposed || cancellation.IsCancellationRequested) { bitmap.Dispose(); return; }
            var previous = preview.Image;
            preview.Image = bitmap;
            previous?.Dispose();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed && !cancellation.IsCancellationRequested) status.Text = ex.Message; }
    }

    private async Task ExportAsync()
    {
        if (metadata is null || operationRunning) return;
        var extension = format.SelectedIndex == 0 ? "mp4" : "wmv";
        using var dialog = new SaveFileDialog
        {
            Title = "导出剪辑", Filter = extension == "mp4" ? "MP4 视频|*.mp4" : "Windows Media 视频|*.wmv",
            AddExtension = true, DefaultExt = extension, OverwritePrompt = true,
            FileName = Path.GetFileNameWithoutExtension(filePath.Text) + "_剪辑." + extension,
            InitialDirectory = Path.GetDirectoryName(filePath.Text)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        operationRunning = exporting = true;
        previewCancellation?.Cancel();
        operationCancellation?.Dispose();
        operationCancellation = new CancellationTokenSource();
        SetEnabledState();
        progress.Value = 0;
        status.Text = "正在导出… 0%";
        try
        {
            var updates = new Progress<double>(percent =>
            {
                if (IsDisposed || !exporting) return;
                progress.Value = Math.Clamp((int)(percent * 10), 0, 1000);
                status.Text = $"正在导出… {percent:0}%";
            });
            lastOutput = await VideoTrimService.ExportAsync(filePath.Text, dialog.FileName, (double)start.Value, (double)end.Value,
                configuredFFmpeg, updates, operationCancellation.Token);
            status.Text = "已导出：" + lastOutput;
            progress.Value = 1000;
        }
        catch (OperationCanceledException) { status.Text = "导出已取消。原视频保留，可调整范围后重试。"; progress.Value = 0; }
        catch (Exception ex) { ShowFailure(ex); }
        finally { operationRunning = exporting = false; SetEnabledState(); }
    }

    private void ShowFailure(Exception exception)
    {
        status.Text = exception.Message;
        MessageBox.Show(this, exception.Message, "视频剪辑", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            previewTimer.Dispose();
            previewCancellation?.Cancel();
            previewCancellation?.Dispose();
            operationCancellation?.Cancel();
            operationCancellation?.Dispose();
            preview.Image?.Dispose();
            brandMark?.Dispose();
        }
        base.Dispose(disposing);
    }
}
