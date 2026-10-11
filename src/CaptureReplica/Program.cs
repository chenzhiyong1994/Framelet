using System.Drawing.Imaging;

namespace CaptureReplica;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetDefaultFont(new Font("Microsoft YaHei UI", 9F));
        Application.ThreadException += (_, e) => { AppDiagnostics.Error(e.Exception); MessageBox.Show(e.Exception.Message, BrandTheme.Name, MessageBoxButtons.OK, MessageBoxIcon.Error); };
        int profile = Array.IndexOf(args, "--profile");
        if (profile >= 0 && profile + 1 < args.Length) AppSettings.ProfilePath = args[profile + 1];
        if (args.Contains("--self-test")) return AppSelfTests.Run(args);
        if (args.Contains("--fixture")) { Application.Run(new ValidationForm()); return 0; }
        string? initial = args.FirstOrDefault(a => File.Exists(a) && !a.Equals(AppSettings.ProfilePath, StringComparison.OrdinalIgnoreCase));
        var request = new LaunchRequest(initial == null ? null : Path.GetFullPath(initial), args.Contains("--editor"), args.Contains("--recorder"));
        // Keep the internal channel and profile location compatible with the 0.1 release.
        string channelName = "CaptureStudio_" + StableId(AppSettings.ProfilePath);
        using var single = new Mutex(true, "Local\\" + channelName, out bool created);
        if (!created) { if (!SingleInstanceChannel.Send(channelName, request)) MessageBox.Show("帧简已在运行。请双击通知区域中的图标显示工具栏。", BrandTheme.Name); return 0; }
        var settings = AppSettings.Load();
        AppDiagnostics.Trace("Main: constructing panel");
        var panel = new CapturePanel(settings);
        AppDiagnostics.Trace("Main: panel constructed");
        using var channel = new SingleInstanceChannel(channelName, panel);
        AppDiagnostics.Trace("Main: initial image " + (initial != null));
        if (initial != null) panel.Shown += (_, _) => { AppDiagnostics.Trace("Shown: opening image"); panel.OpenFile(initial); AppDiagnostics.Trace("Shown: opened image"); };
        if (args.Contains("--editor")) panel.Shown += (_, _) => panel.OpenBlank();
        if (args.Contains("--recorder")) panel.Shown += (_, _) => panel.OpenRecorder();
        Application.Run(panel);
        return 0;
    }
    private static string StableId(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(value).ToLowerInvariant())))[..16];
}
