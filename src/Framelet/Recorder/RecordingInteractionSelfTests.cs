namespace Framelet;

/// <summary>Selection geometry and explicit recording discard, without showing UI or capturing the desktop.</summary>
internal static class RecordingInteractionSelfTests
{
    public static async Task<IReadOnlyList<string>> RunAsync()
    {
        CheckSelectionGeometry();
        await CheckDiscardAsync().ConfigureAwait(false);
        return
        [
            "PASS Recording selection: reverse drag, corner and interior drag adjustments, 2 x 2 minimum, desktop boundaries and negative origins",
            "PASS Recording discard: paused synthetic recording is removed; neighboring data survives; discarded recording cannot resume"
        ];
    }

    private static void CheckSelectionGeometry()
    {
        var canvas = new Size(640, 480);
        Equal(new Rectangle(10, 20, 290, 230),
            RecordingSelectionGeometry.FromDrag(new Point(300, 250), new Point(10, 20), canvas),
            "Dragging up and left normalizes the selection");
        Equal(new Rectangle(0, 0, 640, 480),
            RecordingSelectionGeometry.FromDrag(new Point(-10, -20), new Point(800, 700), canvas),
            "Dragging beyond the desktop clamps all edges");
        Equal(new Rectangle(638, 478, 2, 2),
            RecordingSelectionGeometry.FromDrag(new Point(638, 478), new Point(900, 900), canvas),
            "The bottom-right corner permits an exact 2 x 2 selection");
        Assert(RecordingSelectionGeometry.IsUsable(new Rectangle(638, 478, 2, 2)), "A 2 x 2 selection can be confirmed");
        Assert(!RecordingSelectionGeometry.IsUsable(new Rectangle(0, 0, 1, 2)), "One-pixel width cannot be confirmed");
        Assert(!RecordingSelectionGeometry.IsUsable(new Rectangle(0, 0, 2, 1)), "One-pixel height cannot be confirmed");
        Assert(!RecordingSelectionGeometry.IsUsable(Rectangle.Empty), "An empty selection cannot be confirmed");

        var selection = new Rectangle(10, 20, 100, 80);
        Equal(new Rectangle(0, 0, 100, 80), RecordingSelectionGeometry.Move(selection, -99, -99, canvas),
            "Moving past the top-left preserves size and stays within the desktop");
        Equal(new Rectangle(540, 400, 100, 80), RecordingSelectionGeometry.Move(selection, 999, 999, canvas),
            "Moving past the bottom-right preserves size and stays within the desktop");
        Equal(new Rectangle(11, 19, 100, 80), RecordingSelectionGeometry.Move(selection, 1, -1, canvas),
            "Arrow-key movement changes the location by one pixel");
        Equal(new Rectangle(10, 20, 2, 2), RecordingSelectionGeometry.Resize(selection, -999, -999, canvas),
            "Shrinking stops at 2 x 2 and preserves the top-left anchor");
        Equal(new Rectangle(600, 450, 40, 30),
            RecordingSelectionGeometry.Resize(new Rectangle(600, 450, 10, 10), 999, 999, canvas),
            "Growing stops at the bottom-right desktop edge");
        Equal(new Rectangle(10, 20, 101, 79), RecordingSelectionGeometry.Resize(selection, 1, -1, canvas),
            "Shift-arrow changes the selected dimensions by one pixel");

        Equal(new Rectangle(-1820, -80, 320, 200),
            RecordingSelectionGeometry.ToDesktop(new Rectangle(100, 120, 320, 200), new Point(-1920, -200)),
            "Selections on a monitor above and left of the primary retain negative desktop coordinates");
        Equal(new Rectangle(-20, 50, 200, 200),
            RecordingSelectionGeometry.ToDesktop(new Rectangle(1900, 50, 200, 200), new Point(-1920, 0)),
            "A selection spanning the primary monitor boundary retains its entire width");
        Equal(new Point(1620, 1020),
            RecordingSelectionGeometry.KeepVisible(new Point(1900, 1070), new Size(300, 60), new Rectangle(0, 0, 1920, 1080)),
            "Confirmation controls remain visible near the bottom-right corner");
        Equal(new Point(-2560, -240),
            RecordingSelectionGeometry.KeepVisible(new Point(-2800, -500), new Size(300, 60), new Rectangle(-2560, -240, 2560, 1400)),
            "Instruction controls remain visible on a negative-origin monitor");
        CheckMouseAdjustments();
    }

