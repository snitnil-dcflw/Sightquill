using System.Buffers.Binary;

namespace Sightquill.Core;

public sealed record CrosshairMark(double X, double Y, double Width, double Height, double Opacity = 1, double Outline = 0, double OutlineOpacity = 1, int AxisX = 0, int AxisY = 0, double MoveMultiplier = 1, double FireMultiplier = 1, bool FadeOnFire = false)
{
    public CrosshairMark AtMotion(double movementPixels, double firingPixels, double firingAmount, double ratio)
    {
        var expansion = Math.Round(movementPixels * MoveMultiplier + firingPixels * FireMultiplier) / ratio;
        var alpha = FadeOnFire ? 1 - Math.Clamp(firingAmount, 0, 1) : 1;
        return this with { X = X + AxisX * expansion, Y = Y + AxisY * expansion, Opacity = Opacity * alpha, OutlineOpacity = OutlineOpacity * alpha };
    }
}
public sealed class ImportedArtwork
{
    public string Source { get; set; } = "Imported";
    public string? PngBase64 { get; set; }
    public double ReferenceSize { get; set; } = 32;
    public List<CrosshairMark> Marks { get; set; } = [];
    public int GeometryVersion { get; set; }
    public ImportedArtwork Clone() => new() { Source = Source, PngBase64 = PngBase64, ReferenceSize = ReferenceSize, Marks = Marks?.ToList() ?? [], GeometryVersion = GeometryVersion };
    public void Validate()
    {
        if (!double.IsFinite(ReferenceSize) || ReferenceSize < 1 || ReferenceSize > 2048 || Source == null || Source.Length > 64)
            throw new InvalidDataException("Invalid imported artwork dimensions.");
        if (PngBase64 != null)
        {
            if (PngBase64.Length > 400_000) throw new InvalidDataException("The embedded PNG is too large.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(PngBase64); } catch (FormatException) { throw new InvalidDataException("Invalid embedded PNG data."); }
            ValidatePngHeader(bytes, 256);
            if (Marks is { Count: > 0 }) throw new InvalidDataException("The profile mixes two artwork formats.");
        }
        else
        {
            if (Marks == null || Marks.Count is < 1 or > 16) throw new InvalidDataException("No visible crosshair marks were found.");
            foreach (var m in Marks)
            {
                if (m == null || new[] { m.X, m.Y, m.Width, m.Height, m.Opacity, m.Outline, m.OutlineOpacity, m.MoveMultiplier, m.FireMultiplier }.Any(v => !double.IsFinite(v)) ||
                    Math.Abs(m.X) > 1024 || Math.Abs(m.Y) > 1024 || m.Width <= 0 || m.Width > 1024 || m.Height <= 0 || m.Height > 1024 || m.Opacity < 0 || m.Opacity > 1 || m.Outline < 0 || m.Outline > 16 || m.OutlineOpacity < 0 || m.OutlineOpacity > 1 || m.MoveMultiplier < 0 || m.MoveMultiplier > 3 || m.FireMultiplier < 0 || m.FireMultiplier > 3 || Math.Abs((long)m.AxisX) + Math.Abs((long)m.AxisY) > 1)
                    throw new InvalidDataException("Invalid imported crosshair geometry.");
                if (Math.Max(Math.Max(Math.Abs(m.X), Math.Abs(m.X + m.Width)), Math.Max(Math.Abs(m.Y), Math.Abs(m.Y + m.Height))) + m.Outline > ReferenceSize / 2 + .01)
                    throw new InvalidDataException("Imported artwork extends outside its declared size.");
            }
            if (GeometryVersion == 0 && Source is "Valorant" or "CS:GO / CS2")
            {
                var completeGroups = Source == "Valorant" && Marks.Count is 4 or 5 or 8 or 9 && Enumerable.Range(0, Marks.Count / 4).All(group =>
                {
                    var n = group * 4;
                    return Marks[n].X + Marks[n].Width <= 0 && Marks[n + 1].X >= 0 && Marks[n + 2].Y >= 0 && Marks[n + 3].Y + Marks[n + 3].Height <= 0;
                });
                for (var i = 0; i < Marks.Count; i++)
                {
                    var mark = Marks[i]; var cx = mark.X + mark.Width / 2; var cy = mark.Y + mark.Height / 2;
                    var dot = (completeGroups && Marks.Count % 4 == 1 && i == Marks.Count - 1) || (Math.Abs(cx) <= .5 && Math.Abs(cy) <= .5 && mark.Width == mark.Height);
                    var x = dot ? 0 : completeGroups ? (i % 4 == 0 ? -1 : i % 4 == 1 ? 1 : 0) : Math.Abs(cx) >= Math.Abs(cy) ? Math.Sign(cx) : 0;
                    var y = dot ? 0 : completeGroups ? (i % 4 == 2 ? 1 : i % 4 == 3 ? -1 : 0) : Math.Abs(cy) > Math.Abs(cx) ? Math.Sign(cy) : 0;
                    Marks[i] = mark with { AxisX = x, AxisY = y, X = x == 0 ? -mark.Width / 2 : mark.X, Y = y == 0 ? -mark.Height / 2 : mark.Y };
                }
                GeometryVersion = 1;
            }
        }
    }
    public static (int Width, int Height) ValidatePngHeader(byte[] data, int maximum)
    {
        if (data.Length < 33 || !data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) || !data.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("Select a valid PNG image.");
        var width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16, 4)); var height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20, 4));
        if (width < 1 || height < 1 || width > maximum || height > maximum) throw new InvalidDataException($"PNG dimensions must be between 1 and {maximum} pixels.");
        return (width, height);
    }
}
