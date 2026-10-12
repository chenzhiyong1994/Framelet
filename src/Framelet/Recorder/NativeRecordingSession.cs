using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;

namespace Framelet;

/// <summary>Owns the capture bitmap and Windows encoder on one MTA thread.</summary>
internal sealed class NativeRecordingSession : IRecordingSessionBackend
{
    private readonly RecordingOptions options;
    private readonly Action<Bitmap, long>? renderFrame;
    private readonly BlockingCollection<Action> commands = new();
    private readonly Stopwatch activeTime = new();
    private readonly object timeLock = new();
    private readonly object lifecycleLock = new();
    private readonly TaskCompletionSource workerStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread worker;
    private volatile RecordingState state = RecordingState.Ready;
    private volatile bool finalized;
    private bool stopping;
    private Task? disposeTask;
    private Bitmap? frame;
    private NativeMediaWriter? writer;
    private Exception? recordingError;
    private long lastFrameSlot = -1;
    private long frameNumber;
    private readonly string recoveryFile;

    public RecordingState State => state;
    public TimeSpan RecordedTime { get { lock (timeLock) return activeTime.Elapsed; } }
    public string RecoveryDirectory { get; }
    public bool EncoderExited => State == RecordingState.Faulted;
    public bool CanResume => State == RecordingState.Paused && !finalized;

    public NativeRecordingSession(RecordingOptions options, Action<Bitmap, long>? renderFrame = null)
    {
        if (options.Bounds.Width < 2 || options.Bounds.Height < 2)
            throw new ArgumentException("录像区域必须至少为 2 × 2 像素。");
        if (options.Bounds.Width > 8192 || options.Bounds.Height > 8192 || (long)options.Bounds.Width * options.Bounds.Height > 33_554_432)
            throw new ArgumentException("录像区域过大，请缩小区域后重试。");
        if (options.FramesPerSecond is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(options), "帧率必须在 1 到 60 之间。");
        this.options = options;
        this.renderFrame = renderFrame;
        RecoveryDirectory = Path.Combine(Path.GetTempPath(), "Framelet", "recording-" + Guid.NewGuid().ToString("N"));
        recoveryFile = Path.Combine(RecoveryDirectory, "recording.mp4");
        worker = new Thread(Work) { IsBackground = true, Name = "Framelet screen recorder" };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
    }

    public Task StartAsync() => InvokeAsync(() =>
    {
        EnsureState(RecordingState.Ready);
        try
        {
            Directory.CreateDirectory(RecoveryDirectory);
            frame = new Bitmap((options.Bounds.Width + 1) & ~1, (options.Bounds.Height + 1) & ~1, PixelFormat.Format32bppRgb);
            writer = new NativeMediaWriter(recoveryFile, frame.Width, frame.Height, options.FramesPerSecond);
            state = RecordingState.Recording;
            lock (timeLock) activeTime.Start();
            CaptureFrame();
        }
        catch (Exception ex)
        {
            Fault(ex);
            throw new InvalidOperationException("无法开始录屏。请确认 Windows 媒体组件可用，或尝试缩小录制区域。\n" + ex.Message, ex);
        }
    });

    public Task PauseAsync() => InvokeAsync(() =>
    {
        EnsureState(RecordingState.Recording);
        lock (timeLock) activeTime.Stop();
        state = RecordingState.Paused;
    });

    public Task ResumeAsync() => InvokeAsync(() =>
    {
        EnsureState(RecordingState.Paused);
        if (finalized) throw new InvalidOperationException("录像已经完成编码，请重新选择位置保存。");
        lock (timeLock) activeTime.Start();
        state = RecordingState.Recording;
    });

