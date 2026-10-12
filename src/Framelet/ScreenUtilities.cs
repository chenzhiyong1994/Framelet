using System.Drawing.Drawing2D;

namespace Framelet;

internal enum UtilityMode { ColorPicker, Magnifier, Crosshair, Focus }

internal sealed class ScreenUtilityForm : Form
{
    private readonly UtilityMode mode;
    private readonly Bitmap? frozen;
    private readonly Rectangle screen;
    private readonly System.Windows.Forms.Timer ticker = new() { Interval = 40 };
    private Bitmap? magnified;
    private Point point;
    private int zoom = 6;
    private int radius = 150;
    private Color currentColor;
    public ScreenUtilityForm(UtilityMode mode)
    {
        this.mode = mode; screen = SystemInformation.VirtualScreen;
        Text = mode switch { UtilityMode.ColorPicker => "屏幕取色器", UtilityMode.Magnifier => "屏幕放大镜", UtilityMode.Crosshair => "屏幕十字线", _ => "屏幕聚光灯" };
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = true; TopMost = true; StartPosition = FormStartPosition.Manual; Bounds = screen; DoubleBuffered = true; KeyPreview = true;
        if (mode == UtilityMode.ColorPicker) frozen = CaptureEngine.CaptureScreen(screen);
        if (mode is UtilityMode.Focus or UtilityMode.Crosshair) { BackColor = Color.Magenta; TransparencyKey = Color.Magenta; if (mode == UtilityMode.Focus) Opacity = .72; }
        if (mode == UtilityMode.Magnifier) { Bounds = new Rectangle(screen.Left + 40, screen.Top + 40, 258, 226); BackColor = BrandTheme.Ink; Cursor = Cursors.Default; }
        else Cursor = Cursors.Cross;
        ticker.Tick += (_, _) => UpdatePointer(); ticker.Start();
        MouseMove += (_, _) => UpdatePointer();
        MouseWheel += (_, e) => { zoom = Math.Clamp(zoom + Math.Sign(e.Delta), 2, 20); radius = Math.Clamp(radius + 20 * Math.Sign(e.Delta), 40, 700); UpdatePointer(); };
        MouseDown += (_, e) => { if (mode == UtilityMode.ColorPicker && e.Button == MouseButtons.Left) CopyColor(false); else if (e.Button == MouseButtons.Right) Close(); };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); if (mode == UtilityMode.ColorPicker && e.KeyCode is Keys.Enter or Keys.Space or Keys.C) CopyColor(false); if (mode == UtilityMode.ColorPicker && e.KeyCode == Keys.R) CopyColor(true); };
        Shown += (_, _) => { Activate(); UpdatePointer(); };
    }
    private void CopyColor(bool rgb)
    {
        string text = rgb ? $"rgb({currentColor.R}, {currentColor.G}, {currentColor.B})" : $"#{currentColor.R:X2}{currentColor.G:X2}{currentColor.B:X2}";
        Clipboard.SetText(text); Close();
    }
    private void UpdatePointer()
    {
        point = new Point(Cursor.Position.X - screen.Left, Cursor.Position.Y - screen.Top);
        if (frozen != null) currentColor = frozen.GetPixel(Math.Clamp(point.X, 0, frozen.Width - 1), Math.Clamp(point.Y, 0, frozen.Height - 1));
        if (mode == UtilityMode.Magnifier)
        {
            var current = Cursor.Position; var work = Screen.FromPoint(current).WorkingArea;
            int marginX = Math.Max(30, 120 / zoom + 12), marginY = Math.Max(30, 89 / zoom + 12);
            int x = current.X + marginX; int y = current.Y + marginY;
            if (x + Width > work.Right) x = current.X - Width - marginX;
            if (y + Height > work.Bottom) y = current.Y - Height - marginY;
            Location = new Point(Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - Width)), Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - Height)));
            int w = Math.Max(8, 240 / zoom); int h = Math.Max(8, 178 / zoom);
            var area = Rectangle.Intersect(screen, new Rectangle(current.X - w / 2, current.Y - h / 2, w, h));
            try { var sample = CaptureEngine.CaptureScreen(area); magnified?.Dispose(); magnified = sample; } catch (Exception) { Close(); return; }
        }
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (frozen != null) g.DrawImageUnscaled(frozen, 0, 0);
        if (mode == UtilityMode.Focus)
        {
            g.Clear(Color.Black); using var hole = new SolidBrush(Color.Magenta); g.FillEllipse(hole, point.X - radius, point.Y - radius, radius * 2, radius * 2);
            DrawHint(g, new Rectangle(24, 24, 400, 44), "屏幕聚光灯  ·  滚轮调整大小  ·  Esc 退出"); return;
        }
        if (mode == UtilityMode.Crosshair)
        {
            g.Clear(Color.Magenta); using var pen = new Pen(Color.FromArgb(231, 53, 78)); g.DrawLine(pen, point.X, 0, point.X, Height); g.DrawLine(pen, 0, point.Y, Width, point.Y);
            DrawHint(g, new Rectangle(24, 24, 475, 44), $"X {Cursor.Position.X}   Y {Cursor.Position.Y}     ·     屏幕十字线 / Esc 退出"); return;
        }
        if (mode == UtilityMode.Magnifier)
        {
            g.Clear(BackColor); g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
            if (magnified != null) g.DrawImage(magnified, new Rectangle(8, 8, 242, 180));
            using var pen = new Pen(Color.FromArgb(240, 51, 74)); g.DrawLine(pen,128,76,128,118);g.DrawLine(pen,108,98,149,98);
            TextRenderer.DrawText(g, $"{zoom}×  ·  滚轮缩放  ·  Esc 退出", Font, new Rectangle(8,197,245,22),Color.White);return;
        }
        g.DrawLine(Pens.White, point.X-9,point.Y,point.X+9,point.Y);g.DrawLine(Pens.White,point.X,point.Y-9,point.X,point.Y+9);
        int left = Math.Clamp(point.X + 28, 0, Math.Max(0, Width - 242)), top = Math.Clamp(point.Y + 28, 0, Math.Max(0, Height - 230));
        var box = new Rectangle(left, top, 240, 220); using var background = new SolidBrush(BrandTheme.Ink);g.FillRectangle(background,box);
        var sampleRect = new Rectangle(Math.Clamp(point.X-10,0,Math.Max(0,Width-21)),Math.Clamp(point.Y-10,0,Math.Max(0,Height-21)),Math.Min(21,Width),Math.Min(21,Height));
        g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;g.DrawImage(frozen!,new Rectangle(left+10,top+10,126,126),sampleRect,GraphicsUnit.Pixel);
        g.DrawRectangle(Pens.White,left+70,top+70,6,6);using var swatch=new SolidBrush(currentColor);g.FillRectangle(swatch,left+151,top+10,76,126);
        TextRenderer.DrawText(g,$"#{currentColor.R:X2}{currentColor.G:X2}{currentColor.B:X2}   RGB {currentColor.R}, {currentColor.G}, {currentColor.B}",Font,new Rectangle(left+10,top+147,225,24),Color.White);
        TextRenderer.DrawText(g,"单击复制 HEX  ·  R 复制 RGB\nEsc 取消",Font,new Rectangle(left+10,top+179,225,38),Color.LightGray);
    }
    private void DrawHint(Graphics g, Rectangle box, string message)
    { using var b = new SolidBrush(BrandTheme.Ink);g.FillRectangle(b,box);TextRenderer.DrawText(g,message,Font,box,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter); }
    protected override void Dispose(bool disposing) { if(disposing){ticker.Dispose();frozen?.Dispose();magnified?.Dispose();}base.Dispose(disposing); }
}

