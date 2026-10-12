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
            var settings = new AppSettings { OutputFolder = Path.Combine(output, "自动保存"), IncludeCursor = true, FixedWidth = 1280, FixedHeight = 720, Output = OutputTarget.EditorAndClipboard, AlternateHotkeys = true };
            settings.Save(); var loaded = AppSettings.Load();
            Assert(loaded.OutputFolder == settings.OutputFolder && loaded.IncludeCursor && loaded.FixedWidth == 1280 && loaded.FixedHeight == 720 && loaded.Output == OutputTarget.EditorAndClipboard && loaded.AlternateHotkeys, "settings round-trip");
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
    private static void Assert(bool condition, string behavior) { if (!condition) throw new InvalidOperationException(behavior); }
}
