using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;

namespace Framelet;

public enum AnnotationTool { Select, Pen, Line, Arrow, Rectangle, Ellipse, Text, Highlight, Mosaic, Number }

/// <summary>An owned image and revision history. UI coordinates never enter this model.</summary>
public sealed class EditorDocument : IDisposable
{
    private sealed record Revision(Bitmap Bitmap, long Id);
    private readonly List<Revision> undo = [];
    private readonly List<Revision> redo = [];
    private long revision;
    private long nextRevision;
    private long savedRevision;
    private bool disposed;

    public Bitmap Image { get; private set; }
    public string? FilePath { get; private set; }
    public bool IsDirty => revision != savedRevision;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public event EventHandler? Changed;

    public EditorDocument(Bitmap image, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        Image = Copy(image);
        FilePath = filePath;
        savedRevision = filePath is null ? -1 : 0;
    }

    public static Bitmap Copy(System.Drawing.Image source)
    {
        var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImage(source, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }

    public void Crop(Rectangle bounds)
    {
        bounds.Intersect(new Rectangle(Point.Empty, Image.Size));
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentException("请选择图像内的有效区域。", nameof(bounds));
        if (bounds == new Rectangle(Point.Empty, Image.Size)) return;
        Commit(Image.Clone(bounds, PixelFormat.Format32bppArgb));
    }

    public void Resize(int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > 80_000_000)
            throw new ArgumentOutOfRangeException(nameof(width), "图像尺寸必须为正数，且总像素不超过 8000 万。");
        if (Image.Width == width && Image.Height == height) return;
        var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(resized))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(Image, new Rectangle(0, 0, width, height), 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel, attributes);
        }
        Commit(resized);
    }

    public void Rotate(RotateFlipType operation) => Edit(image => image.RotateFlip(operation));

    public void ReplaceImage(Bitmap image) => Commit(Copy(image));

    public static Bitmap Combine(IReadOnlyList<Bitmap> images, bool vertically, int spacing = 12)
    {
        if (images.Count == 0) throw new ArgumentException("至少需要一张图像。", nameof(images));
        spacing = Math.Clamp(spacing, 0, 1000);
        long totalLength = images.Sum(x => vertically ? (long)x.Height : x.Width) + (long)(images.Count - 1) * spacing;
        int crossLength = images.Max(x => vertically ? x.Width : x.Height);
        if (totalLength > 50000 || totalLength * crossLength > 80_000_000) throw new ArgumentException("合并后图像过大，请减少图像数量或先缩小尺寸。");
        int width = vertically ? crossLength : (int)totalLength, height = vertically ? (int)totalLength : crossLength;
        var result = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(result);
        graphics.Clear(Color.White);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        int position = 0;
        foreach (var image in images)
        {
            graphics.DrawImageUnscaled(image, vertically ? (width - image.Width) / 2 : position, vertically ? position : (height - image.Height) / 2);
            position += (vertically ? image.Height : image.Width) + spacing;
        }
        return result;
    }

    public void Grayscale() => ApplyMatrix(new ColorMatrix([
        [.299f, .299f, .299f, 0, 0], [.587f, .587f, .587f, 0, 0],
        [.114f, .114f, .114f, 0, 0], [0, 0, 0, 1, 0], [0, 0, 0, 0, 1]]));

    public void Invert() => ApplyMatrix(new ColorMatrix([
        [-1, 0, 0, 0, 0], [0, -1, 0, 0, 0], [0, 0, -1, 0, 0], [0, 0, 0, 1, 0], [1, 1, 1, 0, 1]]));

    public void Brightness(float amount)
    {
        amount = Math.Clamp(amount, -1, 1);
        ApplyMatrix(new ColorMatrix([[1, 0, 0, 0, 0], [0, 1, 0, 0, 0], [0, 0, 1, 0, 0],
            [0, 0, 0, 1, 0], [amount, amount, amount, 0, 1]]));
    }

    public void AddBorder(int thickness, Color color)
    {
        thickness = Math.Clamp(thickness, 1, 200);
        ValidateExpansion(Image.Width + (long)thickness * 2, Image.Height + (long)thickness * 2);
        var bitmap = new Bitmap(Image.Width + thickness * 2, Image.Height + thickness * 2);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(color);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(Image, thickness, thickness);
        }
        Commit(bitmap);
    }

    public void AddCaption(string text, Color foreground, Color background, int fontSize)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        using var font = new Font("Microsoft YaHei UI", Math.Clamp(fontSize, 8, 150), FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        int margin = Math.Min(8, Image.Width / 8);
        int availableWidth = Math.Max(1, Image.Width - margin * 2);
        int height;
        using (var measure = Graphics.FromImage(Image))
        {
            measure.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var measured = measure.MeasureString(text, font, new SizeF(availableWidth, 1000000), format);
            height = Math.Max(32, (int)Math.Ceiling(measured.Height) + 24);
        }
        ValidateExpansion(Image.Width, (long)Image.Height + height);
        var bitmap = new Bitmap(Image.Width, Image.Height + height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(background);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(Image, 0, 0);
            g.CompositingMode = CompositingMode.SourceOver;
            using var brush = new SolidBrush(foreground);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.DrawString(text, font, brush, new RectangleF(margin, Image.Height + 12, availableWidth, height - 24), format);
        }
        Commit(bitmap);
    }

    public void Mosaic(Rectangle bounds, int blockSize = 12)
    {
        bounds.Intersect(new Rectangle(Point.Empty, Image.Size));
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;
        blockSize = Math.Clamp(blockSize, 2, 100);
        Edit(bitmap =>
        {
            using var g = Graphics.FromImage(bitmap);
            g.CompositingMode = CompositingMode.SourceCopy;
            for (int y = bounds.Top; y < bounds.Bottom; y += blockSize)
                for (int x = bounds.Left; x < bounds.Right; x += blockSize)
                {
                    int w = Math.Min(blockSize, bounds.Right - x), h = Math.Min(blockSize, bounds.Bottom - y);
                    long r = 0, green = 0, b = 0, alpha = 0;
                    int samples = 0;
                    int stride = Math.Max(1, blockSize / 4);
                    for (int sy = y; sy < y + h; sy += stride)
                        for (int sx = x; sx < x + w; sx += stride)
                        {
                            var pixel = Image.GetPixel(sx, sy);
                            r += pixel.R; green += pixel.G; b += pixel.B; alpha += pixel.A; samples++;
                        }
                    using var brush = new SolidBrush(Color.FromArgb((int)(alpha / samples), (int)(r / samples), (int)(green / samples), (int)(b / samples)));
                    g.FillRectangle(brush, x, y, w, h);
                }
        });
    }

    public void Annotate(AnnotationTool tool, PointF start, PointF end, Color color, float width,
        string? text = null, IReadOnlyList<PointF>? points = null, int number = 1, float fontSize = 24)
    {
        if (tool == AnnotationTool.Select) return;
        if (tool == AnnotationTool.Mosaic)
        {
            Mosaic(Rectangle.Round(Normalized(start, end)), Math.Max(4, (int)width * 4));
            return;
        }
        Edit(bitmap =>
        {
            using var g = Graphics.FromImage(bitmap);
            RenderAnnotation(g, tool, start, end, color, width, text, points, number, fontSize);
        });
    }

    public static RectangleF Normalized(PointF start, PointF end) =>
        RectangleF.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));

    public static void RenderAnnotation(Graphics g, AnnotationTool tool, PointF start, PointF end, Color color,
        float width, string? text = null, IReadOnlyList<PointF>? points = null, int number = 1, float fontSize = 24)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        width = Math.Clamp(width, 1, 50);
        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);
        var bounds = Normalized(start, end);
        switch (tool)
        {
            case AnnotationTool.Pen:
                if (points?.Count > 1) g.DrawLines(pen, points.ToArray());
                else g.FillEllipse(brush, start.X - width / 2, start.Y - width / 2, width, width);
                break;
            case AnnotationTool.Line: g.DrawLine(pen, start, end); break;
            case AnnotationTool.Arrow:
                using (var arrow = new AdjustableArrowCap(4, 5, true))
                {
                    pen.CustomEndCap = arrow;
                    g.DrawLine(pen, start, end);
                }
                break;
            case AnnotationTool.Rectangle:
                if (bounds.Width > 0 && bounds.Height > 0) g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                break;
            case AnnotationTool.Ellipse:
                if (bounds.Width > 0 && bounds.Height > 0) g.DrawEllipse(pen, bounds);
                break;
            case AnnotationTool.Highlight:
                using (var marker = new SolidBrush(Color.FromArgb(85, color))) g.FillRectangle(marker, bounds);
                break;
            case AnnotationTool.Text:
                using (var font = new Font("Microsoft YaHei UI", fontSize, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.DrawString(text, font, brush, start);
                }
                break;
            case AnnotationTool.Number:
                float size = Math.Max(24, fontSize * 1.5f);
                var circle = new RectangleF(start.X - size / 2, start.Y - size / 2, size, size);
                g.FillEllipse(brush, circle);
                using (var outline = new Pen(Color.White, 1.5f)) g.DrawEllipse(outline, circle);
                using (var font = new Font("Segoe UI", fontSize * .8f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(number.ToString(), font, Brushes.White, circle, format);
                break;
        }
    }

    public void Edit(Action<Bitmap> edit)
    {
        var copy = Copy(Image);
        try { edit(copy); }
        catch { copy.Dispose(); throw; }
        Commit(copy);
    }

    public void Undo()
    {
        if (!CanUndo) return;
        redo.Add(new Revision(Image, revision));
        var previous = undo[^1]; undo.RemoveAt(undo.Count - 1);
        Image = previous.Bitmap; revision = previous.Id;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        undo.Add(new Revision(Image, revision));
        var next = redo[^1]; redo.RemoveAt(redo.Count - 1);
        Image = next.Bitmap; revision = next.Id;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Save(string path)
        => WriteFile(path, true);

    public void Export(string path)
        => WriteFile(path, false);

    private void WriteFile(string path, bool markSaved)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        var format = extension switch
        {
            ".png" => ImageFormat.Png, ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".bmp" => ImageFormat.Bmp, ".gif" => ImageFormat.Gif, ".tif" or ".tiff" => ImageFormat.Tiff,
            ".pdf" => null, _ => throw new NotSupportedException("支持 PNG、JPEG、BMP、GIF、TIFF 和 PDF 格式。")
        };
        string fullPath = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            if (extension == ".pdf") WritePdf(Image, temporary);
            else if (format == ImageFormat.Jpeg)
            {
                using var flattened = Flatten(Image);
                var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var parameters = new EncoderParameters(1);
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
                flattened.Save(temporary, encoder, parameters);
            }
            else Image.Save(temporary, format!);
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        if (markSaved)
        {
            FilePath = fullPath; savedRevision = revision;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyMatrix(ColorMatrix matrix)
    {
        var result = new Bitmap(Image.Width, Image.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        using (var attributes = new ImageAttributes())
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            attributes.SetColorMatrix(matrix);
            g.DrawImage(Image, new Rectangle(Point.Empty, Image.Size), 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel, attributes);
        }
        Commit(result);
    }

    private static void ValidateExpansion(long width, long height)
    {
        if (width < 1 || height < 1 || width > 50000 || height > 50000 || width * height > 80_000_000)
            throw new ArgumentException("处理后图像过大：边长不能超过 50,000 像素，总像素不能超过 8000 万。请缩小图像或减少标题文字。");
    }

    private void Commit(Bitmap next)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        undo.Add(new Revision(Image, revision));
        foreach (var item in redo) item.Bitmap.Dispose();
        redo.Clear();
        Image = next; revision = ++nextRevision;
        long historyBytes = undo.Sum(x => (long)x.Bitmap.Width * x.Bitmap.Height * 4);
        while (undo.Count > 1 && (undo.Count > 30 || historyBytes > 256L * 1024 * 1024))
        {
            historyBytes -= (long)undo[0].Bitmap.Width * undo[0].Bitmap.Height * 4;
            undo[0].Bitmap.Dispose(); undo.RemoveAt(0);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static Bitmap Flatten(Bitmap image)
    {
        var result = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(result);
        g.Clear(Color.White); g.DrawImageUnscaled(image, 0, 0);
        return result;
    }

    private static void WritePdf(Bitmap image, string path)
    {
        using var flattened = Flatten(image);
        using var jpeg = new MemoryStream();
        flattened.Save(jpeg, ImageFormat.Jpeg);
        byte[] pixels = jpeg.ToArray();
        using var output = File.Create(path);
        var offsets = new List<long> { 0 };
        void Write(string value) { byte[] bytes = Encoding.ASCII.GetBytes(value); output.Write(bytes); }
        void Object(int id, string value) { offsets.Add(output.Position); Write($"{id} 0 obj\n{value}\nendobj\n"); }
        float scale = Math.Min(1, 14400f / Math.Max(image.Width, image.Height));
        string w = (image.Width * .75f * scale).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        string h = (image.Height * .75f * scale).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        Write("%PDF-1.4\n");
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object(3, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {w} {h}] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>");
        offsets.Add(output.Position);
        Write($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {pixels.Length} >>\nstream\n");
        output.Write(pixels); Write("\nendstream\nendobj\n");
        string content = $"q\n{w} 0 0 {h} 0 0 cm\n/Im0 Do\nQ\n";
        Object(5, $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream");
        long xref = output.Position;
        Write("xref\n0 6\n0000000000 65535 f \n");
        foreach (long offset in offsets.Skip(1)) Write(offset.ToString("D10") + " 00000 n \n");
        Write($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Image.Dispose();
        foreach (var item in undo.Concat(redo)) item.Bitmap.Dispose();
        undo.Clear(); redo.Clear();
    }
}
