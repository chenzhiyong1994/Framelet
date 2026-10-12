using System.IO.Pipes;
using System.Text.Json;

namespace Framelet;

internal sealed record LaunchRequest(string? FilePath, bool Editor = false, bool Recorder = false);

internal sealed class SingleInstanceChannel : IDisposable
{
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task listener;
    public SingleInstanceChannel(string name, CapturePanel panel)
    {
        listener = Task.Run(async () =>
        {
            while (!shutdown.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(shutdown.Token);
                    using var reader = new StreamReader(pipe);
                    string? line = await reader.ReadLineAsync(shutdown.Token);
                    if (line is null || line.Length > 32768) continue;
                    var request = JsonSerializer.Deserialize<LaunchRequest>(line);
                    if (request != null && panel.IsHandleCreated && !panel.IsDisposed) panel.BeginInvoke(() => panel.ReceiveLaunch(request));
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { AppDiagnostics.Error(ex); }
            }
        });
    }
    public static bool Send(string name, LaunchRequest request)
    {
        try { using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out); pipe.Connect(1500); using var writer = new StreamWriter(pipe) { AutoFlush = true }; writer.WriteLine(JsonSerializer.Serialize(request)); return true; }
        catch (Exception ex) { AppDiagnostics.Error(ex); return false; }
    }
    public void Dispose() { shutdown.Cancel(); }
}
