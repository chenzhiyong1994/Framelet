namespace Framelet;

internal static class FFmpegDiscoveryChecks
{
    public static IReadOnlyList<string> Run(string output)
    {
        var root = Path.Combine(output, "engine-discovery");
        Directory.CreateDirectory(root);
        var explicitEngine = Fixture("explicit", "ffmpeg.exe");
        var environmentEngine = Fixture("environment", "ffmpeg.exe");
        var portableEngine = Fixture("portable", "tools", "ffmpeg", "bin", "ffmpeg.exe");
        var pathEngine = Fixture("path", "ffmpeg.exe");
        var wingetEngine = Fixture("winget-packages", "Packages", "Vendor.FFmpeg_Test", "ffmpeg-build", "bin", "ffmpeg.exe");
        var emptyApp = Path.Combine(root, "empty-app");
        Directory.CreateDirectory(emptyApp);

        string Resolve(string? configured, string? environment, string app, string? path, params string[] winget) =>
            FFmpegLocator.Resolve(configured, environment, app, path, winget);
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        Assert(Resolve(explicitEngine, environmentEngine, emptyApp, null) == explicitEngine, "Selected engine takes precedence");
        Assert(Resolve(null, environmentEngine, emptyApp, null) == environmentEngine, "Environment engine is detected");
        Assert(Resolve(null, null, Path.Combine(root, "portable"), null) == portableEngine, "Portable tools directory is detected");
        Assert(Resolve(null, null, emptyApp, " ;\"" + Path.GetDirectoryName(pathEngine) + "\";") == pathEngine, "Quoted PATH directory is detected");
        Assert(Resolve(null, null, emptyApp, null, Path.Combine(root, "winget-packages")) == wingetEngine, "WinGet package is detected without PATH refresh");
        var wingetLink = Fixture("winget-links", "Links", "ffmpeg.exe");
        Fixture("winget-links", "Packages", "Vendor.FFmpeg_Test", "bin", "ffmpeg.exe");
        Assert(Resolve(null, null, emptyApp, null, Path.Combine(root, "winget-links")) == wingetLink, "WinGet link takes precedence over package scanning");
        try
        {
            Resolve(Path.Combine(root, "missing.exe"), environmentEngine, emptyApp, Path.GetDirectoryName(pathEngine));
            throw new InvalidOperationException("A missing explicit engine must not silently switch versions");
        }
        catch (FileNotFoundException) { }
        try
        {
            Resolve(null, Path.Combine(root, "missing-environment.exe"), emptyApp, Path.GetDirectoryName(pathEngine));
            throw new InvalidOperationException("A missing environment engine must not silently switch versions");
        }
        catch (FileNotFoundException ex)
        {
            Assert(ex.Message.Contains("FRAMELET_FFMPEG", StringComparison.Ordinal), "Invalid environment configuration explains how to recover");
        }
        var previousDirectory = Environment.CurrentDirectory;
        Fixture("ffmpeg.exe");
        try
        {
            Environment.CurrentDirectory = Path.GetFullPath(root);
            try
            {
                Resolve(null, null, emptyApp, " ;.;;relative-folder");
                throw new InvalidOperationException("Empty and relative PATH entries must not search the working directory");
            }
            catch (FileNotFoundException) { }
        }
        finally { Environment.CurrentDirectory = previousDirectory; }
        return ["PASS Recording engine: explicit precedence, environment, portable tools, quoted PATH, WinGet, missing configuration and safe empty PATH"];

        string Fixture(params string[] segments)
        {
            var path = Path.GetFullPath(Path.Combine([root, .. segments]));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, []);
            return path;
        }
    }
}