internal sealed class RulerForm : Form
{
    private bool vertical;
    private Point mouseStart, locationStart;
    private bool dragging;
    public RulerForm()
    {
        Text = BrandTheme.Name + " · 屏幕标尺";FormBorderStyle=FormBorderStyle.None;TopMost=true;StartPosition=FormStartPosition.CenterScreen;Size=new Size(700,76);BackColor=BrandTheme.AccentPale;Opacity=.94;DoubleBuffered=true;KeyPreview=true;
        MouseDown+=(_,e)=>{ if(e.Button==MouseButtons.Right){Close();return;} dragging=true;mouseStart=Cursor.Position;locationStart=Location;Capture=true; };
        MouseMove+=(_,_)=>{if(dragging) Location=new Point(locationStart.X+Cursor.Position.X-mouseStart.X,locationStart.Y+Cursor.Position.Y-mouseStart.Y);};
        MouseUp+=(_,_)=>{dragging=false;Capture=false;};MouseDoubleClick+=(_,_)=>Rotate();
        KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape)Close();if(e.KeyCode==Keys.Space)Rotate();};
        MouseWheel+=(_,e)=>{if(vertical)Height=Math.Clamp(Height+Math.Sign(e.Delta)*100,200,2000);else Width=Math.Clamp(Width+Math.Sign(e.Delta)*100,200,3000);Invalidate();};
    }
    private void Rotate(){vertical=!vertical;Size=new Size(Height,Width);Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.DrawRectangle(Pens.DarkGoldenrod,0,0,Width-1,Height-1);
        if(vertical){g.TranslateTransform(Width,0);g.RotateTransform(90);}
        int length=vertical?Height:Width;
        for(int x=0;x<length;x+=5){int h=x%50==0?25:x%10==0?17:9;g.DrawLine(Pens.SaddleBrown,x,0,x,h);if(x%50==0)g.DrawString(x.ToString(),Font,Brushes.SaddleBrown,x+3,25);}
        g.DrawString("px    拖动移动 · 双击/空格旋转 · 滚轮改长度 · Esc 关闭",Font,Brushes.SaddleBrown,12,53);
    }
}
