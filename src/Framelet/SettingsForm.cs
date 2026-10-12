namespace Framelet;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings settings;
    private readonly CheckBox cursor = Check("包含鼠标指针"), copy = Check("捕捉完成后自动复制到剪贴板"), timestamp = Check("自动添加日期时间戳");
    private readonly CheckBox topmost = Check("悬浮工具栏置顶"), hide = Check("捕捉时隐藏悬浮工具栏"), hotkeys = Check("启用全局快捷键"), alternate = Check("使用 Ctrl + Shift + 数字 1–7（减少快捷键冲突）");
    private readonly NumericUpDown delay = Number(0, 30), fixedWidth = Number(1, 20000), fixedHeight = Number(1, 20000);
    private readonly TextBox folder = new() { Width = 390 };
    private readonly ComboBox format = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly ComboBox target = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private Image? brandMark;

    public SettingsForm(AppSettings settings, IReadOnlyList<string> conflicts)
    {
        this.settings = settings;
        BrandTheme.Apply(this);
        Text = BrandTheme.Name + " · 设置"; StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(840, 568);
        brandMark = BrandTheme.CreateMark(42);
        Controls.Add(new PictureBox { Image = brandMark, SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(22, 28, 42, 42) });
        Controls.Add(new Label { Text = BrandTheme.Wordmark, Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = BrandTheme.Accent, Bounds = new Rectangle(78, 20, 220, 23) });
        Controls.Add(new Label { Text = "按你的习惯，轻松捕捉", Font = new Font(Font.FontFamily, 19F, FontStyle.Bold), Bounds = new Rectangle(76, 43, 650, 40) });
        var navigation = new Panel { Bounds = new Rectangle(20, 102, 160, 394), BackColor = BrandTheme.Background };
        navigation.Controls.Add(new Label { Text = "偏好设置", ForeColor = BrandTheme.Muted, Bounds = new Rectangle(12, 10, 132, 24) });
        navigation.Controls.Add(new Label { Text = "只需设置一次，\n每次捕捉都顺手。", ForeColor = BrandTheme.Muted, Bounds = new Rectangle(12, 306, 136, 60) });
        Controls.Add(navigation);
        var capturePage = Page("捕捉", "选择需要记录的内容与捕捉节奏。");
        var savePage = Page("文件输出", "给每张截图一个合适的去处。");
        var keysPage = Page("快捷键", "双手留在键盘上，也能快速完成捕捉。");
        var panelPage = Page("工具栏", "让帧简停在你顺手的位置。");
        var pages = new[] { capturePage, savePage, keysPage, panelPage };
        var navigationButtons = new List<Button>();
        for (int index = 0; index < pages.Length; index++)
        {
            int selected = index;
            var button = new Button { Text = $"0{index + 1}   {pages[index].Name}", Bounds = new Rectangle(0, 50 + index * 48, 160, 40), TextAlign = ContentAlignment.MiddleLeft, TabIndex = index, Tag = pages[index] };
            BrandTheme.StyleButton(button);
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => SelectPage(selected);
            navigationButtons.Add(button);
            navigation.Controls.Add(button);
        }
        void SelectPage(int selected)
        {
            for (int index = 0; index < pages.Length; index++)
            {
                pages[index].Visible = index == selected;
                navigationButtons[index].BackColor = index == selected ? BrandTheme.AccentPale : BrandTheme.Background;
                navigationButtons[index].ForeColor = index == selected ? BrandTheme.Accent : BrandTheme.Ink;
                navigationButtons[index].AccessibleDescription = index == selected ? "当前设置页" : "打开设置页";
            }
        }

        Place(capturePage, cursor, 93); Place(capturePage, copy, 131); Place(capturePage, timestamp, 169);
        Label(capturePage, "捕捉前延迟", 22, 219); delay.Location = new Point(214, 213); capturePage.Controls.Add(delay); Label(capturePage, "秒", 335, 219);
        Divider(capturePage, 263);
        Add(capturePage, "固定大小区域", 282, true);
        Label(capturePage, "宽度", 22, 330); fixedWidth.Location = new Point(76, 324); capturePage.Controls.Add(fixedWidth);
        Label(capturePage, "×", 201, 330); fixedHeight.Location = new Point(236, 324); capturePage.Controls.Add(fixedHeight); Label(capturePage, "像素", 360, 330);

        Label(savePage, "输出文件夹", 22, 92); folder.SetBounds(22, 121, 434, 31); folder.BorderStyle = BorderStyle.FixedSingle; savePage.Controls.Add(folder);
        var browse = new Button { Text = "浏览…", Bounds = new Rectangle(470, 115, 130, 36) };
        BrandTheme.StyleButton(browse);
        browse.Click += (_, _) => { using var chooser = new FolderBrowserDialog { Description = "选择截图输出文件夹", UseDescriptionForTitle = true }; if (Directory.Exists(folder.Text)) chooser.InitialDirectory = folder.Text; if (chooser.ShowDialog(this) == DialogResult.OK) folder.Text = chooser.SelectedPath; }; savePage.Controls.Add(browse);
        Label(savePage, "自动保存格式", 22, 191); format.SetBounds(214, 184, 290, 31); format.FlatStyle = FlatStyle.Flat; format.BackColor = BrandTheme.Background; format.Items.AddRange(["PNG · 无损", "JPEG · 照片", "BMP · 位图", "GIF", "TIFF"]); savePage.Controls.Add(format);
        Label(savePage, "截图输出到", 22, 242); target.SetBounds(214, 235, 386, 31); target.FlatStyle = FlatStyle.Flat; target.BackColor = BrandTheme.Background; target.Items.AddRange(["编辑器", "剪贴板", "文件（每次选择路径）", "文件（自动保存）", "编辑器和剪贴板", "打印机"]); savePage.Controls.Add(target);
        Add(savePage, "文件名包含日期、时间和毫秒，重名时自动编号。\n录像文件会在结束录制时选择保存位置。", 306);

        Place(keysPage, hotkeys, 89); Place(keysPage, alternate, 125);
        alternate.Text = "使用 Ctrl + Shift + 数字 1–7";
        var list = new ListView { View = View.Details, FullRowSelect = true, Bounds = new Rectangle(22, 163, 578, 171), HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.FixedSingle, BackColor = BrandTheme.Surface, ForeColor = BrandTheme.Ink };
        list.Columns.Add("动作", 280); list.Columns.Add("默认快捷键", 254);
        foreach (var item in CapturePanel.Captures.Take(7)) list.Items.Add(new ListViewItem([item.Name, item.Shortcut]));
        keysPage.Controls.Add(list);
        Add(keysPage, conflicts.Count > 0 ? $"有 {conflicts.Count} 个热键被占用，可切换为数字组合。" : "Ctrl+Shift+F12 显示工具栏 · Ctrl+Shift+F9 打开录像机", 351);
        Place(panelPage, topmost, 93); Place(panelPage, hide, 131);
        Divider(panelPage, 181);
        Add(panelPage, "顺手的小技巧", 204, true);
        Add(panelPage, "拖动标题区域，可以移动悬浮工具栏。\n点击 − 收起到通知区域，双击托盘图标重新显示。\n将图像文件拖到工具栏，即可打开编辑。\n设置保存在当前 Windows 用户的本地应用数据目录。", 250);
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(568, 514, 112, 38) };
        var save = new Button { Text = "保存设置", Bounds = new Rectangle(692, 514, 128, 38) };
        BrandTheme.StyleButton(cancel); BrandTheme.StyleButton(save, true); save.Click += (_, _) => SaveSettings();
        Controls.AddRange([cancel, save]); AcceptButton = save; CancelButton = cancel;
        cursor.Checked = settings.IncludeCursor; copy.Checked = settings.CopyAfterCapture; timestamp.Checked = settings.Timestamp; topmost.Checked = settings.TopMost; hide.Checked = settings.HideOnCapture; hotkeys.Checked = settings.EnableHotkeys; alternate.Checked = settings.AlternateHotkeys;
        delay.Value = settings.DelaySeconds; fixedWidth.Value = settings.FixedWidth; fixedHeight.Value = settings.FixedHeight; folder.Text = settings.OutputFolder;
        format.SelectedIndex = Array.IndexOf(new[] { "png", "jpg", "bmp", "gif", "tiff" }, settings.OutputFormat); target.SelectedIndex = (int)settings.Output;
        SelectPage(0);
    }
    private void SaveSettings()
    {
        try { if (string.IsNullOrWhiteSpace(folder.Text) || !Path.IsPathFullyQualified(folder.Text)) throw new ArgumentException("请选择有效的绝对文件夹路径。"); _ = Path.GetFullPath(folder.Text); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "检查输出位置", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        settings.IncludeCursor = cursor.Checked; settings.CopyAfterCapture = copy.Checked; settings.Timestamp = timestamp.Checked; settings.TopMost = topmost.Checked; settings.HideOnCapture = hide.Checked; settings.EnableHotkeys = hotkeys.Checked; settings.AlternateHotkeys = alternate.Checked;
        settings.DelaySeconds = (int)delay.Value; settings.FixedWidth = (int)fixedWidth.Value; settings.FixedHeight = (int)fixedHeight.Value;
        settings.OutputFolder = folder.Text.Trim(); settings.OutputFormat = new[] { "png", "jpg", "bmp", "gif", "tiff" }[Math.Max(0, format.SelectedIndex)]; settings.Output = (OutputTarget)Math.Max(0, target.SelectedIndex);
        DialogResult = DialogResult.OK; Close();
    }
    private Panel Page(string title, string description)
    {
        var page = new Panel { Name = title, Bounds = new Rectangle(196, 102, 624, 394), BackColor = BrandTheme.Surface };
        page.Paint += (_, e) => { using var pen = new Pen(BrandTheme.Border); e.Graphics.DrawRectangle(pen, 0, 0, page.Width - 1, page.Height - 1); };
        page.Controls.Add(new Label { Text = title, Font = new Font(Font.FontFamily, 14F, FontStyle.Bold), Bounds = new Rectangle(22, 20, 570, 32) });
        page.Controls.Add(new Label { Text = description, ForeColor = BrandTheme.Muted, Bounds = new Rectangle(22, 57, 578, 26) });
        Controls.Add(page); return page;
    }
    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true };
    private static NumericUpDown Number(int min, int max) => new() { Minimum = min, Maximum = max, Width = 108, BorderStyle = BorderStyle.FixedSingle };
    private static void Place(Control p, Control c, int y) { c.Location = new Point(22, y); p.Controls.Add(c); }
    private static void Label(Control p, string text, int x, int y) => p.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y) });
    private static void Add(Control p, string text, int y, bool strong = false) => p.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(578, 0), Location = new Point(22, y), ForeColor = strong ? BrandTheme.Ink : BrandTheme.Muted, Font = new Font("Microsoft YaHei UI", strong ? 10 : 9, strong ? FontStyle.Bold : FontStyle.Regular) });
    private static void Divider(Control parent, int y) => parent.Controls.Add(new Panel { Bounds = new Rectangle(22, y, 578, 1), BackColor = BrandTheme.Border });
    protected override void Dispose(bool disposing)
    {
        if (disposing) brandMark?.Dispose();
        base.Dispose(disposing);
    }
}
