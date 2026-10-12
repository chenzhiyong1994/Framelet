using System.Drawing.Drawing2D;

namespace Framelet;

internal static class EditorIcons
{
    public static Bitmap Make(string name, int size = 24, Color? tint = null)
    {
        var result = new Bitmap(size, size);
        using var g = Graphics.FromImage(result);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(size / 24f, size / 24f);
        Color color = tint ?? BrandTheme.Ink;
        using var pen = new Pen(color, 1.65f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);
        switch (name)
        {
            case "open":
                g.DrawLines(pen, [new(3, 18), new(3, 5), new(9, 5), new(12, 8), new(21, 8), new(21, 11)]);
                g.DrawPolygon(pen, [new(3, 20), new(6, 11), new(22, 11), new(19, 20)]); break;
            case "save":
                g.DrawPolygon(pen, [new(4, 3), new(17, 3), new(21, 7), new(21, 21), new(4, 21)]);
                g.DrawRectangle(pen, 8, 3, 8, 6); g.DrawRectangle(pen, 8, 14, 9, 7); break;
            case "copy":
                g.DrawRectangle(pen, 8, 8, 13, 13); g.DrawLines(pen, [new(16, 5), new(16, 3), new(3, 3), new(3, 16), new(5, 16)]); break;
            case "paste":
                g.DrawLines(pen, [new(7, 5), new(4, 5), new(4, 21), new(20, 21), new(20, 5), new(17, 5)]);
                g.DrawRectangle(pen, 8, 3, 8, 5); g.DrawLine(pen, 8, 12, 16, 12); g.DrawLine(pen, 8, 16, 14, 16); break;
            case "cut":
                g.DrawEllipse(pen, 2, 15, 6, 6); g.DrawEllipse(pen, 16, 15, 6, 6);
                g.DrawLine(pen, 7, 16, 19, 3); g.DrawLine(pen, 17, 16, 5, 3); break;
            case "draw": case "pen":
                g.DrawPolygon(pen, [new(4, 15), new(16, 3), new(21, 8), new(9, 20), new(3, 21)]);
                g.DrawLine(pen, 13, 6, 18, 11); g.DrawLine(pen, 4, 15, 9, 20); break;
            case "crop":
                g.DrawLines(pen, [new(7, 3), new(7, 17), new(21, 17)]);
                g.DrawLines(pen, [new(3, 7), new(17, 7), new(17, 21)]); break;
            case "resize":
                g.DrawLines(pen, [new(3, 10), new(3, 21), new(14, 21)]);
                g.DrawLines(pen, [new(10, 3), new(21, 3), new(21, 14)]);
                g.DrawLine(pen, 8, 16, 20, 4); g.DrawLines(pen, [new(8, 11), new(8, 16), new(13, 16)]); break;
            case "border":
                g.DrawRectangle(pen, 3, 3, 18, 18); g.DrawRectangle(pen, 7, 7, 10, 10); break;
            case "caption":
                g.DrawRectangle(pen, 3, 3, 18, 12); g.DrawLine(pen, 5, 19, 19, 19); g.DrawLine(pen, 9, 22, 15, 22); break;
            case "text":
                g.DrawLines(pen, [new(4, 7), new(4, 4), new(20, 4), new(20, 7)]);
                g.DrawLine(pen, 12, 4, 12, 21); g.DrawLine(pen, 8, 21, 16, 21); break;
            case "undo": case "redo":
                if (name == "redo") { g.TranslateTransform(24, 0); g.ScaleTransform(-1, 1); }
                g.DrawLines(pen, [new(8, 4), new(3, 9), new(8, 14)]);
                g.DrawLine(pen, 3, 9, 14, 9); g.DrawArc(pen, 9, 9, 12, 11, 270, 180); g.DrawLine(pen, 15, 20, 10, 20); break;
            case "zoomIn": case "zoomOut": case "zoom":
                g.DrawEllipse(pen, 3, 3, 13, 13); g.DrawLine(pen, 15, 15, 21, 21);
                g.DrawLine(pen, 6, 9.5f, 13, 9.5f); if (name != "zoomOut") g.DrawLine(pen, 9.5f, 6, 9.5f, 13); break;
            case "100":
                g.DrawLines(pen, [new(8, 3), new(3, 3), new(3, 8)]); g.DrawLines(pen, [new(16, 3), new(21, 3), new(21, 8)]);
                g.DrawLines(pen, [new(3, 16), new(3, 21), new(8, 21)]); g.DrawLines(pen, [new(21, 16), new(21, 21), new(16, 21)]);
                g.DrawLine(pen, 10, 8, 10, 16); g.DrawLine(pen, 14, 8, 14, 16); break;
            case "select":
                pen.DashStyle = DashStyle.Dash; g.DrawRectangle(pen, 4, 4, 16, 16); break;
            case "rectangle": g.DrawRectangle(pen, 3, 5, 18, 14); break;
            case "ellipse": g.DrawEllipse(pen, 3, 4, 18, 16); break;
            case "line": g.DrawLine(pen, 4, 20, 20, 4); break;
            case "arrow":
                g.DrawLine(pen, 4, 20, 20, 4); g.DrawLines(pen, [new(9, 4), new(20, 4), new(20, 15)]); break;
            case "highlight":
                g.DrawPolygon(pen, [new(5, 13), new(15, 3), new(21, 9), new(11, 19)]);
                g.DrawLines(pen, [new(5, 13), new(3, 18), new(7, 22), new(11, 19)]); g.DrawLine(pen, 13, 22, 22, 22); break;
            case "mosaic":
                g.DrawRectangle(pen, 3, 3, 18, 18);
                g.DrawLine(pen, 9, 3, 9, 21); g.DrawLine(pen, 15, 3, 15, 21);
                g.DrawLine(pen, 3, 9, 21, 9); g.DrawLine(pen, 3, 15, 21, 15);
                g.FillRectangle(brush, 9, 3, 6, 6); g.FillRectangle(brush, 3, 15, 6, 6); g.FillRectangle(brush, 15, 9, 6, 6); break;
            case "number":
                g.DrawEllipse(pen, 2.5f, 2.5f, 19, 19); g.DrawLines(pen, [new(9, 9), new(12, 6), new(12, 18)]); g.DrawLine(pen, 8, 18, 16, 18); break;
            case "print":
                g.DrawRectangle(pen, 7, 3, 10, 5); g.DrawLines(pen, [new(7, 18), new(3, 18), new(3, 8), new(21, 8), new(21, 18), new(17, 18)]);
                g.DrawRectangle(pen, 7, 14, 10, 7); g.DrawLine(pen, 17, 11, 18, 11); break;
            case "pdf":
                g.DrawPolygon(pen, [new(5, 2), new(14, 2), new(20, 8), new(20, 22), new(5, 22)]);
                g.DrawLines(pen, [new(14, 2), new(14, 8), new(20, 8)]); g.DrawLine(pen, 8, 13, 16, 13); g.DrawLine(pen, 8, 17, 14, 17); break;
            case "combine":
                g.DrawRectangle(pen, 3, 3, 13, 9); g.DrawRectangle(pen, 8, 15, 13, 6);
                g.DrawLines(pen, [new(4, 16), new(4, 20), new(6, 20)]); break;
            case "close": g.DrawLine(pen, 6, 6, 18, 18); g.DrawLine(pen, 18, 6, 6, 18); break;
            case "rotate":
                g.DrawArc(pen, 4, 4, 16, 16, 0, 280); g.DrawLines(pen, [new(11, 2), new(15, 4), new(12, 8)]); break;
            default: g.DrawRectangle(pen, 4, 4, 16, 16); break;
        }
        return result;
    }
}
