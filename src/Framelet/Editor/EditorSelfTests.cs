using System.Drawing.Imaging;

namespace Framelet;

/// <summary>Behavior checks for the independently implemented editor model.</summary>
public static class EditorSelfTests
{
    public static List<string> Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var results = new List<string>();
        Check("editor, annotation and recorder labels fit at 100, 150 and 200 percent simulated layout scales", () =>
        {
            using var image = new Bitmap(80, 60);
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                using var editor = new EditorForm(image);
                using var draw = new DrawForm(image);
                using var recorder = new RecorderForm();
                using var finish = new RecordingFinishDialog(false);
                using var close = new RecordingFinishDialog(true);
                foreach (var window in new Form[] { editor, draw, recorder, finish, close })
                {
                    // Exercise proportional control and text sizing without changing the user's monitor settings.
                    // This is a layout check; real per-monitor DPI transitions still require a display test.
                    ScaleLayout(window, scale);
                    LayoutTree(window);
                    foreach (var button in Descendants(window).OfType<Button>().Where(button => button.Text.Length > 0))
                    {
                        var preferred = button.GetPreferredSize(Size.Empty);
                        Assert(button.Width >= preferred.Width && button.Height >= preferred.Height,
                            $"{window.Text}: {button.Text} needs {preferred}, has {button.Size} at {scale:P0} layout scale");
                        if (button.Parent is ScrollableControl { AutoScroll: false } parent)
                            Assert(parent.ClientRectangle.Contains(button.Bounds), $"{button.Text} is clipped by its parent at {scale:P0} layout scale");
                    }
                    foreach (var strip in Descendants(window).OfType<ToolStrip>())
                        foreach (var item in strip.Items.Cast<ToolStripItem>().Where(item => item is ToolStripButton or ToolStripMenuItem))
                        {
                            var preferred = item.GetPreferredSize(Size.Empty);
                            Assert(item.Width >= preferred.Width && item.Height >= preferred.Height,
                                $"{item.Text} needs {preferred}, has {item.Size} at {scale:P0} layout scale");
                        }
                    AssertTextControlsFit(window, $"{scale:P0} simulated layout scale");
                }
                AssertEditorStructure(editor);
                AssertDrawingStructure(draw, scale);
                AssertRecorderStructure(recorder);
            }
        });
        Check("initial editor, drawing and recorder layouts fit after native window handles are created", () =>
        {
            using var image = new Bitmap(80, 60);
            using var editor = new EditorForm(image);
            using var draw = new DrawForm(image);
            using var recorder = new RecorderForm();
            using var finish = new RecordingFinishDialog(false);
            using var close = new RecordingFinishDialog(true);
            foreach (var window in new Form[] { editor, draw, recorder, finish, close })
            {
                // Create the real native control handles, including ComboBox/NumericUpDown, without
                // showing a window, recording the desktop, or changing the monitor's DPI setting.
                _ = window.Handle;
                foreach (var control in Descendants(window).ToArray()) _ = control.Handle;
                LayoutTree(window);
                Assert(window.IsHandleCreated, "form uses its actual HandleCreated layout");
                AssertTextControlsFit(window, $"initial native handle at {window.DeviceDpi} DPI");
            }
            AssertEditorStructure(editor);
            AssertDrawingStructure(draw, draw.DeviceDpi / 96f);
            AssertRecorderStructure(recorder);
        });
        Check("annotation icons fit a constrained 1024 by 768 window at 100, 150 and 200 percent simulated layout scales", () =>
        {
            using var image = new Bitmap(80, 60);
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                using var draw = new DrawForm(image) { MaximumSize = new Size(1024, 768) };
                ScaleLayout(draw, scale);
                draw.MaximumSize = new Size(1024, 768);
                draw.Size = draw.MaximumSize;
                LayoutTree(draw);
                AssertDrawingStructure(draw, scale);
            }
        });
        Check("hidden panel-hosted annotation layout scrolls every icon into a short viewport and restores square targets after expansion", () =>
        {
            using var image = new Bitmap(80, 60);
            using var draw = new DrawForm(image);
            ScaleLayout(draw, 2);
            var layout = draw.Controls.Find("DrawingLayout", true).Single();
            using var host = new Panel { Visible = false, ClientSize = new Size(1008, 561), Font = draw.Font };
            try
            {
                draw.Controls.Add(host);
                draw.Controls.Remove(layout);
                host.Controls.Add(layout);
                _ = draw.Handle;
                _ = host.Handle;
                foreach (var control in Descendants(host).ToArray()) _ = control.Handle;
                host.ClientSize = new Size(1008, 561);
                LayoutTree(host);
                Assert(!draw.Visible && !host.Visible, "simulated constrained layout must not show a window or change the monitor DPI");
                Assert(host.Size == new Size(1008, 561) && host.ClientSize == new Size(1008, 561),
                    $"short simulated host must keep its requested geometry; size {host.Size}, client {host.ClientSize}");
                var palette = host.Controls.Find("DrawingTools", true).OfType<TableLayoutPanel>().Single();
                Assert(palette.AutoScroll && palette.VerticalScroll.Visible,
                    $"short annotation viewport must scroll without shrinking hit targets below 24 DIP; host {host.ClientSize}, viewport {palette.ClientRectangle}, display {palette.DisplayRectangle}");
                Assert(!palette.HorizontalScroll.Visible,
                    $"short annotation viewport must retain both complete icon columns without horizontal scrolling; viewport {palette.ClientRectangle}, display {palette.DisplayRectangle}, host {host.ClientSize}");
                foreach (var button in palette.Controls.OfType<Button>())
                {
                    palette.ScrollControlIntoView(button);
                    Assert(palette.ClientRectangle.Contains(button.Bounds),
                        $"scrolling must reveal the complete {button.AccessibleName} icon {button.Bounds} inside viewport {palette.ClientRectangle}; scroll {palette.AutoScrollPosition}, host {host.ClientSize}");
                    Assert(button.Height >= Math.Ceiling(button.Width * .75) && button.ClientSize.Height >= button.Image!.Height,
                        $"{button.AccessibleName} icon {button.Bounds} must retain its complete image and at least a 24-DIP hit target in viewport {palette.ClientRectangle}");
                }
                host.ClientSize = new Size(1904, 1041);
                LayoutTree(host);
                Assert(host.Size == new Size(1904, 1041) && host.ClientSize == new Size(1904, 1041),
                    $"expanded simulated host must keep its requested geometry; size {host.Size}, client {host.ClientSize}");
                Assert(!palette.VerticalScroll.Visible,
                    $"expanded annotation viewport must no longer need vertical scrolling; viewport {palette.ClientRectangle}, display {palette.DisplayRectangle}, host {host.ClientSize}");
                AssertDrawingStructure(draw, 2, host);
                foreach (var button in palette.Controls.OfType<Button>())
                    Assert(button.Height == button.Width,
                        $"expanding the simulated viewport must restore square {button.AccessibleName} icon {button.Bounds}; viewport {palette.ClientRectangle}, host {host.ClientSize}");
            }
            finally { draw.Controls.Add(layout); }
        });
        Check("recording controls keep stable native-handle layouts across 100 pause and resume cycles", () =>
        {
            var recordingBounds = new Rectangle(100, 100, 640, 360);
            using var controlBar = new RecordingControlBar(recordingBounds);
            using var countdown = new RecordingCountdown(recordingBounds, 15, true, false);
            foreach (var window in new Form[] { controlBar, countdown })
            {
                _ = window.Handle;
                foreach (var control in Descendants(window).ToArray()) _ = control.Handle;
                LayoutTree(window);
                Assert(window.IsHandleCreated && !window.Visible, "overlay layout tests create hidden native windows without showing them");
            }
            var work = Screen.FromRectangle(recordingBounds).WorkingArea;
            var stateSizes = new Dictionary<RecordingState, Size>();
            for (int iteration = 0; iteration < 100; iteration++)
            {
                foreach (var state in new[] { RecordingState.Recording, RecordingState.Paused, RecordingState.Recording })
                {
                    // A constant displayed time isolates accumulating layout growth from changing glyph widths.
                    controlBar.UpdateState(state, TimeSpan.FromSeconds(12), false, true, true, true);
                    LayoutTree(controlBar);
                    var preferred = controlBar.GetPreferredSize(Size.Empty);
                    Assert(controlBar.Width >= preferred.Width && controlBar.Height >= preferred.Height,
                        $"{state} control bar must accommodate its complete preferred layout");
                    Assert(controlBar.Width <= work.Width && controlBar.Height <= work.Height,
                        $"{state} control bar must remain smaller than the available screen");
                    if (stateSizes.TryGetValue(state, out var expected))
                        Equal(expected, controlBar.Size);
                    else stateSizes.Add(state, controlBar.Size);
                    var expectedActions = state == RecordingState.Paused ? new[] { "继续", "结束录制…", "放弃" } : new[] { "暂停", "结束录制…", "放弃" };
                    var buttons = Descendants(controlBar).OfType<Button>().ToArray();
                    foreach (string action in expectedActions)
                    {
                        var button = buttons.Single(button => button.Text == action);
                        Assert(button.Enabled, $"{state} control bar must expose an enabled {action} action");
                        AssertOverlayControlFits(button, controlBar);
                    }
                    foreach (var label in Descendants(controlBar).OfType<Label>()) AssertOverlayControlFits(label, controlBar);
                }
            }
            AssertTextControlsFit(countdown, "hidden countdown native-handle layout");
            Assert(countdown.CancelButton is Button { DialogResult: DialogResult.Cancel } cancel && cancel.Text == "取消",
                "countdown must offer a complete cancel action without starting its timer");
            Assert(Descendants(countdown).OfType<Label>().Any(label => label.Text.Contains("640 × 360")),
                "countdown must show the selected recording dimensions");
            Assert(Descendants(countdown).OfType<Label>().Any(label => label.Text.Contains("Esc")),
                "countdown must explain how to cancel");
            Assert(!controlBar.Visible && !countdown.Visible && countdown.DialogResult == DialogResult.None,
                "layout verification must never show overlays or start a recording countdown");
        });
        Check("recording selection guidance keeps complete wrapped text at 100, 150 and 200 percent font and padding scales", () =>
        {
            string[] instructions =
            [
                "按住鼠标左键，拖动框选录制区域\n松开后可确认或重新选择 · Esc / 右键取消",
                "已选择 3840 × 2160 像素\nEnter / 空格确认 · 方向键移动 · Shift + 方向键调整大小",
                "区域过小，请重新框选（至少 2 × 2 像素）\n按住鼠标左键拖动 · Esc / 右键取消"
            ];
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                using var font = new Font("Microsoft YaHei UI", 9F * scale);
                using var guidance = new RecordingSelectionGuidance
                {
                    Font = font,
                    Padding = new Padding((int)Math.Round(12 * scale), (int)Math.Round(9 * scale),
                        (int)Math.Round(12 * scale), (int)Math.Round(9 * scale))
                };
                // This exercises the production instruction layout without constructing the desktop picker.
                // Font/padding stress is not a substitute for a real per-monitor DPI transition test.
                foreach (int availableWidth in new[] { 1920, 320, 200, 1920 })
                    foreach (string instruction in instructions)
                    {
                        guidance.SetMessage(instruction, availableWidth);
                        LayoutTree(guidance);
                        var label = guidance.MessageLabel;
                        Equal(instruction, label.Text);
                        Assert(!label.AutoEllipsis, "selection guidance must preserve every instruction instead of eliding text");
                        Assert(guidance.Width > guidance.Padding.Horizontal,
                            $"selection guidance must have a text area, not collapse to its padding at {scale:P0}");
                        Assert(guidance.Width <= availableWidth,
                            $"selection guidance width {guidance.Width} must fit available width {availableWidth} at {scale:P0}");
                        var preferred = label.GetPreferredSize(new Size(availableWidth - guidance.Padding.Horizontal, 0));
                        Assert(label.Width >= preferred.Width && label.Height >= preferred.Height,
                            $"complete selection instruction needs {preferred}, has {label.Size} at {scale:P0} and width {availableWidth}");
                        var content = new Rectangle(guidance.Padding.Left, guidance.Padding.Top,
                            guidance.ClientSize.Width - guidance.Padding.Horizontal, guidance.ClientSize.Height - guidance.Padding.Vertical);
                        Assert(content.Contains(label.Bounds),
                            $"selection instruction {label.Bounds} must stay inside content area {content} at {scale:P0}");
                    }
            }
        });
        Check("opening a large screenshot keeps actual size after first layout", () =>
        {
            using var image = new Bitmap(2000, 1400);
            using var editor = new EditorForm(image);
            using var draw = new DrawForm(image);
            LayoutTree(editor);
            LayoutTree(draw);
            // Raise WinForms' protected lifecycle event without displaying or activating either window.
            var shown = typeof(Form).GetMethod("OnShown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            shown.Invoke(editor, [EventArgs.Empty]);
            shown.Invoke(draw, [EventArgs.Empty]);
            var canvas = Descendants(editor).OfType<ImageCanvas>().Single();
            Equal(1f, canvas.Zoom);
            Equal(1f, Descendants(draw).OfType<ImageCanvas>().Single().Zoom);
            Assert(canvas.AutoScrollMinSize.Width >= image.Width && canvas.AutoScrollMinSize.Height >= image.Height,
                "large screenshots can be scrolled at their original dimensions");
            var status = Descendants(editor).OfType<StatusStrip>().Single();
            Assert(status.Items.OfType<ToolStripStatusLabel>().Any(item => item.Text?.Contains("实际大小") == true),
                "zoom status identifies the current actual-size view");
        });
        Check("actual-size canvas renders each source pixel without interpolation", () =>
        {
            using var image = new Bitmap(5, 5);
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++) image.SetPixel(x, y, (x + y) % 2 == 0 ? Color.Black : Color.White);
            using var document = new EditorDocument(image);
            using var canvas = new ImageCanvas { Document = document, CenterVertically = true, AllowEdgeCropping = true, Size = new Size(100, 100) };
            using var rendered = new Bitmap(100, 100);
            canvas.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
            var origin = EditorViewport.Origin(canvas.ClientSize, image.Size, 1, Point.Empty, true);
            Equal((float)Math.Truncate(origin.X), origin.X);
            Equal((float)Math.Truncate(origin.Y), origin.Y);
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    Equal(image.GetPixel(x, y).ToArgb(), rendered.GetPixel((int)origin.X + x, (int)origin.Y + y).ToArgb());
        });
        Check("editor and drawing windows construct with valid menu shortcuts", () =>
        {
            using var image = new Bitmap(80, 60);
            using var editor = new EditorForm(image, Path.Combine(directory, "ui-source.png"));
            Assert(editor.MainMenuStrip != null, "editor has an initialized menu");
            editor.AddImage(image);
            Assert(editor.Text.Contains("截图"), "adding a second image selects its tab");
            using var draw = new DrawForm(image);
            Assert(draw.EditedImage.Size == image.Size && !draw.HasChanges, "drawing window starts with an unmodified owned image");
            LayoutTree(editor);
            LayoutTree(draw);
            AssertEditorStructure(editor);
            AssertDrawingStructure(draw, 1);
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
        Check("dragging every editor image edge and corner crops exact pixels as one undoable edit", () =>
        {
            using var input = new Bitmap(80, 60);
            for (int y = 0; y < input.Height; y++)
                for (int x = 0; x < input.Width; x++) input.SetPixel(x, y, Color.FromArgb(255, x * 3, y * 4, 80));
            var cases = new (Point Start, Point End, Rectangle Retained)[]
            {
                (new(0, 30), new(10, 30), new(10, 0, 70, 60)),
                (new(80, 30), new(70, 30), new(0, 0, 70, 60)),
                (new(40, 0), new(40, 10), new(0, 10, 80, 50)),
                (new(40, 60), new(40, 50), new(0, 0, 80, 50)),
                (new(0, 0), new(10, 10), new(10, 10, 70, 50)),
                (new(80, 0), new(70, 10), new(0, 10, 70, 50)),
                (new(0, 60), new(10, 50), new(10, 0, 70, 50)),
                (new(80, 60), new(70, 50), new(0, 0, 70, 50))
            };
            foreach (var sample in cases)
            {
                using var editor = new EditorForm(input);
                LayoutTree(editor);
                var canvas = Descendants(editor).OfType<ImageCanvas>().Single();
                var doc = canvas.Document!;
                var origin = EditorViewport.Origin(canvas.ClientSize, input.Size, canvas.Zoom, canvas.AutoScrollPosition, false);
                Point Client(Point point) => Point.Round(new PointF(origin.X + point.X, origin.Y + point.Y));
                CanvasMouse(canvas, "OnMouseDown", MouseButtons.Left, Client(sample.Start));
                CanvasMouse(canvas, "OnMouseMove", MouseButtons.Left, Client(sample.End));
                Equal(input.Size, doc.Image.Size);
                Assert(!doc.CanUndo, "crop preview must not mutate the image or fill revision history");
                CanvasMouse(canvas, "OnMouseUp", MouseButtons.Left, Client(sample.End));
                Equal(sample.Retained.Size, doc.Image.Size);
                Equal(input.GetPixel(sample.Retained.X, sample.Retained.Y).ToArgb(), doc.Image.GetPixel(0, 0).ToArgb());
                Equal(input.GetPixel(sample.Retained.Right - 1, sample.Retained.Bottom - 1).ToArgb(),
                    doc.Image.GetPixel(doc.Image.Width - 1, doc.Image.Height - 1).ToArgb());
                Assert(!canvas.HasSelection, "committing an edge crop clears the old selection");
                doc.Undo();
                Equal(input.Size, doc.Image.Size);
                Assert(!doc.CanUndo, "one complete edge gesture creates exactly one revision");
                doc.Redo();
                Equal(sample.Retained.Size, doc.Image.Size);
            }
        });
        Check("edge crop follows zoomed scrolled pixels and clamps to one retained pixel", () =>
        {
            using var input = new Bitmap(800, 600);
            using var doc = new EditorDocument(input);
            using var canvas = new ImageCanvas { Document = doc, AllowEdgeCropping = true, Size = new Size(260, 200), Zoom = 2 };
            _ = canvas.Handle;
            canvas.AutoScrollPosition = new Point(300, 250);
            Assert(canvas.AutoScrollPosition.X < 0 && canvas.AutoScrollPosition.Y < 0, "exercise actual scrolling");
            var scroll = canvas.AutoScrollPosition;
            CanvasMouse(canvas, "OnMouseDown", MouseButtons.Middle, new Point(120, 100));
            CanvasMouse(canvas, "OnMouseMove", MouseButtons.Middle, new Point(140, 115));
            CanvasMouse(canvas, "OnMouseUp", MouseButtons.Middle, new Point(140, 115));
            Equal(new Point(scroll.X + 20, scroll.Y + 15), canvas.AutoScrollPosition);
            Assert(!canvas.IsCropping && !doc.CanUndo, "middle-button pan keeps the image and crop history unchanged");
            var origin = EditorViewport.Origin(canvas.ClientSize, input.Size, canvas.Zoom, canvas.AutoScrollPosition, false);
            var from = Point.Round(new PointF(origin.X + 800 * 2, origin.Y + 300 * 2));
            var to = new Point(from.X - 50, from.Y);
            CanvasMouse(canvas, "OnMouseDown", MouseButtons.Left, from);
            CanvasMouse(canvas, "OnMouseMove", MouseButtons.Left, to);
            Equal(new Rectangle(0, 0, 775, 600), canvas.CropPreview);
            CanvasMouse(canvas, "OnMouseUp", MouseButtons.Left, to);
            Equal(new Size(775, 600), doc.Image.Size);
            doc.Undo();
            canvas.Zoom = .5f;
            canvas.AutoScrollPosition = Point.Empty;
            origin = EditorViewport.Origin(canvas.ClientSize, input.Size, canvas.Zoom, canvas.AutoScrollPosition, false);
            from = Point.Round(new PointF(origin.X, origin.Y));
            to = new Point(from.X + 500, from.Y + 400);
            CanvasMouse(canvas, "OnMouseDown", MouseButtons.Left, from);
            CanvasMouse(canvas, "OnMouseMove", MouseButtons.Left, to);
            Equal(new Rectangle(799, 599, 1, 1), canvas.CropPreview);
            CanvasMouse(canvas, "OnMouseUp", MouseButtons.Left, to);
            Equal(new Size(1, 1), doc.Image.Size);
        });
        Check("edge crop cancels on Escape right click or lost capture and outward drags create no edit", () =>
        {
            using var input = new Bitmap(160, 100);
            using var doc = new EditorDocument(input);
            using var canvas = new ImageCanvas { Document = doc, AllowEdgeCropping = true, Size = new Size(300, 220) };
            _ = canvas.Handle;
            var origin = EditorViewport.Origin(canvas.ClientSize, input.Size, canvas.Zoom, canvas.AutoScrollPosition, false);
            Point Client(int x, int y) => Point.Round(new PointF(origin.X + x, origin.Y + y));
            CanvasMouse(canvas, "OnMouseDown", MouseButtons.Left, Client(30, 30));
            CanvasMouse(canvas, "OnMouseMove", MouseButtons.Left, Client(100, 70));
            CanvasMouse(canvas, "OnMouseUp", MouseButtons.Left, Client(100, 70));
            Equal(new Rectangle(30, 30, 70, 40), canvas.Selection);
            foreach (var cancel in new Action[]
            {
                () =>
                {
                    object?[] args = [Message.Create(canvas.Handle, 0, IntPtr.Zero, IntPtr.Zero), Keys.Escape];
                    typeof(ImageCanvas).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(canvas, args);
                },
                () => CanvasMouse(canvas, "OnMouseDown", MouseButtons.Right, Client(20, 50)),
                () => canvas.Capture = false
            })
            {
                CanvasMouse(canvas, "OnMouseDown", MouseButtons.Left, Client(0, 50));
                CanvasMouse(canvas, "OnMouseMove", MouseButtons.Left, Client(20, 50));
                Assert(canvas.IsCropping && canvas.Capture, "edge drag owns mouse capture until completed or cancelled");
                Equal(new Rectangle(20, 0, 140, 100), canvas.CropPreview);
                cancel();
                Assert(!canvas.IsCropping && !canvas.Capture, "cancelling must release the gesture and capture");
                CanvasMouse(canvas, "OnMouseUp", MouseButtons.Left, Client(20, 50));
                Equal(input.Size, doc.Image.Size);
                Equal(new Rectangle(30, 30, 70, 40), canvas.Selection);
                Assert(!doc.CanUndo, "cancelled preview must not create history");
            }
            // A grab a few display pixels outside the image must retain its offset and stay a no-op when pulled outwards.
            CanvasMouse(canvas, "OnMouseDown", MouseButtons.Left, Client(-4, 50));
            CanvasMouse(canvas, "OnMouseMove", MouseButtons.Left, Client(-15, 50));
            CanvasMouse(canvas, "OnMouseUp", MouseButtons.Left, Client(-15, 50));
            Equal(input.Size, doc.Image.Size);
            Assert(!doc.CanUndo, "outward edge drag cannot expand the image or create an empty revision");
            CanvasMouse(canvas, "OnMouseDown", MouseButtons.Left, Client(-4, 50));
            CanvasMouse(canvas, "OnMouseMove", MouseButtons.Left, Client(16, 50));
            CanvasMouse(canvas, "OnMouseUp", MouseButtons.Left, Client(16, 50));
            Equal(new Size(140, 100), doc.Image.Size);
        });
        Check("edge crop preview shades discarded pixels and preserves drawing-tool behavior", () =>
        {
            using var input = new Bitmap(160, 100);
            using (var graphics = Graphics.FromImage(input)) graphics.Clear(Color.White);
            using var doc = new EditorDocument(input);
            using var canvas = new ImageCanvas { Document = doc, AllowEdgeCropping = true, Size = new Size(300, 220) };
            var origin = EditorViewport.Origin(canvas.ClientSize, input.Size, 1, Point.Empty, false);
            Point Client(int x, int y) => Point.Round(new PointF(origin.X + x, origin.Y + y));
            CanvasMouse(canvas, "OnMouseMove", MouseButtons.None, Client(0, 50));
            Equal(Cursors.SizeWE, canvas.Cursor);
            CanvasMouse(canvas, "OnMouseMove", MouseButtons.None, Client(80, 0));
            Equal(Cursors.SizeNS, canvas.Cursor);
            CanvasMouse(canvas, "OnMouseMove", MouseButtons.None, Client(160, 100));
            Equal(Cursors.SizeNWSE, canvas.Cursor);
            CanvasMouse(canvas, "OnMouseDown", MouseButtons.Left, Client(0, 50));
            CanvasMouse(canvas, "OnMouseMove", MouseButtons.Left, Client(40, 50));
            using (var rendered = new Bitmap(canvas.Width, canvas.Height))
            {
                canvas.DrawToBitmap(rendered, canvas.ClientRectangle);
                Assert(rendered.GetPixel(Client(15, 50).X, Client(15, 50).Y).R < 180, "discarded area must be visibly shaded");
                Equal(Color.White.ToArgb(), rendered.GetPixel(Client(55, 50).X, Client(55, 50).Y).ToArgb());
            }
            canvas.CancelCrop();
            canvas.Tool = AnnotationTool.Line;
            CanvasMouse(canvas, "OnMouseDown", MouseButtons.Left, Client(0, 50));
            CanvasMouse(canvas, "OnMouseMove", MouseButtons.Left, Client(40, 50));
            CanvasMouse(canvas, "OnMouseUp", MouseButtons.Left, Client(40, 50));
            Equal(input.Size, doc.Image.Size);
            Assert(doc.CanUndo && doc.Image.GetPixel(20, 50).G < 180, "drawing a line from the image edge must still annotate");
            using var draw = new DrawForm(input);
            Assert(!Descendants(draw).OfType<ImageCanvas>().Single().AllowEdgeCropping,
                "drawing palette selection keeps its existing edge-selection behavior");
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

    private static void CanvasMouse(ImageCanvas canvas, string method, MouseButtons button, Point location) =>
        typeof(ImageCanvas).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(canvas, [new MouseEventArgs(button, 1, location.X, location.Y, 0)]);

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

    private static void AssertEditorStructure(EditorForm editor)
    {
        Assert(editor.Controls.Find("FrameletHeader", true).Length == 0, "editor must not restore a large branding header");
        var menu = editor.Controls.Find("EditorMenu", true).OfType<MenuStrip>().Single();
        var toolbar = editor.Controls.Find("EditorToolbar", true).OfType<ToolStrip>().Single();
        var tabs = editor.Controls.Find("EditorTabs", true).OfType<TabControl>().Single();
        var status = Descendants(editor).OfType<StatusStrip>().Single();
        var rowBounds = new[] { menu, toolbar, (Control)tabs, status }.Select(control => BoundsWithin(control, editor)).ToArray();
        for (int index = 0; index < rowBounds.Length; index++)
        {
            Assert(rowBounds[index].Width > 0 && rowBounds[index].Height > 0 && editor.ClientRectangle.Contains(rowBounds[index]),
                "menu, toolbar, tabs and status must each occupy a complete visible row");
            if (index > 0) Assert(rowBounds[index - 1].Bottom <= rowBounds[index].Top,
                "editor menu, toolbar, tabs and status must be ordered and never overlap");
        }
        Assert(menu.Items.OfType<ToolStripMenuItem>().Count() >= 7, "common editor menus must remain independently discoverable");
        Assert(menu.AutoSize && toolbar.AutoSize, "text rows must size to the actual font");
        foreach (string name in new[] { "open", "save", "select", "zoomIn", "zoomOut", "100", "draw", "crop", "cut", "copy", "paste" })
        {
            var action = toolbar.Items.OfType<ToolStripButton>().Single(item => item.Tag as string == name);
            Assert(action.Available && !string.IsNullOrWhiteSpace(action.AccessibleName) && !string.IsNullOrWhiteSpace(action.ToolTipText),
                $"{name} must have an available, named toolbar action and a tooltip");
            Assert(action.DisplayStyle == ToolStripItemDisplayStyle.ImageAndText && action.TextImageRelation == TextImageRelation.ImageAboveText,
                $"{name} must keep a complete short label below its icon");
        }
        foreach (string name in new[] { "save", "copy", "draw", "100" })
            Assert(toolbar.Items.OfType<ToolStripButton>().Single(item => item.Tag as string == name).Enabled,
                $"{name} must be usable when a document is open");
        var page = tabs.SelectedTab!;
        var canvas = page.Controls.OfType<ImageCanvas>().Single();
        Assert(page.ClientRectangle.Contains(canvas.Bounds) && canvas.Width > 0 && canvas.Height > 0,
            "the selected document canvas must remain inside the workspace below the toolbar");
    }

    private static void AssertDrawingStructure(DrawForm draw, float scale, Control? viewportRoot = null)
    {
        var root = viewportRoot ?? draw;
        Assert(root.Controls.Find("FrameletHeader", true).Length == 0, "drawing must not restore a large branding header");
        var palette = root.Controls.Find("DrawingTools", true).OfType<TableLayoutPanel>().Single();
        var canvas = Descendants(root).OfType<ImageCanvas>().Single();
        var footer = root.Controls.Find("DrawingFooter", true).Single();
        var paletteBounds = BoundsWithin(palette, root);
        var canvasBounds = BoundsWithin(canvas, root);
        var footerBounds = BoundsWithin(footer, root);
        Assert(palette.ColumnCount == 2 && palette.Width <= Math.Ceiling(96 * scale), "annotation tools must occupy a narrow two-column palette");
        Assert(root.ClientRectangle.Contains(paletteBounds) && root.ClientRectangle.Contains(canvasBounds) && root.ClientRectangle.Contains(footerBounds),
            "drawing palette, canvas and footer must stay inside the window");
        Assert(paletteBounds.Right <= canvasBounds.Left && paletteBounds.Bottom <= footerBounds.Top && canvasBounds.Bottom <= footerBounds.Top,
            "annotation tools, canvas and options must not cover one another");
        var toolTip = typeof(DrawForm).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(ToolTip)).Select(field => (ToolTip)field.GetValue(draw)!).Single();
        foreach (var button in palette.Controls.OfType<Button>())
        {
            Assert(button.Text.Length == 0 && button.Image is not null && !string.IsNullOrWhiteSpace(button.AccessibleName),
                "narrow annotation buttons must use named icons instead of truncated labels");
            Assert(button.Height >= Math.Ceiling(button.Width * .75) && button.ClientSize.Height >= button.Image!.Height,
                $"{button.AccessibleName} icon {button.Bounds} must retain its complete image and at least a 24-DIP hit target in viewport {palette.ClientRectangle}");
            Assert(!string.IsNullOrWhiteSpace(toolTip.GetToolTip(button)), "every annotation icon must have a discoverable tooltip");
            Assert(palette.ClientRectangle.Contains(button.Bounds),
                $"annotation icon {button.AccessibleName} {button.Bounds} must remain fully inside palette {palette.ClientRectangle}; root {root.ClientSize}, scale {scale:P0}, font {root.Font.Name}, DPI {root.DeviceDpi}");
        }
        foreach (var tool in new[] { AnnotationTool.Select, AnnotationTool.Arrow, AnnotationTool.Text, AnnotationTool.Number,
            AnnotationTool.Pen, AnnotationTool.Line, AnnotationTool.Rectangle, AnnotationTool.Ellipse, AnnotationTool.Highlight, AnnotationTool.Mosaic })
            Assert(palette.Controls.OfType<Button>().Count(button => button.Tag is AnnotationTool candidate && candidate == tool) == 1,
                $"daily annotation tool {tool} must remain present exactly once");
    }

    private static void AssertRecorderStructure(RecorderForm recorder)
    {
        var groups = Descendants(recorder).OfType<GroupBox>().ToArray();
        var capture = groups.Single(group => group.Text == "录制范围");
        var options = groups.Single(group => group.Text == "录制选项");
        var captureBounds = BoundsWithin(capture, recorder);
        var optionBounds = BoundsWithin(options, recorder);
        Assert(recorder.ClientRectangle.Contains(captureBounds) && recorder.ClientRectangle.Contains(optionBounds),
            $"recorder groups must fit inside the settings window: window {recorder.ClientRectangle}, capture {captureBounds}, options {optionBounds}");
        Assert(captureBounds.Bottom <= optionBounds.Top, "capture range must appear above recording options without overlap");
        var ranges = Descendants(capture).OfType<RadioButton>().OrderBy(button => BoundsWithin(button, capture).Top).ToArray();
        Equal(2, ranges.Length);
        Assert(ranges.Any(button => button.Text.Contains("全屏")) && ranges.Any(button => button.Text.Contains("矩形")),
            "recorder must offer the supported full-screen and rectangle choices");
        Assert(BoundsWithin(ranges[0], capture).Bottom <= BoundsWithin(ranges[1], capture).Top,
            "recording range choices must occupy separate vertical rows");
        var fullScreen = ranges.Single(button => button.Text.Contains("全屏"));
        var rectangle = ranges.Single(button => button.Text.Contains("矩形"));
        rectangle.Checked = true;
        Assert(rectangle.Checked && !fullScreen.Checked, "rectangle and full-screen remain mutually exclusive across nested layout panels");
        fullScreen.Checked = true;
        Assert(fullScreen.Checked && !rectangle.Checked, "switching back to full-screen clears the rectangle option");
        foreach (var group in groups)
            foreach (var control in Descendants(group).Where(control => control is ButtonBase or Label or ComboBox or NumericUpDown))
            {
                var bounds = BoundsWithin(control, group);
                Assert(group.ClientRectangle.Contains(bounds) && bounds.Top >= group.Font.Height,
                    $"recording control {control.Text} must stay clear of the group caption and borders");
            }
        foreach (var panel in Descendants(recorder).Where(control => control is TableLayoutPanel or FlowLayoutPanel))
        {
            var controls = panel.Controls.Cast<Control>().Where(control => control is ButtonBase or Label or ComboBox or NumericUpDown).ToArray();
            for (int first = 0; first < controls.Length; first++)
                for (int second = first + 1; second < controls.Length; second++)
                    Assert(!controls[first].Bounds.IntersectsWith(controls[second].Bounds),
                        $"recorder controls {controls[first].Text} and {controls[second].Text} must not overlap");
        }
        var audio = Descendants(options).OfType<ComboBox>().Single();
        Assert(audio.SelectedIndex == 0 && audio.SelectedItem?.ToString() == "不录制声音", "initial recording remains ready for native silent MP4");
        Assert(recorder.AcceptButton is Button { Enabled: true } start && start.Text.Contains("录制"), "initial recorder must expose an enabled start action");
    }

    private static void AssertOverlayControlFits(Control control, Form window)
    {
        var preferred = control.GetPreferredSize(Size.Empty);
        Assert(control.Width >= preferred.Width && control.Height >= preferred.Height,
            $"overlay control {control.Text} needs {preferred}, has {control.Size}");
        Assert(control.Parent is not null && control.Parent.ClientRectangle.Contains(control.Bounds),
            $"overlay control {control.Text} must stay fully inside its immediate row");
        Assert(window.ClientRectangle.Contains(BoundsWithin(control, window)),
            $"overlay control {control.Text} must stay fully inside the control bar");
    }

    private static void AssertTextControlsFit(Form window, string context)
    {
        foreach (var control in Descendants(window).Where(control => control is ButtonBase or Label or ComboBox or NumericUpDown))
        {
            if (control is Label { AutoEllipsis: true } || control.Text.Length == 0) continue;
            var constraint = control is Label ? new Size(control.Width, 0) : Size.Empty;
            var preferred = control.GetPreferredSize(constraint);
            Assert(control.Width >= preferred.Width && control.Height >= preferred.Height,
                $"{window.Text}: {control.Text} needs {preferred}, has {control.Size} after {context}");
            if (control.Parent is ScrollableControl { AutoScroll: false } parent)
                Assert(parent.ClientRectangle.Contains(control.Bounds), $"{control.Text} is clipped by its parent after {context}");
        }
        foreach (var strip in Descendants(window).OfType<ToolStrip>())
            foreach (var item in strip.Items.Cast<ToolStripItem>().Where(item => item is ToolStripButton or ToolStripMenuItem))
            {
                var preferred = item.GetPreferredSize(Size.Empty);
                Assert(item.Width >= preferred.Width && item.Height >= preferred.Height,
                    $"{item.Text} needs {preferred}, has {item.Size} after {context}");
                if (item.Placement == ToolStripItemPlacement.Main)
                    Assert(strip.ClientRectangle.Contains(item.Bounds), $"{item.Text} is clipped by its menu or toolbar after {context}");
            }
    }

    private static void LayoutTree(Control control)
    {
        control.PerformLayout();
        foreach (Control child in control.Controls) LayoutTree(child);
    }

    private static void ScaleLayout(Form window, float scale)
    {
        if (scale == 1) return;
        var fonts = Descendants(window).Prepend(window).Select(control => (Control: control, Font: control.Font)).ToArray();
        window.SuspendLayout();
        window.Scale(new SizeF(scale, scale));
        foreach (var entry in fonts)
            entry.Control.Font = new Font(entry.Font.FontFamily, entry.Font.Size * scale, entry.Font.Style, entry.Font.Unit);
        window.ResumeLayout(true);
    }

    private static Rectangle BoundsWithin(Control control, Control ancestor)
    {
        var bounds = control.Bounds;
        for (var parent = control.Parent; parent != ancestor && parent != null; parent = parent.Parent)
            bounds.Offset(parent.Location);
        return bounds;
    }
}
