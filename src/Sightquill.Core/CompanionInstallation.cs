using System.Security.Cryptography;
using System.Text.Json;

namespace Sightquill.Core;

public static class CompanionInstallation
{
    public const string PluginPath = "BepInEx/plugins/Sightquill/Sightquill.HowToFish.dll";
    private const string JournalPath = "BepInEx/config/Sightquill.installation.json";
    public static string ValidateGame(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new IOException("Choose the How to Fish game folder first.");
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        if (!File.Exists(Path.Combine(root, "How to Fish.exe")) || !File.Exists(Path.Combine(root, "How to Fish_Data/Managed/Assembly-CSharp.dll")))
            throw new IOException("Select the folder containing How to Fish.exe (Unity Mono version).");
        return root;
    }
    public static string SafePath(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("An installation path is outside the game folder.");
        for (var part = full; part != null; part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked installation folders are not supported. Choose the original folder.");
        return full;
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static Dictionary<string, string> ReadJournal(string root)
    {
        var path = SafePath(root, JournalPath);
        if (!File.Exists(path)) return new(StringComparer.OrdinalIgnoreCase);
        try { return new(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [], StringComparer.OrdinalIgnoreCase); }
        catch (JsonException) { throw new IOException("The companion installation record is unreadable. Restore it before updating."); }
    }
    public static void Install(string directory, string payload)
    {
        var root = ValidateGame(directory);
        var journal = ReadJournal(root);
        var files = Directory.GetFiles(payload, "*", SearchOption.AllDirectories).Select(source => (Source: source, Relative: Path.GetRelativePath(payload, source))).ToList();
        if (!files.Any(f => f.Relative.Replace('\\', '/') == PluginPath)) throw new IOException("The companion package is incomplete.");
        foreach (var file in files)
        {
            var target = SafePath(root, file.Relative);
            if (!File.Exists(target)) continue;
            if (journal.TryGetValue(file.Relative, out var expected) && Hash(target) == expected) continue;
            // Adopt our older companion only; never overwrite another loader or mod.
            if (file.Relative.Replace('\\', '/') == PluginPath && System.Reflection.AssemblyName.GetAssemblyName(target).Name == "Sightquill.HowToFish") continue;
            throw new IOException("Existing file preserved: " + file.Relative);
        }
        var journalTarget = SafePath(root, JournalPath);
        foreach (var file in files)
        {
            var target = SafePath(root, file.Relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                var backup = SafePath(root, "BepInEx/config/Sightquill-backups/" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "/" + file.Relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Copy(target, backup);
            }
            var staged = target + ".sightquill-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(file.Source, staged);
                journal[file.Relative] = Hash(staged);
                SettingsStore.AtomicWrite(journalTarget, JsonSerializer.Serialize(journal));
                // Same-directory rename leaves either the old complete file or the new one.
                File.Move(staged, target, true);
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }
    }
    public static string Remove(string directory)
    {
        var root = ValidateGame(directory); var journal = ReadJournal(root);
        var plugin = SafePath(root, PluginPath);
        if (journal.Count == 0 && File.Exists(plugin))
        {
            if (System.Reflection.AssemblyName.GetAssemblyName(plugin).Name != "Sightquill.HowToFish") throw new IOException("Unrecognized companion file preserved.");
            File.Delete(plugin);
            return "Companion removed. The existing mod loader was preserved.";
        }
        var plugins = SafePath(root, "BepInEx/plugins");
        var patchers = SafePath(root, "BepInEx/patchers");
        var shared = (Directory.Exists(plugins) && Directory.GetFiles(plugins, "*.dll", SearchOption.AllDirectories).Any(p => !p.Equals(plugin, StringComparison.OrdinalIgnoreCase)))
            || (Directory.Exists(patchers) && Directory.GetFiles(patchers, "*.dll", SearchOption.AllDirectories).Length > 0);
        // Validate every path before deleting any file.
        foreach (var entry in journal) _ = SafePath(root, entry.Key);
        foreach (var entry in journal.ToArray())
        {
            if (shared && entry.Key.Replace('\\', '/') != PluginPath) continue;
            var target = SafePath(root, entry.Key);
            if (!File.Exists(target)) { journal.Remove(entry.Key); continue; }
            if (Hash(target) != entry.Value) continue;
            File.Delete(target); journal.Remove(entry.Key);
        }
        SettingsStore.AtomicWrite(SafePath(root, JournalPath), JsonSerializer.Serialize(journal));
        return journal.Count > 0 ? "Companion cleanup finished. Shared or modified files were preserved." : "Companion removed. Original game files were preserved.";
    }
}
