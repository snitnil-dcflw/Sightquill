using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Sightquill.Bridge;

namespace Sightquill;
public sealed class AimReceiver : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private AimFrame? latest;
    private string status = "Waiting for companion";
    public AimFrame? Latest => Volatile.Read(ref latest);
    public string Status => Volatile.Read(ref status);
    public event Action? Updated;
    public AimReceiver(string pipeName = AimProtocol.PipeName) { worker = Task.Run(() => Receive(pipeName)); }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    private async Task Receive(string pipeName)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
                Volatile.Write(ref status, "Waiting for companion");
                await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var sender)) throw new IOException("Companion identity unavailable.");
                var bytes = new byte[AimProtocol.PacketSize]; long lastSequence = -1;
                while (!stop.IsCancellationRequested)
                {
                    await pipe.ReadExactlyAsync(bytes, stop.Token).ConfigureAwait(false);
                    if (!AimProtocol.TryDecode(bytes, (int)sender, out var frame) || frame!.Sequence <= lastSequence) throw new IOException("Invalid companion frame.");
                    lastSequence = frame.Sequence;
                    Volatile.Write(ref latest, frame); Volatile.Write(ref status, "Companion connected");
                    Updated?.Invoke();
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Volatile.Write(ref status, "Companion disconnected");
            }
            finally { Volatile.Write(ref latest, null); Updated?.Invoke(); }
            try { await Task.Delay(200, stop.Token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
    }
    public void Dispose()
    {
        stop.Cancel();
        // The worker never dispatches onto the UI thread; cancellation unblocks reads/accepts.
        try { worker.GetAwaiter().GetResult(); } finally { stop.Dispose(); }
    }
}
