using System.Drawing.Drawing2D;
using System.Drawing.Printing;

namespace Framelet;

public sealed class EditorForm : Form
{
    public event Action<CaptureMode>? CaptureRequested;
    public event Action? SettingsRequested;
    public event Action? RecorderRequested;

    private sealed class ImageTab(EditorDocument document, ImageCanvas canvas, TabPage page, string name)
    {
        public EditorDocument Document { get; } = document;
        public ImageCanvas Canvas { get; } = canvas;
        public TabPage Page { get; } = page;
        public string Name { get; } = name;
    }

    private readonly TabControl tabs;
    private readonly MenuStrip menu;
    private readonly ToolStrip toolbar;
    private readonly ToolStripStatusLabel details;
    private readonly ToolStripStatusLabel message;
    private readonly ToolStripStatusLabel zoomStatus;
    private readonly List<ImageTab> images = [];
    private readonly List<Image> icons = [];
    private readonly List<ToolStripItem> requiresImage = [];
    private readonly ToolStripMenuItem undoMenu;
    private readonly ToolStripMenuItem redoMenu;
    private readonly ToolStripButton cropButton;
    private readonly ToolStripMenuItem cutMenu;
    private readonly ToolStripButton copyAction;
    private readonly ToolStripButton saveAction;
    private bool closing;
    private int imageSequence;

    private ImageTab? Current => images.FirstOrDefault(x => x.Page == tabs.SelectedTab);

