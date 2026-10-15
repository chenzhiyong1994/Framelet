using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Framelet;

public enum ScrollMatchStatus { Matched, Duplicate, Upward, Unmatched }

public readonly record struct ScrollFrameMatch(ScrollMatchStatus Status, int VerticalShift = 0, int FixedTop = 0, int FixedBottom = 0);

public static class ImageStitcher
{
    public static Bitmap Stitch(IReadOnlyList<Bitmap> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0) throw new ArgumentException("至少需要一张图片。", nameof(frames));
        if (frames.Any(frame => frame == null)) throw new ArgumentException("输入列表不能包含空图片。", nameof(frames));
        using var session = new ScrollCaptureSession();
        foreach (var frame in frames)
        {
            var match = session.Append(frame);
            if (match.Status == ScrollMatchStatus.Upward)
                throw new InvalidOperationException("检测到向上滚动，请向下滚动后继续采集。");
            if (match.Status == ScrollMatchStatus.Unmatched)
                throw new InvalidOperationException("无法可靠对齐滚动内容，请缩小滚动距离或重新选择内容区域。");
        }
        return session.ToBitmap();
    }

    public static int FindVerticalOverlap(Bitmap previous, Bitmap next)
    {
        var match = Analyze(previous, next);
        return match.Status switch
        {
            ScrollMatchStatus.Matched => previous.Height - match.VerticalShift,
            ScrollMatchStatus.Duplicate => previous.Height,
            _ => 0
        };
    }

    public static bool AreEquivalent(Bitmap previous, Bitmap next)
    {
        ValidateDimensions(previous, next);
        return AreEquivalent(Pixels(previous), Pixels(next), previous.Width, previous.Height);
    }

    public static ScrollFrameMatch Analyze(Bitmap previous, Bitmap next)
    {
        ValidateDimensions(previous, next);
        int width = previous.Width, height = previous.Height;
        var upper = Pixels(previous);
        var lower = Pixels(next);
        if (AreEquivalent(upper, lower, width, height)) return new(ScrollMatchStatus.Duplicate);
        var columns = ActiveColumns(upper, lower, width, height);
        if (columns.Length < 4) return new(ScrollMatchStatus.Unmatched);
        int fixedTop = 0, fixedBottom = 0;
        while (fixedTop < height && SameRow(upper, lower, width, fixedTop, columns)) fixedTop++;
        while (fixedBottom < height - fixedTop && SameRow(upper, lower, width, height - fixedBottom - 1, columns)) fixedBottom++;
        int minimumOverlap = Math.Min(height, Math.Max(16, Math.Min(96, height / 5)));
        var candidates = CandidateShifts(upper, lower, width, height, columns, fixedTop, fixedBottom, minimumOverlap);
        var matches = new List<(int Shift, double Score)>();
        foreach (int shift in candidates)
        {
            double score = AlignmentScore(upper, lower, width, height, columns, fixedTop, fixedBottom, shift, minimumOverlap);
            if (!double.IsPositiveInfinity(score)) matches.Add((shift, score));
        }
        if (matches.Count == 0) return new(ScrollMatchStatus.Unmatched);
        matches.Sort((left, right) => left.Score.CompareTo(right.Score));
        var best = matches[0];
        if (matches.Count > 1 && matches[1].Score <= best.Score + 0.5)
            return new(ScrollMatchStatus.Unmatched);
        return new(best.Shift > 0 ? ScrollMatchStatus.Matched : ScrollMatchStatus.Upward, best.Shift, fixedTop, fixedBottom);
    }

    private static int[] ActiveColumns(int[] upper, int[] lower, int width, int height)
    {
        int inset = Math.Min(12, width / 10);
        int count = Math.Min(192, width - inset * 2);
        var columns = new List<int>(count);
        for (int sample = 0; sample < count; sample++)
        {
            int column = inset + sample * (width - inset * 2 - 1) / Math.Max(1, count - 1);
            int changed = 0;
            for (int row = 0; row < height; row++)
                if (Different(upper[row * width + column], lower[row * width + column])) changed++;
            if (changed >= Math.Max(3, height / 250)) columns.Add(column);
        }
        return columns.ToArray();
    }

    private static bool SameRow(int[] upper, int[] lower, int width, int row, int[] columns)
    {
        int changed = 0;
        foreach (int column in columns)
            if (Different(upper[row * width + column], lower[row * width + column])) changed++;
        return changed <= columns.Length / 100;
    }

    private static IEnumerable<int> CandidateShifts(int[] upper, int[] lower, int width, int height, int[] columns,
        int fixedTop, int fixedBottom, int minimumOverlap)
    {
        int groupCount = columns.Length < 24 ? 1 : 3;
        var votes = new Dictionary<int, int>();
        for (int group = 0; group < groupCount; group++)
        {
            int firstColumn = group * columns.Length / groupCount;
            int lastColumn = (group + 1) * columns.Length / groupCount;
            var rows = new Dictionary<ulong, List<int>>();
            for (int row = fixedTop; row < height - fixedBottom; row++)
            {
                ulong signature = Signature(upper, width, row, columns, firstColumn, lastColumn);
                if (!rows.TryGetValue(signature, out var positions)) rows.Add(signature, positions = []);
                if (positions.Count <= 24) positions.Add(row);
            }
            for (int row = fixedTop; row < height - fixedBottom; row++)
            {
                ulong signature = Signature(lower, width, row, columns, firstColumn, lastColumn);
                if (!rows.TryGetValue(signature, out var positions) || positions.Count > 24) continue;
                foreach (int previousRow in positions)
                {
                    int shift = previousRow - row;
                    if (shift == 0 || Math.Abs(shift) > height - fixedTop - fixedBottom - minimumOverlap) continue;
                    votes.TryGetValue(shift, out int count);
                    votes[shift] = count + 1;
                }
            }
        }
        return votes.Where(pair => pair.Value >= 4).OrderByDescending(pair => pair.Value).Take(20).Select(pair => pair.Key);
    }

    private static ulong Signature(int[] pixels, int width, int row, int[] columns, int first, int last)
    {
        ulong signature = 14695981039346656037;
        for (int index = first; index < last; index++)
        {
            signature ^= (uint)pixels[row * width + columns[index]] & 0x00FCFCFC;
            signature *= 1099511628211;
        }
        return signature;
    }

    private static double AlignmentScore(int[] upper, int[] lower, int width, int height, int[] columns,
        int fixedTop, int fixedBottom, int shift, int minimumOverlap)
    {
        int start = fixedTop + Math.Max(0, -shift);
        int end = height - fixedBottom - Math.Max(0, shift);
        if (end - start < minimumOverlap) return double.PositiveInfinity;
        long featureDifference = 0;
        int featureCount = 0, featureChanged = 0, featureRows = 0, changed = 0, samples = 0;
        for (int row = start; row < end; row++)
        {
            int rowFeatures = 0;
            foreach (int column in columns)
            {
                int upperOffset = (row + shift) * width + column;
                int lowerOffset = row * width + column;
                int upperColor = upper[upperOffset], lowerColor = lower[lowerOffset];
                bool differs = Different(upperColor, lowerColor);
                if (differs) changed++;
                samples++;
                bool feature = column > 0 && (Different(upperColor, upper[upperOffset - 1]) || Different(lowerColor, lower[lowerOffset - 1]));
                if (row > start)
                    feature |= Different(upperColor, upper[upperOffset - width]) || Different(lowerColor, lower[lowerOffset - width]);
                if (!feature) continue;
                featureCount++;
                rowFeatures++;
                if (differs) featureChanged++;
                featureDifference += Difference(upperColor, lowerColor);
            }
            if (rowFeatures > 0) featureRows++;
        }
        if (featureCount < 48 || featureRows < 8 || (double)changed / samples > 0.025 || (double)featureChanged / featureCount > 0.04)
            return double.PositiveInfinity;
        double score = (double)featureDifference / (featureCount * 3);
        return score <= 3.5 ? score : double.PositiveInfinity;
    }

    private static bool AreEquivalent(int[] upper, int[] lower, int width, int height)
    {
        int step = width * (long)height > 1_000_000 ? 2 : 1;
        int sampleCount = ((width + step - 1) / step) * ((height + step - 1) / step);
        int allowance = Math.Max(2, sampleCount / 2500), changed = 0;
        int tileColumns = (width + 63) / 64;
        var tileChanges = new int[tileColumns * ((height + 63) / 64)];
        int tileAllowance = 48 / (step * step);
        for (int row = 0; row < height; row += step)
            for (int column = 0; column < width; column += step)
            {
                if (!Different(upper[row * width + column], lower[row * width + column])) continue;
                if (++changed > allowance || ++tileChanges[row / 64 * tileColumns + column / 64] > tileAllowance) return false;
            }
        return true;
    }

    private static bool Different(int left, int right)
        => Math.Abs((left & 255) - (right & 255)) > 18
            || Math.Abs(((left >> 8) & 255) - ((right >> 8) & 255)) > 18
            || Math.Abs(((left >> 16) & 255) - ((right >> 16) & 255)) > 18;

    private static int Difference(int left, int right)
        => Math.Abs((left & 255) - (right & 255)) + Math.Abs(((left >> 8) & 255) - ((right >> 8) & 255))
            + Math.Abs(((left >> 16) & 255) - ((right >> 16) & 255));

    private static void ValidateDimensions(Bitmap previous, Bitmap next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        if (previous.Size != next.Size) throw new ArgumentException("滚动截图必须具有相同尺寸。", nameof(next));
    }

    private static int[] Pixels(Bitmap input)
    {
        using var bitmap = input.Clone(new Rectangle(Point.Empty, input.Size), PixelFormat.Format32bppArgb);
        var locked = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new int[bitmap.Width * bitmap.Height];
            for (int row = 0; row < bitmap.Height; row++)
                Marshal.Copy(IntPtr.Add(locked.Scan0, row * locked.Stride), pixels, row * bitmap.Width, bitmap.Width);
            return pixels;
        }
        finally { bitmap.UnlockBits(locked); }
    }
}
