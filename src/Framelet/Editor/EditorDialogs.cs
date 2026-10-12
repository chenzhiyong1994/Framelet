namespace Framelet;

internal static class EditorDialogs
{
    public static string? Text(IWin32Window owner, string title, string label, string initial = "")
    {
        using var form = Base(title, new Size(440, 238));
        var caption = new Label { Text = label, Location = new Point(20, 18), AutoSize = true };
        var input = new TextBox { Text = initial, Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new Point(20, 45), Size = new Size(400, 123), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        form.Controls.Add(caption); form.Controls.Add(input); Buttons(form);
        form.Shown += (_, _) => { input.Focus(); input.SelectAll(); };
        return form.ShowDialog(owner) == DialogResult.OK ? input.Text : null;
    }

    public static Size? Resize(IWin32Window owner, Size original)
    {
        using var form = Base("调整图像大小", new Size(350, 222));
        var width = Number(original.Width, 1, 30000, new Point(115, 24));
        var height = Number(original.Height, 1, 30000, new Point(115, 67));
        var ratio = new CheckBox { Text = "保持纵横比", Checked = true, AutoSize = true, Location = new Point(115, 112) };
        form.Controls.AddRange([new Label { Text = "宽度（像素）", AutoSize = true, Location = new Point(20, 28) }, width,
            new Label { Text = "高度（像素）", AutoSize = true, Location = new Point(20, 71) }, height, ratio]);
        bool updating = false;
        width.ValueChanged += (_, _) =>
        {
            if (updating || !ratio.Checked) return;
            updating = true; height.Value = Math.Clamp(Math.Round(width.Value * original.Height / original.Width), 1, 30000); updating = false;
        };
        height.ValueChanged += (_, _) =>
        {
            if (updating || !ratio.Checked) return;
            updating = true; width.Value = Math.Clamp(Math.Round(height.Value * original.Width / original.Height), 1, 30000); updating = false;
        };
        Buttons(form);
        return form.ShowDialog(owner) == DialogResult.OK ? new Size((int)width.Value, (int)height.Value) : null;
    }

    public static int? Value(IWin32Window owner, string title, string label, int value, int min, int max)
    {
        using var form = Base(title, new Size(350, 155));
        form.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(20, 25) });
        var numeric = Number(value, min, max, new Point(175, 20));
        form.Controls.Add(numeric); Buttons(form);
        return form.ShowDialog(owner) == DialogResult.OK ? (int)numeric.Value : null;
    }

    public static Form Base(string title, Size size)
    {
        var form = new Form
        {
            Text = title + " · " + BrandTheme.ShortName, ClientSize = size, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false
        };
        BrandTheme.Apply(form);
        return form;
    }

    public static NumericUpDown Number(int value, int min, int max, Point location) => new()
    { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Location = location, Width = 130, ThousandsSeparator = true };

    public static void Buttons(Form form)
    {
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Size = new Size(84, 32), Location = new Point(form.ClientSize.Width - 196, form.ClientSize.Height - 49), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Size = new Size(84, 32), Location = new Point(form.ClientSize.Width - 104, form.ClientSize.Height - 49), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        BrandTheme.StyleButton(ok, true); BrandTheme.StyleButton(cancel);
        form.Controls.Add(ok); form.Controls.Add(cancel); form.AcceptButton = ok; form.CancelButton = cancel;
    }
}
