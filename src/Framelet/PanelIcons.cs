using System.Drawing.Drawing2D;

namespace Framelet;

internal static class PanelIcons
{
    public static Bitmap Create(string kind, int size = 24, Color? color = null)
    {
        var image = new Bitmap(size, size);
        using var g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(size / 24F, size / 24F);
        using var p = new Pen(color ?? BrandTheme.Ink, 1.7F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(color ?? BrandTheme.Ink);
        switch (kind)
        {
            case "rect":
                g.DrawLines(p, [new(3,9),new(3,3),new(9,3)]); g.DrawLines(p,[new(15,3),new(21,3),new(21,9)]);
                g.DrawLines(p,[new(21,15),new(21,21),new(15,21)]);g.DrawLines(p,[new(9,21),new(3,21),new(3,15)]);break;
            case "active": case "window":
                g.DrawRectangle(p,3,4,18,16); g.DrawLine(p,3,9,21,9); g.FillEllipse(fill,5,6,1.5F,1.5F);
                if(kind=="window")g.DrawRectangle(p,10,12,11,9);break;
            case "full":
                g.DrawRectangle(p,2,3,20,15);g.DrawLine(p,12,18,12,22);g.DrawLine(p,7,22,17,22);break;
            case "scroll":
                g.DrawLines(p,[new(15,3),new(4,3),new(4,21),new(15,21)]);g.DrawLine(p,19,4,19,20);g.DrawLines(p,[new(16,17),new(19,20),new(22,17)]);break;
            case "fixed":
                g.DrawRectangle(p,4,4,16,16);g.DrawLine(p,8,2,8,6);g.DrawLine(p,16,18,16,22);g.DrawLine(p,2,16,6,16);g.DrawLine(p,18,8,22,8);break;
            case "free":
                g.DrawClosedCurve(p,[new(4,14),new(5,5),new(12,7),new(19,4),new(20,15),new(13,20),new(7,17)]);break;
            case "repeat":
                g.DrawArc(p,4,4,16,16,25,295);g.DrawLines(p,[new(16,2),new(20,5),new(15,7)]);break;
            case "record":
                g.DrawRectangle(p,3,5,13,14);g.DrawPolygon(p,[new(16,10),new(22,7),new(22,17),new(16,14)]);g.FillEllipse(fill,7,9,5,5);break;
            case "editor":
                g.DrawRectangle(p,3,3,18,18);g.DrawLines(p,[new(5,17),new(10,11),new(14,15),new(17,12),new(21,17)]);g.DrawEllipse(p,14,6,3,3);break;
            case "open":
                g.DrawLines(p,[new(3,19),new(3,4),new(10,4),new(13,7),new(21,7),new(21,10)]);g.DrawPolygon(p,[new(3,19),new(6,10),new(23,10),new(20,19)]);break;
            case "clipboard":
                g.DrawRectangle(p,5,4,14,18);g.DrawRectangle(p,9,2,6,5);g.DrawLine(p,9,12,15,12);g.DrawLine(p,9,16,15,16);break;
            case "settings":
                g.DrawLine(p,4,6,20,6);g.DrawLine(p,4,12,20,12);g.DrawLine(p,4,18,20,18);g.FillEllipse(fill,7,3,6,6);g.FillEllipse(fill,14,9,6,6);g.FillEllipse(fill,5,15,6,6);break;
            case "ruler":
                g.DrawRectangle(p,2,6,20,12);for(int x=6;x<22;x+=4)g.DrawLine(p,x,6,x,x%8==2?13:10);break;
            case "color":
                g.DrawPolygon(p,[new(5,15),new(15,5),new(19,9),new(9,19),new(4,20)]);g.DrawLine(p,14,3,21,10);break;
            case "magnifier":
                g.DrawEllipse(p,3,3,13,13);g.DrawLine(p,15,15,22,22);g.DrawLine(p,9.5F,6,9.5F,13);g.DrawLine(p,6,9.5F,13,9.5F);break;
            default:
                g.DrawEllipse(p,5,5,14,14);g.DrawLine(p,12,1,12,7);g.DrawLine(p,12,17,12,23);g.DrawLine(p,1,12,7,12);g.DrawLine(p,17,12,23,12);break;
        }
        return image;
    }
}