    public Task<string> FinishAsync(string outputPath) => InvokeAsync(() =>
    {
        if (state is not (RecordingState.Recording or RecordingState.Paused))
            throw new InvalidOperationException("没有可保存的录像。", recordingError);
        lock (timeLock) activeTime.Stop();
        state = RecordingState.Paused;
        if (!Path.GetExtension(outputPath).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("直接录屏保存为 MP4；其他格式可在视频剪辑中转换。");
        var destination = Path.GetFullPath(outputPath);
        if (destination.StartsWith(RecoveryDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请选择录像临时目录之外的保存位置。");
        string? staging = null;
        try
        {
            if (Directory.Exists(destination)) throw new IOException("该名称已被文件夹占用，请更换文件名。");
            var parent = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(parent);
            staging = Path.Combine(parent, ".framelet-recording-" + Guid.NewGuid().ToString("N") + ".mp4");
            // Check destination access before sealing the encoder, keeping resume possible on early failures.
            using (var target = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                state = RecordingState.Saving;
                CompleteWriter();
                using var source = File.OpenRead(recoveryFile);
                source.CopyTo(target);
                target.Flush(flushToDisk: true);
            }
            File.Move(staging, destination, overwrite: true);
            state = RecordingState.Finished;
            CleanSuccessfulRecording();
            return destination;
        }
        catch
        {
            if (state != RecordingState.Faulted) state = RecordingState.Paused;
            if (staging != null)
            {
                try { File.Delete(staging); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
    });

    private void CaptureFrame()
    {
        var slot = RecordedTime.Ticks * options.FramesPerSecond / TimeSpan.TicksPerSecond;
        if (slot <= lastFrameSlot) return;
        if (renderFrame != null) renderFrame(frame!, frameNumber);
        else
        {
            using var graphics = Graphics.FromImage(frame!);
            graphics.Clear(Color.Black);
            graphics.CopyFromScreen(options.Bounds.Location, Point.Empty, options.Bounds.Size, CopyPixelOperation.SourceCopy);
            if (options.IncludeCursor) NativeCapture.DrawCursor(graphics, options.Bounds.Location);
        }
        long timestamp = slot * TimeSpan.TicksPerSecond / options.FramesPerSecond;
        long end = (slot + 1) * TimeSpan.TicksPerSecond / options.FramesPerSecond;
        writer!.WriteFrame(frame!, timestamp, end - timestamp);
        lastFrameSlot = slot;
        frameNumber++;
    }

    private void CompleteWriter()
    {
        if (finalized) return;
        try
        {
            writer?.Complete();
            finalized = true;
        }
        catch (Exception ex)
        {
            recordingError = ex;
            state = RecordingState.Faulted;
            throw;
        }
        finally { writer?.Dispose(); writer = null; }
    }

    private void Fault(Exception error)
    {
        recordingError = error;
        lock (timeLock) activeTime.Stop();
        state = RecordingState.Faulted;
        try { CompleteWriter(); } catch (Exception) { /* Keep the original recording error and recovery file. */ }
    }

    private void Work()
    {
        try
        {
            while (!stopping)
            {
                var wait = Timeout.Infinite;
                if (state == RecordingState.Recording)
                {
                    long due = (lastFrameSlot + 1) * TimeSpan.TicksPerSecond / options.FramesPerSecond;
                    wait = (int)Math.Clamp((due - RecordedTime.Ticks) / TimeSpan.TicksPerMillisecond, 1, 1000);
                }
                if (commands.TryTake(out var command, wait)) command();
                else if (state == RecordingState.Recording)
                {
                    try { CaptureFrame(); }
                    catch (Exception ex) { Fault(ex); }
                }
            }
        }
        finally
        {
            try { writer?.Dispose(); }
            finally
            {
                frame?.Dispose();
                commands.Dispose();
                workerStopped.TrySetResult();
            }
        }
    }

    private Task InvokeAsync(Action action) => InvokeAsync(() => { action(); return true; });
    private Task<T> InvokeAsync<T>(Func<T> action)
    {
        lock (lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(disposeTask != null, this);
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            commands.Add(() =>
            {
                try { completion.SetResult(action()); }
                catch (Exception ex) { completion.SetException(ex); }
            });
            return completion.Task;
        }
    }

    private void EnsureState(RecordingState expected)
    {
        if (recordingError != null) throw new InvalidOperationException("录屏已中断：" + recordingError.Message, recordingError);
        if (state != expected) throw new InvalidOperationException($"当前录像状态为 {state}，不能执行此操作。");
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycleLock)
        {
            if (disposeTask != null) return new ValueTask(disposeTask);
            commands.Add(() =>
            {
                lock (timeLock) activeTime.Stop();
                try { CompleteWriter(); } catch (Exception) { /* Preserve finalized data whenever possible. */ }
                if (state is RecordingState.Ready or RecordingState.Recording) state = RecordingState.Paused;
                stopping = true;
            });
            disposeTask = workerStopped.Task;
            return new ValueTask(disposeTask);
        }
    }

    private void CleanSuccessfulRecording()
    {
        try { Directory.Delete(RecoveryDirectory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
