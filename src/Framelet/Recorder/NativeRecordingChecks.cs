using System.Buffers.Binary;
using System.Text;

namespace Framelet;

/// <summary>Exercises real Windows encoding with synthetic pixels, without capturing the desktop or microphone.</summary>
internal static class NativeRecordingChecks
{
    public static async Task<IReadOnlyList<string>> RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        string? previousEngine = Environment.GetEnvironmentVariable("FRAMELET_FFMPEG");
        string unavailableEngine = Path.Combine(output, "missing-engine-" + Guid.NewGuid().ToString("N"), "ffmpeg.exe");
        Environment.SetEnvironmentVariable("FRAMELET_FFMPEG", unavailableEngine);
        try
        {
            var options = new RecordingOptions(new Rectangle(0, 0, 320, 240), 15, false, FFmpegPath: unavailableEngine);
            // Test normal backend selection separately; the injected frame source never touches the desktop.
            await using (var selected = new ScreenRecordingSession(options))
                Assert(selected.UsesNativeBackend, "Silent recording must select Windows media services even with a missing configured FFmpeg");

            int frames = 0;
            await using (var session = new ScreenRecordingSession(options, (bitmap, number) =>
            {
                DrawBands(bitmap, number);
                Interlocked.Increment(ref frames);
            }))
            {
                Assert(session.UsesNativeBackend, "Synthetic source uses the native recording backend");
                await session.StartAsync().ConfigureAwait(false);
                await Task.Delay(420).ConfigureAwait(false);
                await session.PauseAsync().ConfigureAwait(false);
                TimeSpan pausedAt = session.RecordedTime;
                int pausedFrames = Volatile.Read(ref frames);
                Assert(pausedFrames > 1 && session.State == RecordingState.Paused && session.CanResume, "Native recording captures frames and can pause");
                await Task.Delay(600).ConfigureAwait(false);
                Assert(session.RecordedTime == pausedAt && Volatile.Read(ref frames) == pausedFrames, "Pause freezes both elapsed time and captured frames");
                await session.ResumeAsync().ConfigureAwait(false);
                await Task.Delay(420).ConfigureAwait(false);
                Assert(Volatile.Read(ref frames) > pausedFrames && session.RecordedTime > pausedAt, "Resume continues recording");

                // An existing directory must survive an invalid save destination, including its contents.
                string conflict = Path.Combine(output, "occupied.mp4");
                Directory.CreateDirectory(conflict);
                string sentinel = Path.Combine(conflict, "keep.txt");
                const string sentinelText = "Existing destination data must remain untouched.";
                await File.WriteAllTextAsync(sentinel, sentinelText).ConfigureAwait(false);
                await ExpectSaveFailureAsync(session, conflict).ConfigureAwait(false);
                Assert(session.State == RecordingState.Paused && session.CanResume, "Early save failure leaves the recording paused and resumable");
                Assert(await File.ReadAllTextAsync(sentinel).ConfigureAwait(false) == sentinelText, "Failed save preserves the destination directory and contents");
                Assert(Directory.EnumerateFiles(session.RecoveryDirectory, "*.mp4").Any(), "Failed save retains the recovery recording");

                // A locked existing file fails after encoding is finalized: retain the original and allow Save again.
                string lockedPath = Path.Combine(output, "locked.mp4");
                byte[] original = Encoding.UTF8.GetBytes("Do not replace an inaccessible existing file.");
                await File.WriteAllBytesAsync(lockedPath, original).ConfigureAwait(false);
                using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    await ExpectSaveFailureAsync(session, lockedPath).ConfigureAwait(false);
                    Assert(session.State == RecordingState.Paused && !session.CanResume, "Late save failure permits retrying the save but cannot append to finalized video");
                    var preserved = new byte[original.Length];
                    locked.ReadExactly(preserved);
                    Assert(locked.Length == original.Length && preserved.SequenceEqual(original), "Failed replacement preserves the original file bytes");
                }
                string recovery = Directory.EnumerateFiles(session.RecoveryDirectory, "*.mp4").Single();
                AssertMovie(recovery, new Size(320, 240));
                string finalPath = Path.GetFullPath(Path.Combine(output, "native-recording.mp4"));
                string saved = await session.FinishAsync(finalPath).ConfigureAwait(false);
                Assert(saved == finalPath && session.State == RecordingState.Finished, "Retry saves the original recording successfully");
                AssertMovie(saved, new Size(320, 240), session.RecordedTime);
                Assert(!Directory.Exists(session.RecoveryDirectory), "Successful save removes its recovery directory");
                Assert(!Directory.EnumerateFiles(output, ".framelet-recording-*.mp4").Any(), "Save attempts remove temporary destination files");
            }

            await using (var odd = new ScreenRecordingSession(options with { Bounds = new Rectangle(0, 0, 321, 241) }, DrawBands))
            {
                await odd.StartAsync().ConfigureAwait(false);
                await Task.Delay(220).ConfigureAwait(false);
                string saved = await odd.FinishAsync(Path.Combine(output, "native-odd-size.mp4")).ConfigureAwait(false);
                AssertMovie(saved, new Size(322, 242));
                Assert(!Directory.Exists(odd.RecoveryDirectory), "Odd-sized capture cleans its recovery directory");
            }

