namespace Framelet;

internal sealed class ScrollingCaptureOptions : Form
{
    internal CaptureMode SelectionMode { get; private set; } = CaptureMode.Rectangle;

    internal ScrollingCaptureOptions()
    {
        BrandTheme.Apply(this);
        Text = BrandTheme.Name + " · 自动滚动捕捉";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var layout = new TableLayoutPanel { AutoSize = true, Padding = new Padding(20), ColumnCount = 1 };
        layout.Controls.Add(new Label
        {
            Text = "选择捕捉范围，随后自动向下滚动并拼接。\n请先滚到需要的起点。框选时让中心落在正文；点选屏幕时点击需要滚动的内容。",
            AutoSize = true, MaximumSize = new Size(430, 0), Margin = new Padding(0, 0, 0, 16)
        });
        var regionButton = new Button { Text = "框选滚动区域", AutoSize = true, MinimumSize = new Size(430, 42) };
        var screenButton = new Button { Text = "点选整个屏幕", AutoSize = true, MinimumSize = new Size(430, 42) };
        BrandTheme.StyleButton(regionButton, true);
        BrandTheme.StyleButton(screenButton);
        regionButton.Click += (_, _) => SelectMode(CaptureMode.Rectangle);
        screenButton.Click += (_, _) => SelectMode(CaptureMode.FullScreen);
        layout.Controls.Add(regionButton);
        layout.Controls.Add(screenButton);
        layout.Controls.Add(new Label
        {
            Text = "F8 暂停 / 继续 · F9 完成 · Esc 取消\n框选正文通常更准确；悬浮视频、动态页面建议先暂停。",
            AutoSize = true, ForeColor = BrandTheme.Muted, Margin = new Padding(0, 14, 0, 0)
        });
        Controls.Add(layout);
        AcceptButton = regionButton;
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keys)
    {
        if (keys == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
        return base.ProcessCmdKey(ref message, keys);
    }

    private void SelectMode(CaptureMode mode)
    {
        SelectionMode = mode;
        DialogResult = DialogResult.OK;
    }
}
