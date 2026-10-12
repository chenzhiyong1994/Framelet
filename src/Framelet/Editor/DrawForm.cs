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
    private readonly ToolTip tooltip;
    public Bitmap EditedImage => document.Image;
    public bool HasChanges => document.CanUndo;

    public DrawForm(Bitmap image)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        BrandTheme.Apply(this);
        Text = "绘图与标注 · " + BrandTheme.Name;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1200, 820); MinimumSize = new Size(760, 530);
        BackColor = BrandTheme.Background;
        KeyPreview = true;
        document = new EditorDocument(image);
        canvas = new ImageCanvas { Dock = DockStyle.Fill, Document = document, CenterVertically = true, StrokeColor = BrandTheme.Coral, Margin = Padding.Empty };
        canvas.RequestText = () => EditorDialogs.Text(this, "添加文字", "输入文字（支持换行）：");
        canvas.OperationFailed += ex => MessageBox.Show(this, ex.Message, "绘图", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        var layout = new TableLayoutPanel { Name = "DrawingLayout", Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        var palette = new TableLayoutPanel { Name = "DrawingTools", Dock = DockStyle.Fill, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 10,
            Margin = Padding.Empty, Padding = new Padding(3), BackColor = BrandTheme.Surface };
        palette.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        palette.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int row = 0; row < 9; row++) palette.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        palette.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(palette, 0, 0); layout.Controls.Add(canvas, 1, 0);
        tooltip = new ToolTip { AutoPopDelay = 7000, ShowAlways = true };
        var definitions = new (AnnotationTool Tool, string Icon, string Name)[]
        {
            (AnnotationTool.Select, "select", "选择"), (AnnotationTool.Arrow, "arrow", "箭头"),
            (AnnotationTool.Text, "text", "文字"), (AnnotationTool.Number, "number", "序号"),
            (AnnotationTool.Pen, "pen", "画笔"), (AnnotationTool.Line, "line", "直线"),
            (AnnotationTool.Rectangle, "rectangle", "矩形"), (AnnotationTool.Ellipse, "ellipse", "椭圆"),
            (AnnotationTool.Highlight, "highlight", "高亮"), (AnnotationTool.Mosaic, "mosaic", "马赛克")
        };
        for (int index = 0; index < definitions.Length; index++)
        {
            var definition = definitions[index];
            var button = SmallButton(definition.Icon, definition.Name, () => SetTool(definition.Tool));
            button.Tag = definition.Tool;
            tooltip.SetToolTip(button, definition.Name);
            toolButtons.Add(definition.Tool, button); palette.Controls.Add(button, index % 2, index / 2);
        }
        var divider = new Panel { Size = new Size(1, 1), Dock = DockStyle.Fill, BackColor = BrandTheme.Border, Margin = new Padding(2, 7, 2, 7) };
        palette.Controls.Add(divider, 0, 5); palette.SetColumnSpan(divider, 2);
        undo = SmallButton("undo", "撤销", () => document.Undo());
        redo = SmallButton("redo", "重做", () => document.Redo());
        palette.Controls.Add(undo, 0, 6); palette.Controls.Add(redo, 1, 6);
        tooltip.SetToolTip(undo, "撤销 (Ctrl+Z)"); tooltip.SetToolTip(redo, "重做 (Ctrl+Y)");
        var zoomIn = SmallButton("zoomIn", "放大", () => canvas.Zoom *= 1.25f);
        var zoomOut = SmallButton("zoomOut", "缩小", () => canvas.Zoom *= .8f);
        var actual = SmallButton("100", "实际大小", () => canvas.Zoom = 1);
        var fit = SmallButton("zoom", "适合画布", () => canvas.Fit());
        tooltip.SetToolTip(actual, "实际大小 · 100% (Ctrl+0)"); tooltip.SetToolTip(fit, "适合画布");
        tooltip.SetToolTip(zoomIn, "放大图像"); tooltip.SetToolTip(zoomOut, "缩小图像");
        palette.Controls.Add(zoomIn, 0, 7); palette.Controls.Add(actual, 1, 7);
        palette.Controls.Add(zoomOut, 0, 8); palette.Controls.Add(fit, 1, 8);

        var bottom = new TableLayoutPanel { Name = "DrawingFooter", Dock = DockStyle.Fill, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = BrandTheme.Surface,
            Padding = new Padding(8, 5, 8, 4), Margin = Padding.Empty, ColumnCount = 2, RowCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(bottom, 0, 1); layout.SetColumnSpan(bottom, 2);
        var options = new FlowLayoutPanel { Name = "DrawingOptions", Dock = DockStyle.Fill, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, WrapContents = true };
        bottom.Controls.Add(options, 0, 0);
        options.Controls.Add(new Label { Text = "颜色", AutoSize = true, Margin = new Padding(0, 6, 5, 0) });
        foreach (var color in new[] { BrandTheme.Coral, BrandTheme.Accent, Color.FromArgb(64, 119, 177), Color.FromArgb(232, 192, 88), BrandTheme.Ink, Color.White })
        {
            var swatch = new Button { BackColor = color, Size = new Size(22, 26), Margin = new Padding(1, 2, 1, 2),
                FlatStyle = FlatStyle.Flat, AccessibleName = "颜色 " + color.Name };
            swatch.FlatAppearance.BorderColor = BrandTheme.Border; swatch.Click += (_, _) => canvas.StrokeColor = color;
            tooltip.SetToolTip(swatch, "颜色 " + color.Name); options.Controls.Add(swatch);
        }
        var custom = new Button { Text = "更多…", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(58, 30), Margin = new Padding(5, 0, 9, 0), AccessibleName = "更多颜色" };
        BrandTheme.StyleButton(custom);
        custom.Click += (_, _) => { using var dialog = new ColorDialog { Color = canvas.StrokeColor, FullOpen = true }; if (dialog.ShowDialog(this) == DialogResult.OK) canvas.StrokeColor = dialog.Color; };
        tooltip.SetToolTip(custom, "选择更多颜色"); options.Controls.Add(custom);
        options.Controls.Add(new Label { Text = "线宽", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        var width = new NumericUpDown { Minimum = 1, Maximum = 30, Value = 3, Width = 48,
            Margin = new Padding(0, 3, 9, 0), AccessibleName = "线宽" };
        width.ValueChanged += (_, _) => canvas.StrokeWidth = (float)width.Value; options.Controls.Add(width);
        options.Controls.Add(new Label { Text = "字号", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        var fontSize = new NumericUpDown { Minimum = 8, Maximum = 150, Value = 24, Width = 52,
            Margin = new Padding(0, 3, 6, 0), AccessibleName = "字号" };
        fontSize.ValueChanged += (_, _) => canvas.TextSize = (float)fontSize.Value; options.Controls.Add(fontSize);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        bottom.Controls.Add(actions, 1, 0);
        var ok = new Button { Text = "完成", DialogResult = DialogResult.OK, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(76, 30), Margin = new Padding(4, 0, 4, 0), AccessibleName = "完成标注" };
        BrandTheme.StyleButton(ok, true);
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(76, 30), Margin = new Padding(4, 0, 0, 0), AccessibleName = "取消标注" };
        BrandTheme.StyleButton(cancel);
        tooltip.SetToolTip(ok, "完成标注并返回编辑器"); tooltip.SetToolTip(cancel, "放弃本次标注 (Esc)");
        actions.Controls.Add(ok); actions.Controls.Add(cancel);
        hint = new Label { Name = "DrawingHint", Text = "", Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 5, 6, 0),
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = BrandTheme.Muted };
        bottom.Controls.Add(hint, 0, 1);
        zoomLabel = new Label { Text = "实际大小 · 100%", AutoSize = true, TextAlign = ContentAlignment.MiddleRight,
            Dock = DockStyle.Fill, ForeColor = BrandTheme.Muted, Margin = new Padding(0, 5, 0, 0) };
        bottom.Controls.Add(zoomLabel, 1, 1);
        CancelButton = cancel;
        document.Changed += (_, _) => { undo.Enabled = document.CanUndo; redo.Enabled = document.CanRedo; };
        canvas.ZoomChanged += (_, _) => zoomLabel.Text = canvas.Zoom == 1 ? "实际大小 · 100%" : $"缩放 {canvas.Zoom:P0}";
        undo.Enabled = redo.Enabled = false;
        SetTool(AnnotationTool.Arrow);
        Shown += (_, _) => canvas.Focus();
        ResumeLayout(true);
    }

    private Button SmallButton(string iconName, string name, Action action)
    {
        var icon = EditorIcons.Make(iconName, 20); icons.Add(icon);
        var button = new Button { Image = icon, Text = "", AccessibleName = name, Size = new Size(32, 32),
            Margin = new Padding(1), FlatStyle = FlatStyle.Flat, TabStop = true };
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
        if (disposing) { tooltip.Dispose(); canvas.Document = null; document.Dispose(); foreach (var icon in icons) icon.Dispose(); }
        base.Dispose(disposing);
    }
}
