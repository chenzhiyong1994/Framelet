namespace Framelet;

internal enum PanelEdge { None, Top, Left, Right, Bottom }

internal static class PanelDocking
{
    public static (Point Location, PanelEdge Edge) Snap(Point location, Size size, Rectangle workArea, int threshold)
    {
        Point clamped = Anchor(location, size, workArea, PanelEdge.None);
        var distances = new (PanelEdge Edge, int Distance)[] {
            (PanelEdge.Top, clamped.Y - workArea.Top),
            (PanelEdge.Left, clamped.X - workArea.Left),
            (PanelEdge.Right, workArea.Right - clamped.X - size.Width),
            (PanelEdge.Bottom, workArea.Bottom - clamped.Y - size.Height)
        };
        var nearest = distances.MinBy(candidate => Math.Abs(candidate.Distance));
        PanelEdge edge = Math.Abs(nearest.Distance) <= threshold ? nearest.Edge : PanelEdge.None;
        return (Anchor(clamped, size, workArea, edge), edge);
    }

    public static Point Anchor(Point location, Size size, Rectangle workArea, PanelEdge edge)
    {
        int right = Math.Max(workArea.Left, workArea.Right - size.Width);
        int bottom = Math.Max(workArea.Top, workArea.Bottom - size.Height);
        int x = Math.Clamp(location.X, workArea.Left, right);
        int y = Math.Clamp(location.Y, workArea.Top, bottom);
        return edge switch {
            PanelEdge.Top => new Point(x, workArea.Top),
            PanelEdge.Left => new Point(workArea.Left, y),
            PanelEdge.Right => new Point(right, y),
            PanelEdge.Bottom => new Point(x, bottom),
            _ => new Point(x, y)
        };
    }
}
