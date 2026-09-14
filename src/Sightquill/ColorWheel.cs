using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sightquill;

public sealed class ColorWheel : StackPanel
{
    private readonly WheelSurface surface;
    private readonly Slider brightness;
    private bool syncing;
    private string selected = "";
    public event Action<string>? SelectedColorChanged;
    public ColorWheel()
    {
        surface = new WheelSurface { Width = 180, Height = 180, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };
        Children.Add(surface);
        Children.Add(new TextBlock { Text = "Brightness", FontSize = 11 });
        brightness = new Slider { Minimum = 0, Maximum = 1, Value = 1, SmallChange = .01, LargeChange = .1, Margin = new Thickness(0, 4, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(brightness, "Color brightness");
        Children.Add(brightness);
        surface.Changed += Publish;
        brightness.ValueChanged += (_, _) => { surface.Value = brightness.Value; surface.InvalidateVisual(); Publish(); };
    }
    public void SetColor(string hex)
    {
        if (selected.Equals(hex, StringComparison.OrdinalIgnoreCase)) return;
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var r = color.R / 255d; var g = color.G / 255d; var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
        syncing = true;
        if (delta > 0) surface.Hue = ((max == r ? (g - b) / delta : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4) * 60 + 360) % 360;
        surface.Saturation = max == 0 ? 0 : delta / max;
        surface.Value = max; brightness.Value = max; selected = hex;
        surface.InvalidateVisual(); syncing = false;
    }
    private void Publish()
    {
        if (syncing) return;
        var c = WheelSurface.FromHsv(surface.Hue, surface.Saturation, surface.Value);
        selected = $"#{c.R:X2}{c.G:X2}{c.B:X2}"; SelectedColorChanged?.Invoke(selected);
    }
    private sealed class WheelSurface : FrameworkElement
    {
        public double Hue, Saturation = 1, Value = 1;
        public event Action? Changed;
        private static readonly BitmapSource disk = CreateDisk();
        public WheelSurface()
        {
            Focusable = true; Cursor = Cursors.Cross;
            ToolTip = "Drag to choose hue and saturation. Arrow keys adjust the selection; Shift makes larger steps.";
            System.Windows.Automation.AutomationProperties.SetName(this, "Hue and saturation wheel");
        }
        public static Color FromHsv(double hue, double saturation, double value)
        {
            var c = value * saturation; var x = c * (1 - Math.Abs((hue / 60) % 2 - 1)); var m = value - c;
            var (r, g, b) = hue switch { < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x) };
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }
        private static BitmapSource CreateDisk()
        {
            const int size = 512; var pixels = new byte[size * size * 4];
            for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
            {
                var dx = (x + .5 - size / 2d) / (size / 2d); var dy = (y + .5 - size / 2d) / (size / 2d);
                var radius = Math.Sqrt(dx * dx + dy * dy); if (radius > 1) continue;
                var color = FromHsv((Math.Atan2(-dy, dx) * 180 / Math.PI + 360) % 360, radius, 1);
                var i = (y * size + x) * 4; pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = 255;
            }
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4); bitmap.Freeze(); return bitmap;
        }
        protected override void OnRender(DrawingContext dc)
        {
            var r = Math.Min(ActualWidth, ActualHeight) / 2 - 6; var center = new Point(ActualWidth / 2, ActualHeight / 2);
            dc.DrawImage(disk, new Rect(center.X - r, center.Y - r, r * 2, r * 2));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)Math.Round((1 - Value) * 255), 0, 0, 0)), null, center, r, r);
            var angle = Hue * Math.PI / 180;
            var marker = new Point(center.X + Math.Cos(angle) * Saturation * r, center.Y - Math.Sin(angle) * Saturation * r);
            dc.DrawEllipse(null, new Pen(Brushes.Black, 4), marker, 5, 5); dc.DrawEllipse(null, new Pen(Brushes.White, 2), marker, 5, 5);
            if (IsKeyboardFocused) dc.DrawEllipse(null, new Pen(Brushes.White, 1), center, r + 4, r + 4);
        }
        private void Pick(Point point)
        {
            var x = point.X - ActualWidth / 2; var y = point.Y - ActualHeight / 2; var r = Math.Min(ActualWidth, ActualHeight) / 2 - 6;
            Hue = (Math.Atan2(-y, x) * 180 / Math.PI + 360) % 360; Saturation = Math.Clamp(Math.Sqrt(x * x + y * y) / r, 0, 1);
            InvalidateVisual(); Changed?.Invoke();
        }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { base.OnMouseLeftButtonDown(e); Focus(); CaptureMouse(); Pick(e.GetPosition(this)); e.Handled = true; }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) Pick(e.GetPosition(this)); }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { base.OnMouseLeftButtonUp(e); if (IsMouseCaptured) ReleaseMouseCapture(); }
        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            switch (e.Key) { case Key.Left: Hue = (Hue - step + 360) % 360; break; case Key.Right: Hue = (Hue + step) % 360; break; case Key.Up: Saturation = Math.Min(1, Saturation + step * .01); break; case Key.Down: Saturation = Math.Max(0, Saturation - step * .01); break; default: base.OnKeyDown(e); return; }
            e.Handled = true; InvalidateVisual(); Changed?.Invoke();
        }
    }
}
