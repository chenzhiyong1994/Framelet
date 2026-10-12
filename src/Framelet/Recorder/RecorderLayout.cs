namespace Framelet;

public sealed partial class RecorderForm
{
    private void BuildRecorderLayout()
    {
        Text = "屏幕录像 - " + BrandTheme.Name;
        BackColor = SystemColors.Control;
        ForeColor = SystemColors.ControlText;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var layout = RecorderTable(1);
        layout.Name = "RecorderLayout";
        layout.MinimumSize = new Size(368, 0);
        layout.Dock = DockStyle.Top;
        // A top-docked panel contributes only height to Form.PreferredSize; keep its readable width explicitly.
        MinimumSize = SizeFromClientSize(new Size(layout.MinimumSize.Width + Padding.Horizontal, 0));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        ConfigureRecorderGroup(captureGroup, "录制范围");
        var captureLayout = RecorderTable(1);
        captureLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fullScreen.Text = "全屏（主显示器）";
        fullScreen.Margin = new Padding(0, 0, 0, 5);
        captureLayout.Controls.Add(fullScreen);

        var regionRow = RecorderTable(2);
        regionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        regionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        region.Text = "矩形区域";
        region.Anchor = AnchorStyles.Left;
        region.Margin = Padding.Empty;
        ConfigureRecorderButton(selectArea, "选择区域…");
        selectArea.Margin = new Padding(8, 0, 0, 0);
        regionRow.Controls.Add(region, 0, 0);
        regionRow.Controls.Add(selectArea, 1, 0);
        captureLayout.Controls.Add(regionRow);

        areaLabel.AutoSize = true;
        areaLabel.AutoEllipsis = false;
        areaLabel.MaximumSize = new Size(342, 0);
        areaLabel.Dock = DockStyle.Fill;
        areaLabel.Margin = new Padding(0, 6, 0, 0);
        areaLabel.TextAlign = ContentAlignment.MiddleLeft;
        areaLabel.ForeColor = SystemColors.GrayText;
        captureLayout.Controls.Add(areaLabel);
        captureGroup.Controls.Add(captureLayout);
        layout.Controls.Add(captureGroup);

        ConfigureRecorderGroup(optionsGroup, "录制选项");
        var optionsLayout = RecorderTable(1);
        optionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var audioRow = RecorderTable(3);
        audioRow.Margin = new Padding(0, 0, 0, 7);
        audioRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        audioRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        audioRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        audioRow.Controls.Add(RecorderFieldLabel("声音"), 0, 0);
        audioDevice.FlatStyle = FlatStyle.Standard;
        audioDevice.Dock = DockStyle.Fill;
        audioDevice.MinimumSize = new Size(118, 0);
        audioDevice.Margin = new Padding(6, 3, 0, 3);
        if (audioDevice.Items.Count == 0) audioDevice.Items.Add("不录制声音");
        audioDevice.SelectedIndex = 0;
        ConfigureRecorderButton(extensionButton, "录音设置…");
        extensionButton.Margin = new Padding(6, 0, 0, 0);
        audioRow.Controls.Add(audioDevice, 1, 0);
        audioRow.Controls.Add(extensionButton, 2, 0);
        optionsLayout.Controls.Add(audioRow);

        var frameRow = new FlowLayoutPanel
        {
            Name = "RecorderFrameOptions", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 0, 0, 7)
        };
        var fpsLabel = RecorderFieldLabel("帧率");
        fpsLabel.Margin = new Padding(0, 4, 6, 0);
        frameRate.MinimumSize = new Size(54, 0);
        frameRate.Width = 54;
        frameRate.Margin = new Padding(0, 0, 4, 0);
        var fpsUnit = RecorderFieldLabel("帧/秒");
        fpsUnit.Margin = new Padding(0, 4, 16, 0);
        includeCursor.Text = "录制鼠标指针";
        includeCursor.Margin = new Padding(0, 2, 0, 0);
        frameRow.Controls.AddRange([fpsLabel, frameRate, fpsUnit, includeCursor]);
        optionsLayout.Controls.Add(frameRow);
        minimize.Text = "录制时收起设置窗口";
        minimize.Margin = Padding.Empty;
        optionsLayout.Controls.Add(minimize);
        optionsGroup.Controls.Add(optionsLayout);
        layout.Controls.Add(optionsGroup);

