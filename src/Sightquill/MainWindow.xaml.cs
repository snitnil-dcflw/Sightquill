using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Sightquill.Core;
using Forms = System.Windows.Forms;

namespace Sightquill;
public partial class MainWindow : Window
{
    private readonly SettingsStore store;
    private readonly AppSettings state;
    private readonly OverlayController overlay;
    private readonly DispatcherTimer saveTimer;
    private readonly Forms.NotifyIcon tray;
    private bool ready;
    private bool syncing;
    private bool favoritesOnly;
    private bool closed;
    private string currentName = "";
    private DispatcherTimer? previewMotionTimer;
    private sealed record DisplayOption(string Id, string Label);

    public MainWindow(SettingsStore? settingsStore = null)
    {
        store = settingsStore ?? new SettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sightquill", "settings.json"));
        state = store.Load();
        InitializeComponent();
        Wheel.SelectedColorChanged += color => ColorBox.Text = color;
        StateChanged += (_, _) =>
        {
            var maximized = WindowState == WindowState.Maximized;
            MaximizeGlyph.Data = Geometry.Parse(maximized ? "M 4,1 L 14,1 14,11 M 1,4 L 11,4 11,14 1,14 Z" : "M 1,1 L 13,1 13,13 1,13 Z");
            MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
            AutomationProperties.SetName(MaximizeButton, maximized ? "Restore" : "Maximize");
        };
        // A Viewbox can resize its retained drawing without changing the child's
        // RenderSize. Redraw after layout so physical-pixel compensation stays current.
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(() => { if (!closed) Preview.InvalidateVisual(); }, DispatcherPriority.Loaded);
        MinWidth = Math.Min(MinWidth, SystemParameters.WorkArea.Width);
        MinHeight = Math.Min(MinHeight, SystemParameters.WorkArea.Height);
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        overlay = new OverlayController();
        overlay.Changed += (_, _) => RefreshActivation();
        overlay.DisplaysChanged += (_, _) => RefreshDisplays();
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); Persist(); };
        CategoryBox.ItemsSource = new[] { "All families" }.Concat(PresetCatalog.All.Select(x => x.Category).Distinct()).ToList();
        CategoryBox.SelectedIndex = 0;
        SearchBox.SetCurrentValue(TextBox.TextProperty, "");
        BuildSwatches(); RefreshDisplays(); RefreshProfiles();
        currentName = PresetCatalog.All.First(x => x.Id == state.SelectedPresetId).Name;
        tray = new Forms.NotifyIcon { Icon = CreateTrayIcon(), Text = "Sightquill — off", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Sightquill", null, (_, _) => Dispatcher.Invoke(RestoreWindow));
        menu.Items.Add("Toggle crosshair (Ctrl+Alt+X)", null, (_, _) => Dispatcher.Invoke(overlay.Toggle));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Close));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreWindow);
        ready = true;
        SyncControls(); RenderLibrary(); ApplyCurrent(); RefreshActivation();
        if (!overlay.HotkeyRegistered) HotkeyLabel.Text = "Ctrl+Alt+X is already in use by another application.";
        if (store.LastWarning != null) Status(store.LastWarning);
    }
    private static System.Drawing.Icon CreateTrayIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.FromArgb(27, 35, 48));
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(128, 216, 247), 2);
            g.DrawEllipse(pen, 8, 8, 16, 16); g.DrawLine(pen, 16, 3, 16, 12); g.DrawLine(pen, 16, 20, 16, 29); g.DrawLine(pen, 3, 16, 12, 16); g.DrawLine(pen, 20, 16, 29, 16);
        }
        var handle = bitmap.GetHicon();
        try { using var original = System.Drawing.Icon.FromHandle(handle); return (System.Drawing.Icon)original.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private void BuildSwatches()
    {
        foreach (var color in new[] { "#80D8F7", "#FFFFFF", "#A5EF80", "#FFE082", "#FF8C9D", "#C8AAFF", "#FFAB73" })
        {
            var button = new Button { Background = Brush(color), Height = 25, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(0), ToolTip = color };
            AutomationProperties.SetName(button, "Color " + color);
            button.Click += (_, _) => ColorBox.Text = color; SwatchGrid.Children.Add(button);
        }
    }
    private void RenderLibrary()
    {
        if (!ready) return;
        PresetGrid.Children.Clear();
        var category = CategoryBox.SelectedItem as string;
        var matches = PresetCatalog.All.Where(p =>
            (category == "All families" || p.Category == category) &&
            (!favoritesOnly || state.Favorites.Contains(p.Id)) &&
            (p.Name.Contains(SearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase) || p.Category.Contains(SearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var preset in matches)
        {
            var selected = preset.Id == state.SelectedPresetId;
            var layout = new Grid { Height = 93, Margin = new Thickness(0, 0, 7, 8) };
            var content = new StackPanel();
            content.Children.Add(new ReticleView { Settings = preset.Create(), FitThumbnail = true, Height = 50, Width = 65 });
            content.Children.Add(new TextBlock { Text = preset.Name, FontSize = 11, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) });
            var choose = new Button { Content = content, Padding = new Thickness(3, 8, 3, 5), Background = Brush(selected ? "#2F475D" : "#202936"), BorderBrush = Brush(selected ? "#80D8F7" : "#354252"), ToolTip = preset.Name + " · " + preset.Category };
            AutomationProperties.SetName(choose, "Choose " + preset.Name);
            choose.Click += (_, _) => SelectPreset(preset);
            layout.Children.Add(choose);
            var star = new Button { Content = state.Favorites.Contains(preset.Id) ? "★" : "☆", Style = (Style)FindResource("QuietButton"), FontSize = 13, Padding = new Thickness(3, 0, 3, 1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Foreground = Brush(state.Favorites.Contains(preset.Id) ? "#FFE082" : "#93A8BD"), ToolTip = "Add or remove favorite" };
            AutomationProperties.SetName(star, "Favorite " + preset.Name);
            star.Click += (_, _) => { if (!state.Favorites.Add(preset.Id)) state.Favorites.Remove(preset.Id); RenderLibrary(); QueueSave(); };
            layout.Children.Add(star); PresetGrid.Children.Add(layout);
        }
        CatalogCount.Text = matches.Count + (matches.Count == 1 ? " preset" : " presets");
        EmptyLibrary.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SelectPreset(ReticlePreset preset)
    {
        state.SelectedPresetId = preset.Id; state.Current = preset.Create(); currentName = preset.Name;
        syncing = true; ProfilesBox.SelectedIndex = -1; syncing = false;
        SyncControls(); RenderLibrary(); ApplyCurrent(); Status(preset.Name + " selected. Changes apply immediately.");
    }
    private void SyncControls()
    {
        syncing = true;
        var s = state.Current;
        SizeSlider.Value = s.Size; ThicknessSlider.Value = s.Thickness; GapSlider.Value = s.Gap;
        OpacitySlider.Value = s.Opacity * 100; RotationSlider.Value = s.Rotation;
        ColorBox.Text = s.Color; CenterDotBox.IsChecked = s.CenterDot; OutlineBox.IsChecked = s.Outline;
        Wheel.SetColor(s.Color);
        OffsetXBox.Text = state.OffsetX.ToString(CultureInfo.InvariantCulture); OffsetYBox.Text = state.OffsetY.ToString(CultureInfo.InvariantCulture);
        GameModeBox.SelectedIndex = state.HowToFishMode ? 1 : 0; MonitorBox.IsEnabled = !state.HowToFishMode;
        IntegrationButton.IsEnabled = state.HowToFishMode; ResolutionBox.IsChecked = s.ScaleWithResolution;
        DynamicBox.IsChecked = s.Dynamic; MovementKeysBox.SelectedIndex = s.AzertyMovement ? 0 : 1;
        MoveSpreadSlider.Value = s.MovementSpread; FireSpreadSlider.Value = s.FiringSpread;
        DynamicControls.Visibility = s.Dynamic ? Visibility.Visible : Visibility.Collapsed;
        syncing = false;
    }
    private void ApplyCurrent()
    {
        if (!ready) return;
        state.Current.Normalize(); var s = state.Current;
        var display = Forms.Screen.AllScreens.FirstOrDefault(x => x.DeviceName == state.MonitorId) ?? Forms.Screen.PrimaryScreen!;
        Preview.ResolutionScale = s.ResolutionScale(display.Bounds.Height);
        Preview.Settings = s.Clone(); overlay.Update(state);
        TrackingText.Text = state.HowToFishMode ? overlay.TrackingStatus : "Screen center";
        PresetTitle.Text = currentName;
        PreviewMetrics.Text = FormattableString.Invariant($"{s.Size * Preview.ResolutionScale:0} px  /  {s.Opacity * 100:0}% opacity");
        SizeValue.Text = $"{s.Size:0} px"; ThicknessValue.Text = $"{s.Thickness:0} px"; GapValue.Text = $"{s.Gap:0} px"; OpacityValue.Text = $"{s.Opacity * 100:0} %"; RotationValue.Text = $"{s.Rotation:0}°";
        GapSlider.IsEnabled = s.Shape is ReticleShape.Cross or ReticleShape.TShape or ReticleShape.Hybrid;
        ThicknessSlider.IsEnabled = s.Shape != ReticleShape.Dot; CenterDotBox.IsEnabled = s.Shape != ReticleShape.Dot;
        if (s.Shape == ReticleShape.Imported) { ThicknessSlider.IsEnabled = false; CenterDotBox.IsEnabled = false; }
        OutlineBox.IsEnabled = s.Shape != ReticleShape.Imported;
        var raster = s.Artwork?.PngBase64 != null;
        ColorBox.IsEnabled = !raster; SwatchGrid.IsEnabled = !raster; Wheel.IsEnabled = !raster;
        ColorWheelExpander.IsEnabled = !raster;
        QueueSave();
    }
    private void SettingsChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!ready || syncing) return;
        state.Current.Size = SizeSlider.Value; state.Current.Thickness = ThicknessSlider.Value; state.Current.Gap = GapSlider.Value;
        state.Current.Opacity = OpacitySlider.Value / 100; state.Current.Rotation = RotationSlider.Value;
        ApplyCurrent();
    }
    private void DynamicChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || syncing) return;
        state.Current.Dynamic = DynamicBox.IsChecked == true;
        state.Current.AzertyMovement = MovementKeysBox.SelectedIndex == 0;
        state.Current.MovementSpread = MoveSpreadSlider.Value; state.Current.FiringSpread = FireSpreadSlider.Value;
        DynamicControls.Visibility = state.Current.Dynamic ? Visibility.Visible : Visibility.Collapsed;
        if (!state.Current.Dynamic) { previewMotionTimer?.Stop(); Preview.SetMotion(0, 0); }
        ApplyCurrent();
    }
    private void PreviewAnimation(object sender, RoutedEventArgs e)
    {
        previewMotionTimer?.Stop(); var clock = System.Diagnostics.Stopwatch.StartNew(); var previous = 0d; var demo = new DynamicMotion();
        previewMotionTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        previewMotionTimer.Tick += (_, _) =>
        {
            var time = clock.Elapsed.TotalSeconds; demo.Step(time < .65, time >= .65 && time < 1.05, time - previous); previous = time;
            Preview.SetMotion(demo.Movement, demo.Firing);
            if (time > 1.8) { previewMotionTimer.Stop(); Preview.SetMotion(0, 0); }
        };
        previewMotionTimer.Start();
    }
    private void OptionChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || syncing) return;
        state.Current.CenterDot = CenterDotBox.IsChecked == true; state.Current.Outline = OutlineBox.IsChecked == true; state.Current.ScaleWithResolution = ResolutionBox.IsChecked == true; ApplyCurrent();
    }
    private void ColorChanged(object sender, TextChangedEventArgs e)
    {
        if (!ready || syncing) return;
        var valid = Regex.IsMatch(ColorBox.Text, "^#[0-9a-fA-F]{6}$");
        ColorBox.BorderBrush = Brush(valid ? "#354252" : "#F0AA7C");
        if (valid) { state.Current.Color = ColorBox.Text; Wheel.SetColor(ColorBox.Text); ApplyCurrent(); } else Status("Enter a color as #RRGGBB, for example #80D8F7.");
    }
    private void RefreshDisplays()
    {
        var previousSync = syncing; syncing = true;
        var screens = Forms.Screen.AllScreens.Select((x, i) => new DisplayOption(x.DeviceName, $"Display {i + 1} · {x.Bounds.Width} × {x.Bounds.Height}" + (x.Primary ? " · primary" : ""))).ToList();
        MonitorBox.ItemsSource = screens;
        MonitorBox.SelectedItem = screens.FirstOrDefault(x => x.Id == state.MonitorId) ?? screens.FirstOrDefault(x => x.Id == Forms.Screen.PrimaryScreen?.DeviceName) ?? screens[0];
        state.MonitorId = ((DisplayOption)MonitorBox.SelectedItem).Id;
        syncing = previousSync;
        if (ready) ApplyCurrent();
    }
    private void MonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || syncing || MonitorBox.SelectedItem is not DisplayOption display) return;
        state.MonitorId = display.Id; ApplyCurrent();
    }
    private void GameModeChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || syncing) return;
        state.HowToFishMode = GameModeBox.SelectedIndex == 1; MonitorBox.IsEnabled = !state.HowToFishMode; IntegrationButton.IsEnabled = state.HowToFishMode;
        ApplyCurrent();
        Status(state.HowToFishMode ? "How to Fish selected. Enable the crosshair and return to the game with its companion loaded." : "Universal mode selected.");
    }
    private void ManageIntegration(object sender, RoutedEventArgs e) => new GameIntegrationWindow { Owner = this }.ShowDialog();
    private void OffsetChanged(object sender, TextChangedEventArgs e)
    {
        if (!ready || syncing) return;
        var validX = int.TryParse(OffsetXBox.Text, out var x) && x >= -2000 && x <= 2000;
        var validY = int.TryParse(OffsetYBox.Text, out var y) && y >= -2000 && y <= 2000;
        OffsetXBox.BorderBrush = Brush(validX ? "#354252" : "#F0AA7C"); OffsetYBox.BorderBrush = Brush(validY ? "#354252" : "#F0AA7C");
        if (validX && validY) { state.OffsetX = x; state.OffsetY = y; ApplyCurrent(); } else Status("Offsets must be whole numbers between −2000 and 2000.");
    }
    private void Recenter(object sender, RoutedEventArgs e) { state.OffsetX = state.OffsetY = 0; SyncControls(); ApplyCurrent(); Status("Crosshair offsets reset."); }
    private void RefreshProfiles()
    {
        var wasSyncing = syncing; syncing = true;
        ProfilesBox.ItemsSource = state.Profiles.ToList(); DeleteProfileButton.IsEnabled = state.Profiles.Count > 0;
        syncing = wasSyncing;
    }
    private void SaveProfile(object sender, RoutedEventArgs e)
    {
        var name = AppSettings.CleanName(ProfileNameBox.Text);
        var existing = state.Profiles.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null) existing.Reticle = state.Current.Clone();
        else { if (state.Profiles.Count >= 100) { Status("You have 100 profiles. Delete one before adding another."); return; } state.Profiles.Add(new SavedProfile { Name = name, Reticle = state.Current.Clone() }); }
        RefreshProfiles(); if (Persist()) Status("Profile: " + name + " saved.");
    }
    private void ProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || syncing || ProfilesBox.SelectedItem is not SavedProfile profile) return;
        state.Current = profile.Reticle.Clone(); currentName = profile.Name; ProfileNameBox.Text = profile.Name;
        SyncControls(); ApplyCurrent(); Status("Profile: " + profile.Name + " loaded.");
    }
    private void DeleteProfile(object sender, RoutedEventArgs e)
    {
        if (ProfilesBox.SelectedItem is not SavedProfile profile) { Status("Select a profile to delete first."); return; }
        state.Profiles.Remove(profile); RefreshProfiles(); if (Persist()) Status("Profile deleted. You can still save the current crosshair again.");
    }
    private void ImportProfile(object sender, RoutedEventArgs e)
    {
        var dialog = new CrosshairImportWindow { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedProfile is SavedProfile profile) ApplyImportedProfile(profile);
    }
    private void ApplyImportedProfile(SavedProfile profile)
    {
        state.Current = profile.Reticle; currentName = profile.Name; ProfileNameBox.Text = profile.Name;
        SyncControls(); ApplyCurrent(); Status("Crosshair imported. Click Save to keep it in My profiles. Export JSON to preserve all settings.");
    }
    public bool ImportFromFile(string path)
    {
        try { ApplyImportedProfile(CrosshairImportWindow.ReadFile(path)); return true; }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { Status(ex.Message); return false; }
    }
    public bool ImportFromCode(string code)
    {
        try { ApplyImportedProfile(GameCrosshairImport.Read(code)); return true; }
        catch (InvalidDataException ex) { Status(ex.Message); return false; }
    }
    private void ExportProfile(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Sightquill profile (*.json)|*.json|Transparent PNG (*.png)|*.png", FileName = "sightquill-crosshair", DefaultExt = ".json", Title = "Export current crosshair" };
        if (dialog.ShowDialog(this) != true) return;
        ExportToFile(dialog.FileName);
    }
    public bool ExportToFile(string path)
    {
        try { if (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)) PngCrosshair.Export(path, state.Current); else ProfileExchange.Write(path, new SavedProfile { Name = ProfileNameBox.Text, Reticle = state.Current }); Status("Crosshair exported."); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status("Could not export: " + ex.Message); return false; }
    }
    private void QueueSave() { if (!ready || closed) return; saveTimer.Stop(); saveTimer.Start(); }
    private bool Persist()
    {
        try { store.Save(state); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status("Could not save: " + ex.Message); return false; }
    }
    private void Status(string text) { StatusText.Text = text; StatusText.ToolTip = text; }
    private void RefreshActivation()
    {
        var active = overlay.IsEnabled;
        TrackingText.Text = state.HowToFishMode ? overlay.TrackingStatus : "Screen center";
        HeaderStatus.Text = active ? "Crosshair on" : "Crosshair off"; StatusDot.Fill = Brush(active ? "#A5EF80" : "#93A4B6");
        ToggleButton.Content = active ? "Disable crosshair" : "Enable crosshair";
        ToggleButton.Background = Brush(active ? "#B3E9C2" : "#80D8F7");
        tray.Text = active ? "Sightquill — on" : "Sightquill — off";
    }
    private void ToggleOverlay(object sender, RoutedEventArgs e) => overlay.Toggle();
    private void FilterChanged(object sender, TextChangedEventArgs e) => RenderLibrary();
    private void CategoryChanged(object sender, SelectionChangedEventArgs e) => RenderLibrary();
    private void ToggleFavorites(object sender, RoutedEventArgs e) { favoritesOnly = !favoritesOnly; FavoritesButton.Content = favoritesOnly ? "★" : "☆"; FavoritesButton.Foreground = Brush(favoritesOnly ? "#FFE082" : "#F0F5FA"); RenderLibrary(); }
    private void ResetPreset(object sender, RoutedEventArgs e) => SelectPreset(PresetCatalog.All.First(x => x.Id == state.SelectedPresetId));
    private void ChangeBackdrop(object sender, RoutedEventArgs e)
    {
        Backdrop.Mode = int.Parse((string)((Button)sender).Tag); Backdrop.InvalidateVisual();
        var light = Backdrop.Mode == 2; PresetTitle.Foreground = Brush(light ? "#1A3444" : "#F0F5FA"); PreviewMetrics.Foreground = Brush(light ? "#3F5866" : "#C3D4E5");
    }
    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
            for (var parent = source; parent != null; parent = VisualTreeHelper.GetParent(parent)) if (parent is Button) return;
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    internal void RestoreWindow() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
    private void HideToTray(object sender, RoutedEventArgs e) { Hide(); tray.ShowBalloonTip(2500, "Sightquill is still running", "Double-click the tray icon to reopen settings. Ctrl+Alt+X toggles the crosshair.", Forms.ToolTipIcon.Info); }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void ShowHelp(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "1. Choose a crosshair and customize its size and color.\n2. Choose Universal for a screen-centered overlay, or How to Fish for weapon tracking.\n3. For How to Fish, open Set up to install its optional companion with the game closed. Then launch the game.\n4. Enable your crosshair or press Ctrl+Alt+X.\n\nUse windowed or borderless fullscreen. Exclusive fullscreen is not guaranteed.\n\nHow to Fish tracking only appears in the foreground game. It hides in menus or when the signal expires. It follows the nominal barrel direction, without predicting random spread or bullet drop.\n\nScale with resolution keeps the same proportions at 1080p and 4K. Disable it for a fixed pixel size.\n\nSettings save automatically. The crosshair starts disabled. Closing Sightquill also closes the overlay; Minimize to tray keeps it running.\n\nSettings: %LOCALAPPDATA%\\Sightquill\\settings.json",
        "Sightquill quick start", MessageBoxButton.OK, MessageBoxImage.Information);
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e); if (e.Cancel || closed) return; closed = true;
        saveTimer.Stop(); Persist(); overlay.Dispose();
        previewMotionTimer?.Stop();
        tray.Visible = false; var icon = tray.Icon; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); icon?.Dispose();
    }
    protected override void OnClosed(EventArgs e) { base.OnClosed(e); if (Application.Current is App) Application.Current.Shutdown(); }
}
