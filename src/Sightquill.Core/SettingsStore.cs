using System.Text.Json;

namespace Sightquill.Core;
public sealed class SettingsStore(string path)
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string? LastWarning { get; private set; }
    public AppSettings Load()
    {
        LastWarning = null;
        if (!File.Exists(path)) return new();
        try
        {
            if (new FileInfo(path).Length > 50_000_000) throw new JsonException("File is too large.");
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? throw new JsonException("Empty file.");
            settings.Normalize(); return settings;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            LastWarning = "Settings could not be read. Defaults restored.";
            try { File.Move(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastWarning += " The original file could not be moved."; }
            return new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastWarning = "Could not read settings: " + ex.Message; return new();
        }
    }
    public void Save(AppSettings settings)
    {
        settings.Normalize();
        AtomicWrite(path, JsonSerializer.Serialize(settings, JsonOptions));
    }
    internal static void AtomicWrite(string path, string content)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, content); File.Move(temporary, full, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
public static class ProfileExchange
{
    private sealed class Envelope
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        public SavedProfile? Profile { get; set; }
    }
    public static SavedProfile Read(string path)
    {
        if (new FileInfo(path).Length > 500_000) throw new InvalidDataException("This profile exceeds the 500 KB limit.");
        try
        {
            var data = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path));
            if (data?.Format != "sightquill-profile" || data.Version is not (1 or 2 or 3) || data.Profile?.Reticle == null)
                throw new InvalidDataException("This is not a compatible Sightquill profile.");
            data.Profile.Reticle.Normalize(); data.Profile.Name = AppSettings.CleanName(data.Profile.Name);
            return data.Profile;
        }
        catch (JsonException ex) { throw new InvalidDataException("The JSON file could not be read.", ex); }
    }
    public static void Write(string path, SavedProfile profile)
    {
        var copy = new SavedProfile { Name = AppSettings.CleanName(profile.Name), Reticle = profile.Reticle.Clone() };
        copy.Reticle.Normalize();
        SettingsStore.AtomicWrite(path, JsonSerializer.Serialize(new Envelope { Format = "sightquill-profile", Version = copy.Reticle.Dynamic ? 3 : copy.Reticle.Artwork == null ? 1 : 2, Profile = copy }, SettingsStore.JsonOptions));
    }
}
