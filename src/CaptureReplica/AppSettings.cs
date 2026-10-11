using System.Text.Json;

namespace CaptureReplica;

public enum OutputTarget { Editor, Clipboard, File, AutoSave, EditorAndClipboard, Printer }

public sealed class AppSettings
{
    public bool IncludeCursor { get; set; }
    public bool CopyAfterCapture { get; set; }
    public bool TopMost { get; set; } = true;
    public bool HideOnCapture { get; set; } = true;
    public bool EnableHotkeys { get; set; } = true;
    public bool AlternateHotkeys { get; set; }
    public bool Timestamp { get; set; }
    public int DelaySeconds { get; set; }
    public int FixedWidth { get; set; } = 640;
    public int FixedHeight { get; set; } = 480;
    public int PanelX { get; set; } = -1;
    public int PanelY { get; set; } = -1;
    public string OutputFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Framelet");
    public string OutputFormat { get; set; } = "png";
    public OutputTarget Output { get; set; }
    public static string ProfilePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FSCaptureReplica", "settings.json");
    public static string? LoadWarning { get; private set; }
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(ProfilePath)) return new();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ProfilePath)) ?? new();
            settings.DelaySeconds = Math.Clamp(settings.DelaySeconds, 0, 30);
            settings.FixedWidth = Math.Clamp(settings.FixedWidth, 1, 20000);
            settings.FixedHeight = Math.Clamp(settings.FixedHeight, 1, 20000);
            if (settings.OutputFormat is not ("png" or "jpg" or "bmp" or "gif" or "tiff")) settings.OutputFormat = "png";
            if (!Enum.IsDefined(settings.Output)) settings.Output = OutputTarget.Editor;
            return settings;
        }
        catch (Exception ex) { LoadWarning = ex.Message; return new(); }
    }
    public void Save()
    {
        string path = Path.GetFullPath(ProfilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}
