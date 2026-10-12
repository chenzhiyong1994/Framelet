using System.Text.Json;

namespace Framelet;

internal static class AppSelfTests
{
    public static int Run(string[] args)
    {
        int outputIndex = Array.IndexOf(args, "--output");
        string output = Path.GetFullPath(outputIndex >= 0 && outputIndex + 1 < args.Length ? args[outputIndex + 1] : Path.Combine(Environment.CurrentDirectory, "artifacts", "self-tests"));
        Directory.CreateDirectory(output);
        var results = new List<string>();
        try
        {
            results.AddRange(EditorSelfTests.Run(Path.Combine(output, "editor")));
            if (results.Any(result => result.StartsWith("FAIL", StringComparison.Ordinal))) throw new InvalidOperationException("Editor behavior checks failed; see individual results above.");
            SettingsRoundTrip(output, results);
            PanelDockingChecks(results);
            results.AddRange(FFmpegDiscoveryChecks.Run(output));
            TestFileFormats(output, results);
            if (args.Contains("--with-recording"))
            {
                Task.Run(async () => await ScreenRecordingSession.RunEncodingSelfTestAsync(Path.Combine(output, "recording"))).GetAwaiter().GetResult();
                Task.Run(async () => await VideoTrimService.RunSelfTestAsync(Path.Combine(output, "recording", "recording-self-test.mp4"), Path.Combine(output, "recording"))).GetAwaiter().GetResult();
                results.Add("PASS Recording: actual H.264 / WMV encoding, pause/resume and segment joining");
                results.Add("PASS Video trim: preview, MP4/WMV trimmed output, decoding and input protection");
            }
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { success = true, timestamp = DateTimeOffset.Now, results }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            results.Add("FAIL " + ex);
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { success = false, timestamp = DateTimeOffset.Now, results }, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
    }
    private static void SettingsRoundTrip(string output, List<string> results)
    {
        string original = AppSettings.ProfilePath;
        try
        {
            AppSettings.ProfilePath = Path.Combine(output, "test-profile.json");
            var settings = new AppSettings { OutputFolder = Path.Combine(output, "自动保存"), IncludeCursor = true, FixedWidth = 1280, FixedHeight = 720, Output = OutputTarget.EditorAndClipboard, AlternateHotkeys = true, PanelX = -1920, PanelY = 250, PanelDockEdge = "Left" };
            settings.Save(); var loaded = AppSettings.Load();
            Assert(loaded.OutputFolder == settings.OutputFolder && loaded.IncludeCursor && loaded.FixedWidth == 1280 && loaded.FixedHeight == 720 && loaded.Output == OutputTarget.EditorAndClipboard && loaded.AlternateHotkeys, "settings round-trip");
            Assert(loaded.PanelX == -1920 && loaded.PanelY == 250 && loaded.PanelDockEdge == "Left", "docked panel position persists");
            File.WriteAllText(AppSettings.ProfilePath, "{\"FixedWidth\":-50,\"DelaySeconds\":10000,\"OutputFormat\":\"invalid\",\"Output\":999}");
            loaded = AppSettings.Load(); Assert(loaded.FixedWidth == 1 && loaded.DelaySeconds == 30 && loaded.OutputFormat == "png" && loaded.Output == OutputTarget.Editor, "settings validation");
            File.WriteAllText(AppSettings.ProfilePath, "{ malformed"); loaded = AppSettings.Load();
            Assert(loaded.FixedWidth == 640 && AppSettings.LoadWarning != null && File.ReadAllText(AppSettings.ProfilePath) == "{ malformed", "corrupt profile preservation");
            results.Add("PASS Settings: Unicode path, durable round-trip, invalid value bounds, corrupt input preservation");
        }
        finally { AppSettings.ProfilePath = original; }
    }
    private static void TestFileFormats(string output, List<string> results)
    {
        using var image = new Bitmap(83, 61);
        using (var g = Graphics.FromImage(image)) { g.Clear(Color.White); g.FillRectangle(Brushes.CornflowerBlue, 10, 10, 20, 30); }
        foreach (string extension in new[] { "png", "jpg", "bmp", "gif", "tiff" })
        {
            string path = Path.Combine(output, "capture-export." + extension);
            CapturePanel.SaveBitmap(image, path);
            using var restored = new Bitmap(path);
            Assert(restored.Width == 83 && restored.Height == 61, "output " + extension);
            if (extension is "png" or "bmp" or "tiff") Assert(restored.GetPixel(15, 15).ToArgb() == Color.CornflowerBlue.ToArgb(), "lossless output " + extension);
        }
        results.Add("PASS Capture output: real PNG/JPEG/BMP/GIF/TIFF files decode at expected dimensions");
    }
    private static void PanelDockingChecks(List<string> results)
    {
        var area = new Rectangle(0, 0, 1920, 1040);
        var panel = new Size(420, 208);
        Assert(PanelDocking.Snap(new Point(740, 15), panel, area, 20) == (new Point(740, 0), PanelEdge.Top), "panel snaps to top");
        Assert(PanelDocking.Snap(new Point(8, 300), panel, area, 20) == (new Point(0, 300), PanelEdge.Left), "panel snaps left");
        Assert(PanelDocking.Snap(new Point(1488, 300), panel, area, 20) == (new Point(1500, 300), PanelEdge.Right), "panel snaps right");
        Assert(PanelDocking.Snap(new Point(500, 820), panel, area, 20) == (new Point(500, 832), PanelEdge.Bottom), "panel avoids taskbar");
        Assert(PanelDocking.Snap(new Point(500, 300), panel, area, 20) == (new Point(500, 300), PanelEdge.None), "free panel stays undocked");
        Assert(PanelDocking.Anchor(new Point(1500, 300), new Size(48, 48), area, PanelEdge.Right) == new Point(1872, 300), "collapsed icon stays at right edge");
        Assert(PanelDocking.Anchor(new Point(1872, 300), panel, area, PanelEdge.Right) == new Point(1500, 300), "expanded panel remains on screen");
        var secondary = new Rectangle(-2560, -240, 2560, 1400);
        Assert(PanelDocking.Snap(new Point(-2550, 400), panel, secondary, 20) == (new Point(-2560, 400), PanelEdge.Left), "negative monitor coordinates");
        Assert(PanelDocking.Anchor(new Point(3000, 2000), panel, area, PanelEdge.None) == new Point(1500, 832), "removed monitor recovery");
        Assert(PanelDocking.Anchor(Point.Empty, panel, new Rectangle(0, 0, 320, 200), PanelEdge.Right) == Point.Empty, "small work area clamps safely");
        results.Add("PASS Floating panel: four edges, icon expansion, taskbar avoidance, negative monitors and off-screen recovery");
    }
    private static void Assert(bool condition, string behavior) { if (!condition) throw new InvalidOperationException(behavior); }
}
