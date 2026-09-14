using System;
using System.IO;

namespace Sightquill.Bridge;
public enum AimKind { Hidden, Camera, Barrel, Scope }
public static class AimVisibility
{
    public static bool ShouldShow(bool focused, bool cursorLocked, bool playerActive, bool dead, bool paused, bool thinking, bool mainMenu)
        => focused && cursorLocked && playerActive && !dead && !paused && !thinking && !mainMenu;
}
public sealed class AimFrame
{
    public int ProcessId { get; }
    public long Sequence { get; }
    public long UtcTicks { get; }
    public float X { get; }
    public float Y { get; }
    public AimKind Kind { get; }
    public AimFrame(int processId, long sequence, long utcTicks, float x, float y, AimKind kind)
    { ProcessId = processId; Sequence = sequence; UtcTicks = utcTicks; X = x; Y = y; Kind = kind; }
    public bool IsFresh(long nowUtcTicks) => UtcTicks >= nowUtcTicks - TimeSpan.TicksPerMillisecond * 200 && UtcTicks <= nowUtcTicks + TimeSpan.TicksPerMillisecond * 100;
}
public static class AimProtocol
{
    public const string PipeName = "Sightquill.HowToFish.v1";
    public const int PacketSize = 40;
    public static byte[] Encode(AimFrame frame)
    {
        using (var stream = new MemoryStream(PacketSize))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0x31515348); writer.Write(frame.ProcessId); writer.Write(frame.Sequence);
            writer.Write(frame.UtcTicks); writer.Write(frame.X); writer.Write(frame.Y); writer.Write((int)frame.Kind); writer.Write(1);
            return stream.ToArray();
        }
    }
    public static bool TryDecode(byte[] bytes, int trustedProcessId, out AimFrame? frame)
    {
        frame = null;
        if (bytes.Length != PacketSize) return false;
        using (var reader = new BinaryReader(new MemoryStream(bytes, false)))
        {
            if (reader.ReadInt32() != 0x31515348) return false;
            var pid = reader.ReadInt32(); var sequence = reader.ReadInt64(); var ticks = reader.ReadInt64();
            var x = reader.ReadSingle(); var y = reader.ReadSingle(); var kind = reader.ReadInt32(); var version = reader.ReadInt32();
            if (version != 1 || pid <= 0 || pid != trustedProcessId || sequence < 0 || ticks <= 0 || ticks > DateTime.MaxValue.Ticks ||
                float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y) || x < 0 || x > 1 || y < 0 || y > 1 || kind < 0 || kind > 3) return false;
            frame = new AimFrame(pid, sequence, ticks, x, y, (AimKind)kind); return true;
        }
    }
    public static (int X, int Y) ToScreen(AimFrame frame, int left, int top, int width, int height, int offsetX = 0, int offsetY = 0)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var x = left + (int)Math.Round(frame.X * width) + offsetX;
        var y = top + (int)Math.Round(frame.Y * height) + offsetY;
        return (Math.Min(left + width - 1, Math.Max(left, x)), Math.Min(top + height - 1, Math.Max(top, y)));
    }
}
