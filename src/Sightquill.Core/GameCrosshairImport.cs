using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Sightquill.Core;

public static class GameCrosshairImport
{
    public static SavedProfile Read(string code)
    {
        code = code.Trim();
        if (code.Length > 4096) throw new InvalidDataException("The crosshair code is too long.");
        if (code.StartsWith("CSGO-", StringComparison.Ordinal)) return CounterStrike(code);
        if (code.StartsWith("0;", StringComparison.Ordinal)) return Valorant(code);
        throw new InvalidDataException("Paste a Valorant code starting with 0; or a CS:GO / CS2 code starting with CSGO-. Other game codes are not supported.");
    }
    private static void Arms(List<CrosshairMark> marks, double horizontal, double vertical, double thickness, double offset, double opacity, double outline, double outlineOpacity, bool tShape = false)
    {
        if (thickness <= 0 || (opacity <= 0 && (outline <= 0 || outlineOpacity <= 0))) return;
        var half = thickness / 2;
        if (horizontal > 0)
        {
            marks.Add(new(-offset - horizontal, -half, horizontal, thickness, opacity, outline, outlineOpacity, -1, 0));
            marks.Add(new(offset, -half, horizontal, thickness, opacity, outline, outlineOpacity, 1, 0));
        }
        if (vertical > 0)
        {
            marks.Add(new(-half, offset, thickness, vertical, opacity, outline, outlineOpacity, 0, 1));
            if (!tShape) marks.Add(new(-half, -offset - vertical, thickness, vertical, opacity, outline, outlineOpacity, 0, -1));
        }
    }
    private static SavedProfile Finish(string source, string color, List<CrosshairMark> marks)
    {
        if (marks.Count == 0) throw new InvalidDataException("This code has no visible static crosshair. Enable visible lines or a center dot in the source game.");
        var extent = Math.Ceiling(marks.Max(m => Math.Max(Math.Max(Math.Abs(m.X), Math.Abs(m.X + m.Width)), Math.Max(Math.Abs(m.Y), Math.Abs(m.Y + m.Height))) + m.Outline) * 2);
        var artwork = new ImportedArtwork { Source = source, ReferenceSize = Math.Max(4, extent), Marks = marks, GeometryVersion = source == "Valorant" ? 2 : 1 };
        artwork.Validate();
        return new SavedProfile { Name = source + " crosshair", Reticle = new ReticleSettings { Shape = ReticleShape.Imported, Color = color, Size = Math.Clamp(artwork.ReferenceSize, 4, 160), Outline = false, ScaleWithResolution = false, Artwork = artwork } };
    }
    private static SavedProfile Valorant(string code)
    {
        var tokens = code.Split(';'); var fields = new Dictionary<string, string>(); var section = "G"; var primarySeen = false;
        for (var i = 1; i < tokens.Length;)
        {
            var key = tokens[i++];
            if (key is "P" or "A" or "S") { section = key; if (key == "P") { if (primarySeen) throw new InvalidDataException("Duplicate Valorant primary section."); primarySeen = true; } continue; }
            if (key.Length == 0 || i >= tokens.Length || tokens[i] is "P" or "A" or "S" || tokens[i].Length == 0) throw new InvalidDataException("Incomplete Valorant key/value pair.");
            var value = tokens[i++];
            if (section == "P" && !fields.TryAdd(key, value)) throw new InvalidDataException("Duplicate Valorant setting: " + key);
        }
        if (!primarySeen) throw new InvalidDataException("The Valorant code has no primary crosshair section (P).");
        var known = new HashSet<string>("c u h o t d a z f s m b 0b 0a 0l 0v 0g 0t 0o 0m 0s 0f 0e 1b 1a 1l 1v 1g 1t 1o 1m 1s 1f 1e".Split(' '));
        foreach (var key in fields.Keys) if (!known.Contains(key)) throw new InvalidDataException("Unsupported Valorant setting: " + key);
        double Number(string key, double fallback, double min, double max)
        {
            if (!fields.TryGetValue(key, out var text)) return fallback;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || n < min || n > max)
                throw new InvalidDataException("Invalid Valorant setting: " + key);
            return n;
        }
        bool Flag(string key, bool fallback) { var n = Number(key, fallback ? 1 : 0, 0, 1); if (n != 0 && n != 1) throw new InvalidDataException("Invalid Valorant switch: " + key); return n == 1; }
        var colors = new[] { "#FFFFFF", "#00FF00", "#7FFF00", "#DFFF00", "#FFFF00", "#00FFFF", "#FF00FF", "#FF0000", "#FFFFFF" };
        var colorIndex = Number("c", 0, 0, 8);
        if (colorIndex != Math.Truncate(colorIndex)) throw new InvalidDataException("Invalid Valorant color index.");
        var color = colors[(int)colorIndex];
        if (colorIndex == 8 && fields.TryGetValue("u", out var custom))
        {
            if (!Regex.IsMatch(custom, "^[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$")) throw new InvalidDataException("Invalid Valorant custom color.");
            color = "#" + custom[..6].ToUpperInvariant();
        }
        var outline = Flag("h", true) ? Number("t", 1, 0, 6) : 0;
        var outlineAlpha = Number("o", .5, 0, 1);
        var marks = new List<CrosshairMark>();
        var dotIndex = 0;
        for (var layer = 0; layer <= 1; layer++)
        {
            var p = layer.ToString(CultureInfo.InvariantCulture);
            if (!Flag(p + "b", true)) continue;
            var length = Number(p + "l", layer == 0 ? 6 : 2, 0, 40);
            var vertical = Flag(p + "g", false) ? Number(p + "v", layer == 0 ? 6 : 2, 0, 40) : length;
            var first = marks.Count;
            var offset = Number(p + "o", layer == 0 ? 3 : 10, 0, 40);
            // VCRDB reference preview includes a four-pixel firing-error base offset.
            if (Flag(p + "f", true) && !Flag("m", false)) offset += 4;
            Arms(marks, length, vertical, Number(p + "t", 2, 0, 10), offset, Number(p + "a", layer == 0 ? .8 : .35, 0, 1), outline, outlineAlpha);
            for (var i = first; i < marks.Count; i++)
            {
                var mark = marks[i];
                var odd = (mark.AxisX != 0 ? mark.Height : mark.Width) % 2;
                marks[i] = mark with {
                    X = mark.AxisX == 0 ? Math.Floor(mark.X) : mark.X - (mark.AxisX < 0 ? odd : 0),
                    Y = mark.AxisY == 0 ? Math.Floor(mark.Y) : mark.Y - (mark.AxisY < 0 ? odd : 0),
                    MoveMultiplier = Flag(p + "m", layer == 1) ? Number(p + "s", 1, 0, 3) : 0,
                    FireMultiplier = Flag(p + "f", true) ? Number(p + "e", 1, 0, 3) : 0,
                    FadeOnFire = mark.AxisY == -1 && Flag("f", true)
                };
            }
            // Reference order is right, left, bottom, top within each line layer.
            if (marks.Count > first + 1 && marks[first].AxisX == -1)
                (marks[first], marks[first + 1]) = (marks[first + 1], marks[first]);
            if (layer == 0) dotIndex = marks.Count;
        }
        if (Flag("d", false)) { var size = Number("z", 2, 1, 6); var alpha = Number("a", 1, 0, 1); if (alpha > 0 || (outline > 0 && outlineAlpha > 0)) marks.Insert(dotIndex, new(-Math.Ceiling(size / 2), -Math.Ceiling(size / 2), size, size, alpha, outline, outlineAlpha)); }
        var profile = Finish("Valorant", color, marks);
        profile.Reticle.Dynamic = marks.Any(m => (m.AxisX != 0 || m.AxisY != 0) && (m.MoveMultiplier > 0 || m.FireMultiplier > 0 || m.FadeOnFire));
        return profile;
    }
    private static SavedProfile CounterStrike(string code)
    {
        const string alphabet = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefhijkmnopqrstuvwxyz23456789";
        if (!Regex.IsMatch(code, "^CSGO(-[A-Za-z0-9]{5}){5}$")) throw new InvalidDataException("Invalid CS:GO / CS2 share-code format.");
        var encoded = code[5..].Replace("-", ""); var total = BigInteger.Zero;
        for (var i = encoded.Length - 1; i >= 0; i--) { var digit = alphabet.IndexOf(encoded[i]); if (digit < 0) throw new InvalidDataException("Invalid character in Counter-Strike code."); total = total * alphabet.Length + digit; }
        var raw = total.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length > 18) throw new InvalidDataException("Counter-Strike code is out of range.");
        var bytes = new byte[18]; raw.CopyTo(bytes, 18 - raw.Length);
        if (bytes[1] != 1 || (bytes.Skip(1).Sum(b => (int)b) & 255) != bytes[0]) throw new InvalidDataException("Counter-Strike checksum or version is invalid. Copy the complete crosshair code again.");
        var index = bytes[10] & 7;
        var palette = new[] { "#FF0000", "#00FF00", "#FFFF00", "#0000FF", "#00FFFF" };
        var color = index < palette.Length ? palette[index] : $"#{bytes[4]:X2}{bytes[5]:X2}{bytes[6]:X2}";
        // Static reference at 1080p. Weapon-dependent offsets and dynamic behavior are not reproduced.
        const double units = 1080d / 480;
        var length = Math.Round(bytes[14] / 10d * units);
        var thickness = Math.Max(1, Math.Round(bytes[12] / 10d * units));
        var gap = Math.Round((4 + unchecked((sbyte)bytes[2]) / 10d) * units);
        var outline = (bytes[10] & 8) != 0 ? Math.Min(16, bytes[3] / 2d) : 0;
        var alpha = (bytes[13] & 64) != 0 ? bytes[7] / 255d : 1;
        var marks = new List<CrosshairMark>();
        Arms(marks, length, length, thickness, gap, alpha, outline, 1, (bytes[13] & 128) != 0);
        if ((bytes[13] & 16) != 0 && alpha > 0) marks.Add(new(-Math.Floor(thickness / 2), -Math.Floor(thickness / 2), thickness, thickness, alpha, outline));
        return Finish("CS:GO / CS2", color, marks);
    }
}
