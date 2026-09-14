using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Sightquill.Core;

namespace Sightquill;

public sealed class CrosshairImportWindow : Window
{
    public SavedProfile? SelectedProfile { get; private set; }
    private readonly ReticleView preview = new() { Height = 110, PhysicalPixels = true };
    private readonly TextBlock status = new() { Text = "Choose a file or preview a game code.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) };
    private readonly Button accept = new() { Content = "Import crosshair", IsEnabled = false, Padding = new Thickness(18, 10, 18, 10), HorizontalAlignment = HorizontalAlignment.Right };
    public CrosshairImportWindow()
    {
        Title = "Import crosshair"; Width = 540; Height = 610; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("BaseBrush"); Foreground = (Brush)Application.Current.FindResource("TextBrush");
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Bring your own crosshair", FontSize = 22, FontWeight = FontWeights.SemiBold });
        var fileButton = new Button { Content = "Choose PNG or Sightquill JSON…", Margin = new Thickness(0, 16, 0, 8) };
        fileButton.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "Crosshair files (*.png;*.json)|*.png;*.json|Transparent PNG (*.png)|*.png|Sightquill profile (*.json)|*.json", Title = "Import crosshair" };
            if (dialog.ShowDialog(this) == true) TryPreview(() => ReadFile(dialog.FileName));
        };
        panel.Children.Add(fileButton);
        panel.Children.Add(new TextBlock { Text = "PNG: transparent background, up to 2 MB / 2048 × 2048. Images are stored at up to 256 × 256 inside the profile.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(new TextBlock { Text = "Or paste a Valorant / CS:GO / CS2 code", FontWeight = FontWeights.SemiBold });
        var code = new TextBox { Height = 72, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 4096, Margin = new Thickness(0, 8, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(code, "Game crosshair code");
        code.TextChanged += (_, _) => { accept.IsEnabled = false; SelectedProfile = null; preview.Settings = null; status.Text = "Preview the code before importing."; };
        panel.Children.Add(code);
        var decode = new Button { Content = "Preview code", HorizontalAlignment = HorizontalAlignment.Left };
        decode.Click += (_, _) => TryPreview(() => GameCrosshairImport.Read(code.Text)); panel.Children.Add(decode);
        panel.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(38, 51, 67)), CornerRadius = new CornerRadius(8), Child = preview, Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(status); panel.Children.Add(accept);
        accept.Click += (_, _) => { DialogResult = true; };
        Content = panel;
    }
    public static SavedProfile ReadFile(string path)
    {
        var profile = Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ? PngCrosshair.Read(path) : ProfileExchange.Read(path);
        PngCrosshair.Validate(profile.Reticle); return profile;
    }
    private void TryPreview(Func<SavedProfile> read)
    {
        SelectedProfile = null; accept.IsEnabled = false; preview.Settings = null;
        try
        {
            var profile = read(); SelectedProfile = profile; preview.Settings = profile.Reticle.Clone(); accept.IsEnabled = true;
            status.Text = profile.Reticle.Artwork?.Source == "Valorant"
                ? "Primary crosshair imported with per-layer movement, firing multipliers and firing fade. Dynamic simulates keyboard/mouse input; weapon accuracy, ADS and recoil are not tracked. Use Preview animation in the editor."
                : profile.Reticle.Artwork?.Source == "CS:GO / CS2"
                ? "Static approximation only. Valorant imports Primary aim; ADS, sniper, recoil and movement animations are not reproduced. Size can be adjusted after import."
                : "Ready. The image and its transparency will stay embedded when you save or share this profile.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { status.Text = ex.Message; }
    }
}
