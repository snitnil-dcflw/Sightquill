namespace Sightquill.Core;

public enum ReticleShape { Cross, Dot, Circle, Chevron, Diamond, Brackets, TShape, Hybrid, Imported }

public sealed class ReticleSettings
{
    public ReticleShape Shape { get; set; } = ReticleShape.Cross;
    public double Size { get; set; } = 28;
    public double Thickness { get; set; } = 2;
    public double Gap { get; set; } = 5;
    public double Opacity { get; set; } = 1;
    public double Rotation { get; set; }
    public string Color { get; set; } = "#80D8F7";
    public bool CenterDot { get; set; }
    public bool Outline { get; set; } = true;
    public bool ScaleWithResolution { get; set; } = true;
    public bool Dynamic { get; set; }
    public bool AzertyMovement { get; set; } = true;
    public double MovementSpread { get; set; } = 8;
    public double FiringSpread { get; set; } = 6;
    public ImportedArtwork? Artwork { get; set; }
    public double ResolutionScale(int height) => ScaleWithResolution ? Math.Clamp(height / 1080d, 1, 4) : 1;
    public void Normalize()
    {
        Size = Bound(Size, 4, 160, 28);
        Thickness = Bound(Thickness, 1, 16, 2);
        Gap = Bound(Gap, 0, 40, 5);
        Opacity = Bound(Opacity, .1, 1, 1);
        Rotation = Bound(Rotation, 0, 360, 0);
        MovementSpread = Bound(MovementSpread, 0, 24, 8); FiringSpread = Bound(FiringSpread, 0, 24, 6);
        if (!Enum.IsDefined(Shape)) Shape = ReticleShape.Cross;
        if (Color == null || !System.Text.RegularExpressions.Regex.IsMatch(Color, "^#[0-9a-fA-F]{6}$")) Color = "#80D8F7";
        Color = Color.ToUpperInvariant();
        if (Shape == ReticleShape.Imported) { if (Artwork == null) throw new InvalidDataException("This imported crosshair has no artwork."); Artwork.Validate(); }
        else Artwork = null;
    }
    private static double Bound(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    public ReticleSettings Clone() { var copy = (ReticleSettings)MemberwiseClone(); copy.Artwork = Artwork?.Clone(); return copy; }
}

public sealed class SavedProfile
{
    public string Name { get; set; } = "My crosshair";
    public ReticleSettings Reticle { get; set; } = new();
}

public sealed class AppSettings
{
    public ReticleSettings Current { get; set; } = PresetCatalog.All[0].Create();
    public HashSet<string> Favorites { get; set; } = [];
    public List<SavedProfile> Profiles { get; set; } = [];
    public string SelectedPresetId { get; set; } = "pinpoint";
    public string MonitorId { get; set; } = "";
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public bool HowToFishMode { get; set; }
    public void Normalize()
    {
        Current ??= new(); Current.Normalize();
        Favorites = (Favorites ?? []).Where(x => x != null && PresetCatalog.All.Any(p => p.Id == x)).ToHashSet();
        Profiles = (Profiles ?? []).Where(x => x != null && x.Reticle != null).Take(100).ToList();
        foreach (var profile in Profiles) { profile.Reticle.Normalize(); profile.Name = CleanName(profile.Name); }
        OffsetX = Math.Clamp(OffsetX, -2000, 2000); OffsetY = Math.Clamp(OffsetY, -2000, 2000);
        MonitorId ??= "";
        if (!PresetCatalog.All.Any(x => x.Id == SelectedPresetId)) SelectedPresetId = "pinpoint";
    }
    public static string CleanName(string? name) => string.IsNullOrWhiteSpace(name) ? "My crosshair" : name.Trim()[..Math.Min(name.Trim().Length, 48)];
}
