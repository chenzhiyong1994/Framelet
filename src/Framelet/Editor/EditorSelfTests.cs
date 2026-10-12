using System.Drawing.Imaging;

namespace Framelet;

/// <summary>Behavior checks for the independently implemented editor model.</summary>
public static class EditorSelfTests
{
    public static List<string> Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var results = new List<string>();
        Check("editor and drawing windows construct with valid menu shortcuts", () =>
        {
            using var image = new Bitmap(80, 60);
            using var editor = new EditorForm(image, Path.Combine(directory, "ui-source.png"));
            Assert(editor.MainMenuStrip != null, "editor has an initialized menu");
            editor.AddImage(image);
            Assert(editor.Text.Contains("截图"), "adding a second image selects its tab");
            using var draw = new DrawForm(image);
            Assert(draw.EditedImage.Size == image.Size && !draw.HasChanges, "drawing window starts with an unmodified owned image");
            foreach (var window in new Form[] { editor, draw })
            {
                LayoutTree(window);
                var header = window.Controls.Find("FrameletHeader", true).Single();
                foreach (Control child in header.Controls)
                    Assert(header.ClientRectangle.Contains(child.Bounds), "brand header children remain inside the header");
            }
        });
        Check("drawing actions remain inside the footer without covering options at default and minimum sizes", () =>
        {
            using var image = new Bitmap(80, 60);
            foreach (bool minimum in new[] { false, true })
            {
                using var draw = new DrawForm(image);
                if (minimum) draw.Size = draw.MinimumSize;
                LayoutTree(draw);
                var footer = draw.Controls.Find("DrawingFooter", true).Single();
                var options = draw.Controls.Find("DrawingOptions", true).Single();
                var hint = draw.Controls.Find("DrawingHint", true).Single();
                var buttons = Descendants(footer).OfType<Button>().Where(x => x.DialogResult is DialogResult.OK or DialogResult.Cancel).ToArray();
                Equal(2, buttons.Length);
                var buttonBounds = buttons.Select(x => BoundsWithin(x, footer)).ToArray();
                foreach (var bounds in buttonBounds)
                {
                    Assert(bounds.Width > 0 && bounds.Height > 0 && footer.ClientRectangle.Contains(bounds), "action must be fully inside footer");
                    Assert(!bounds.IntersectsWith(BoundsWithin(options, footer)), "action must not overlap drawing options");
                    Assert(!bounds.IntersectsWith(BoundsWithin(hint, footer)), "action must not overlap hint");
                }
                Assert(!buttonBounds[0].IntersectsWith(buttonBounds[1]), "complete and cancel must not overlap");
            }
        });
        Check("crop selects exact pixels and can be undone/redone", () =>
        {
            using var input = new Bitmap(4, 3);
            input.SetPixel(1, 1, Color.Red);
            input.SetPixel(2, 1, Color.Blue);
            using var doc = new EditorDocument(input);
            input.SetPixel(1, 1, Color.Green);
            doc.Crop(new Rectangle(1, 1, 2, 1));
            Equal(2, doc.Image.Width);
            Equal(1, doc.Image.Height);
            Equal(Color.Red.ToArgb(), doc.Image.GetPixel(0, 0).ToArgb());
            Equal(Color.Blue.ToArgb(), doc.Image.GetPixel(1, 0).ToArgb());
            Assert(doc.IsDirty, "editing marks document dirty");
            doc.Undo();
            Equal(4, doc.Image.Width);
            Equal(Color.Red.ToArgb(), doc.Image.GetPixel(1, 1).ToArgb());
            doc.Redo();
            Equal(2, doc.Image.Width);
        });
        Check("save checkpoint survives undo and a new edit invalidates redo", () =>
        {
            using var input = new Bitmap(3, 2);
            using var doc = new EditorDocument(input);
            string path = Path.Combine(directory, "checkpoint.png");
            doc.Save(path);
            Assert(!doc.IsDirty, "saved image is clean");
            doc.Resize(6, 4);
            Equal(new Size(6, 4), doc.Image.Size);
            doc.Undo();
            Assert(!doc.IsDirty, "undo restores the saved revision");
            doc.Invert();
            Assert(!doc.CanRedo, "new edits discard redo history");
            Assert(doc.IsDirty, "new edit is dirty");
        });
        Check("rotation, inverse colors and grayscale produce expected pixels", () =>
        {
            using var input = new Bitmap(2, 1);
            input.SetPixel(0, 0, Color.FromArgb(255, 10, 40, 90));
            input.SetPixel(1, 0, Color.Red);
            using var doc = new EditorDocument(input);
            doc.Invert();
            Equal(Color.FromArgb(255, 245, 215, 165).ToArgb(), doc.Image.GetPixel(0, 0).ToArgb());
            doc.Undo();
            doc.Rotate(RotateFlipType.Rotate90FlipNone);
            Equal(new Size(1, 2), doc.Image.Size);
            Equal(Color.Red.ToArgb(), doc.Image.GetPixel(0, 1).ToArgb());
            doc.Grayscale();
            var gray = doc.Image.GetPixel(0, 1);
            Assert(gray.R >= 75 && gray.R <= 77 && gray.R == gray.G && gray.G == gray.B, "red luminance becomes about 76");
        });
        Check("mosaic changes only selected region and drawing is undoable", () =>
        {
            using var input = new Bitmap(20, 20);
            using (var graphics = Graphics.FromImage(input)) graphics.Clear(Color.White);
            input.SetPixel(5, 5, Color.Black);
            using var doc = new EditorDocument(input);
            doc.Mosaic(new Rectangle(4, 4, 4, 4), 4);
            Equal(Color.White.ToArgb(), doc.Image.GetPixel(0, 0).ToArgb());
            Assert(doc.Image.GetPixel(5, 5).R > 0, "mosaic replaces the selected black pixel");
            doc.Annotate(AnnotationTool.Line, new PointF(2, 10), new PointF(18, 10), Color.Red, 3);
            Assert(doc.Image.GetPixel(10, 10).R > 240 && doc.Image.GetPixel(10, 10).G < 10, "line is drawn in image coordinates");
            doc.Undo();
            Equal(Color.White.ToArgb(), doc.Image.GetPixel(10, 10).ToArgb());
        });
        Check("all advertised export formats produce readable files", () =>
        {
            using var input = new Bitmap(24, 16);
            using (var g = Graphics.FromImage(input)) g.Clear(Color.CornflowerBlue);
            using var doc = new EditorDocument(input);
            foreach (var extension in new[] { ".png", ".jpg", ".bmp", ".gif", ".tiff" })
            {
                string path = Path.Combine(directory, "export" + extension);
                doc.Save(path);
                using var loaded = System.Drawing.Image.FromFile(path);
                Equal(new Size(24, 16), loaded.Size);
            }
            string pdf = Path.Combine(directory, "export.pdf");
            doc.Save(pdf);
            string source = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf));
            Assert(source.StartsWith("%PDF-1.4"), "PDF header");
            Assert(source.Contains("/Width 24 /Height 16"), "PDF contains the original dimensions");
            string offset = source.Split("startxref\n")[1].Split('\n')[0];
            Assert(source.Substring(int.Parse(offset)).StartsWith("xref\n"), "PDF cross-reference points at the actual table");
        });
        Check("zoomed scrolled selection maps to original image pixels", () =>
        {
            var origin = EditorViewport.Origin(new Size(800, 600), new Size(1000, 800), 2, new Point(-500, -300), false);
            Equal(new PointF(-482, -282), origin);
            Equal(new PointF(300, 200), EditorViewport.ToImage(new PointF(118, 118), origin, 2));
            var centered = EditorViewport.Origin(new Size(800, 600), new Size(100, 80), .5f, Point.Empty, true);
            Equal(new PointF(375, 280), centered);
            Equal(new PointF(20, 30), EditorViewport.ToImage(new PointF(385, 295), centered, .5f));
        });
        Check("combining images retains order, spacing and original pixels", () =>
        {
            using var red = new Bitmap(3, 2);
            using var blue = new Bitmap(2, 4);
            using (var graphics = Graphics.FromImage(red)) graphics.Clear(Color.Red);
            using (var graphics = Graphics.FromImage(blue)) graphics.Clear(Color.Blue);
            using var combined = EditorDocument.Combine([red, blue], true, 2);
            Equal(new Size(3, 8), combined.Size);
            Equal(Color.Red.ToArgb(), combined.GetPixel(1, 1).ToArgb());
            Equal(Color.White.ToArgb(), combined.GetPixel(1, 2).ToArgb());
            Equal(Color.Blue.ToArgb(), combined.GetPixel(1, 4).ToArgb());
            using var horizontal = EditorDocument.Combine([red, blue], false, 1);
            Equal(new Size(6, 4), horizontal.Size);
            Equal(Color.Blue.ToArgb(), horizontal.GetPixel(4, 2).ToArgb());
        });
        Check("invalid edits preserve the image and a failed save preserves the original file", () =>
        {
            using var input = new Bitmap(8, 6);
            using (var graphics = Graphics.FromImage(input)) graphics.Clear(Color.Red);
            using var doc = new EditorDocument(input);
            string path = Path.Combine(directory, "protected-original.png");
            doc.Save(path);
            try { doc.Crop(new Rectangle(100, 100, 10, 10)); throw new InvalidOperationException("invalid crop was accepted"); }
            catch (ArgumentException) { }
            try { doc.Resize(0, 10); throw new InvalidOperationException("zero width was accepted"); }
            catch (ArgumentOutOfRangeException) { }
            Equal(new Size(8, 6), doc.Image.Size);
            Assert(!doc.IsDirty, "invalid edits do not create a revision");
            doc.Invert();
            using (var locked = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try { doc.Save(path); throw new InvalidOperationException("locked file save unexpectedly succeeded"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            Assert(doc.IsDirty, "failed save does not mark changes as saved");
            using var original = new Bitmap(path);
            Equal(Color.Red.ToArgb(), original.GetPixel(2, 2).ToArgb());
            Assert(!Directory.EnumerateFiles(directory, ".protected-original.png.*.tmp").Any(), "failed-save temporary file is removed");
        });
        Check("annotation changes and transparency survive undo and lossless save", () =>
        {
            using var input = new Bitmap(80, 60);
            input.SetPixel(2, 2, Color.FromArgb(127, 100, 80, 40));
            using var doc = new EditorDocument(input);
            doc.Annotate(AnnotationTool.Rectangle, new PointF(10, 10), new PointF(60, 40), Color.Red, 4);
            Assert(doc.Image.GetPixel(30, 10).R > 200, "rectangle top edge is visible");
            doc.Undo();
            Equal(0, doc.Image.GetPixel(30, 10).A);
            var transparent = doc.Image.GetPixel(2, 2);
            Equal(127, transparent.A);
            string path = Path.Combine(directory, "transparency.png"); doc.Save(path);
            using var loaded = new Bitmap(path);
            Equal(127, loaded.GetPixel(2, 2).A);
            Equal(0, loaded.GetPixel(30, 10).A);
        });
        Check("PDF export preserves the source path and unsaved edit checkpoint", () =>
        {
            using var image = new Bitmap(12, 10);
            using var doc = new EditorDocument(image);
            string path = Path.Combine(directory, "export-source.png");
            doc.Save(path); doc.Invert();
            string pdf = Path.Combine(directory, "export-independent.pdf");
            doc.Export(pdf);
            Equal(Path.GetFullPath(path), doc.FilePath!);
            Assert(doc.IsDirty, "export does not mark source edits saved");
            Assert(File.Exists(pdf), "PDF export exists");
            doc.Undo();
            Assert(!doc.IsDirty, "undo returns to the PNG saved checkpoint");
        });
        Check("border, caption and mosaic preserve original alpha", () =>
        {
            using var image = new Bitmap(24, 20);
            using (var g = Graphics.FromImage(image)) g.Clear(Color.FromArgb(128, 100, 80, 60));
            using var doc = new EditorDocument(image);
            doc.AddBorder(3, Color.Blue);
            Equal(128, doc.Image.GetPixel(4, 4).A);
            Equal(Color.Blue.ToArgb(), doc.Image.GetPixel(0, 0).ToArgb());
            doc.Undo();
            doc.AddCaption("Caption", Color.Black, Color.White, 12);
            Equal(128, doc.Image.GetPixel(1, 1).A);
            Assert(doc.Image.Height > 20, "caption expands the image");
            doc.Undo();
            doc.Mosaic(new Rectangle(0, 0, 24, 20), 4);
            Equal(128, doc.Image.GetPixel(1, 1).A);
            Assert(Math.Abs(doc.Image.GetPixel(1, 1).R - 100) < 3, "mosaic preserves a constant translucent color");
        });
        Check("a long caption wraps completely below a narrow image", () =>
        {
            using var image = new Bitmap(80, 40);
            using var doc = new EditorDocument(image);
            doc.AddCaption("这是一段需要完整显示的长标题。狭窄图像下方应自动换行，最后一句仍然必须保留，不应被截掉。", Color.Black, Color.White, 14);
            Assert(doc.Image.Height > 180, "caption height includes automatically wrapped lines");
            bool hasLowerText = false;
            for (int y = 120; y < doc.Image.Height - 12 && !hasLowerText; y++)
                for (int x = 2; x < 78; x++) if (doc.Image.GetPixel(x, y).R < 100) { hasLowerText = true; break; }
            Assert(hasLowerText, "wrapped text continues below the original one-line caption area");
        });
        return results;

        void Check(string name, Action action)
        {
            try { action(); results.Add("PASS editor: " + name); }
            catch (Exception ex) { results.Add("FAIL editor: " + name + " — " + ex.Message); }
        }
    }

    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; got {actual}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void LayoutTree(Control control)
    {
        control.PerformLayout();
        foreach (Control child in control.Controls) LayoutTree(child);
    }

    private static Rectangle BoundsWithin(Control control, Control ancestor)
    {
        var bounds = control.Bounds;
        for (var parent = control.Parent; parent != ancestor && parent != null; parent = parent.Parent)
            bounds.Offset(parent.Location);
        return bounds;
    }
}