    private static void CheckMouseAdjustments()
    {
        var canvas = new Size(640, 480);
        var selection = new Rectangle(50, 60, 100, 80);
        Assert(RecordingSelectionGeometry.HitTest(selection, new Point(100, 100), 7) == RecordingSelectionDragMode.Move,
            "Dragging inside a confirmed selection moves it instead of starting a new one");
        Assert(RecordingSelectionGeometry.HitTest(selection, new Point(170, 100), 7) == RecordingSelectionDragMode.Create,
            "Dragging outside the selected area begins a replacement selection");
        Assert(RecordingSelectionGeometry.HitTest(Rectangle.Empty, new Point(100, 100), 7) == RecordingSelectionDragMode.Create,
            "The first drag always creates a selection");
        Assert(RecordingSelectionGeometry.HitTest(selection, new Point(50, 60), 7) == RecordingSelectionDragMode.TopLeft,
            "The displayed top-left corner is a resize target");
        Assert(RecordingSelectionGeometry.HitTest(selection, new Point(149, 60), 7) == RecordingSelectionDragMode.TopRight,
            "The displayed top-right corner is a resize target");
        Assert(RecordingSelectionGeometry.HitTest(selection, new Point(50, 139), 7) == RecordingSelectionDragMode.BottomLeft,
            "The displayed bottom-left corner is a resize target");
        Assert(RecordingSelectionGeometry.HitTest(selection, new Point(153, 142), 7) == RecordingSelectionDragMode.BottomRight,
            "A corner resize target extends slightly outside its small visual marker");
        Assert(RecordingSelectionGeometry.HitTest(new Rectangle(10, 20, 2, 2), new Point(11, 21), 7) == RecordingSelectionDragMode.BottomRight,
            "Overlapping hit areas on a 2 x 2 selection choose the nearest visible corner");

        Equal(new Rectangle(30, 45, 120, 95),
            RecordingSelectionGeometry.ResizeFromCorner(selection, new Point(-20, -15), RecordingSelectionDragMode.TopLeft, canvas),
            "Top-left dragging preserves the bottom-right anchor");
        Equal(new Rectangle(50, 45, 120, 95),
            RecordingSelectionGeometry.ResizeFromCorner(selection, new Point(20, -15), RecordingSelectionDragMode.TopRight, canvas),
            "Top-right dragging preserves the bottom-left anchor");
        Equal(new Rectangle(30, 60, 120, 95),
            RecordingSelectionGeometry.ResizeFromCorner(selection, new Point(-20, 15), RecordingSelectionDragMode.BottomLeft, canvas),
            "Bottom-left dragging preserves the top-right anchor");
        Equal(new Rectangle(50, 60, 120, 95),
            RecordingSelectionGeometry.ResizeFromCorner(selection, new Point(20, 15), RecordingSelectionDragMode.BottomRight, canvas),
            "Bottom-right dragging preserves the top-left anchor");

        var limits = new[]
        {
            (RecordingSelectionDragMode.TopLeft, new Point(999, 999), new Rectangle(148, 138, 2, 2)),
            (RecordingSelectionDragMode.TopRight, new Point(-999, 999), new Rectangle(50, 138, 2, 2)),
            (RecordingSelectionDragMode.BottomLeft, new Point(999, -999), new Rectangle(148, 60, 2, 2)),
            (RecordingSelectionDragMode.BottomRight, new Point(-999, -999), new Rectangle(50, 60, 2, 2)),
            (RecordingSelectionDragMode.TopLeft, new Point(-999, -999), new Rectangle(0, 0, 150, 140)),
            (RecordingSelectionDragMode.TopRight, new Point(999, -999), new Rectangle(50, 0, 590, 140)),
            (RecordingSelectionDragMode.BottomLeft, new Point(-999, 999), new Rectangle(0, 60, 150, 420)),
            (RecordingSelectionDragMode.BottomRight, new Point(999, 999), new Rectangle(50, 60, 590, 420))
        };
        foreach (var (corner, delta, expected) in limits)
            Equal(expected, RecordingSelectionGeometry.ResizeFromCorner(selection, delta, corner, canvas),
                $"{corner} drag respects the 2 x 2 minimum and the physical desktop edges");
        foreach (var corner in new[] { RecordingSelectionDragMode.TopLeft, RecordingSelectionDragMode.TopRight,
            RecordingSelectionDragMode.BottomLeft, RecordingSelectionDragMode.BottomRight })
            Equal(selection, RecordingSelectionGeometry.ResizeFromCorner(selection, Point.Empty, corner, canvas),
                "Grabbing a corner without movement must not jump or replace the selected rectangle");
    }

