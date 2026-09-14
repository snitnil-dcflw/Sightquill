namespace Sightquill.Core;
public sealed class ReticlePreset(string id, string name, string category, ReticleSettings settings)
{
    private readonly ReticleSettings template = settings.Clone();
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Category { get; } = category;
    public ReticleSettings Create() => template.Clone();
}
public static class PresetCatalog
{
    public static IReadOnlyList<ReticlePreset> All { get; } = Array.AsReadOnly(new[] {
        Make("pinpoint", "Pinpoint", "Dots", ReticleShape.Dot, 4, 1, 0),
        Make("pearl", "Pearl", "Dots", ReticleShape.Dot, 6, 1, 0),
        Make("beacon", "Beacon", "Dots", ReticleShape.Dot, 10, 1, 0),
        Make("needle", "Needle", "Crosses", ReticleShape.Cross, 18, 1, 6),
        Make("classic", "Classic", "Crosses", ReticleShape.Cross, 28, 2, 8),
        Make("wide", "Wide angle", "Crosses", ReticleShape.Cross, 44, 2, 12),
        Make("orbit", "Orbit", "Rings", ReticleShape.Circle, 18, 1, 0),
        Make("halo", "Halo", "Rings", ReticleShape.Circle, 26, 1, 0),
        Make("horizon", "Horizon", "Rings", ReticleShape.Circle, 48, 2, 0),
        Make("swift", "Swift", "Chevrons", ReticleShape.Chevron, 18, 2, 0),
        Make("falcon", "Falcon", "Chevrons", ReticleShape.Chevron, 28, 2, 0),
        Make("wing", "Wing", "Chevrons", ReticleShape.Chevron, 44, 2, 0),
        Make("prism", "Prism", "Diamonds", ReticleShape.Diamond, 18, 1, 0),
        Make("facet", "Facet", "Diamonds", ReticleShape.Diamond, 28, 1, 0),
        Make("kite", "Kite", "Diamonds", ReticleShape.Diamond, 44, 2, 0),
        Make("frame", "Frame", "Brackets", ReticleShape.Brackets, 20, 1, 0),
        Make("box", "Box cut", "Brackets", ReticleShape.Brackets, 32, 2, 0),
        Make("outpost", "Outpost", "Brackets", ReticleShape.Brackets, 48, 2, 0),
        Make("anchor", "Anchor", "T-shaped", ReticleShape.TShape, 18, 1, 6),
        Make("trident", "Trident", "T-shaped", ReticleShape.TShape, 28, 2, 8),
        Make("monument", "Monument", "T-shaped", ReticleShape.TShape, 44, 2, 12),
        Make("focus", "Focus", "Precision", ReticleShape.Hybrid, 24, 1, 4, true),
        Make("scope", "Scope", "Precision", ReticleShape.Hybrid, 36, 1, 6, true),
        Make("survey", "Survey", "Precision", ReticleShape.Hybrid, 52, 2, 10, true)
    });
    private static ReticlePreset Make(string id, string name, string category, ReticleShape shape, double size, double thickness, double gap, bool dot = false)
        => new(id, name, category, new ReticleSettings { Shape = shape, Size = size, Thickness = thickness, Gap = gap, CenterDot = dot });
}
