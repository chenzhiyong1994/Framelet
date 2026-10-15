using System.Drawing.Imaging;

namespace Framelet;

public sealed class ScrollCaptureSession : IDisposable
{
    private List<Bitmap> strips = [];
    private Bitmap? previous;
    private bool disposed;

    public int FrameCount { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public ScrollFrameMatch Append(Bitmap frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        var match = previous == null ? new ScrollFrameMatch(ScrollMatchStatus.Matched) : ImageStitcher.Analyze(previous, frame);
        if (match.Status != ScrollMatchStatus.Matched) return match;
        long newHeight = previous == null ? frame.Height : (long)Height + match.VerticalShift;
        if (newHeight > 64000 || newHeight * frame.Width > 80_000_000)
            throw new ArgumentException("拼接图像已达到尺寸上限，请完成当前截图。", nameof(frame));
        var retained = new List<Bitmap>(strips);
        var replaced = new List<Bitmap>();
        var created = new List<Bitmap>();
        Bitmap? newPrevious = null;
        try
        {
            int trim = match.FixedBottom;
            while (trim > 0)
            {
                var last = retained[^1];
                retained.RemoveAt(retained.Count - 1);
                replaced.Add(last);
                if (trim < last.Height)
                {
                    var shortened = CopyRows(last, 0, last.Height - trim);
                    created.Add(shortened);
                    retained.Add(shortened);
                    trim = 0;
                }
                else trim -= last.Height;
            }
            int start = previous == null ? 0 : frame.Height - match.FixedBottom - match.VerticalShift;
            var tail = CopyRows(frame, start, frame.Height - start);
            created.Add(tail);
            retained.Add(tail);
            newPrevious = CopyRows(frame, 0, frame.Height);
        }
        catch
        {
            foreach (var bitmap in created) bitmap.Dispose();
            newPrevious?.Dispose();
            throw;
        }
        strips = retained;
        foreach (var bitmap in replaced) bitmap.Dispose();
        previous?.Dispose();
        previous = newPrevious;
        Width = frame.Width;
        Height = checked((int)newHeight);
        FrameCount++;
        return match;
    }

    public Bitmap ToBitmap()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (previous == null) throw new InvalidOperationException("尚未采集任何图像。");
        var result = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(result);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            int top = 0;
            foreach (var strip in strips)
            {
                graphics.DrawImageUnscaled(strip, 0, top);
                top += strip.Height;
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var strip in strips) strip.Dispose();
        strips.Clear();
        previous?.Dispose();
        previous = null;
    }

    private static Bitmap CopyRows(Bitmap source, int top, int height)
        => source.Clone(new Rectangle(0, top, source.Width, height), PixelFormat.Format32bppArgb);
}
