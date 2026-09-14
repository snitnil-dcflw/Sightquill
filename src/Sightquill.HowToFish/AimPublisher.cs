using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using Sightquill.Bridge;

namespace Sightquill.HowToFish;
// No Unity API or blocking pipe operations run on the game's render thread.
internal sealed class AimPublisher : IDisposable
{
    private readonly Thread worker;
    private readonly object gate = new object();
    private byte[]? latest;
    private volatile bool stopping;
    private NamedPipeClientStream? active;
    private readonly string pipeName;
    public AimPublisher(string pipeName = AimProtocol.PipeName)
    {
        this.pipeName = pipeName;
        worker = new Thread(Run) { IsBackground = true, Name = "Sightquill aim transport" }; worker.Start();
    }
    public void Publish(AimFrame frame) { var bytes = AimProtocol.Encode(frame); lock (gate) { latest = bytes; Monitor.Pulse(gate); } }
    private void Run()
    {
        while (!stopping)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
                {
                    lock (gate) { if (stopping) return; active = pipe; }
                    pipe.Connect(700);
                    byte[]? sent = null;
                    while (!stopping && pipe.IsConnected)
                    {
                        byte[]? bytes;
                        lock (gate)
                        {
                            while (!stopping && ReferenceEquals(latest, sent)) Monitor.Wait(gate);
                            if (stopping) return;
                            bytes = latest;
                        }
                        if (bytes != null && !ReferenceEquals(bytes, sent)) { pipe.Write(bytes, 0, bytes.Length); sent = bytes; }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is TimeoutException || ex is ObjectDisposedException || ex is UnauthorizedAccessException) { }
            finally { lock (gate) active = null; }
            if (!stopping) Thread.Sleep(300);
        }
    }
    public void Dispose() { stopping = true; lock (gate) { Monitor.PulseAll(gate); active?.Dispose(); active = null; } }
}
