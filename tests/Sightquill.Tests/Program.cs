using Sightquill.Core;
using Sightquill.Bridge;

var failed = 0;
void Test(string name, Action run)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
void Check(bool value, string message) { if (!value) throw new Exception(message); }

Test("Invalid geometry cannot reach the renderer", () => {
    var s = new ReticleSettings { Size = double.NaN, Thickness = 1000, Gap = -10, Opacity = double.PositiveInfinity, Rotation = double.NaN, Color = "not a color", Shape = (ReticleShape)999 };
    s.Normalize();
    Check(double.IsFinite(s.Size) && s.Size >= 4 && s.Size <= 160, "size must be finite and bounded");
    Check(s.Thickness == 16 && s.Gap == 0, "thickness and gap bounds");
    Check(double.IsFinite(s.Opacity) && s.Opacity <= 1 && s.Opacity >= .1, "opacity must be finite");
    Check(double.IsFinite(s.Rotation) && s.Color == "#80D8F7" && Enum.IsDefined(s.Shape), "invalid appearance must recover");
});
Test("Editing one preset never changes the catalog", () => {
    var a = PresetCatalog.All[0].Create(); var b = PresetCatalog.All[0].Create();
    var original = b.Size; a.Size = 151;
    Check(b.Size == original && PresetCatalog.All[0].Create().Size == original, "preset aliasing");
    Check(PresetCatalog.All.Count >= 24 && PresetCatalog.All.Select(x => x.Id).Distinct().Count() == PresetCatalog.All.Count, "catalog must have unique presets");
    Check(PresetCatalog.All.Select(x => x.Create().Shape).Distinct().Count() >= 8, "shape variety");
});
var dir = Path.Combine(Path.GetTempPath(), "sightquill-tests-" + Guid.NewGuid());
Test("Aim transport rejects invalid and foreign frames", () => {
    var bytes = AimProtocol.Encode(new AimFrame(123, 7, DateTime.UtcNow.Ticks, .25f, .75f, AimKind.Barrel));
    Check(AimProtocol.TryDecode(bytes, 123, out var frame) && frame!.X == .25f && frame.Y == .75f && frame.Kind == AimKind.Barrel, "valid packet lost coordinates");
    Check(!AimProtocol.TryDecode(bytes, 456, out _), "sender PID mismatch accepted");
    Check(!AimProtocol.TryDecode(new byte[39], 123, out _), "truncated packet accepted");
    foreach (var bad in new[] { float.NaN, float.PositiveInfinity, -.1f, 1.1f }) {
        Check(!AimProtocol.TryDecode(AimProtocol.Encode(new AimFrame(123, 1, DateTime.UtcNow.Ticks, bad, .5f, AimKind.Barrel)), 123, out _), "invalid coordinate accepted");
    }
});
Test("Old and future aim samples cannot keep a stale marker visible", () => {
    var now = DateTime.UtcNow.Ticks;
    Check(new AimFrame(123, 1, now - TimeSpan.FromMilliseconds(100).Ticks, .5f, .5f, AimKind.Barrel).IsFresh(now), "fresh sample rejected");
    Check(!new AimFrame(123, 1, now - TimeSpan.FromMilliseconds(300).Ticks, .5f, .5f, AimKind.Barrel).IsFresh(now), "old sample accepted");
    Check(!new AimFrame(123, 1, now + TimeSpan.FromSeconds(5).Ticks, .5f, .5f, AimKind.Barrel).IsFresh(now), "future sample accepted");
});
Test("Controller menu states hide aim even when the cursor remains locked", () => {
    Check(AimVisibility.ShouldShow(true, true, true, false, false, false, false), "active player hidden");
    Check(!AimVisibility.ShouldShow(true, true, true, false, true, false, false), "pause with locked cursor accepted");
    Check(!AimVisibility.ShouldShow(true, true, true, false, false, true, false), "thinking/menu accepted");
    Check(!AimVisibility.ShouldShow(true, true, true, false, false, false, true), "main menu accepted");
    Check(!AimVisibility.ShouldShow(false, true, true, false, false, false, false), "background game accepted");
});
Test("Aim coordinates follow the game client including negative monitor coordinates", () => {
    var f = new AimFrame(123, 1, DateTime.UtcNow.Ticks, .25f, .75f, AimKind.Barrel);
    Check(AimProtocol.ToScreen(f, -1920, 100, 1280, 720) == (-1600, 640), "wrong client coordinate mapping");
    Check(AimProtocol.ToScreen(f, 100, 40, 800, 600, 10, -20) == (310, 470), "offset mapping wrong");
    var edge = new AimFrame(123, 2, DateTime.UtcNow.Ticks, 1, 0, AimKind.Camera);
    Check(AimProtocol.ToScreen(edge, 100, 40, 800, 600, 2000, -2000) == (899, 40), "marker escaped client bounds");
});
Directory.CreateDirectory(dir);
Test("Valorant branches share one center and retain explicit movement axes", () => {
    var art = GameCrosshairImport.Read("0;P;h;0;0t;1;0l;4;0o;0;0a;1;1t;3;1l;1;1o;2;1a;1").Reticle.Artwork!;
    for (var i = 0; i < art.Marks.Count; i++) {
        var m = art.Marks[i];
        Check(m.AxisX != 0 ? m.Y + m.Height / 2 == -.5 : m.X + m.Width / 2 == -.5, "branch perpendicular axis is displaced");
        Check((m.AxisX, m.AxisY) == ((i % 4) switch { 0 => 1, 1 => -1, _ => 0 }, (i % 4) switch { 2 => 1, 3 => -1, _ => 0 }), "wrong branch direction");
    }
    var legacy = art.Clone(); legacy.GeometryVersion = 0;
    legacy.Marks = legacy.Marks.Select(m => m with { AxisX = 0, AxisY = 0, X = m.AxisX == 0 ? Math.Ceiling(m.X) : m.X, Y = m.AxisY == 0 ? Math.Ceiling(m.Y) : m.Y }).ToList();
    legacy.Validate();
    Check(legacy.GeometryVersion == 1 && legacy.Marks.All(m => m.AxisX != 0 ? m.Y + m.Height / 2 == 0 : m.X + m.Width / 2 == 0), "legacy profiles must keep their existing centering migration");
});
Test("Dynamic motion expands, settles and is independent of frame rate", () => {
    var motion = new DynamicMotion();
    motion.Step(true, false, 1d / 60); Check(motion.Movement > 0 && motion.Movement < 1 && motion.Firing == 0, "movement must ease in independently");
    for (var i = 0; i < 60; i++) motion.Step(true, true, 1d / 60);
    Check(motion.Movement == 1 && motion.Firing == 1, "held input must settle at the limit");
    for (var i = 0; i < 90; i++) motion.Step(false, false, 1d / 60);
    Check(motion.Movement == 0 && motion.Firing == 0, "released input must return to rest");
    var fast = new DynamicMotion(); var slow = new DynamicMotion();
    for (var i = 0; i < 12; i++) slow.Step(true, true, 1d / 60);
    for (var i = 0; i < 48; i++) fast.Step(true, true, 1d / 240);
    Check(Math.Abs(fast.Movement - slow.Movement) < .00001, "animation depends on FPS");
    fast.Reset(); Check(fast.Movement == 0 && fast.Firing == 0, "focus loss reset failed");
    var path = Path.Combine(dir, "dynamic.json"); var original = new SavedProfile { Reticle = new ReticleSettings { Dynamic = true, MovementSpread = 12, FiringSpread = 4, AzertyMovement = false } };
    ProfileExchange.Write(path, original); var restored = ProfileExchange.Read(path).Reticle;
    Check(restored.Dynamic && restored.MovementSpread == 12 && restored.FiringSpread == 4 && !restored.AzertyMovement, "dynamic profile lost settings");
});
Test("Valorant import preserves independent layers and custom color", () => {
    var imported = GameCrosshairImport.Read("0;P;c;8;u;12AB EFFF".Replace(" ", "") + ";h;0;d;1;z;2;0t;1;0l;4;0g;1;0v;2;0o;3;0a;0.7;1t;2;1l;2;1o;10;1a;0.4");
    var art = imported.Reticle.Artwork!;
    Check(imported.Reticle.Color == "#12ABEF" && art.Marks.Count == 9, "lost custom color or a layer");
    Check(art.Marks[0].Width == 4 && art.Marks[2].Height == 2 && art.Marks[0].Opacity == .7 && art.Marks[5].Opacity == .4, "independent lengths or opacity lost");
    var path = Path.Combine(dir, "valorant.json"); ProfileExchange.Write(path, imported);
    Check(System.Text.Json.JsonSerializer.Serialize(ProfileExchange.Read(path)) == System.Text.Json.JsonSerializer.Serialize(imported), "game artwork lost in sharing");
    var original = imported.Reticle.Clone(); original.Artwork!.Marks.Clear(); Check(art.Marks.Count == 9, "artwork aliases across profiles");
    var simple = GameCrosshairImport.Read("0;P;h;0;0t;1;0l;2;0o;1;0a;1;0f;0;1b;0;A;c;7;S;c;1");
    Check(simple.Reticle.Artwork!.Marks.Count == 4 && simple.Reticle.Color == "#FFFFFF", "ADS overwrote primary settings");
});
Test("Valorant animation preserves independent source switches, multipliers and fade", () => {
    var profile = GameCrosshairImport.Read("0;P;h;0;d;1;f;1;0t;2;0l;4;0o;10;0m;0;0f;1;0e;0.5;1t;2;1l;3;1o;25;1m;1;1s;2;1f;0");
    var marks = profile.Reticle.Artwork!.Marks;
    var inner = marks[0]; var dot = marks[4]; var outer = marks[5];
    Check(profile.Reticle.Dynamic && inner.MoveMultiplier == 0 && inner.FireMultiplier == .5 && outer.MoveMultiplier == 2 && outer.FireMultiplier == 0, "source animation settings lost");
    Check(inner.AtMotion(8, 6, 1, 1).X == inner.X + 3 && outer.AtMotion(8, 6, 1, 1).X == outer.X + 16, "layers do not animate independently");
    Check(dot.AtMotion(8, 6, 1, 1) == dot, "center dot must stay fixed");
    Check(marks[3].AtMotion(0, 6, 1, 1).Opacity == 0 && marks[3].AtMotion(0, 6, 1, 1).OutlineOpacity == 0, "upper branch and outline must fade together");
    Check(marks.All(m => m.AtMotion(0, 0, 0, 1) == m), "release must restore exact source geometry");
    var path = Path.Combine(dir, "animated-valorant.json"); ProfileExchange.Write(path, profile);
    Check(ProfileExchange.Read(path).Reticle.Artwork!.Marks.SequenceEqual(marks), "animation metadata lost in JSON round trip");
    var disabled = GameCrosshairImport.Read("0;P;h;0;f;0;0m;0;0f;0;1m;0;1f;0");
    Check(!disabled.Reticle.Dynamic, "fully static source code must stay static");
    var invalid = profile.Reticle.Artwork.Clone(); invalid.Marks[0] = inner with { MoveMultiplier = double.NaN };
    try { invalid.Validate(); throw new Exception("invalid multiplier accepted"); } catch (InvalidDataException) { }
});
Test("Counter-Strike reference share code and invalid codes", () => {
    var imported = GameCrosshairImport.Read("CSGO-WsnnD-eHaMw-QNDf9-oxuDh-ydOUD");
    var marks = imported.Reticle.Artwork!.Marks;
    Check(imported.Reticle.Color == "#00FF00" && marks.Count == 4 && marks[0].Width == 22 && marks[1].X == 4 && marks[0].Outline == 1 && marks[0].Opacity == 200 / 255d, "reference code decoded incorrectly");
    foreach (var bad in new[] { "garbage", "CSGO-AsnnD-eHaMw-QNDf9-oxuDh-ydOUD", "CSGO-GADqf-jjyJ8-cSP2r-smZRo-TO2xK", "0;P;h", "0;P;0t;NaN", "0;P;h;0;h;1", "0;P;0b;0;1b;0;d;0" })
    { try { GameCrosshairImport.Read(bad); throw new Exception("invalid code accepted: " + bad); } catch (InvalidDataException) { } }
});
Test("Resolution scaling preserves size preferences", () => {
    var settings = new ReticleSettings { Size = 24 };
    Check(settings.ResolutionScale(1080) == 1 && settings.ResolutionScale(2160) == 2, "1080p / 4K ratio");
    settings.ScaleWithResolution = false;
    Check(settings.ResolutionScale(2160) == 1 && settings.Clone().ScaleWithResolution == false && settings.Size == 24, "fixed-pixel preference lost");
});
Test("Companion installation is optional, removable and preserves unrelated files", () => {
    try { CompanionInstallation.ValidateGame(""); throw new Exception("empty folder accepted"); } catch (IOException) { }
    var game = Path.Combine(dir, "game"); var payload = Path.Combine(dir, "payload");
    Directory.CreateDirectory(Path.Combine(game, "How to Fish_Data/Managed"));
    File.WriteAllText(Path.Combine(game, "How to Fish.exe"), "game");
    File.WriteAllText(Path.Combine(game, "How to Fish_Data/Managed/Assembly-CSharp.dll"), "original");
    Directory.CreateDirectory(Path.Combine(payload, "BepInEx/plugins/Sightquill"));
    File.WriteAllText(Path.Combine(payload, CompanionInstallation.PluginPath), "plugin");
    File.WriteAllText(Path.Combine(payload, "winhttp.dll"), "loader");
    CompanionInstallation.Install(game, payload);
    Check(File.Exists(Path.Combine(game, CompanionInstallation.PluginPath)), "plugin missing");
    File.WriteAllText(Path.Combine(game, "BepInEx/plugins/another.dll"), "other mod");
    CompanionInstallation.Remove(game);
    Check(!File.Exists(Path.Combine(game, CompanionInstallation.PluginPath)) && File.Exists(Path.Combine(game, "winhttp.dll")), "shared loader or companion removal wrong");
    Check(File.ReadAllText(Path.Combine(game, "How to Fish_Data/Managed/Assembly-CSharp.dll")) == "original", "game assembly changed");
    File.Delete(Path.Combine(game, "BepInEx/plugins/another.dll"));
    CompanionInstallation.Remove(game);
    Check(!File.Exists(Path.Combine(game, "winhttp.dll")), "owned loader left behind without other mods");
    File.WriteAllText(Path.Combine(game, "winhttp.dll"), "existing loader");
    try { CompanionInstallation.Install(game, payload); throw new Exception("existing loader overwritten"); } catch (IOException) { }
    Check(File.ReadAllText(Path.Combine(game, "winhttp.dll")) == "existing loader" && !File.Exists(Path.Combine(game, CompanionInstallation.PluginPath)), "conflict must fail before copying");
    try { CompanionInstallation.SafePath(game, "../outside.dll"); throw new Exception("path escaped"); } catch (IOException) { }
});
try {
    Test("Preferences survive a save and a fresh store", () => {
        var path = Path.Combine(dir, "settings.json"); var store = new SettingsStore(path);
        var state = new AppSettings { Current = new ReticleSettings { Size = 73, Color = "#FFAA00" }, OffsetX = -50, MonitorId = "screen-2", HowToFishMode = true };
        state.Favorites.Add("pinpoint"); state.Profiles.Add(new SavedProfile { Name = "Mon profil", Reticle = new ReticleSettings { Size = 99 } });
        store.Save(state); var loaded = new SettingsStore(path).Load();
        Check(loaded.Current.Size == 73 && loaded.Current.Color == "#FFAA00" && loaded.OffsetX == -50 && loaded.MonitorId == "screen-2", "settings were lost");
        Check(loaded.Favorites.Contains("pinpoint") && loaded.Profiles.Single().Reticle.Size == 99, "profiles or favorites lost");
        Check(loaded.HowToFishMode, "game tracking preference lost");
    });
    Test("Corrupt preferences recover without destroying the original", () => {
        var path = Path.Combine(dir, "broken.json"); File.WriteAllText(path, "{broken");
        var store = new SettingsStore(path); var loaded = store.Load();
        Check(loaded.Current.Size > 0 && store.LastWarning != null, "recovery must report warning");
        Check(Directory.GetFiles(dir, "broken.json.corrupt-*").Length == 1, "original must be preserved");
    });
    Test("Null collections from imported settings are repaired", () => {
        var path = Path.Combine(dir, "null.json"); File.WriteAllText(path, "{\"Current\":null,\"Favorites\":null,\"Profiles\":[null]}");
        var loaded = new SettingsStore(path).Load();
        Check(loaded.Current != null && loaded.Favorites != null && loaded.Profiles.Count == 0, "null state escaped validation");
    });
    Test("Profile exchange round-trips and rejects foreign files", () => {
        var path = Path.Combine(dir, "profile.json");
        foreach (var shape in Enum.GetValues<ReticleShape>().Where(s => s != ReticleShape.Imported)) {
            var original = new ReticleSettings { Shape = shape, Size = 42, Thickness = 3, Gap = 9, Opacity = .65, Rotation = 37, Color = "#12ABEF", CenterDot = true, Outline = false, ScaleWithResolution = false };
            ProfileExchange.Write(path, new SavedProfile { Name = "Precision", Reticle = original });
            var loaded = ProfileExchange.Read(path);
            Check(loaded.Name == "Precision" && System.Text.Json.JsonSerializer.Serialize(loaded.Reticle) == System.Text.Json.JsonSerializer.Serialize(original), "profile lost appearance settings for " + shape);
        }
        File.WriteAllText(path, "{}");
        try { ProfileExchange.Read(path); throw new Exception("empty foreign file accepted"); } catch (InvalidDataException) { }
    });
} finally { Directory.Delete(dir, true); }
Console.WriteLine($"{failed} failed");
return failed == 0 ? 0 : 1;
