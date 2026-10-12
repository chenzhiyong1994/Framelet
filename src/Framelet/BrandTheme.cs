using System.Drawing.Drawing2D;

namespace Framelet;

internal static class BrandTheme
{
    public const string Name = "帧简 Framelet";
    public const string ShortName = "帧简";
    public const string Wordmark = "FRAMELET";
    public static readonly Color Background = Color.FromArgb(245, 244, 239);
    public static readonly Color Surface = Color.White;
    public static readonly Color Ink = Color.FromArgb(28, 48, 46);
    public static readonly Color Accent = Color.FromArgb(20, 125, 112);
    public static readonly Color AccentPale = Color.FromArgb(229, 242, 236);
    public static readonly Color Muted = Color.FromArgb(115, 129, 123);
    public static readonly Color Border = Color.FromArgb(217, 225, 219);
    public static readonly Color Canvas = Color.FromArgb(234, 239, 235);
    public static readonly Color Coral = Color.FromArgb(229, 119, 97);

    public static void Apply(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Ink;
        form.Font = new Font("Microsoft YaHei UI", 9F);
        form.Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
    }

    public static void StyleButton(Button button, bool primary = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        button.BackColor = primary ? Accent : Surface;
        button.ForeColor = primary ? Color.White : Ink;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(15, 105, 94) : AccentPale;
        button.FlatAppearance.MouseDownBackColor = primary ? Ink : Border;
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
        button.Padding = new Padding(6, 0, 6, 0);
    }

    public static Bitmap CreateMark(int size)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(size / 32F, size / 32F);
        using var background = new SolidBrush(Accent);
        using var shape = Rounded(new Rectangle(1, 1, 30, 30), 7);
        g.FillPath(background, shape);
        using var pen = new Pen(Background, 2.5F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, [new PointF(14, 8), new PointF(8, 8), new PointF(8, 14)]);
        g.DrawLines(pen, [new PointF(8, 18), new PointF(8, 24), new PointF(14, 24)]);
        g.DrawLines(pen, [new PointF(18, 24), new PointF(24, 24), new PointF(24, 18)]);
        using var fold = new SolidBrush(AccentPale);
        g.FillPolygon(fold, [new PointF(18, 8), new PointF(24, 8), new PointF(24, 14)]);
        using var ink = new SolidBrush(Ink);
        using var flap = new GraphicsPath();
        flap.AddLines([new PointF(18, 8), new PointF(24, 14), new PointF(20, 14)]);
        flap.AddBezier(20, 14, 18, 14, 18, 14, 18, 12); flap.CloseFigure();
        g.FillPath(ink, flap);
        return bitmap;
    }

    internal static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }

    public static ToolStripRenderer CreateToolStripRenderer() => new FrameletRenderer();

    private sealed class FrameletRenderer : ToolStripProfessionalRenderer
    {
        public FrameletRenderer() : base(new FrameletColors()) { RoundedEdges = false; }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown) base.OnRenderToolStripBorder(e);
        }
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) => PaintButton(e);
        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) => PaintButton(e);
        private static void PaintButton(ToolStripItemRenderEventArgs e)
        {
            var color = e.Item.BackColor == Accent ? Accent : e.Item.Selected || (e.Item is ToolStripButton b && b.Checked) ? AccentPale : e.Item.BackColor;
            if (e.Item.Pressed) color = e.Item.BackColor == Accent ? Ink : Border;
            if (color == Color.Transparent || color.A == 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Rounded(new Rectangle(1, 2, Math.Max(4, e.Item.Width - 3), Math.Max(4, e.Item.Height - 5)), 7);
            using var fill = new SolidBrush(color); e.Graphics.FillPath(fill, path);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Muted : e.Item.ForeColor == Color.White ? Color.White : Ink;
            base.OnRenderItemText(e);
        }
    }

    private sealed class FrameletColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuItemSelected => AccentPale;
        public override Color MenuItemSelectedGradientBegin => AccentPale;
        public override Color MenuItemSelectedGradientEnd => AccentPale;
        public override Color MenuItemBorder => AccentPale;
        public override Color MenuBorder => Border;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;
        public override Color ToolStripGradientBegin => Background;
        public override Color ToolStripGradientMiddle => Background;
        public override Color ToolStripGradientEnd => Background;
    }
}
