using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Sightquill.Core;

namespace Sightquill;

public sealed class GameIntegrationWindow : Window
{
    private readonly TextBox folder = new() { Margin = new Thickness(0, 8, 0, 12), IsReadOnly = true };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) };
    private readonly Button install = new() { Content = "Install / update companion", Margin = new Thickness(0, 16, 8, 0) };
    private readonly Button remove = new() { Content = "Remove companion", Margin = new Thickness(0, 16, 0, 0) };
    private bool busy;
    public GameIntegrationWindow()
    {
        Title = "How to Fish integration"; Width = 570; Height = 460; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = (System.Windows.Media.Brush)Application.Current.FindResource("BaseBrush");
        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextBrush");
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(new TextBlock { Text = "How to Fish", FontSize = 25, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Optional weapon tracking", FontSize = 14, Margin = new Thickness(0, 4, 0, 16) });
        panel.Children.Add(new TextBlock { Text = "Install the local companion in this game only. Close the game before installing or removing it. If needed, setup downloads the official BepInEx 5 loader (internet required).", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(folder);
        var browse = new Button { Content = "Choose game folder…", HorizontalAlignment = HorizontalAlignment.Left };
        browse.Click += (_, _) => { if (busy) return; var dialog = new OpenFolderDialog { Title = "Select the folder containing How to Fish.exe" }; if (dialog.ShowDialog(this) == true) { folder.Text = dialog.FolderName; RefreshStatus(); } };
        panel.Children.Add(browse);
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; actions.Children.Add(install); actions.Children.Add(remove); panel.Children.Add(actions); panel.Children.Add(status);
        install.Click += async (_, _) => await Run(true); remove.Click += async (_, _) => await Run(false);
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        Content = panel; folder.Text = DetectGame() ?? ""; RefreshStatus();
    }
    private void RefreshStatus()
    {
        if (string.IsNullOrEmpty(folder.Text)) { status.Text = "Game not found automatically. Choose its installation folder."; return; }
        try { var root = CompanionInstallation.ValidateGame(folder.Text); status.Text = File.Exists(Path.Combine(root, CompanionInstallation.PluginPath)) ? "Companion installed. Select How to Fish in Sightquill to use tracking." : "Ready to install. No companion detected."; }
        catch (IOException ex) { status.Text = ex.Message; }
    }
    private async Task Run(bool installing)
    {
        if (busy) return;
        busy = true; install.IsEnabled = remove.IsEnabled = false;
        try
        {
            var root = CompanionInstallation.ValidateGame(folder.Text);
            var processes = Process.GetProcessesByName("How to Fish");
            var running = processes.Length > 0; foreach (var process in processes) process.Dispose();
            if (running) throw new IOException("Close How to Fish after saving, then try again.");
            status.Text = installing ? "Preparing the companion…" : "Removing the companion…";
            if (installing) { await Install(root); status.Text = "Installed. Start How to Fish, select its mode and enable your crosshair."; }
            else status.Text = await Task.Run(() => CompanionInstallation.Remove(root));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or BadImageFormatException)
        { status.Text = ex is UnauthorizedAccessException ? "Windows denied access to this game folder. Reopen Sightquill as administrator to install the companion." : ex.Message; }
        finally { busy = false; install.IsEnabled = remove.IsEnabled = true; }
    }
    private async Task Install(string root)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Integrations/HowToFish/Sightquill.HowToFish.dll");
        if (!File.Exists(source)) throw new IOException("Companion package missing. Extract the complete Sightquill ZIP, including its Integrations folder.");
        var temporary = Path.Combine(Path.GetTempPath(), "Sightquill-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var loader = Path.Combine(root, "BepInEx/core/BepInEx.dll");
            if (File.Exists(loader))
            {
                if (AssemblyName.GetAssemblyName(loader).Version?.Major != 5) throw new IOException("A different mod loader is already installed. Its files were preserved.");
            }
            else
            {
                status.Text = "Downloading the official BepInEx loader…";
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
                var bytes = await client.GetByteArrayAsync("https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip");
                if (Convert.ToHexString(SHA256.HashData(bytes)) != "82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4") throw new IOException("Loader verification failed. No game files were changed.");
                using var archive = new ZipArchive(new MemoryStream(bytes));
                archive.ExtractToDirectory(temporary);
            }
            var target = CompanionInstallation.SafePath(temporary, CompanionInstallation.PluginPath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target);
            status.Text = "Installing companion…";
            await Task.Run(() => CompanionInstallation.Install(root, temporary));
        }
        finally { Directory.Delete(temporary, true); }
    }
    public static string? DetectGame()
    {
        try
        {
            var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (steam == null) return null;
            var libraries = new System.Collections.Generic.List<string> { steam };
            var vdf = Path.Combine(steam, "steamapps/libraryfolders.vdf");
            if (File.Exists(vdf)) foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"")) libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
            foreach (var library in libraries.Distinct())
            {
                var manifest = Path.Combine(library, "steamapps/appmanifest_4001890.acf");
                if (!File.Exists(manifest)) continue;
                var match = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                if (!match.Success) continue;
                var common = Path.Combine(library, "steamapps/common", match.Groups[1].Value);
                foreach (var candidate in new[] { common, Path.Combine(common, "How to Fish") }) if (File.Exists(Path.Combine(candidate, "How to Fish.exe"))) return candidate;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return null;
    }
}
