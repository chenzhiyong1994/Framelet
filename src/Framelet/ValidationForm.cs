using System.Drawing.Drawing2D;

namespace Framelet;

internal sealed class ValidationForm : Form
{
    public ValidationForm()
    {
        Text = BrandTheme.Name + " · 验证画板"; StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(1000, 700); BackColor = Color.White;
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BrandTheme.Background };
        var board = new Board { Size = new Size(940, 2400), Location = new Point(20, 20) };
        scroll.Controls.Add(board); Controls.Add(scroll);
    }
    private sealed class Board : Control
    {
        public Board() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Color.White); g.SmoothingMode = SmoothingMode.AntiAlias;
            using var titleFont = new Font("Segoe UI", 28, FontStyle.Bold);
            using var headingFont = new Font("Segoe UI", 17, FontStyle.Bold);
            using var bodyFont = new Font("Microsoft YaHei UI", 11);
            using var blue = new SolidBrush(BrandTheme.Accent);
            using var ink = new SolidBrush(BrandTheme.Ink);
            using var muted = new SolidBrush(BrandTheme.Muted);
            g.FillRectangle(blue, 0, 0, Width, 165);
            g.DrawString(BrandTheme.Name, titleFont, Brushes.White, 34, 30);
            g.DrawString("截图 · 标注 · 录屏       功能验证画板", bodyFont, Brushes.White, 39, 104);
            var colors = new[] { BrandTheme.Accent, Color.FromArgb(17, 155, 126), Color.FromArgb(244, 172, 54), Color.FromArgb(225, 77, 105) };
            for (int i = 0; i < 4; i++)
            {
                using var fill = new SolidBrush(colors[i]); g.FillRectangle(fill, 40 + i * 221, 203, 199, 122);
                g.DrawString($"#{colors[i].R:X2}{colors[i].G:X2}{colors[i].B:X2}", bodyFont, ink, 40 + i * 221, 338);
            }
            g.DrawString("Ready for a closer look.", headingFont, ink, 40, 407);
            g.DrawString("选择一块区域，添加箭头、文字与编号，然后导出你的第一张截图。", bodyFont, muted, 40, 450);
            for (int n = 0; n < 12; n++)
            {
                int y = 525 + n * 145;
                using var b = new SolidBrush(n % 2 == 0 ? BrandTheme.AccentPale : BrandTheme.Background);
                g.FillRectangle(b,40,y,Width-80,123);
                g.DrawString($"{n+1:00}   Scrolling capture checkpoint", headingFont, ink, 62, y+18);
                g.DrawString($"长截图测试段落 {n+1:00}     ·     源图 Y = {y} px     ·     保持画面重叠以拼接",bodyFont,muted,62,y+63);
                using var marker = new SolidBrush(colors[n%4]);g.FillRectangle(marker, 840,y+20,20,78);
            }
        }
    }
}