        var recordingInfo = RecorderTable(2);
        recordingInfo.Name = "RecorderStatus";
        recordingInfo.Margin = new Padding(0, 2, 0, 5);
        recordingInfo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        recordingInfo.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        status.AutoSize = true;
        status.AutoEllipsis = false;
        status.MaximumSize = new Size(270, 0);
        status.Dock = DockStyle.Fill;
        status.Margin = Padding.Empty;
        status.TextAlign = ContentAlignment.MiddleLeft;
        elapsed.AutoSize = true;
        elapsed.Font = new Font("Consolas", 10F, FontStyle.Bold);
        elapsed.Anchor = AnchorStyles.Right;
        elapsed.Margin = new Padding(8, 0, 0, 0);
        recordingInfo.Controls.Add(status, 0, 0);
        recordingInfo.Controls.Add(elapsed, 1, 0);
        layout.Controls.Add(recordingInfo);

        var recordingActions = new FlowLayoutPanel
        {
            Name = "RecorderSessionActions", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 0, 0, 5)
        };
        ConfigureRecorderButton(pauseButton, "暂停");
        ConfigureRecorderButton(stopButton, "结束并保存");
        stopButton.Margin = Padding.Empty;
        pauseButton.Margin = new Padding(0, 0, 6, 0);
        recordingActions.Controls.AddRange([stopButton, pauseButton]);
        layout.Controls.Add(recordingActions);

        var actions = new TableLayoutPanel
        {
            Name = "RecorderMainActions", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 2, 0, 0)
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ConfigureRecorderButton(editButton, "视频剪辑…");
        ConfigureRecorderButton(startButton, "开始录制");
        ConfigureRecorderButton(closeButton, "关闭");
        editButton.Margin = Padding.Empty;
        startButton.Margin = new Padding(8, 0, 6, 0);
        closeButton.Margin = Padding.Empty;
        actions.Controls.Add(editButton, 0, 0);
        actions.Controls.Add(startButton, 2, 0);
        actions.Controls.Add(closeButton, 3, 0);
        layout.Controls.Add(actions);

        hints.AutoSize = true;
        hints.AutoEllipsis = false;
        hints.MaximumSize = new Size(368, 0);
        hints.Dock = DockStyle.Fill;
        hints.ForeColor = SystemColors.GrayText;
        hints.Text = "Ctrl+F10 暂停/继续 · Ctrl+F11 结束并保存";
        hints.Margin = new Padding(0, 8, 0, 0);
        layout.Controls.Add(hints);

        toolTip.SetToolTip(audioDevice, "默认录制无声 MP4。需要录音时，请打开录音设置。");
        toolTip.SetToolTip(extensionButton, "配置可选录音组件与声音设备。无声录屏无需配置。");
        toolTip.SetToolTip(minimize, "开始后收起设置窗口；通过录制控制条或快捷键暂停和结束。");
        toolTip.SetToolTip(editButton, "打开可选的视频剪辑工具。");
        toolTip.SetToolTip(pauseButton, "暂停或继续录制（Ctrl+F10）");
        toolTip.SetToolTip(stopButton, "结束录制并保存视频（Ctrl+F11）");
        AcceptButton = startButton;
    }

    private static TableLayoutPanel RecorderTable(int columns) => new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
        ColumnCount = columns, RowCount = 0, GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        Margin = Padding.Empty, Padding = Padding.Empty
    };

    private static Label RecorderFieldLabel(string text) => new()
    {
        Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static void ConfigureRecorderGroup(GroupBox group, string title)
    {
        group.Text = title;
        group.AutoSize = true;
        group.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        group.Dock = DockStyle.Fill;
        group.Padding = new Padding(10, 7, 10, 10);
        group.Margin = new Padding(0, 0, 0, 9);
    }

    private static void ConfigureRecorderButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new Size(72, 28);
        button.Padding = new Padding(6, 0, 6, 0);
        button.FlatStyle = FlatStyle.System;
        button.UseVisualStyleBackColor = true;
        button.ForeColor = SystemColors.ControlText;
        button.BackColor = SystemColors.Control;
    }
}
