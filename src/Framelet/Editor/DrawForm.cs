namespace Framelet;

internal sealed class DrawForm : Form
{
    private readonly EditorDocument document;
    private readonly ImageCanvas canvas;
    private readonly List<Image> icons = [];
    private readonly Dictionary<AnnotationTool, Button> toolButtons = [];
    private readonly Label hint;
    private readonly Button undo;
    private readonly Button redo;
    private readonly Label zoomLabel;
    public Bitmap EditedImage => document.Image;
    public bool HasChanges => document.CanUndo;

    public DrawForm(Bitmap image)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        BrandTheme.Apply(this);
        Text = "标注画室 · " + BrandTheme.Name;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1200, 820); MinimumSize = new Size(760, 530);
        BackColor = BrandTheme.Background;
        KeyPreview = true;
        document = new EditorDocument(image);
        canvas = new ImageCanvas { Dock = DockStyle.Fill, Document = document, CenterVertically = true, StrokeColor = BrandTheme.Coral, Margin = Padding.Empty };
        canvas.RequestText = () => EditorDialogs.Text(this, "添加文字", "输入文字（支持换行）：");
        canvas.OperationFailed += ex => MessageBox.Show(this, ex.Message, "绘图", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 208));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        Controls.Add(layout);
        var header = new TableLayoutPanel { Name = "FrameletHeader", Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(20, 10, 20, 10), ColumnCount = 2, RowCount = 1, BackColor = BrandTheme.Background };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var identity = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 8, 0, 0) };
        var mark = BrandTheme.CreateMark(28); icons.Add(mark);
        identity.Controls.Add(new PictureBox { Image = mark, Size = new Size(28, 28), Margin = new Padding(0, 0, 9, 0) });
        identity.Controls.Add(new Label { Text = BrandTheme.Wordmark, Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = BrandTheme.Ink, AutoSize = true, Margin = new Padding(0, 2, 0, 0) });
        header.Controls.Add(identity, 0, 0);
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heading.Controls.Add(new Label { Text = "标注画室", Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold), ForeColor = BrandTheme.Ink, Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
        heading.Controls.Add(new Label { Text = "让重点清晰可见。完成后，返回图像工作台。", ForeColor = BrandTheme.Muted, Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
        header.Controls.Add(heading, 1, 0); layout.Controls.Add(header, 0, 0); layout.SetColumnSpan(header, 2);
        var palette = new FlowLayoutPanel { Name = "DrawingTools", Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(10, 8, 8, 0), WrapContents = true, BackColor = BrandTheme.Surface, AutoScroll = true };
        layout.Controls.Add(palette, 0, 1); layout.Controls.Add(canvas, 1, 1);
        var tooltip = new ToolTip { AutoPopDelay = 7000 };
        var definitions = new (AnnotationTool Tool, string Icon, string Name)[]
        {
            (AnnotationTool.Select, "select", "选择"), (AnnotationTool.Arrow, "arrow", "箭头"),
            (AnnotationTool.Text, "text", "文字"), (AnnotationTool.Number, "number", "序号"),
            (AnnotationTool.Pen, "pen", "画笔"), (AnnotationTool.Line, "line", "直线"),
            (AnnotationTool.Rectangle, "rectangle", "矩形"), (AnnotationTool.Ellipse, "ellipse", "椭圆"),
            (AnnotationTool.Highlight, "highlight", "高亮"), (AnnotationTool.Mosaic, "mosaic", "马赛克")
        };
        foreach (var definition in definitions)
        {
            if (definition.Tool == AnnotationTool.Select) AddGroup("选择与文字");
            if (definition.Tool == AnnotationTool.Pen) AddGroup("线条与形状");
            if (definition.Tool == AnnotationTool.Highlight) AddGroup("强调与遮挡");
            var icon = EditorIcons.Make(definition.Icon, 20); icons.Add(icon);
            var button = new Button { Text = definition.Name, Image = icon, TextImageRelation = TextImageRelation.ImageBeforeText,
                AutoSize = true, MinimumSize = new Size(82, 42), Margin = new Padding(2, 3, 2, 3), Tag = definition.Tool, AccessibleName = definition.Name };
            BrandTheme.StyleButton(button); button.Padding = new Padding(4, 0, 4, 0);
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => SetTool(definition.Tool);
            tooltip.SetToolTip(button, definition.Name);
            toolButtons.Add(definition.Tool, button); palette.Controls.Add(button);
        }
        var divider = new Label { Height = 1, Width = 160, BackColor = BrandTheme.Border, Margin = new Padding(2, 20, 2, 12) };
        palette.Controls.Add(divider);
        undo = SmallButton("undo", "撤销", () => document.Undo());
        redo = SmallButton("redo", "重做", () => document.Redo());
        palette.Controls.Add(undo); palette.Controls.Add(redo);
        tooltip.SetToolTip(undo, "撤销 Ctrl+Z"); tooltip.SetToolTip(redo, "重做 Ctrl+Y");
        var actual = new Button { Text = "实际大小 · 100%", AutoSize = true, MinimumSize = new Size(160, 34), Margin = new Padding(2, 12, 2, 2) };
        BrandTheme.StyleButton(actual); actual.Click += (_, _) => canvas.Zoom = 1; palette.Controls.Add(actual);
        var fit = new Button { Text = "适合画布", AutoSize = true, MinimumSize = new Size(160, 34), Margin = new Padding(2, 2, 2, 2) };
        BrandTheme.StyleButton(fit); fit.Click += (_, _) => canvas.Fit(); palette.Controls.Add(fit);
        zoomLabel = new Label { Text = "实际大小 · 100%", TextAlign = ContentAlignment.MiddleCenter, Size = new Size(160, 27), ForeColor = BrandTheme.Muted };
        palette.Controls.Add(zoomLabel);

        var bottom = new TableLayoutPanel { Name = "DrawingFooter", Dock = DockStyle.Fill, BackColor = BrandTheme.Surface,
            Padding = new Padding(14, 8, 14, 8), Margin = Padding.Empty, ColumnCount = 2, RowCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 204));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(bottom, 0, 2); layout.SetColumnSpan(bottom, 2);
        hint = new Label { Name = "DrawingHint", Text = "", Dock = DockStyle.Fill, Margin = Padding.Empty,
            AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = BrandTheme.Muted };
        bottom.Controls.Add(hint, 0, 0); bottom.SetColumnSpan(hint, 2);
        var options = new FlowLayoutPanel { Name = "DrawingOptions", Dock = DockStyle.Fill, Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0), WrapContents = true };
        bottom.Controls.Add(options, 0, 1);
        options.Controls.Add(new Label { Text = "颜色", AutoSize = true, Margin = new Padding(0, 8, 7, 0) });
        foreach (var color in new[] { BrandTheme.Coral, BrandTheme.Accent, Color.FromArgb(64, 119, 177), Color.FromArgb(232, 192, 88), BrandTheme.Ink, Color.White })
        {
            var swatch = new Button { BackColor = color, Size = new Size(24, 26), Margin = new Padding(2, 2, 2, 2), FlatStyle = FlatStyle.Flat, AccessibleName = "颜色 " + color.Name };
            swatch.FlatAppearance.BorderColor = BrandTheme.Border; swatch.Click += (_, _) => canvas.StrokeColor = color;
            options.Controls.Add(swatch);
        }
        var custom = new Button { Text = "更多…", AutoSize = true, MinimumSize = new Size(66, 32), Margin = new Padding(5, 1, 12, 0) };
        BrandTheme.StyleButton(custom);
        custom.Click += (_, _) => { using var dialog = new ColorDialog { Color = canvas.StrokeColor, FullOpen = true }; if (dialog.ShowDialog(this) == DialogResult.OK) canvas.StrokeColor = dialog.Color; };
        options.Controls.Add(custom);
        options.Controls.Add(new Label { Text = "线宽", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        var width = new NumericUpDown { Minimum = 1, Maximum = 30, Value = 3, Width = 54, Margin = new Padding(2, 3, 12, 0) };
        width.ValueChanged += (_, _) => canvas.StrokeWidth = (float)width.Value; options.Controls.Add(width);
        options.Controls.Add(new Label { Text = "字号", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        var fontSize = new NumericUpDown { Minimum = 8, Maximum = 150, Value = 24, Width = 58, Margin = new Padding(2, 3, 12, 0) };
        fontSize.ValueChanged += (_, _) => canvas.TextSize = (float)fontSize.Value; options.Controls.Add(fontSize);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4, 4, 0, 0) };
        bottom.Controls.Add(actions, 1, 1);
        var ok = new Button { Text = "完成标注", DialogResult = DialogResult.OK, Size = new Size(100, 36), Margin = new Padding(3, 1, 5, 0) };
        BrandTheme.StyleButton(ok, true);
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Size = new Size(80, 36), Margin = new Padding(3, 1, 3, 0) };
        BrandTheme.StyleButton(cancel);
        actions.Controls.Add(ok); actions.Controls.Add(cancel);
        CancelButton = cancel;
        document.Changed += (_, _) => { undo.Enabled = document.CanUndo; redo.Enabled = document.CanRedo; };
        canvas.ZoomChanged += (_, _) => zoomLabel.Text = canvas.Zoom == 1 ? "实际大小 · 100%" : $"缩放 {canvas.Zoom:P0}";
        undo.Enabled = redo.Enabled = false;
        SetTool(AnnotationTool.Arrow);
        Shown += (_, _) => canvas.Focus();
        FormClosed += (_, _) => tooltip.Dispose();
        ResumeLayout(true);

        void AddGroup(string title)
        {
            palette.Controls.Add(new Label { Text = title, Width = 160, Height = 24, ForeColor = BrandTheme.Muted,
                Margin = new Padding(5, 10, 0, 2), TextAlign = ContentAlignment.MiddleLeft });
        }
    }

    private Button SmallButton(string iconName, string name, Action action)
    {
        var icon = EditorIcons.Make(iconName, 18); icons.Add(icon);
        var button = new Button { Image = icon, Text = name, TextImageRelation = TextImageRelation.ImageBeforeText, AccessibleName = name, Size = new Size(78, 32), Margin = new Padding(2, 2, 2, 2) };
        BrandTheme.StyleButton(button); button.Padding = Padding.Empty;
        button.FlatAppearance.BorderSize = 0; button.Click += (_, _) => action(); return button;
    }

    private void SetTool(AnnotationTool tool)
    {
        canvas.Tool = tool; canvas.ClearSelection();
        foreach (var pair in toolButtons)
        {
            pair.Value.BackColor = pair.Key == tool ? BrandTheme.AccentPale : BrandTheme.Surface;
            pair.Value.ForeColor = pair.Key == tool ? BrandTheme.Accent : BrandTheme.Ink;
            pair.Value.FlatAppearance.BorderSize = pair.Key == tool ? 1 : 0;
            pair.Value.FlatAppearance.BorderColor = BrandTheme.Accent;
        }
        string action = tool switch
        {
            AnnotationTool.Text => "文字：单击图像添加文字",
            AnnotationTool.Number => "编号：单击图像连续添加编号",
            AnnotationTool.Select => "矩形选择：拖动选择区域",
            AnnotationTool.Mosaic => "马赛克：拖动覆盖要遮挡的区域",
            AnnotationTool.Highlight => "高亮：拖动框选需要强调的内容",
            AnnotationTool.Pen => "自由画笔：按住鼠标左键绘制",
            _ => "拖动绘制；按住 Shift 绘制正方形或正圆"
        };
        hint.Text = action + "    ·    Ctrl + 滚轮缩放，中键拖动平移";
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z)) { document.Undo(); return true; }
        if (keyData == (Keys.Control | Keys.Y)) { document.Redo(); return true; }
        if (keyData == (Keys.Control | Keys.D0)) { canvas.Zoom = 1; return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { canvas.Document = null; document.Dispose(); foreach (var icon in icons) icon.Dispose(); }
        base.Dispose(disposing);
    }
}