            // Closing an unsaved recording must finalize the recovery file instead of discarding captured frames.
            var interrupted = new ScreenRecordingSession(options, DrawBands);
            try
            {
                await interrupted.StartAsync().ConfigureAwait(false);
                await Task.Delay(220).ConfigureAwait(false);
            }
            finally { await interrupted.DisposeAsync().ConfigureAwait(false); }
            Assert(Directory.Exists(interrupted.RecoveryDirectory), "Disposing an unsaved recording preserves recovery data");
            string disposedRecovery = Directory.EnumerateFiles(interrupted.RecoveryDirectory, "*.mp4").Single();
            AssertMovie(disposedRecovery, new Size(320, 240), interrupted.RecordedTime);
            File.Copy(disposedRecovery, Path.Combine(output, "native-disposed-recovery.mp4"), overwrite: true);
            // Keep the evidence in test output; remove only the exact file and empty directory this session created.
            File.Delete(disposedRecovery);
            Directory.Delete(interrupted.RecoveryDirectory);
            return
            [
                "PASS Native recording: real MP4 without FFmpeg, synthetic color frames, pause/resume excludes paused duration and cleanup",
                "PASS Native recording recovery: save failures protect existing data and allow retry; disposal finalizes unsaved recovery video",
                "PASS Native recording dimensions: 321 x 241 capture exports a valid 322 x 242 MP4"
            ];
        }
        finally { Environment.SetEnvironmentVariable("FRAMELET_FFMPEG", previousEngine); }
    }

    private static void DrawBands(Bitmap bitmap, long number)
    {
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Blue);
        graphics.FillRectangle(Brushes.Red, 0, 0, bitmap.Width, bitmap.Height / 3);
        graphics.FillRectangle(Brushes.Lime, 0, bitmap.Height / 3, bitmap.Width, bitmap.Height / 3);
        graphics.FillRectangle(Brushes.White, (int)(number % 20) * 5, 4, 4, 4);
    }

    private static async Task ExpectSaveFailureAsync(ScreenRecordingSession session, string path)
    {
        try { await session.FinishAsync(path).ConfigureAwait(false); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        throw new InvalidOperationException("An occupied or locked output path must reject the save");
    }

    private static void AssertMovie(string path, Size expectedSize, TimeSpan? expectedDuration = null)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var top = Boxes(bytes, 0, bytes.Length).ToArray();
        Assert(top.Any(box => box.Type == "ftyp" && box.Length >= 8), "MP4 contains its file type box");
        Assert(top.Any(box => box.Type == "mdat" && box.Length > 0), "MP4 contains encoded media data");
        var movie = top.Single(box => box.Type == "moov");
        if (expectedDuration is { } elapsed)
        {
            var header = Boxes(bytes, movie.Offset, movie.Length).Single(box => box.Type == "mvhd");
            int timescaleOffset = bytes[header.Offset] switch { 0 => 12, 1 => 20, _ => throw new InvalidDataException("Unsupported movie header version") };
            bool extendedDuration = bytes[header.Offset] == 1;
            Assert(header.Length >= timescaleOffset + (extendedDuration ? 12 : 8), "MP4 movie header contains its timescale and duration");
            uint timescale = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(header.Offset + timescaleOffset, 4));
            ulong duration = extendedDuration
                ? BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(header.Offset + timescaleOffset + 4, 8))
                : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(header.Offset + timescaleOffset + 4, 4));
            Assert(timescale > 0 && duration > 0, "MP4 has a positive duration and valid timescale");
            double seconds = (double)duration / timescale;
            // 150 ms allows two 15 fps frames of rounding, but cannot hide the 600 ms pause above.
            Assert(Math.Abs(seconds - elapsed.TotalSeconds) <= 0.15,
                $"MP4 duration {seconds:F3}s matches active recording time {elapsed.TotalSeconds:F3}s without paused time");
        }
        bool expectedTrack = false;
        foreach (var track in Boxes(bytes, movie.Offset, movie.Length).Where(box => box.Type == "trak"))
        {
            var header = Boxes(bytes, track.Offset, track.Length).Single(box => box.Type == "tkhd");
            int widthOffset = bytes[header.Offset] switch { 0 => 76, 1 => 88, _ => throw new InvalidDataException("Unsupported track header version") };
            Assert(header.Length >= widthOffset + 8, "MP4 track header includes display dimensions");
            uint width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(header.Offset + widthOffset, 4));
            uint height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(header.Offset + widthOffset + 4, 4));
            expectedTrack |= width == (uint)expectedSize.Width << 16 && height == (uint)expectedSize.Height << 16;
        }
        Assert(expectedTrack, $"MP4 video track has expected dimensions {expectedSize.Width} x {expectedSize.Height}");
    }

    private static IEnumerable<(string Type, int Offset, int Length)> Boxes(byte[] bytes, int offset, int length)
    {
        int end = checked(offset + length);
        while (offset < end)
        {
            Assert(end - offset >= 8, "MP4 box header is complete");
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            string type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            int headerSize = 8;
            if (size == 1)
            {
                Assert(end - offset >= 16, "MP4 extended size is complete");
                size = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(offset + 8, 8));
                headerSize = 16;
            }
            if (size == 0) size = (ulong)(end - offset);
            Assert(size >= (ulong)headerSize && size <= (ulong)(end - offset), "MP4 box stays within the file or parent box");
            yield return (type, offset + headerSize, (int)size - headerSize);
            offset += (int)size;
        }
    }

    private static void Assert(bool condition, string behavior)
    {
        if (!condition) throw new InvalidOperationException(behavior);
    }
}