    private static async Task CheckDiscardAsync()
    {
        int capturedFrames = 0;
        var secondFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new RecordingOptions(new Rectangle(-100, -80, 160, 120), 15, false,
            FFmpegPath: Path.Combine(Path.GetTempPath(), "Framelet-unavailable-" + Guid.NewGuid().ToString("N"), "ffmpeg.exe"));
        await using var session = new ScreenRecordingSession(options, (bitmap, number) =>
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Navy);
            graphics.FillRectangle(Brushes.Gold, (int)(number % 10) * 8, 12, 20, 24);
            if (Interlocked.Increment(ref capturedFrames) >= 2) secondFrame.TrySetResult();
        });
        Assert(session.UsesNativeBackend, "The injected frame source uses the native encoder");
        await session.StartAsync().ConfigureAwait(false);
        await secondFrame.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        await session.PauseAsync().ConfigureAwait(false);
        Assert(session.State == RecordingState.Paused && session.CanResume, "The recording is paused before explicit discard");
        Assert(Volatile.Read(ref capturedFrames) >= 2 && session.RecordedTime > TimeSpan.Zero, "The test created real encoded frames before discard");

        string recovery = Path.GetFullPath(session.RecoveryDirectory);
        string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Framelet"));
        Assert(string.Equals(Path.GetDirectoryName(recovery), parent, StringComparison.OrdinalIgnoreCase),
            "The test session owns a directory immediately under the Framelet temporary root");
        Assert(Directory.Exists(recovery) && Directory.EnumerateFiles(recovery, "*.mp4").Any(),
            "The paused recording has a recoverable MP4 file");

        // All sentinels are unique to this test. A common prefix deliberately catches overbroad cleanup.
        string neighboringFile = recovery + "-keep.txt";
        string neighboringDirectory = recovery + "-keep";
        string neighboringClip = Path.Combine(neighboringDirectory, "keep.mp4");
        const string sentinel = "Existing neighboring files must survive explicit recording discard.";
        Directory.CreateDirectory(neighboringDirectory);
        try
        {
            await File.WriteAllTextAsync(neighboringFile, sentinel).ConfigureAwait(false);
            await File.WriteAllTextAsync(neighboringClip, sentinel).ConfigureAwait(false);
            string ownSegmentDirectory = Path.Combine(recovery, "segments");
            Directory.CreateDirectory(ownSegmentDirectory);
            await File.WriteAllTextAsync(Path.Combine(ownSegmentDirectory, "owned-segment.txt"), "This session's temporary segment.").ConfigureAwait(false);

            await session.DiscardAsync().ConfigureAwait(false);
            Assert(!Directory.Exists(recovery), "Discard removes this session's MP4 and nested temporary segments");
            Assert(!session.CanResume, "An explicitly discarded session cannot resume");
            Assert(await File.ReadAllTextAsync(neighboringFile).ConfigureAwait(false) == sentinel,
                "Discard preserves the adjacent file and its contents");
            Assert(await File.ReadAllTextAsync(neighboringClip).ConfigureAwait(false) == sentinel,
                "Discard preserves a neighboring directory whose name shares the recording prefix");
            await ExpectDisposedAsync(session).ConfigureAwait(false);
            await session.DiscardAsync().ConfigureAwait(false);
            Assert(!Directory.Exists(recovery), "Repeated discard is harmless");
        }
        finally
        {
            // Delete only this test's exact sentinel files and then its empty directory, never the shared parent.
            File.Delete(neighboringFile);
            File.Delete(neighboringClip);
            if (Directory.Exists(neighboringDirectory)) Directory.Delete(neighboringDirectory);
        }
    }

    private static async Task ExpectDisposedAsync(ScreenRecordingSession session)
    {
        try { await session.ResumeAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { return; }
        throw new InvalidOperationException("Resuming after discard must reject the disposed recording.");
    }

    private static void Equal<T>(T expected, T actual, string behavior) where T : IEquatable<T>
        => Assert(expected.Equals(actual), $"{behavior}: expected {expected}, got {actual}");

    private static void Assert(bool condition, string behavior)
    {
        if (!condition) throw new InvalidOperationException(behavior);
    }
}
