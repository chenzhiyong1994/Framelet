namespace Framelet;

internal enum RecordingFinishChoice { Save, Discard, Cancel }

internal sealed class RecordingFinishDialog : Form
{
    internal RecordingFinishChoice Choice { get; private set; } = RecordingFinishChoice.Cancel;

    internal RecordingFinishDialog(bool closing)
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        BrandTheme.Apply(this);
        Text = closing ? "退出录像机" : "结束录制";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(20);

        var layout = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty
        };
        layout.Controls.Add(new Label
        {
            Text = "如何处理本次录像？", AutoSize = true,
            Font = new Font(Font.FontFamily, 13, FontStyle.Bold), Margin = new Padding(0, 0, 0, 12)
        });
        layout.Controls.Add(new Label
        {
            Text = "录像已暂停。选择保存后再指定位置。\n放弃会删除本次未保存的录像；返回则保留录像，稍后处理。",
            AutoSize = true, MaximumSize = new Size(430, 0), Margin = new Padding(0, 0, 0, 18)
        });
        var actions = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, Margin = Padding.Empty
        };
        Button Add(string text, RecordingFinishChoice choice)
        {
            var button = new Button
            {
                Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(108, 34), Margin = new Padding(0, 0, 8, 0),
                DialogResult = choice == RecordingFinishChoice.Cancel ? DialogResult.Cancel : DialogResult.OK
            };
            BrandTheme.StyleButton(button, choice == RecordingFinishChoice.Save);
            button.Click += (_, _) => Choice = choice;
            actions.Controls.Add(button);
            return button;
        }
        Add(closing ? "保存并退出" : "保存录像", RecordingFinishChoice.Save);
        Add(closing ? "放弃并退出" : "放弃录像", RecordingFinishChoice.Discard);
        var cancel = Add(closing ? "取消退出" : "返回录制", RecordingFinishChoice.Cancel);
        cancel.Margin = Padding.Empty;
        CancelButton = cancel;
        AcceptButton = cancel;
        layout.Controls.Add(actions);
        Controls.Add(layout);
    }

    internal static RecordingFinishChoice Ask(IWin32Window owner, bool closing)
    {
        using var dialog = new RecordingFinishDialog(closing);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Choice : RecordingFinishChoice.Cancel;
    }
}
