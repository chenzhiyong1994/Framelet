using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CaptureReplica;

public sealed record VideoMetadata(double DurationSeconds, int Width, int Height, string VideoCodec, bool HasAudio);

public static class VideoTrimService
{
    public static async Task<VideoMetadata> InspectAsync(string inputPath, string? ffmpegPath = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputPath)) throw new FileNotFoundException("找不到视频文件。", inputPath);
        var ffmpeg = FFmpegLocator.Resolve(ffmpegPath);
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        if (!File.Exists(probe))
        {
            var fallback = await RunProcessAsync(ffmpeg, ["-hide_banner", "-i", inputPath], null, 0, cancellationToken);
            var durationMatch = Regex.Match(fallback.Errors, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
            var dimensionsMatch = Regex.Match(fallback.Errors, @"Video:\s*([^,\s]+).*?\b(\d{2,5})x(\d{2,5})\b");
            if (!durationMatch.Success || !dimensionsMatch.Success) throw new InvalidOperationException("无法读取视频信息，请确认文件包含可解码的视频流，或安装与 FFmpeg 配套的 ffprobe.exe。");
            var duration = double.Parse(durationMatch.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
                + double.Parse(durationMatch.Groups[2].Value, CultureInfo.InvariantCulture) * 60
                + double.Parse(durationMatch.Groups[3].Value, CultureInfo.InvariantCulture);
            return new VideoMetadata(duration, int.Parse(dimensionsMatch.Groups[2].Value), int.Parse(dimensionsMatch.Groups[3].Value),
                dimensionsMatch.Groups[1].Value, fallback.Errors.Contains("Audio:", StringComparison.Ordinal));
        }
        var result = await RunProcessAsync(probe,
            ["-v", "error", "-show_entries", "format=duration:stream=codec_type,codec_name,width,height,duration", "-of", "json", inputPath], null, 0, cancellationToken);
        if (result.ExitCode != 0) throw new InvalidOperationException("无法读取视频：" + Tail(result.Errors));
        using var document = JsonDocument.Parse(result.Output);
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(stream => stream.TryGetProperty("codec_type", out var type) && type.GetString() == "video");
        if (video.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("该文件没有视频流。");
        string? durationText = null;
        if (document.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var durationProperty)) durationText = durationProperty.GetString();
        if (durationText is null && video.TryGetProperty("duration", out durationProperty)) durationText = durationProperty.GetString();
        if (!double.TryParse(durationText, CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds) || seconds <= 0)
            throw new InvalidOperationException("无法确定视频时长，暂时不支持编辑此文件。");
        return new VideoMetadata(seconds, video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32(),
            video.GetProperty("codec_name").GetString() ?? "未知", streams.Any(stream => stream.TryGetProperty("codec_type", out var type) && type.GetString() == "audio"));
    }

    public static async Task<Bitmap> GetPreviewAsync(string inputPath, double seconds, string? ffmpegPath = null, CancellationToken cancellationToken = default)
    {
        var start = FFmpegLocator.CreateStartInfo(FFmpegLocator.Resolve(ffmpegPath),
            ["-v", "error", "-ss", seconds.ToString("0.###", CultureInfo.InvariantCulture), "-i", inputPath,
                "-frames:v", "1", "-vf", "scale=640:360:force_original_aspect_ratio=decrease", "-f", "image2pipe", "-c:v", "png", "pipe:1"]);
        using var process = new Process { StartInfo = start };
        process.Start();
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        using var bytes = new MemoryStream();
        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(bytes, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var errorText = await errors;
            if (process.ExitCode != 0 || bytes.Length == 0) throw new InvalidOperationException("无法预览这一帧：" + Tail(errorText));
            bytes.Position = 0;
            using var decoded = Image.FromStream(bytes);
            return new Bitmap(decoded);
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }

    public static async Task<string> ExportAsync(string inputPath, string outputPath, double startSeconds, double endSeconds,
        string? ffmpegPath = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var metadata = await InspectAsync(inputPath, ffmpegPath, cancellationToken);
        if (!double.IsFinite(startSeconds) || !double.IsFinite(endSeconds) || startSeconds < 0 || endSeconds <= startSeconds || endSeconds > metadata.DurationSeconds + .05)
            throw new ArgumentException("剪辑范围无效：开始时间必须小于结束时间，并且不能超出视频时长。");
        var fullInput = Path.GetFullPath(inputPath);
        var fullOutput = Path.GetFullPath(outputPath);
        if (string.Equals(fullInput, fullOutput, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请另存为新文件，以保留原视频。");
        var extension = Path.GetExtension(fullOutput).ToLowerInvariant();
        if (extension is not ".mp4" and not ".wmv") throw new ArgumentException("请选择 MP4 或 WMV 输出格式。");
        var directory = Path.GetDirectoryName(fullOutput)!;
        Directory.CreateDirectory(directory);
        var staging = Path.Combine(directory, ".video-export-" + Guid.NewGuid().ToString("N") + extension);
        var duration = endSeconds - startSeconds;
        var arguments = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y", "-ss", startSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            "-i", fullInput, "-t", duration.ToString("0.###", CultureInfo.InvariantCulture), "-map", "0:v:0", "-map", "0:a:0?",
            "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-pix_fmt", "yuv420p"
        };
        if (extension == ".mp4") arguments.AddRange(["-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart"]);
        else arguments.AddRange(["-c:v", "wmv2", "-q:v", "3", "-c:a", "wmav2", "-b:a", "128k"]);
        arguments.AddRange(["-progress", "pipe:1", "-nostats", staging]);
        try
        {
            var result = await RunProcessAsync(FFmpegLocator.Resolve(ffmpegPath), arguments, progress, duration, cancellationToken);
            if (result.ExitCode != 0 || !File.Exists(staging) || new FileInfo(staging).Length < 256)
                throw new InvalidOperationException("视频导出失败：" + Tail(result.Errors));
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(staging, fullOutput, overwrite: true);
            progress?.Report(100);
            return fullOutput;
        }
        finally
        {
            if (File.Exists(staging))
            {
                try { File.Delete(staging); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static async Task<(int ExitCode, string Output, string Errors)> RunProcessAsync(string executable,
        IEnumerable<string> arguments, IProgress<double>? progress, double duration, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = FFmpegLocator.CreateStartInfo(executable, arguments) };
        process.Start();
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        var output = new StringBuilder();
        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                if (progress is null) output.AppendLine(line);
                else if (duration > 0 && line.StartsWith("out_time_us=", StringComparison.Ordinal)
                    && long.TryParse(line.AsSpan("out_time_us=".Length), out var micros))
                    progress.Report(Math.Clamp(micros / 1_000_000d / duration * 100, 0, 99.5));
            }
            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, output.ToString(), await errors);
        }
        catch
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    private static string Tail(string value) => value.Length > 3000 ? value[^3000..] : value;
    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    public static async Task<string[]> RunSelfTestAsync(string inputPath, string outputDirectory, string? ffmpegPath = null)
    {
        var info = await InspectAsync(inputPath, ffmpegPath);
        if (info.DurationSeconds < 2) throw new ArgumentException("剪辑自检需要至少 2 秒的视频。");
        var paths = new List<string>();
        using var thumbnail = await GetPreviewAsync(inputPath, .5, ffmpegPath);
        if (thumbnail.Width < 2 || thumbnail.Height < 2) throw new InvalidOperationException("预览图像无效。");
        foreach (var extension in new[] { "mp4", "wmv" })
        {
            var path = Path.Combine(outputDirectory, "trimmed-self-test." + extension);
            await ExportAsync(inputPath, path, .5, 1.7, ffmpegPath);
            var trimmed = await InspectAsync(path, ffmpegPath);
            if (Math.Abs(trimmed.DurationSeconds - 1.2) > .25 || trimmed.Width != info.Width || trimmed.Height != info.Height)
                throw new InvalidOperationException("裁剪自检失败：视频时长或尺寸不符合预期。");
            var decoded = await FFmpegLocator.RunAsync(FFmpegLocator.Resolve(ffmpegPath), ["-v", "error", "-i", path, "-f", "null", "-"], TimeSpan.FromSeconds(30));
            if (decoded.ExitCode != 0 || !string.IsNullOrWhiteSpace(decoded.Log)) throw new InvalidOperationException("裁剪输出无法完整解码：" + decoded.Log);
            paths.Add(path);
        }
        try
        {
            await ExportAsync(inputPath, inputPath, 0, 1, ffmpegPath);
            throw new InvalidOperationException("同名输入输出保护未生效。");
        }
        catch (ArgumentException) { }
        try
        {
            await ExportAsync(inputPath, Path.Combine(outputDirectory, "invalid.mp4"), 2, 1, ffmpegPath);
            throw new InvalidOperationException("无效时间范围保护未生效。");
        }
        catch (ArgumentException) { }
        return paths.ToArray();
    }
}
