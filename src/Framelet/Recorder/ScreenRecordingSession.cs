using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Framelet;

public enum RecordingState { Ready, Recording, Paused, Saving, Finished, Faulted }

public sealed record RecordingOptions(
    Rectangle Bounds,
    int FramesPerSecond = 15,
    bool IncludeCursor = true,
    string? AudioDevice = null,
    string? FFmpegPath = null);

public static class FFmpegLocator
{
    public static string Resolve(string? configuredPath = null)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return Resolve(configuredPath, Environment.GetEnvironmentVariable("FRAMELET_FFMPEG"), AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable("PATH"),
            [Path.Combine(local, "Microsoft", "WinGet"), Path.Combine(programFiles, "WinGet")]);
    }

    internal static string Resolve(string? configuredPath, string? environmentPath, string appDirectory,
        string? executablePath, IEnumerable<string> wingetRoots)
    {
        var explicitPath = string.IsNullOrWhiteSpace(configuredPath)
            ? environmentPath : configuredPath;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var normalized = Environment.ExpandEnvironmentVariables(explicitPath.Trim().Trim('"'));
            if (File.Exists(normalized)) return Path.GetFullPath(normalized);
            var recovery = string.IsNullOrWhiteSpace(configuredPath)
                ? "环境变量 FRAMELET_FFMPEG 指定的录屏引擎找不到了。请更新该变量，或在“引擎设置”中选择已安装的引擎。"
                : "已选择的录屏引擎找不到了，请在“引擎设置”中重新自动检测或选择。";
            throw new FileNotFoundException(recovery, normalized);
        }
        foreach (var relative in new[] { "ffmpeg.exe", Path.Combine("tools", "ffmpeg", "bin", "ffmpeg.exe") })
        {
            var candidate = Path.Combine(appDirectory, relative);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        foreach (var directory in (executablePath ?? "").Split(Path.PathSeparator))
        {
            var normalized = Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"'));
            // Empty or relative PATH entries would unexpectedly search the working directory.
            if (string.IsNullOrWhiteSpace(normalized) || !Path.IsPathFullyQualified(normalized)) continue;
            var candidate = Path.Combine(normalized, "ffmpeg.exe");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        foreach (var winget in wingetRoots)
        {
            var link = Path.Combine(winget, "Links", "ffmpeg.exe");
            if (File.Exists(link)) return Path.GetFullPath(link);
            var packages = Path.Combine(winget, "Packages");
            if (!Directory.Exists(packages)) continue;
            try
            {
                foreach (var package in Directory.EnumerateDirectories(packages, "*FFmpeg*").Order(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var found = Directory.EnumerateFiles(package, "ffmpeg.exe", new EnumerationOptions
                        {
                            RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint
                        }).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                        if (found is not null) return Path.GetFullPath(found);
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        throw new FileNotFoundException("还没有找到录屏引擎。Framelet 使用 FFmpeg 保存和剪辑视频，请打开“引擎设置”获取或选择已安装的引擎。截图功能不受影响。");
    }

    public static async Task<string> CheckAvailableAsync(string? configuredPath = null)
    {
        var path = Resolve(configuredPath);
        var result = await RunAsync(path, ["-hide_banner", "-version"], TimeSpan.FromSeconds(8));
        if (result.ExitCode != 0 || !Regex.IsMatch(result.Log, @"(?m)^ffmpeg version\s", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("这个文件不是可用的 FFmpeg 引擎，请在“引擎设置”中选择 ffmpeg.exe。");
        return path;
    }

    public static async Task<IReadOnlyList<string>> GetAudioDevicesAsync(string? configuredPath = null)
    {
        var result = await RunAsync(Resolve(configuredPath), ["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"], TimeSpan.FromSeconds(8));
        return Regex.Matches(result.Log, "\"(?<name>[^\"]+)\" \\(audio\\)")
            .Select(match => match.Groups["name"].Value).Distinct().ToArray();
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    internal static async Task<(int ExitCode, string Log)> RunAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout)
    {
        using var process = new Process { StartInfo = CreateStartInfo(executable, arguments) };
        process.Start();
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(timeout); }
        catch (TimeoutException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync();
            throw new TimeoutException("FFmpeg 响应超时。");
        }
        return (process.ExitCode, (await errors) + (await output));
    }
}

public sealed class ScreenRecordingSession : IAsyncDisposable
{
    private readonly IRecordingSessionBackend backend;
    public bool UsesNativeBackend => backend is NativeRecordingSession;
    public bool CanResume => backend.CanResume;
    public RecordingState State => backend.State;
    public TimeSpan RecordedTime => backend.RecordedTime;
    public string RecoveryDirectory => backend.RecoveryDirectory;
    public bool EncoderExited => backend.EncoderExited;

    public ScreenRecordingSession(RecordingOptions options)
    {
        backend = string.IsNullOrWhiteSpace(options.AudioDevice)
            ? new NativeRecordingSession(options)
            : new FFmpegRecordingSession(options);
    }

    internal ScreenRecordingSession(RecordingOptions options, Action<Bitmap, long> renderFrame)
        => backend = new NativeRecordingSession(options, renderFrame);

    public Task StartAsync() => backend.StartAsync();
    public Task PauseAsync() => backend.PauseAsync();
    public Task ResumeAsync() => backend.ResumeAsync();
    public Task<string> FinishAsync(string outputPath) => backend.FinishAsync(outputPath);
    public ValueTask DisposeAsync() => backend.DisposeAsync();
    public async Task DiscardAsync()
    {
        await backend.DisposeAsync();
        // Only this session's generated recovery directory may be removed by an explicit discard.
        var directory = Path.GetFullPath(RecoveryDirectory);
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Framelet"));
        if (!string.Equals(Path.GetDirectoryName(directory), parent, StringComparison.OrdinalIgnoreCase)
            || !Regex.IsMatch(Path.GetFileName(directory), "^recording-[0-9a-f]{32}$"))
            throw new InvalidOperationException("录像临时目录不符合预期，未删除文件。");
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
    public static Task<string[]> RunEncodingSelfTestAsync(string outputDirectory, string? ffmpegPath = null)
        => FFmpegRecordingSession.RunEncodingSelfTestAsync(outputDirectory, ffmpegPath);
}

internal interface IRecordingSessionBackend : IAsyncDisposable
{
    RecordingState State { get; }
    TimeSpan RecordedTime { get; }
    string RecoveryDirectory { get; }
    bool EncoderExited { get; }
    bool CanResume { get; }
    Task StartAsync();
    Task PauseAsync();
    Task ResumeAsync();
    Task<string> FinishAsync(string outputPath);
}

/// <summary>Optional audio backend. Silent screen recording uses Windows media services instead.</summary>
internal sealed class FFmpegRecordingSession : IRecordingSessionBackend
{
    private readonly RecordingOptions options;
    private readonly string ffmpeg;
    private readonly bool synthetic;
    private readonly List<string> segments = [];
    private readonly Stopwatch activeTime = new();
    private Process? process;
    private Task? logReader;
    private readonly StringBuilder errorLog = new();
    private string? currentSegment;
    private bool successful;
    private bool disposed;

    public RecordingState State { get; private set; } = RecordingState.Ready;
    public TimeSpan RecordedTime => activeTime.Elapsed;
    public string RecoveryDirectory { get; }
    public bool EncoderExited => State == RecordingState.Recording && process is { HasExited: true };
    public bool CanResume => State == RecordingState.Paused;

    public FFmpegRecordingSession(RecordingOptions options) : this(options, false) { }

    private FFmpegRecordingSession(RecordingOptions options, bool synthetic)
    {
        if (options.Bounds.Width < 2 || options.Bounds.Height < 2) throw new ArgumentException("录像区域必须至少为 2 × 2 像素。");
        if (options.FramesPerSecond is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(options), "帧率必须在 1 到 60 之间。");
        this.options = options;
        this.synthetic = synthetic;
        ffmpeg = FFmpegLocator.Resolve(options.FFmpegPath);
        RecoveryDirectory = Path.Combine(Path.GetTempPath(), "Framelet", "recording-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RecoveryDirectory);
    }

    public async Task StartAsync()
    {
        EnsureState(RecordingState.Ready);
        await StartSegmentAsync();
    }

    public async Task PauseAsync()
    {
        EnsureState(RecordingState.Recording);
        activeTime.Stop();
        await StopSegmentAsync();
        State = RecordingState.Paused;
    }

    public async Task ResumeAsync()
    {
        EnsureState(RecordingState.Paused);
        await StartSegmentAsync();
    }

    public async Task<string> FinishAsync(string outputPath)
    {
        if (State != RecordingState.Recording && State != RecordingState.Paused)
            throw new InvalidOperationException("没有可保存的录像。");
        var extension = Path.GetExtension(outputPath).ToLowerInvariant();
        if (extension is not ".mp4" and not ".wmv") throw new ArgumentException("录像格式仅支持 MP4 或 WMV。");
        activeTime.Stop();
        if (State == RecordingState.Recording) await StopSegmentAsync();
        State = RecordingState.Saving;
        var absolutePath = Path.GetFullPath(outputPath);
        var destinationDirectory = Path.GetDirectoryName(absolutePath)!;
        Directory.CreateDirectory(destinationDirectory);
        var staging = Path.Combine(destinationDirectory, ".recording-" + Guid.NewGuid().ToString("N") + extension);
        try
        {
            if (segments.Count == 0) throw new InvalidOperationException("未录制到有效视频帧。");
            var concatPath = Path.Combine(RecoveryDirectory, "segments.txt");
            // Segment names are generated by us and intentionally contain no quotes or user input.
            await File.WriteAllLinesAsync(concatPath, segments.Select(path => $"file '{Path.GetFileName(path)}'"), new UTF8Encoding(false));
            var arguments = new List<string> { "-hide_banner", "-loglevel", "warning", "-y", "-f", "concat", "-safe", "1", "-i", concatPath };
            if (extension == ".mp4") arguments.AddRange(["-c", "copy", "-movflags", "+faststart"]);
            else
            {
                arguments.AddRange(["-c:v", "wmv2", "-q:v", "3"]);
                if (options.AudioDevice is not null) arguments.AddRange(["-c:a", "wmav2", "-b:a", "128k"]);
            }
            arguments.Add(staging);
            // WMV requires a transcode and may legitimately take a long time for a long recording.
            var result = await FFmpegLocator.RunAsync(ffmpeg, arguments, TimeSpan.FromHours(4));
            if (result.ExitCode != 0 || !File.Exists(staging) || new FileInfo(staging).Length < 256)
                throw new InvalidOperationException("视频合并失败：" + Tail(result.Log));
            File.Move(staging, absolutePath, overwrite: true);
            successful = true;
            State = RecordingState.Finished;
            TryCleanSuccessfulSession();
            return absolutePath;
        }
        catch
        {
            State = RecordingState.Faulted;
            if (File.Exists(staging)) File.Delete(staging);
            throw;
        }
    }

    private async Task StartSegmentAsync()
    {
        currentSegment = Path.Combine(RecoveryDirectory, $"segment-{segments.Count:D4}.mkv");
        var fps = options.FramesPerSecond.ToString(CultureInfo.InvariantCulture);
        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-y" };
        if (synthetic)
            args.AddRange(["-re", "-f", "lavfi", "-i", $"testsrc2=size={options.Bounds.Width}x{options.Bounds.Height}:rate={fps}"]);
        else
            args.AddRange(["-thread_queue_size", "1024", "-f", "gdigrab", "-framerate", fps,
                "-draw_mouse", options.IncludeCursor ? "1" : "0", "-offset_x", options.Bounds.X.ToString(CultureInfo.InvariantCulture),
                "-offset_y", options.Bounds.Y.ToString(CultureInfo.InvariantCulture), "-video_size", $"{options.Bounds.Width}x{options.Bounds.Height}", "-i", "desktop"]);
        if (options.AudioDevice is not null)
            args.AddRange(["-thread_queue_size", "1024", "-f", "dshow", "-i", "audio=" + options.AudioDevice,
                "-map", "0:v:0", "-map", "1:a:0", "-c:a", "aac", "-b:a", "128k", "-af", "aresample=async=1:first_pts=0"]);
        else args.Add("-an");
        args.AddRange(["-c:v", "libx264", "-preset", "ultrafast", "-crf", "20", "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-pix_fmt", "yuv420p", "-r", fps, "-f", "matroska", currentSegment]);
        lock (errorLog) errorLog.Clear();
        process = new Process { StartInfo = FFmpegLocator.CreateStartInfo(ffmpeg, args) };
        var started = false;
        try
        {
            process.Start();
            started = true;
            logReader = DrainErrorAsync(process);
            activeTime.Start();
            await Task.Delay(750);
            if (process.HasExited)
            {
                await logReader;
                throw new InvalidOperationException("无法开始录像：" + GetLog());
            }
            State = RecordingState.Recording;
        }
        catch
        {
            activeTime.Stop();
            State = RecordingState.Faulted;
            if (started && process is { HasExited: false }) process.Kill(entireProcessTree: true);
            process?.Dispose();
            process = null;
            throw;
        }
    }

    private async Task StopSegmentAsync()
    {
        var currentProcess = process ?? throw new InvalidOperationException("录像进程已结束。");
        try
        {
            if (!currentProcess.HasExited)
            {
                await currentProcess.StandardInput.WriteLineAsync("q");
                await currentProcess.StandardInput.FlushAsync();
                await currentProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            }
            if (logReader is not null) await logReader;
            if (currentProcess.ExitCode != 0 || currentSegment is null || !File.Exists(currentSegment) || new FileInfo(currentSegment).Length < 256)
                throw new InvalidOperationException("录像进程异常结束：" + GetLog());
            segments.Add(currentSegment);
            currentSegment = null;
        }
        catch
        {
            State = RecordingState.Faulted;
            if (!currentProcess.HasExited) currentProcess.Kill(entireProcessTree: true);
            throw;
        }
        finally
        {
            currentProcess.Dispose();
            process = null;
            logReader = null;
        }
    }

    private async Task DrainErrorAsync(Process encoder)
    {
        var buffer = new char[2048];
        int count;
        while ((count = await encoder.StandardError.ReadAsync(buffer)) > 0)
        {
            lock (errorLog)
            {
                errorLog.Append(buffer, 0, count);
                if (errorLog.Length > 16000) errorLog.Remove(0, errorLog.Length - 12000);
            }
        }
    }

    private string GetLog() { lock (errorLog) return Tail(errorLog.ToString()); }
    private static string Tail(string text) => text.Length > 3000 ? text[^3000..] : text;
    private void EnsureState(RecordingState expected)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (State != expected) throw new InvalidOperationException($"当前录像状态为 {State}，不能执行此操作。");
    }
    private void TryCleanSuccessfulSession()
    {
        if (!successful) return;
        try { Directory.Delete(RecoveryDirectory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        activeTime.Stop();
        if (process is not null)
        {
            try { await StopSegmentAsync(); } catch (Exception) { /* Preserve all fragments for recovery. */ }
        }
        TryCleanSuccessfulSession();
    }

    /// <summary>Encodes real synthetic frames, pauses, resumes, concatenates, and writes MP4 and WMV without accessing the desktop or microphone.</summary>
    public static async Task<string[]> RunEncodingSelfTestAsync(string outputDirectory, string? ffmpegPath = null)
    {
        Directory.CreateDirectory(outputDirectory);
        var outputs = new List<string>();
        foreach (var format in new[] { "mp4", "wmv" })
        {
            await using var session = new FFmpegRecordingSession(new RecordingOptions(new Rectangle(0, 0, 320, 240), 10, false, null, ffmpegPath), true);
            await session.StartAsync();
            await Task.Delay(650);
            await session.PauseAsync();
            var stoppedAt = session.RecordedTime;
            await Task.Delay(200);
            if (session.RecordedTime != stoppedAt) throw new InvalidOperationException("暂停后录像计时仍在增加。");
            await session.ResumeAsync();
            await Task.Delay(650);
            var output = Path.Combine(outputDirectory, "recording-self-test." + format);
            await session.FinishAsync(output);
            var decode = await FFmpegLocator.RunAsync(session.ffmpeg,
                ["-v", "error", "-i", output, "-f", "null", "-"], TimeSpan.FromSeconds(30));
            if (decode.ExitCode != 0 || !string.IsNullOrWhiteSpace(decode.Log))
                throw new InvalidOperationException("录像自检无法完整解码导出文件：" + decode.Log);
            outputs.Add(output);
        }
        return outputs.ToArray();
    }
}
