using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Framelet;

/// <summary>Vertical screenshot stitching using RGB overlap matching. Input ownership remains with the caller.</summary>
public static class ImageStitcher
{
    public static Bitmap Stitch(IReadOnlyList<Bitmap> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0) throw new ArgumentException("至少需要一张图片。", nameof(frames));
        if (frames.Any(frame => frame == null)) throw new ArgumentException("输入列表不能包含空图片。", nameof(frames));
        int width = frames[0].Width;
        if (frames.Any(frame => frame.Width != width))
            throw new ArgumentException("滚动截图必须具有相同宽度。", nameof(frames));
        int[] overlap = new int[frames.Count];
        long height = frames[0].Height;
        for (int index = 1; index < frames.Count; index++)
        {
            overlap[index] = FindVerticalOverlap(frames[index - 1], frames[index]);
            height += frames[index].Height - overlap[index];
        }
        if (height > 64000 || height * width > 80_000_000)
            throw new ArgumentException("拼接图像过大，请减少采集帧数或区域宽度。", nameof(frames));
        var result = new Bitmap(width, checked((int)height), PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(result);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            int y = 0;
            for (int index = 0; index < frames.Count; index++)
            {
                int remaining = frames[index].Height - overlap[index];
                if (remaining == 0) continue;
                graphics.DrawImage(frames[index], new Rectangle(0, y, width, remaining),
                    new Rectangle(0, overlap[index], width, remaining), GraphicsUnit.Pixel);
                y += remaining;
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    public static int FindVerticalOverlap(Bitmap previous, Bitmap next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        if (previous.Width != next.Width) throw new ArgumentException("图像宽度不一致。", nameof(next));
        var upper = Pixels(previous);
        var lower = Pixels(next);
        int height = Math.Min(previous.Height, next.Height), width = previous.Width;
        if (height == 0 || width == 0) return 0;
        double bestScore = double.MaxValue;
        int bestOverlap = 0;
        // Exact repeats are common when a page cannot scroll any further.
        if (previous.Height == next.Height && upper.AsSpan().SequenceEqual(lower))
            return height;
        int minimum = Math.Min(height, Math.Max(12, Math.Min(32, height / 10)));
        for (int overlap = minimum; overlap < height; overlap++)
        {
            double score = Score(upper, lower, width, previous.Height, overlap);
            if (score < bestScore - 0.01 || (Math.Abs(score - bestScore) <= 0.01 && overlap > bestOverlap))
            {
                bestScore = score;
                bestOverlap = overlap;
            }
        }
        // No reliable alignment: preserve all pixels rather than discarding unproven overlap.
        return bestScore <= 3.5 ? bestOverlap : 0;
    }

    private static double Score(int[] upper, int[] lower, int width, int upperHeight, int overlap)
    {
        int rows = Math.Min(overlap, 48), columns = Math.Min(width, 32);
        long difference = 0;
        int samples = 0;
        for (int row = 0; row < rows; row++)
        {
            int y = rows == 1 ? 0 : row * (overlap - 1) / (rows - 1);
            for (int column = 0; column < columns; column++)
            {
                // Avoid a small border/scrollbar at either side when possible.
                int inset = Math.Min(12, width / 8);
                int x = columns == 1 ? 0 : inset + column * (width - 1 - inset * 2) / (columns - 1);
                int a = upper[(upperHeight - overlap + y) * width + x], b = lower[y * width + x];
                difference += Math.Abs((a & 255) - (b & 255)) + Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)) + Math.Abs(((a >> 16) & 255) - ((b >> 16) & 255));
                samples++;
            }
        }
        return (double)difference / (samples * 3);
    }

    private static int[] Pixels(Bitmap input)
    {
        using var bitmap = input.Clone(new Rectangle(Point.Empty, input.Size), PixelFormat.Format32bppArgb);
        var locked = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new int[bitmap.Width * bitmap.Height];
            for (int y = 0; y < bitmap.Height; y++)
                Marshal.Copy(IntPtr.Add(locked.Scan0, y * locked.Stride), pixels, y * bitmap.Width, bitmap.Width);
            return pixels;
        }
        finally { bitmap.UnlockBits(locked); }
    }
}
