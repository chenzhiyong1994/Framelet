namespace Framelet;

internal static class AppDiagnostics
{
    public static void Trace(string message)
    {
        if (Environment.GetEnvironmentVariable("FRAMELET_TRACE") != "1") return;
        Write(message);
    }
    public static void Error(Exception error) => Write(error.ToString());
    private static void Write(string message)
    {
        try { var directory = Path.GetDirectoryName(Path.GetFullPath(AppSettings.ProfilePath))!; Directory.CreateDirectory(directory); File.AppendAllText(Path.Combine(directory, "diagnostics.log"), DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine); } catch { }
    }
}
