using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Sightquill;
using Sightquill.Core;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO.Pipes;
using System.Threading;
using Sightquill.Bridge;

internal static class Program
{
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [STAThread] static int Main()
    {
        Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "artifacts"));
        var app = new App(); app.InitializeComponent();
        var failures = 0;
        void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; }
        var pipeName = "Sightquill.Tests." + Guid.NewGuid().ToString("N");
        using (var receiver = new AimReceiver(pipeName))
        {
            var notifications = 0;
            receiver.Updated += () => Interlocked.Increment(ref notifications);
            using (var sender = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
            {
                sender.Connect(3000);
                var packet = AimProtocol.Encode(new AimFrame(Environment.ProcessId, 1, DateTime.UtcNow.Ticks, .3f, .7f, AimKind.Barrel));
                sender.Write(packet, 0, 9); sender.Write(packet, 9, packet.Length - 9);
                Check(SpinWait.SpinUntil(() => receiver.Latest != null, 2000) && receiver.Latest?.X == .3f && receiver.Latest.Y == .7f, "Named pipe reassembles frames and authenticates the Windows sender PID");
                Check(SpinWait.SpinUntil(() => Volatile.Read(ref notifications) > 0, 2000), "A new aim sample wakes the overlay without timer polling");
            }
            Check(SpinWait.SpinUntil(() => receiver.Latest == null, 2000), "Disconnect clears the previous aim sample");
            using var forged = new NamedPipeClientStream(".", pipeName, PipeDirection.Out); forged.Connect(3000);
            var badPacket = AimProtocol.Encode(new AimFrame(Environment.ProcessId + 1, 2, DateTime.UtcNow.Ticks, .5f, .5f, AimKind.Barrel)); forged.Write(badPacket);
            Thread.Sleep(80);
            Check(receiver.Latest == null, "Pipe rejects a forged process identity");
        }
        var transportName = "Sightquill.Tests." + Guid.NewGuid().ToString("N");
        using (var receiver = new AimReceiver(transportName))
        using (var publisher = new Sightquill.HowToFish.AimPublisher(transportName))
        {
            publisher.Publish(new AimFrame(Environment.ProcessId, 1, DateTime.UtcNow.Ticks, .2f, .8f, AimKind.Barrel));
            Check(SpinWait.SpinUntil(() => receiver.Latest?.Sequence == 1, 3000), "Companion publishes its first sample through the real pipe");
            Thread.Sleep(30);
            for (var sequence = 2; sequence <= 1000; sequence++)
                publisher.Publish(new AimFrame(Environment.ProcessId, sequence, DateTime.UtcNow.Ticks, .4f, .6f, AimKind.Barrel));
            Check(SpinWait.SpinUntil(() => receiver.Latest?.Sequence == 1000, 3000), "Idle companion wakes and a burst converges to the newest aim sample");
        }
        using (var overlay = new OverlayController())
        {
            Check(!overlay.IsEnabled && !IsWindowVisible(overlay.WindowHandle), "Startup never enables the overlay");
            overlay.Update(new AppSettings());
            var foreground = GetForegroundWindow();
            overlay.Toggle(); app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(overlay.IsEnabled && IsWindowVisible(overlay.WindowHandle), "Activation shows a real native window");
            Check(GetForegroundWindow() != overlay.WindowHandle, "Activation does not steal keyboard focus");
            var style = GetWindowLong(overlay.WindowHandle, -20);
            Check((style & 0x08000020) == 0x08000020, "Overlay passes clicks and cannot activate");
            Check((style & 0x80) != 0, "Overlay is absent from Alt+Tab");
            Check(overlay.HotkeyRegistered, "Global shortcut registers");
            SendMessage(overlay.HotkeyHandle, 0x0312, new IntPtr(0x5347), IntPtr.Zero);
            Check(!overlay.IsEnabled && !IsWindowVisible(overlay.WindowHandle), "Global shortcut hides the overlay");
            overlay.Toggle(); overlay.Toggle();
            Check(!overlay.IsEnabled && !IsWindowVisible(overlay.WindowHandle), "Repeated toggles stay synchronized");
            overlay.Update(new AppSettings { HowToFishMode = true }); overlay.Toggle();
            Check(overlay.IsEnabled && !IsWindowVisible(overlay.WindowHandle), "Game mode waits for fresh telemetry instead of showing the screen center");
            overlay.Toggle(); overlay.Update(new AppSettings());
            var handle = overlay.WindowHandle;
            overlay.Dispose();
            Check(!IsWindow(handle), "Disposal destroys the native overlay");
        }
        var folder = Path.Combine(Path.GetTempPath(), "sightquill-ui-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var store = new SettingsStore(Path.Combine(folder, "settings.json"));
            var window = new MainWindow(store); window.Show();
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            T Find<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
            Check(Find<ComboBox>("GameModeBox").SelectedIndex == 0 && !Find<Button>("IntegrationButton").IsEnabled, "New installations default to Universal without enabling a game integration");
            Find<ComboBox>("GameModeBox").SelectedIndex = 1;
            Check(Find<Button>("IntegrationButton").IsEnabled && !Find<ComboBox>("MonitorBox").IsEnabled, "How to Fish explicitly enables companion setup and game positioning");
            Find<ComboBox>("GameModeBox").SelectedIndex = 0;
            Check(Find<ComboBox>("MonitorBox").IsEnabled, "Universal mode restores display selection");
            Check(Descendants(Find<ComboBox>("MonitorBox")).OfType<TextBlock>().Any(t => t.Text.StartsWith("Display 1")), "Selected monitor shows a readable label, not an object dump");
            var cards = Find<UniformGrid>("PresetGrid");
            Check(cards.Children.Count == 24, "Library displays all 24 usable presets");
            Find<TextBox>("SearchBox").Text = "Falcon";
            Check(cards.Children.Count == 1, "Search filters real preset cards");
            var card = (Grid)cards.Children[0];
            ((Button)card.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var preview = Find<ReticleView>("Preview");
            Check(preview.Settings?.Shape == ReticleShape.Chevron && preview.Settings.Size == 28, "Choosing a card updates preview geometry");
            Find<Slider>("SizeSlider").Value = 72;
            Check(preview.Settings?.Size == 72, "Size slider changes the real reticle");
            var exportedPath = Path.Combine(folder, "ui-export.json");
            Check(window.ExportToFile(exportedPath), "Editor exports a real JSON file");
            Find<Slider>("SizeSlider").Value = 19;
            Check(window.ImportFromFile(exportedPath) && preview.Settings?.Size == 72, "Import restores the exported crosshair in the live editor");
            Check(!window.ExportToFile(folder), "Export failure is reported instead of claiming success");
            var pngPath = Path.Combine(folder, "crosshair.png");
            Check(window.ExportToFile(pngPath), "Editor exports a transparent PNG");
            Check(window.ImportFromFile(pngPath) && preview.Settings?.Artwork?.PngBase64 != null, "Transparent PNG imports as embedded artwork");
            var embedded = preview.Settings!.Artwork!.PngBase64;
            var imageProfile = Path.Combine(folder, "image-profile.json");
            Check(window.ExportToFile(imageProfile), "Embedded PNG can be shared as a JSON profile");
            File.Delete(pngPath);
            Check(window.ImportFromFile(imageProfile) && preview.Settings?.Artwork?.PngBase64 == embedded, "Image profile survives removal of its source PNG");
            Check(!Find<TextBox>("ColorBox").IsEnabled && !Find<Slider>("ThicknessSlider").IsEnabled, "Image-only controls cannot misleadingly change PNG colors or geometry");
            var restoredImage = PngCrosshair.Decode(Convert.FromBase64String(embedded!));
            var imagePixels = new byte[restoredImage.PixelWidth * restoredImage.PixelHeight * 4]; restoredImage.CopyPixels(imagePixels, restoredImage.PixelWidth * 4, 0);
            Check(imagePixels.Where((_, i) => i % 4 == 3).Any(a => a == 0) && imagePixels.Where((_, i) => i % 4 == 3).Any(a => a > 0), "PNG transparency survives embedding and reload");
            Check(window.ImportFromCode("0;P;h;0;0t;1;0l;2;0o;1;0a;1;0f;0;1b;0") && preview.Settings?.Artwork?.Marks.Count == 4, "Valorant code reaches the live editor");
            Check(window.ImportFromCode("CSGO-WsnnD-eHaMw-QNDf9-oxuDh-ydOUD") && preview.Settings?.Artwork?.Source == "CS:GO / CS2", "CS2 share code reaches the live editor");
            Find<CheckBox>("DynamicBox").IsChecked = true;
            Check(preview.Settings!.Dynamic && Find<StackPanel>("DynamicControls").Visibility == Visibility.Visible, "Dynamic controls enable the active imported crosshair");
            var dynamicView = new ReticleView { Settings = preview.Settings.Clone(), Width = 160, Height = 160 };
            dynamicView.Measure(new Size(160, 160)); dynamicView.Arrange(new Rect(0, 0, 160, 160));
            int Extent()
            {
                dynamicView.UpdateLayout();
                var bitmap = new RenderTargetBitmap(160, 160, 96, 96, PixelFormats.Pbgra32); bitmap.Render(dynamicView);
                var pixels = new byte[160 * 160 * 4]; bitmap.CopyPixels(pixels, 640, 0);
                var columns = Enumerable.Range(0, 160).Where(x => Enumerable.Range(0, 160).Any(y => pixels[(y * 160 + x) * 4 + 3] > 0)).ToArray();
                return columns[^1] - columns[0] + 1;
            }
            var restExtent = Extent(); dynamicView.SetMotion(1, 0); var movingExtent = Extent();
            Check(movingExtent == restExtent + 16, $"Imported branches actually expand by the movement spread ({restExtent} -> {movingExtent})");
            dynamicView.SetMotion(0, 0); Check(Extent() == restExtent, "Dynamic artwork returns exactly to its original shape");
            var snowflake = GameCrosshairImport.Read("0;P;h;0;0t;1;0l;4;0o;0;0a;1;1t;3;1l;1;1o;2;1a;1").Reticle;
            snowflake.Size = snowflake.Artwork!.ReferenceSize; snowflake.Dynamic = true;
            var aligned = new ReticleView { Settings = snowflake, Width = 160, Height = 160 };
            aligned.Measure(new Size(160, 160)); aligned.Arrange(new Rect(0, 0, 160, 160));
            foreach (var movement in new[] { 0d, .37, 1d })
            {
                aligned.SetMotion(movement, 0); aligned.UpdateLayout();
                var bitmap = new RenderTargetBitmap(160, 160, 96, 96, PixelFormats.Pbgra32); bitmap.Render(aligned);
                var pixels = new byte[160 * 160 * 4]; bitmap.CopyPixels(pixels, 640, 0);
                var symmetric = true;
                for (var y = 0; y < 160; y++) for (var x = 0; x < 160; x++)
                    symmetric &= pixels[(y * 160 + x) * 4 + 3] == pixels[(y * 160 + 159 - x) * 4 + 3] && pixels[(y * 160 + x) * 4 + 3] == pixels[((159 - y) * 160 + x) * 4 + 3];
                Check(symmetric, $"Odd-width Snowflake stays at the preview center at motion {movement}");
            }
            foreach (var thickness in new[] { 1, 2, 3, 4 })
            foreach (var sizeScale in new[] { 1d, 2d })
            {
                var mixed = GameCrosshairImport.Read($"0;P;h;0;d;1;z;1;f;0;0t;{thickness};0l;5;0o;4;0a;1;0f;0;0m;0;1t;2;1l;3;1o;16;1a;1;1f;0;1m;0").Reticle;
                mixed.Size = mixed.Artwork!.ReferenceSize * sizeScale; mixed.Dynamic = false;
                var view = new ReticleView { Settings = mixed, Width = 160, Height = 160 };
                view.Measure(new Size(160, 160)); view.Arrange(new Rect(0, 0, 160, 160));
                var bitmap = new RenderTargetBitmap(160, 160, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
                var frame = new byte[160 * 160 * 4]; bitmap.CopyPixels(frame, 640, 0);
                var centered = true;
                for (var y = 0; y < 160; y++) for (var x = 0; x < 160; x++)
                    centered &= frame[(y * 160 + x) * 4 + 3] == frame[(y * 160 + 159 - x) * 4 + 3]
                        && frame[(y * 160 + x) * 4 + 3] == frame[((159 - y) * 160 + x) * 4 + 3];
                Check(centered, $"Mixed Valorant layers and odd dot share the canvas center: thickness {thickness}, scale {sizeScale}");
            }
            var reportedCodes = new[] {
                "0;c;1;s;1;P;t;4;o;1;d;1;z;5;a;0.556;0t;10;0l;20;0v;0;0g;1;0o;13;0a;1;0f;0;1t;1;1l;1;1v;0;1g;1;1o;14;1a;1;1s;0.064;1e;0.375;S;c;3;s;0.628;o;1",
                "0;c;1;P;c;8;u;E279AFFF;o;0.113;d;1;b;1;z;1;f;0;m;1;0t;3;0l;3;0v;13;0g;1;0a;0.729;0e;0.1;1t;7;1l;3;1v;10;1g;1;1o;2;1a;0.71;1m;0;1e;0.1"
            };
            for (var codeIndex = 0; codeIndex < reportedCodes.Length; codeIndex++)
            foreach (var scale in new[] { 1d, 2d })
            foreach (var motion in new[] { (0d, 0d), (.37, .42), (1d, 1d) })
            {
                var reported = GameCrosshairImport.Read(reportedCodes[codeIndex]).Reticle;
                reported.Size = reported.Artwork!.ReferenceSize * scale;
                var view = new ReticleView { Settings = reported, Width = 320, Height = 320 };
                view.Measure(new Size(320, 320)); view.Arrange(new Rect(0, 0, 320, 320));
                view.SetMotion(motion.Item1, motion.Item2);
                var bitmap = new RenderTargetBitmap(320, 320, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
                var frame = new byte[320 * 320 * 4]; bitmap.CopyPixels(frame, 1280, 0);
                var maxDifference = 0;
                for (var y = 0; y < 320; y++) for (var x = 0; x < 320; x++) for (var channel = 0; channel < 4; channel++)
                {
                    var value = frame[(y * 320 + x) * 4 + channel];
                    maxDifference = Math.Max(maxDifference, Math.Abs(value - frame[(y * 320 + 319 - x) * 4 + channel]));
                    maxDifference = Math.Max(maxDifference, Math.Abs(value - frame[((319 - y) * 320 + x) * 4 + channel]));
                }
                Check(maxDifference <= 1, $"Reported Valorant code {codeIndex + 1}: centered RGBA at scale {scale}, motion {motion}, max difference {maxDifference}");
                if (motion == (0d, 0d) && scale == 2)
                {
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var renderedFile = File.Create(Path.Combine("artifacts", $"valorant-reported-{codeIndex + 1}.png")); encoder.Save(renderedFile);
                }
            }
            var layeredSettings = GameCrosshairImport.Read("0;P;h;0;d;1;f;1;0a;1;0t;2;0l;4;0o;10;0m;0;0f;1;0e;0.5;1a;1;1t;2;1l;3;1o;25;1m;1;1s;2;1f;0").Reticle;
            var layeredView = new ReticleView { Settings = layeredSettings, Width = 160, Height = 160 };
            layeredView.Measure(new Size(160, 160)); layeredView.Arrange(new Rect(0, 0, 160, 160));
            byte[] LayerFrame(double move, double fire)
            {
                layeredView.SetMotion(move, fire); layeredView.UpdateLayout();
                var frame = new RenderTargetBitmap(160, 160, 96, 96, PixelFormats.Pbgra32); frame.Render(layeredView);
                var pixels = new byte[160 * 160 * 4]; frame.CopyPixels(pixels, 640, 0); return pixels;
            }
            byte AlphaAt(byte[] frame, int x, int y) => frame[(y * 160 + x) * 4 + 3];
            var layerRest = LayerFrame(0, 0); var layerMove = LayerFrame(1, 0); var layerFire = LayerFrame(0, 1);
            Check(AlphaAt(layerMove, 95, 80) == 255 && AlphaAt(layerMove, 106, 80) == 0 && AlphaAt(layerMove, 122, 80) == 255, "Rendered movement expands only the enabled outer layer at its 2x multiplier");
            Check(AlphaAt(layerFire, 95, 80) == 0 && AlphaAt(layerFire, 100, 80) == 255 && AlphaAt(layerFire, 106, 80) == 255 && AlphaAt(layerFire, 80, 80) == 255, "Rendered firing shifts inner lines at 0.5x while outer lines and dot stay fixed");
            Check(AlphaAt(layerRest, 80, 64) == 255 && AlphaAt(layerFire, 80, 61) == 0, "Rendered upper branch fades during firing");
            Check(LayerFrame(0, 0).SequenceEqual(layerRest), "Layer animation returns pixel-exactly to rest");
            layeredSettings.Dynamic = false;
            Check(LayerFrame(1, 1).SequenceEqual(layerRest), "Dynamic switch disables source animation and fading");
            var glasses = GameCrosshairImport.Read("0;P;t;2;o;1;d;1;0t;10;0l;19;0v;0;0g;1;0o;1;0a;0;0e;0;1l;10;1v;0;1g;1;1o;19;1a;0;1s;0;1e;0");
            var glassesView = new ReticleView { Settings = glasses.Reticle, Width = 160, Height = 160 };
            glassesView.Measure(new Size(160, 160)); glassesView.Arrange(new Rect(0, 0, 160, 160)); glassesView.UpdateLayout();
            var glassesBitmap = new RenderTargetBitmap(160, 160, 96, 96, PixelFormats.Pbgra32); glassesBitmap.Render(glassesView);
            var glassesPixels = new byte[160 * 160 * 4]; glassesBitmap.CopyPixels(glassesPixels, 640, 0);
            // Independent integer raster fixture from the VCRDB Glasses preview rules.
            var expectedGlasses = new byte[160 * 160 * 4];
            void ReferenceBox(int x, int y, int w, int h, bool white)
            {
                for (var py = y - 2; py < y + h + 2; py++) for (var px = x - 2; px < x + w + 2; px++)
                {
                    var inside = px >= x && px < x + w && py >= y && py < y + h;
                    if (inside && !white) continue;
                    var n = ((py + 80) * 160 + px + 80) * 4;
                    expectedGlasses[n] = expectedGlasses[n + 1] = expectedGlasses[n + 2] = (byte)(inside ? 255 : 0);
                    expectedGlasses[n + 3] = 255;
                }
            }
            ReferenceBox(5, -5, 19, 10, false); ReferenceBox(-24, -5, 19, 10, false);
            ReferenceBox(-1, -1, 2, 2, true);
            ReferenceBox(23, -1, 10, 2, false); ReferenceBox(-33, -1, 10, 2, false);
            Check(glassesPixels.SequenceEqual(expectedGlasses), "Glasses matches all pixels of the independent builder fixture");
            var glassesEncoder = new PngBitmapEncoder(); glassesEncoder.Frames.Add(BitmapFrame.Create(glassesBitmap));
            using (var glassesOutput = File.Create(Path.Combine(Environment.CurrentDirectory, "artifacts", "glasses-reference-check.png"))) glassesEncoder.Save(glassesOutput);
            var windmill = GameCrosshairImport.Read("0;P;c;1;t;6;o;1;d;1;z;6;a;0;f;0;m;1;0t;10;0l;20;0o;20;0a;1;0m;1;0e;0.1;1t;10;1l;10;1o;40;1a;1;1m;0");
            Check(windmill.Reticle.Artwork!.Marks.Count == 9, "Windmill retains its transparent outlined dot");
            var windmillView = new ReticleView { Settings = windmill.Reticle, Width = 160, Height = 160 };
            windmillView.Measure(new Size(160, 160)); windmillView.Arrange(new Rect(0, 0, 160, 160)); windmillView.UpdateLayout();
            var windmillBitmap = new RenderTargetBitmap(160, 160, 96, 96, PixelFormats.Pbgra32); windmillBitmap.Render(windmillView);
            var windmillPixels = new byte[160 * 160 * 4]; windmillBitmap.CopyPixels(windmillPixels, 640, 0);
            byte Channel(int x, int y, int c) => windmillPixels[(y * 160 + x) * 4 + c];
            Check(Channel(80, 80, 3) == 0 && Channel(85, 80, 3) == 255 && Channel(85, 80, 1) == 0, "Windmill has a hollow black center outline");
            Check(Channel(105, 80, 1) == 255 && Channel(116, 80, 1) == 0 && Channel(116, 80, 3) == 255 && Channel(125, 80, 1) == 255, "Windmill preserves separate green segments");
            Check(window.ImportFromFile(exportedPath) && preview.Settings?.Size == 72, "Original vector profile still imports after image and code profiles");
            var invalidImage = Path.Combine(folder, "opaque.png");
            var opaque = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat((byte)255, 16).ToArray(), 8);
            var pngEncoder = new PngBitmapEncoder(); pngEncoder.Frames.Add(BitmapFrame.Create(opaque));
            using (var file = File.Create(invalidImage)) pngEncoder.Save(file);
            Check(!window.ImportFromFile(invalidImage) && preview.Settings?.Size == 72, "Opaque PNG is rejected without replacing the current crosshair");
            File.WriteAllText(invalidImage, "not a PNG");
            Check(!window.ImportFromFile(invalidImage), "Malformed PNG is rejected safely");
            var brokenBytes = Convert.FromBase64String(embedded!)[..33];
            var brokenView = new ReticleView { Settings = new ReticleSettings { Shape = ReticleShape.Imported, Artwork = new ImportedArtwork { PngBase64 = Convert.ToBase64String(brokenBytes) } } };
            brokenView.Measure(new Size(64, 64)); brokenView.Arrange(new Rect(0, 0, 64, 64));
            var brokenBitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32); brokenBitmap.Render(brokenView);
            var brokenPixels = new byte[64 * 64 * 4]; brokenBitmap.CopyPixels(brokenPixels, 64 * 4, 0);
            Check(brokenPixels.All(b => b == 0), "Corrupted persisted PNG renders safely without crashing");
            var importTop = Find<Button>("ImportButton").TransformToAncestor(window).Transform(new Point());
            var exportTop = Find<Button>("ExportButton").TransformToAncestor(window).Transform(new Point());
            Check(Math.Abs(importTop.Y - exportTop.Y) < .1 && Find<Button>("ImportButton").ActualHeight == Find<Button>("ExportButton").ActualHeight, "Import and export buttons share the same top edge and height");
            Find<TextBox>("ColorBox").Text = "#FFAA00";
            Check(preview.Settings?.Color == "#FFAA00", "Color editor changes the real reticle");
            Find<TextBox>("ColorBox").Text = "#oops";
            Check(preview.Settings?.Color == "#FFAA00", "Incomplete color cannot corrupt current reticle");
            Find<TextBox>("ColorBox").Text = "#80D8F7";
            Find<TextBox>("ColorBox").Text = "#FF0000";
            var brightness = Descendants(Find<ColorWheel>("Wheel")).OfType<Slider>().Single();
            brightness.Value = .5;
            Check(Find<TextBox>("ColorBox").Text == "#800000" && preview.Settings?.Color == "#800000", "Color wheel brightness updates the hex field and live crosshair");
            brightness.Value = 0; brightness.Value = 1;
            Check(Find<TextBox>("ColorBox").Text == "#FF0000", "Brightness can return from black without losing its hue");
            Find<TextBox>("ColorBox").Text = "#80D8F7";
            var import = typeof(MainWindow).GetMethod("ImportFromFile");
            if (import == null) Check(false, "Invalid import stays in the editor and reports an error");
            else
            {
                var invalidPath = Path.Combine(folder, "bad-profile.json"); File.WriteAllText(invalidPath, "{}");
                Check(import.Invoke(window, new object[] { invalidPath }) is false && window.IsVisible && preview.Settings?.Size == 72, "Invalid import stays in the editor and reports an error");
            }
            card = (Grid)cards.Children[0]; ((Button)card.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<TextBox>("SearchBox").Text = "";
            Find<Button>("FavoritesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(cards.Children.Count == 1, "Favorite selection and filtering work together");
            Find<Button>("FavoritesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<ComboBox>("CategoryBox").SelectedItem = "Rings";
            Check(cards.Children.Count == 3, "Category filtering shows only its family");
            Find<ComboBox>("CategoryBox").SelectedIndex = 0;
            Find<Button>("ToggleButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Find<TextBlock>("HeaderStatus").Text == "Crosshair on", "Activation button synchronizes visible status");
            Find<Button>("ToggleButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<TextBox>("ProfileNameBox").Text = "Test UI";
            FindButton(window, "Save")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(store.Load().Profiles.Single().Reticle.Size == 72, "Save profile persists the edited reticle");
            Find<Slider>("SizeSlider").Value = 24;
            Find<ComboBox>("ProfilesBox").SelectedIndex = 0;
            Check(preview.Settings?.Size == 72, "Loading a profile restores edits");
            Find<Button>("DeleteProfileButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(store.Load().Profiles.Count == 0, "Deleting a profile persists its removal");
            var settingsFile = Path.Combine(folder, "settings.json"); File.Delete(settingsFile); Directory.CreateDirectory(settingsFile);
            FindButton(window, "Save")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Find<TextBlock>("StatusText").Text.StartsWith("Could not save"), "Save failure cannot be reported as success");
            Directory.Delete(settingsFile);
            // Render all actual vector shapes, including extreme sizes and rotation.
            foreach (var preset in PresetCatalog.All)
            {
                var view = new ReticleView { Settings = preset.Create(), Width = 256, Height = 256 };
                view.Measure(new Size(256, 256)); view.Arrange(new Rect(0, 0, 256, 256));
                var bitmap = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
                var pixels = new byte[256 * 256 * 4]; bitmap.CopyPixels(pixels, 256 * 4, 0);
                Check(pixels.Where((_, i) => i % 4 == 3).Any(x => x > 0), "Renderer draws " + preset.Name);
            }
            var crisp = new ReticleView { PhysicalPixels = true, Settings = new ReticleSettings { Shape = ReticleShape.Cross, Size = 24, Thickness = 1, Gap = 6, Outline = false, Color = "#FFFFFF" } };
            crisp.Measure(new Size(64, 64)); crisp.Arrange(new Rect(0, 0, 64, 64));
            var crispBitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32); crispBitmap.Render(crisp);
            var crispPixels = new byte[64 * 64 * 4]; crispBitmap.CopyPixels(crispPixels, 64 * 4, 0);
            Check(crispPixels[(32 * 64 + 24) * 4 + 3] == 255 && crispPixels[(31 * 64 + 24) * 4 + 3] == 0, "One-pixel crosshair arms have solid pixel edges at 1080p");
            var qaFolder = Path.Combine(Environment.CurrentDirectory, "artifacts"); Directory.CreateDirectory(qaFolder);
            var importWindow = new CrosshairImportWindow { Owner = window }; importWindow.Show();
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Descendants(importWindow).OfType<TextBox>().Single().Text = "invalid-code";
            FindButton(importWindow, "Preview code")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(importWindow.IsVisible && importWindow.SelectedProfile == null && !FindButton(importWindow, "Import crosshair")!.IsEnabled, "Invalid game code stays in the import dialog without crashing");
            Descendants(importWindow).OfType<TextBox>().Single().Text = "0;P;c;8;u;12ABEFFF;h;1;d;1;0l;4;0a;1;1b;0";
            FindButton(importWindow, "Preview code")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            importWindow.UpdateLayout();
            Capture(importWindow, Path.Combine(qaFolder, "sightquill-import.png"));
            Check(importWindow.SelectedProfile?.Reticle.Artwork?.Marks.Count == 5, "Import dialog previews the code before acceptance");
            var importAction = FindButton(importWindow, "Import crosshair")!;
            var actionBottom = importAction.TransformToAncestor(importWindow).Transform(new Point(0, importAction.ActualHeight));
            Check(actionBottom.Y <= importWindow.ActualHeight - 8, "Import action fits in the dialog");
            importWindow.Close();
            var integrationWindow = new GameIntegrationWindow { Owner = window }; integrationWindow.Show();
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(integrationWindow, Path.Combine(qaFolder, "sightquill-game-setup.png")); integrationWindow.Close();
            foreach (var dpiScale in new[] { 1d, 1.5d, 2d })
            {
                var scaled = new ReticleView { PhysicalPixels = true, ResolutionScale = 2, Settings = new ReticleSettings { Shape = ReticleShape.Cross, Size = 24, Thickness = 1, Gap = 6, Outline = false, Color = "#FFFFFF" } };
                VisualTreeHelper.SetRootDpi(scaled, new DpiScale(dpiScale, dpiScale));
                scaled.Measure(new Size(128, 128)); scaled.Arrange(new Rect(0, 0, 128, 128));
                var pixelSize = (int)(128 * dpiScale);
                var bitmap = new RenderTargetBitmap(pixelSize, pixelSize, 96 * dpiScale, 96 * dpiScale, PixelFormats.Pbgra32); bitmap.Render(scaled);
                var pixels = new byte[pixelSize * pixelSize * 4]; bitmap.CopyPixels(pixels, pixelSize * 4, 0);
                var columns = Enumerable.Range(0, pixelSize).Where(x => Enumerable.Range(0, pixelSize).Any(y => pixels[(y * pixelSize + x) * 4 + 3] > 0)).ToArray();
                Check(columns.Length > 0 && columns[^1] - columns[0] + 1 == 48, $"4K reticle is 48 physical pixels at {dpiScale * 100:0}% Windows scaling");
            }
            foreach (var resolution in new[] { 1080, 2160 })
            {
                var sheet = new UniformGrid { Columns = 6, Width = 960, Height = 640, Background = new SolidColorBrush(Color.FromRgb(24, 32, 43)) };
                foreach (var preset in PresetCatalog.All)
                {
                    var cell = new Grid { Margin = new Thickness(1), Background = new SolidColorBrush(Color.FromRgb(36, 47, 62)) };
                    cell.Children.Add(new ReticleView { Settings = preset.Create(), PhysicalPixels = true, ResolutionScale = preset.Create().ResolutionScale(resolution) });
                    cell.Children.Add(new TextBlock { Text = preset.Name, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 10) }); sheet.Children.Add(cell);
                }
                sheet.Measure(new Size(960, 640)); sheet.Arrange(new Rect(0, 0, 960, 640));
                var bitmap = new RenderTargetBitmap(960, 640, 96, 96, PixelFormats.Pbgra32); bitmap.Render(sheet);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(qaFolder, $"crosshairs-{resolution}p.png")); encoder.Save(file);
            }
            // Screenshot of the live WPF visual tree, not a web mock-up.
            Find<TextBox>("SearchBox").Text = "Classic";
            ((Button)((Grid)cards.Children[0]).Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<TextBox>("SearchBox").Text = "";
            window.UpdateLayout(); app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var output = Path.Combine(Environment.CurrentDirectory, "artifacts"); Directory.CreateDirectory(output);
            Capture(window, Path.Combine(output, "sightquill-preview.png"));
            Find<Expander>("ColorWheelExpander").IsExpanded = true;
            window.UpdateLayout(); app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(window, Path.Combine(output, "sightquill-color-wheel.png"));
            Find<Expander>("ColorWheelExpander").IsExpanded = false;
            window.Width = 1060; window.Height = 740; window.UpdateLayout();
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(Find<Button>("ToggleButton").IsVisible && Find<ReticleView>("Preview").ActualWidth > 200, "Minimum window size keeps preview and activation available");
            Capture(window, Path.Combine(output, "sightquill-compact.png"));
            window.Width = 900; window.Height = 620; window.UpdateLayout();
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(window.ActualWidth <= 900 && window.ActualHeight <= 620, "Window fits a small logical desktop at high scaling");
            var togglePosition = Find<Button>("ToggleButton").TransformToAncestor(window).Transform(new Point(0, Find<Button>("ToggleButton").ActualHeight));
            Check(togglePosition.Y <= 620, "Activation remains inside the scaled viewport");
            Capture(window, Path.Combine(output, "sightquill-scaled.png"));
            Find<TextBox>("ColorBox").Text = "#FF00E1";
            Find<Slider>("SizeSlider").Value = 80;
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var largeExtent = MagentaExtent(window);
            window.Width = 760; window.Height = 520; window.UpdateLayout();
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var smallExtent = MagentaExtent(window);
            Check(Math.Abs(largeExtent - smallExtent) <= 2 && largeExtent >= 75, "Actual-size preview preserves pixel extent through consecutive small resizes");
            window.Close();
        }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL UI workflow: " + ex); }
        finally { Directory.Delete(folder, true); }
        app.Shutdown(); Console.WriteLine($"{failures} failed"); return failures == 0 ? 0 : 1;
    }
    private static Button? FindButton(DependencyObject parent, string content)
    {
        if (parent is Button b && b.Content is string text && text == content) return b;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var result = FindButton(VisualTreeHelper.GetChild(parent, i), content); if (result != null) return result; }
        return null;
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
    private static void Capture(Window window, string path)
    {
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); encoder.Save(stream);
    }
    private static int MagentaExtent(Window window)
    {
        var w = (int)window.ActualWidth; var h = (int)window.ActualHeight;
        var image = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var pixels = new byte[w * h * 4]; image.CopyPixels(pixels, w * 4, 0);
        var min = w; var max = -1;
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var i = (y * w + x) * 4;
            if (pixels[i + 2] > 220 && pixels[i + 1] < 40 && pixels[i] > 180) { min = Math.Min(min, x); max = Math.Max(max, x); }
        }
        return max < min ? 0 : max - min + 1;
    }
}