    public EditorForm(Bitmap image, string? filePath = null)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        BrandTheme.Apply(this);
        Text = BrandTheme.Name + " · 图像工作台";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 820); MinimumSize = new Size(820, 530);
        BackColor = BrandTheme.Background;
        AllowDrop = true;

        var layout = new TableLayoutPanel { Name = "EditorLayout", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Margin = Padding.Empty, Padding = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        menu = new MenuStrip { Name = "EditorMenu", Dock = DockStyle.Fill, AutoSize = true, BackColor = BrandTheme.Background,
            ForeColor = BrandTheme.Ink, Margin = Padding.Empty, Padding = new Padding(4, 2, 4, 2),
            Renderer = BrandTheme.CreateToolStripRenderer() };
        MainMenuStrip = menu;
        toolbar = new ToolStrip { Name = "EditorToolbar", Dock = DockStyle.Fill, AutoSize = true, GripStyle = ToolStripGripStyle.Hidden,
            CanOverflow = true, ImageScalingSize = new Size(24, 24), Margin = Padding.Empty, Padding = new Padding(4, 3, 4, 3),
            BackColor = BrandTheme.Surface, ForeColor = BrandTheme.Ink, Renderer = BrandTheme.CreateToolStripRenderer() };
        tabs = new TabControl { Name = "EditorTabs", Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Point(16, 4),
            DrawMode = TabDrawMode.OwnerDrawFixed, SizeMode = TabSizeMode.Normal, ItemSize = new Size(150, 28) };
        tabs.DrawItem += DrawTab;
        tabs.MouseDown += TabMouseDown;
        tabs.SelectedIndexChanged += (_, _) => UpdateState();
        var status = new StatusStrip { Dock = DockStyle.Fill, AutoSize = true, SizingGrip = false, BackColor = BrandTheme.Background,
            ForeColor = BrandTheme.Muted, Margin = Padding.Empty, Padding = new Padding(6, 1, 6, 1), Renderer = BrandTheme.CreateToolStripRenderer() };
        details = new ToolStripStatusLabel { BorderSides = ToolStripStatusLabelBorderSides.Right, Padding = new Padding(0, 0, 8, 0) };
        message = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0), ForeColor = BrandTheme.Muted };
        zoomStatus = new ToolStripStatusLabel { IsLink = true, LinkColor = BrandTheme.Accent, ActiveLinkColor = BrandTheme.Ink, ToolTipText = "点击恢复实际大小 · Ctrl+0" };
        zoomStatus.Click += (_, _) => { if (Current != null) Current.Canvas.Zoom = 1; };
        status.Items.AddRange([details, message, zoomStatus]);
        layout.Controls.Add(menu, 0, 0); layout.Controls.Add(toolbar, 0, 1); layout.Controls.Add(tabs, 0, 2); layout.Controls.Add(status, 0, 3);

        var file = TopMenu("文件(&F)");
        var capture = TopMenu("捕捉(&A)");
        var edit = TopMenu("编辑(&E)");
        var imageMenu = TopMenu("图像(&I)");
        var view = TopMenu("查看(&V)");
        var tools = TopMenu("工具(&T)");
        var help = TopMenu("帮助(&H)");
        Item(file, "打开…", OpenImages, Keys.Control | Keys.O, "open", false);
        Item(file, "从剪贴板新建", Paste, Keys.Control | Keys.V, "paste", false);
        file.DropDownItems.Add(new ToolStripSeparator());
        Item(file, "保存", () => SaveCurrent(false), Keys.Control | Keys.S, "save");
        Item(file, "另存为…", () => SaveCurrent(true), Keys.Control | Keys.Shift | Keys.S);
        Item(file, "导出为 PDF…", ExportPdf, Keys.None, "pdf");
        file.DropDownItems.Add(new ToolStripSeparator());
        Item(file, "打印…", Print, Keys.Control | Keys.P, "print");
        file.DropDownItems.Add(new ToolStripSeparator());
        Item(file, "关闭当前图像", CloseCurrent, Keys.Control | Keys.W, "close");
        Item(file, "退出编辑器", Close, Keys.Alt | Keys.F4, null, false);
        foreach (var entry in new (string Text, CaptureMode Mode)[] {
            ("点选窗口", CaptureMode.ActiveWindow), ("窗口 / 对象", CaptureMode.WindowObject),
            ("矩形区域", CaptureMode.Rectangle), ("手绘区域", CaptureMode.Freehand),
            ("点选屏幕", CaptureMode.FullScreen), ("滚动窗口", CaptureMode.Scrolling),
            ("固定大小区域", CaptureMode.FixedRegion), ("重复上次捕捉", CaptureMode.RepeatLast) })
            Item(capture, entry.Text, () => CaptureRequested?.Invoke(entry.Mode), Keys.None, null, false);
        capture.DropDownItems.Add(new ToolStripSeparator());
        var recording = Item(capture, "屏幕录像…", () => RecorderRequested?.Invoke(), Keys.None, null, false);
        capture.DropDownOpening += (_, _) => {
            foreach (ToolStripItem item in capture.DropDownItems) item.Enabled = item == recording ? RecorderRequested != null : CaptureRequested != null;
        };
        undoMenu = Item(edit, "撤销", () => Current?.Document.Undo(), Keys.Control | Keys.Z, "undo");
        redoMenu = Item(edit, "重做", () => Current?.Document.Redo(), Keys.Control | Keys.Y, "redo");
        edit.DropDownItems.Add(new ToolStripSeparator());
        Item(edit, "复制图像 / 选区", Copy, Keys.Control | Keys.C, "copy");
        cutMenu = Item(edit, "剪切选区", Cut, Keys.Control | Keys.X, "cut");
        Item(edit, "粘贴为新图像", Paste, Keys.None, "paste", false);
        edit.DropDownItems.Add(new ToolStripSeparator());
        Item(edit, "全选", () => Current?.Canvas.SelectAll(), Keys.Control | Keys.A, "select");
        Item(edit, "取消选择", () => Current?.Canvas.ClearSelection()).ShortcutKeyDisplayString = "Esc";
        Item(edit, "裁剪到选区", Crop, Keys.Control | Keys.R, "crop");
        Item(edit, "绘图与标注…", Draw, Keys.Control | Keys.D, "draw");
        Item(edit, "调整大小…", ResizeImage, Keys.Control | Keys.M, "resize");
        var color = new ToolStripMenuItem("色彩调整(&C)"); imageMenu.DropDownItems.Add(color);
        Item(color, "灰度", () => Current?.Document.Grayscale());
        Item(color, "负片", () => Current?.Document.Invert());
        Item(color, "亮度…", Brightness);
        var effects = new ToolStripMenuItem("画布与效果(&T)"); imageMenu.DropDownItems.Add(effects);
        Item(effects, "添加边缘…", Border, Keys.None, "border");
        Item(effects, "添加标题…", Caption, Keys.None, "caption");
        Item(effects, "马赛克选区", Mosaic, Keys.None, "mosaic");
        effects.DropDownItems.Add(new ToolStripSeparator());
        Item(effects, "顺时针旋转 90°", () => Current?.Document.Rotate(RotateFlipType.Rotate90FlipNone), Keys.None, "rotate");
        Item(effects, "逆时针旋转 90°", () => Current?.Document.Rotate(RotateFlipType.Rotate270FlipNone));
        Item(effects, "旋转 180°", () => Current?.Document.Rotate(RotateFlipType.Rotate180FlipNone));
        Item(effects, "水平镜像", () => Current?.Document.Rotate(RotateFlipType.RotateNoneFlipX));
        Item(effects, "垂直镜像", () => Current?.Document.Rotate(RotateFlipType.RotateNoneFlipY));
        Item(view, "放大", () => Zoom(1.25f), Keys.Control | Keys.Oemplus, "zoomIn");
        Item(view, "缩小", () => Zoom(.8f), Keys.Control | Keys.OemMinus, "zoomOut");
        Item(view, "实际大小 (100%)", () => { if (Current != null) Current.Canvas.Zoom = 1; }, Keys.Control | Keys.D0, "100");
        Item(view, "适合窗口", () => Current?.Canvas.Fit(), Keys.Control | Keys.D9);
        var background = new ToolStripMenuItem("工作区背景"); view.DropDownItems.Add(background);
        foreach (var entry in new[] { ("柔雾绿", BrandTheme.Canvas), ("暖白", BrandTheme.Background), ("深墨绿", BrandTheme.Ink) })
            Item(background, entry.Item1, () => { foreach (var tab in images) tab.Canvas.BackColor = entry.Item2; });
        var tabMenu = new ToolStripMenuItem("图像标签(&B)"); view.DropDownItems.Add(tabMenu);
        Item(tabMenu, "下一个图像", () => ChangeTab(1), Keys.Control | Keys.Tab);
        Item(tabMenu, "上一个图像", () => ChangeTab(-1), Keys.Control | Keys.Shift | Keys.Tab);
        Item(tabMenu, "关闭当前图像", CloseCurrent);
        tabMenu.DropDownItems.Add(new ToolStripSeparator());
        Item(tabMenu, "合并已打开图像…", CombineImages);
        var settings = new ToolStripMenuItem("设置(&S)"); tools.DropDownItems.Add(settings);
        var applicationSettings = Item(settings, "捕捉与输出设置…", () => SettingsRequested?.Invoke(), Keys.None, null, false);
        settings.DropDownOpening += (_, _) => applicationSettings.Enabled = SettingsRequested != null;
        Item(help, "编辑器快捷键", ShowHelp, Keys.F1, null, false);

        Button("打开", "open", OpenImages, "打开图像 (Ctrl+O)", false);
        saveAction = Button("保存", "save", () => SaveCurrent(false), "保存图像 (Ctrl+S)");
        Separator();
        Button("选择", "select", () => { if (Current != null) { Current.Canvas.Tool = AnnotationTool.Select; Current.Canvas.Focus(); message.Text = "拖动选择区域；选择后可裁剪、复制或马赛克"; } }, "矩形选择");
        Button("放大", "zoomIn", () => Zoom(1.25f), "放大图像 (Ctrl++)");
        Button("缩小", "zoomOut", () => Zoom(.8f), "缩小图像 (Ctrl+-)");
        Button("100%", "100", () => { if (Current != null) Current.Canvas.Zoom = 1; }, "实际大小 (Ctrl+0)");
        Separator();
        Button("标注", "draw", Draw, "箭头、文字、矩形、画笔、编号、高亮、马赛克 (Ctrl+D)");
        Button("标题", "caption", Caption, "添加标题");
        Button("边缘", "border", Border, "添加边缘");
        Button("大小", "resize", ResizeImage, "调整大小 (Ctrl+M)");
        Button("马赛克", "mosaic", Mosaic, "马赛克遮挡选区");
        Separator();
        cropButton = Button("裁剪", "crop", Crop, "裁剪到选区 (Ctrl+R)");
        Button("剪切", "cut", Cut, "剪切选区 (Ctrl+X)");
        copyAction = Button("复制", "copy", Copy, "复制图像或选区 (Ctrl+C)");
        Button("粘贴", "paste", Paste, "粘贴为新图像 (Ctrl+V)", false);
        Separator();
        Button("打印", "print", Print, "打印图像 (Ctrl+P)");
        Button("PDF", "pdf", ExportPdf, "导出为 PDF");
        Button("合并", "combine", CombineImages, "合并已打开图像");
        Button("关闭", "close", CloseCurrent, "关闭当前图像 (Ctrl+W)");
        // History actions share the menu row and stay visible when the main toolbar overflows.
        AddHistoryButton("redo", "重做", () => Current?.Document.Redo(), "重做 (Ctrl+Y)");
        AddHistoryButton("undo", "撤销", () => Current?.Document.Undo(), "撤销 (Ctrl+Z)");

        DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) foreach (var path in paths) OpenPath(path); };
        AddImage(image, filePath);
        FormClosing += ConfirmFormClosing;
        ResumeLayout(true);
    }

    public void AddImage(Bitmap image, string? filePath = null)
    {
        if (InvokeRequired) { Invoke(() => AddImage(image, filePath)); return; }
        var document = new EditorDocument(image, filePath);
        string name = filePath is null ? $"截图 {++imageSequence:000}" : Path.GetFileName(filePath);
        var page = new TabPage(name) { BackColor = BrandTheme.Canvas, Padding = Padding.Empty };
        var canvas = new ImageCanvas
        {
            Dock = DockStyle.Fill, Document = document, AllowEdgeCropping = true,
            AccessibleName = "图像编辑画布", AccessibleDescription = "拖动图像四边或四角直接裁切，松开应用；Esc 取消，Ctrl+Z 撤销。图像内部拖动选择区域。"
        };
        page.Controls.Add(canvas);
        var imageTab = new ImageTab(document, canvas, page, name);
        images.Add(imageTab);
        canvas.SelectionChanged += (_, _) => UpdateState();
        canvas.ZoomChanged += (_, _) => UpdateState();
        canvas.CropPreviewChanged += (_, _) => UpdateState();
        canvas.OperationFailed += ShowError;
        canvas.ImagePointerMoved += position =>
        {
            if (Current == imageTab && !canvas.HasSelection && !canvas.IsCropping)
                message.Text = $"{position.X}, {position.Y}    ·    拖动图像边缘直接裁切；内部拖动选择区域";
        };
        document.Changed += (_, _) => UpdateState();
        tabs.TabPages.Add(page); tabs.SelectedTab = page;
        UpdateState();
    }

    private ToolStripMenuItem TopMenu(string text)
    {
        var item = new ToolStripMenuItem(text); menu.Items.Add(item); return item;
    }

    private ToolStripMenuItem Item(ToolStripMenuItem parent, string text, Action action, Keys shortcut = Keys.None, string? iconName = null, bool needsImage = true)
    {
        var item = new ToolStripMenuItem(text) { ShortcutKeys = shortcut };
        if (iconName != null) { var icon = EditorIcons.Make(iconName, 18); icons.Add(icon); item.Image = icon; }
        item.Click += (_, _) => Run(action); parent.DropDownItems.Add(item);
        if (needsImage) requiresImage.Add(item); return item;
    }

    private ToolStripButton Button(string text, string iconName, Action action, string tip, bool needsImage = true)
    {
        var icon = EditorIcons.Make(iconName, 24); icons.Add(icon);
        var button = new ToolStripButton(text, icon) { DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, TextImageRelation = TextImageRelation.ImageAboveText,
            AutoSize = true, Padding = new Padding(6, 2, 6, 2), Margin = new Padding(1, 0, 1, 0), ToolTipText = tip, AccessibleName = text == "100%" ? "实际大小" : text,
            Tag = iconName, ForeColor = BrandTheme.Ink, Overflow = ToolStripItemOverflow.AsNeeded };
        button.Click += (_, _) => Run(action); toolbar.Items.Add(button);
        if (needsImage) requiresImage.Add(button); return button;
    }

    private void AddHistoryButton(string iconName, string text, Action action, string tip)
    {
        var icon = EditorIcons.Make(iconName, 16); icons.Add(icon);
        var item = new ToolStripButton(text, icon) { AutoSize = true, Alignment = ToolStripItemAlignment.Right,
            DisplayStyle = ToolStripItemDisplayStyle.Image, AccessibleName = text, ToolTipText = tip, Tag = iconName,
            Padding = new Padding(3, 0, 3, 0) };
        item.Click += (_, _) => Run(action);
        menu.Items.Add(item); requiresImage.Add(item);
    }

    private void Separator() => toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(2, 4, 2, 4) });

    private void UpdateState()
    {
        var current = Current;
        foreach (var item in requiresImage) item.Enabled = current != null;
        foreach (var tab in images)
        {
            string label = tab.Document.FilePath is null ? tab.Name : Path.GetFileName(tab.Document.FilePath);
            tab.Page.Text = label + (tab.Document.IsDirty ? " *" : "");
        }
        tabs.Invalidate();
        if (undoMenu != null) undoMenu.Enabled = current?.Document.CanUndo == true;
        if (redoMenu != null) redoMenu.Enabled = current?.Document.CanRedo == true;
        if (cropButton != null) cropButton.Enabled = current?.Canvas.HasSelection == true;
        if (cutMenu != null) cutMenu.Enabled = current?.Canvas.HasSelection == true;
        copyAction.Enabled = saveAction.Enabled = current != null;
        foreach (ToolStripItem item in toolbar.Items.Cast<ToolStripItem>().Concat(menu.Items.Cast<ToolStripItem>()))
        {
            if (item.Tag as string == "undo") item.Enabled = current?.Document.CanUndo == true;
            if (item.Tag as string == "redo") item.Enabled = current?.Document.CanRedo == true;
            if (item.Tag as string == "cut") item.Enabled = current?.Canvas.HasSelection == true;
        }
        if (current == null)
        {
            details.Text = "没有图像"; zoomStatus.Text = ""; message.Text = "打开文件或粘贴剪贴板图像";
            Text = BrandTheme.Name + " · 图像编辑器"; return;
        }
        var image = current.Document.Image;
        details.Text = $"{image.Width:N0} × {image.Height:N0} 像素";
        zoomStatus.Text = current.Canvas.Zoom == 1 ? "实际大小 · 100%" : $"缩放 {current.Canvas.Zoom:P0}";
        message.Text = current.Canvas.IsCropping
            ? $"保留 {current.Canvas.CropPreview.Width} × {current.Canvas.CropPreview.Height}    ·    松开鼠标裁切；Esc / 右键取消；裁切后 Ctrl+Z 撤销"
            : current.Canvas.HasSelection
            ? $"选区 {current.Canvas.Selection.Width} × {current.Canvas.Selection.Height}    ·    可裁剪、复制或添加马赛克"
            : "拖动图像边缘直接裁切；内部选择区域；Ctrl + 滚轮缩放，中键平移";
        Text = $"{current.Page.Text} — {BrandTheme.Name}";
    }

    private void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= tabs.TabCount) return;
        var rect = tabs.GetTabRect(e.Index);
        bool selected = tabs.SelectedIndex == e.Index;
        using var background = new SolidBrush(selected ? BrandTheme.Surface : BrandTheme.Background);
        e.Graphics.FillRectangle(background, rect);
        if (selected) { using var accent = new SolidBrush(BrandTheme.Accent); e.Graphics.FillRectangle(accent, rect.X + 12, rect.Bottom - 3, rect.Width - 24, 2); }
        var textBounds = new Rectangle(rect.X + 12, rect.Y + 3, rect.Width - 38, rect.Height - 4);
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, Font, textBounds, selected ? BrandTheme.Ink : BrandTheme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        using var close = new Pen(BrandTheme.Muted, 1.2f);
        int x = rect.Right - 16, y = rect.Top + rect.Height / 2;
        e.Graphics.DrawLine(close, x - 3, y - 3, x + 3, y + 3); e.Graphics.DrawLine(close, x + 3, y - 3, x - 3, y + 3);
    }

    private void TabMouseDown(object? sender, MouseEventArgs e)
    {
        for (int i = 0; i < tabs.TabCount; i++)
        {
            var rect = tabs.GetTabRect(i);
            if (rect.Contains(e.Location) && (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Left && e.X >= rect.Right - 28))
            { var tab = images.First(x => x.Page == tabs.TabPages[i]); Run(() => CloseTab(tab)); return; }
        }
    }

    private void ChangeTab(int direction)
    {
        if (tabs.TabCount > 0) tabs.SelectedIndex = (tabs.SelectedIndex + direction + tabs.TabCount) % tabs.TabCount;
    }

    private void OpenImages()
    {
        using var dialog = new OpenFileDialog { Title = "打开图像", Filter = "图像文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico|所有文件|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) foreach (var path in dialog.FileNames) OpenPath(path);
    }

    private void OpenPath(string path) => Run(() =>
    {
        using var image = System.Drawing.Image.FromFile(path);
        using var bitmap = EditorDocument.Copy(image);
        AddImage(bitmap, path);
    });

    private bool SaveTab(ImageTab tab, bool saveAs, bool pdf = false)
    {
        string? path = saveAs ? null : tab.Document.FilePath;
        if (path == null || pdf)
        {
            using var dialog = new SaveFileDialog { Title = pdf ? "导出为 PDF" : "保存图像", AddExtension = true,
                Filter = pdf ? "PDF 文档|*.pdf" : "PNG 图像|*.png|JPEG 图像|*.jpg|BMP 图像|*.bmp|GIF 图像|*.gif|TIFF 图像|*.tiff|PDF 文档|*.pdf",
                DefaultExt = pdf ? "pdf" : "png", FileName = Path.GetFileNameWithoutExtension(tab.Document.FilePath ?? tab.Name) + (pdf ? ".pdf" : ".png") };
            if (!pdf && tab.Document.FilePath != null)
            {
                var extensions = new[] { ".png", ".jpg", ".bmp", ".gif", ".tiff", ".pdf" };
                int index = Array.IndexOf(extensions, Path.GetExtension(tab.Document.FilePath).ToLowerInvariant());
                if (index >= 0) dialog.FilterIndex = index + 1;
                dialog.FileName = Path.GetFileName(tab.Document.FilePath);
            }
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            path = dialog.FileName;
        }
        if (pdf) tab.Document.Export(path);
        else tab.Document.Save(path);
        message.Text = (pdf ? "已导出：" : "已保存：") + Path.GetFileName(path); return true;
    }

    private void SaveCurrent(bool saveAs) { if (Current != null) SaveTab(Current, saveAs); }
    private void ExportPdf() { if (Current != null) SaveTab(Current, true, true); }

    private void Copy()
    {
        if (Current == null) return;
        using var image = Current.Canvas.HasSelection ? Current.Document.Image.Clone(Current.Canvas.Selection, System.Drawing.Imaging.PixelFormat.Format32bppArgb) : EditorDocument.Copy(Current.Document.Image);
        Clipboard.SetImage(image); message.Text = Current.Canvas.HasSelection ? "选区已复制到剪贴板" : "图像已复制到剪贴板";
    }

    private void Cut()
    {
        var current = Current; if (current == null || !current.Canvas.HasSelection) return;
        Copy(); var selected = current.Canvas.Selection;
        current.Document.Edit(bitmap => { using var graphics = Graphics.FromImage(bitmap); graphics.CompositingMode = CompositingMode.SourceCopy; graphics.FillRectangle(Brushes.Transparent, selected); });
        current.Canvas.ClearSelection(); message.Text = "选区已剪切，可粘贴为新图像；Ctrl+Z 撤销";
    }

    private void Paste()
    {
        if (!Clipboard.ContainsImage()) { message.Text = "剪贴板中没有图像"; return; }
        using var source = Clipboard.GetImage();
        if (source == null) return;
        using var image = EditorDocument.Copy(source); AddImage(image); message.Text = "已从剪贴板新建图像";
    }

    private void Crop()
    {
        var current = Current;
        if (current == null || !current.Canvas.HasSelection) { message.Text = "请先拖动鼠标选择要保留的区域"; return; }
        current.Document.Crop(current.Canvas.Selection); current.Canvas.ClearSelection();
    }

    private void ResizeImage()
    {
        var current = Current; if (current == null) return;
        var result = EditorDialogs.Resize(this, current.Document.Image.Size);
        if (result.HasValue) { current.Document.Resize(result.Value.Width, result.Value.Height); current.Canvas.ClearSelection(); }
    }

    private void Draw()
    {
        var current = Current; if (current == null) return;
        using var form = new DrawForm(current.Document.Image);
        if (WindowState == FormWindowState.Maximized) form.WindowState = FormWindowState.Maximized;
        if (form.ShowDialog(this) == DialogResult.OK && form.HasChanges) { current.Document.ReplaceImage(form.EditedImage); current.Canvas.ClearSelection(); }
    }

    private void Caption()
    {
        var current = Current; if (current == null) return;
        string? text = EditorDialogs.Text(this, "添加标题", "标题将添加到图像下方：");
        if (!string.IsNullOrWhiteSpace(text)) current.Document.AddCaption(text, BrandTheme.Ink, Color.White, 24);
    }

    private void Border()
    {
        var current = Current; if (current == null) return;
        int? value = EditorDialogs.Value(this, "添加边缘", "边缘宽度（像素）", 8, 1, 200);
        if (!value.HasValue) return;
        using var dialog = new ColorDialog { Color = Color.White, FullOpen = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) current.Document.AddBorder(value.Value, dialog.Color);
    }

    private void Brightness()
    {
        var current = Current; if (current == null) return;
        int? value = EditorDialogs.Value(this, "亮度", "调整范围 −100 至 100", 0, -100, 100);
        if (value.HasValue && value.Value != 0) current.Document.Brightness(value.Value / 100f);
    }

    private void Mosaic()
    {
        var current = Current;
        if (current == null || !current.Canvas.HasSelection) { message.Text = "请先拖动选择需要马赛克的区域"; return; }
        int? value = EditorDialogs.Value(this, "马赛克", "块大小（像素）", 12, 2, 100);
        if (value.HasValue) { current.Document.Mosaic(current.Canvas.Selection, value.Value); current.Canvas.ClearSelection(); }
    }

    private void Zoom(float factor) { if (Current != null) Current.Canvas.Zoom *= factor; }

    private void CombineImages()
    {
        if (images.Count < 2) { message.Text = "请先打开至少两张图像，然后按标签顺序合并"; return; }
        using var form = EditorDialogs.Base("合并图像", new Size(430, 210));
        form.Controls.Add(new Label { Text = $"按标签顺序合并 {images.Count} 张图像，结果作为新标签打开。", AutoSize = true, Location = new Point(20, 20) });
        var vertical = new RadioButton { Text = "垂直排列", Checked = true, AutoSize = true, Location = new Point(25, 58) };
        var horizontal = new RadioButton { Text = "水平排列", AutoSize = true, Location = new Point(180, 58) };
        var spacing = EditorDialogs.Number(12, 0, 1000, new Point(180, 96));
        form.Controls.AddRange([vertical, horizontal, new Label { Text = "图像间距（像素）", AutoSize = true, Location = new Point(25, 100) }, spacing]);
        EditorDialogs.Buttons(form);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        using var combined = EditorDocument.Combine(images.Select(x => x.Document.Image).ToArray(), vertical.Checked, (int)spacing.Value);
        AddImage(combined); Current?.Canvas.Fit(); message.Text = "图像已合并；原图仍保留在各标签中";
    }

    private void Print()
    {
        var current = Current; if (current == null) return;
        using var bitmap = EditorDocument.Copy(current.Document.Image);
        using var document = new PrintDocument { DocumentName = current.Name };
        document.PrintPage += (_, args) =>
        {
            var area = args.MarginBounds;
            float scale = Math.Min(area.Width / (float)bitmap.Width, area.Height / (float)bitmap.Height);
            var target = new RectangleF(area.X + (area.Width - bitmap.Width * scale) / 2, area.Y, bitmap.Width * scale, bitmap.Height * scale);
            args.Graphics!.DrawImage(bitmap, target); args.HasMorePages = false;
        };
        using var dialog = new PrintDialog { Document = document, UseEXDialog = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) { document.Print(); message.Text = "图像已提交到打印队列"; }
    }

    private bool ConfirmSave(ImageTab tab)
    {
        if (!tab.Document.IsDirty) return true;
        var result = MessageBox.Show(this, $"“{tab.Page.Text.TrimEnd(' ', '*')}”尚未保存。是否保存更改？", "保存图像", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return result == DialogResult.No || result == DialogResult.Yes && SaveTab(tab, false);
    }

    private void CloseCurrent() { if (Current != null) CloseTab(Current); }
    private void CloseTab(ImageTab tab)
    {
        if (!ConfirmSave(tab)) return;
        images.Remove(tab); tabs.TabPages.Remove(tab.Page); tab.Canvas.Document = null; tab.Page.Dispose(); tab.Document.Dispose(); UpdateState();
        if (images.Count == 0) Close();
    }

    private void ConfirmFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (closing) return;
        try
        {
            foreach (var tab in images) if (!ConfirmSave(tab)) { e.Cancel = true; return; }
            closing = true;
        }
        catch (Exception ex) { e.Cancel = true; ShowError(ex); }
    }

    private void ShowHelp() => MessageBox.Show(this,
        "Ctrl+O　打开图像\nCtrl+S　保存\nCtrl+Shift+S　另存为\nCtrl+C / X / V　复制 / 剪切选区 / 粘贴为新图像\nCtrl+Z / Y　撤销 / 重做\nCtrl+D　绘图与标注\nCtrl+R　裁剪到选区\nCtrl+M　调整大小\nCtrl+0　实际大小\nCtrl+9　适合窗口\nCtrl+Tab　切换图像\nCtrl+W　关闭图像\n\n拖动图像四边或四角裁切，松开应用；Esc / 右键取消。\nCtrl + 滚轮缩放；中键拖动平移。\nGIF 和 TIFF 以首帧 / 首页静态图像编辑。PDF 仅支持导出。",
        "编辑器快捷键", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void Run(Action action) { try { action(); } catch (Exception ex) { ShowError(ex); } }
    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, "无法完成操作", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            if (Current?.Canvas.IsCropping == true) Current.Canvas.CancelCrop();
            else Current?.Canvas.ClearSelection();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var tab in images) { tab.Canvas.Document = null; tab.Document.Dispose(); }
            images.Clear(); foreach (var icon in icons) icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
