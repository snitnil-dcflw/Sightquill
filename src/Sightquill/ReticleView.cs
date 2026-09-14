using System;
using System.Windows;
using System.Windows.Media;
using Sightquill.Core;
using System.Windows.Media.Imaging;

namespace Sightquill;
public sealed class ReticleView : FrameworkElement
{
    public static readonly DependencyProperty SettingsProperty = DependencyProperty.Register(nameof(Settings), typeof(ReticleSettings), typeof(ReticleView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public ReticleSettings? Settings { get => (ReticleSettings?)GetValue(SettingsProperty); set => SetValue(SettingsProperty, value); }
    public bool PhysicalPixels { get; set; }
    public bool FitThumbnail { get; set; }
    private double resolutionScale = 1;
    private double movement, firing;
    public void SetMotion(double move, double fire) { if (movement == move && firing == fire) return; movement = move; firing = fire; InvalidateVisual(); }
    private string? cachedPng;
    private BitmapSource? cachedBitmap;
    public double ResolutionScale { get => resolutionScale; set { if (resolutionScale == value) return; resolutionScale = value; InvalidateVisual(); } }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var source = Settings; if (source == null) return;
        var s = source.Clone();
        var factor = FitThumbnail ? 1 : ResolutionScale;
        s.Size = Math.Round(s.Size * factor);
        s.Thickness = Math.Max(1, Math.Round(s.Thickness * factor));
        s.Gap = Math.Round(s.Gap * factor);
        var expansion = source.Dynamic ? (movement * source.MovementSpread + firing * source.FiringSpread) * factor : 0;
        if (s.Shape != ReticleShape.Imported) { s.Size += expansion * 2; s.Gap += expansion * 2; }
        var dpi = VisualTreeHelper.GetDpi(this);
        var scale = PhysicalPixels ? 1 / dpi.DpiScaleX : 1;
        if (PhysicalPixels && PresentationSource.FromVisual(this)?.RootVisual is Visual root && root != this)
        {
            var transform = TransformToAncestor(root);
            var origin = transform.Transform(new Point()); var unit = transform.Transform(new Point(1, 0));
            var ancestorScale = (unit - origin).Length;
            if (ancestorScale > 0) scale /= ancestorScale;
        }
        if (FitThumbnail) scale = Math.Min(1, Math.Max(8, Math.Min(ActualWidth, ActualHeight) - 12) / (s.Size + s.Thickness + 4));
        // Odd-width strokes land on half-pixel centers, with solid pixel edges.
        var phase = s.Shape is ReticleShape.Dot or ReticleShape.Imported ? 0 : ((int)s.Thickness % 2) * .5;
        dc.PushTransform(new TranslateTransform(ActualWidth / 2 + phase * scale, ActualHeight / 2 + phase * scale));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.PushTransform(new RotateTransform(s.Rotation));
        dc.PushOpacity(s.Opacity);
        if (s.Shape == ReticleShape.Imported)
        {
            DrawArtwork(dc, s, expansion);
            dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop(); return;
        }
        var color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.Color)); color.Freeze();
        var geometry = MakeGeometry(s); geometry.Freeze();
        var filled = s.Shape == ReticleShape.Dot;
        var ink = filled ? geometry : geometry.GetWidenedPathGeometry(new Pen(Brushes.White, s.Thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat, LineJoin = PenLineJoin.Bevel });
        if (s.Outline) dc.DrawGeometry(Brushes.Black, new Pen(Brushes.Black, 2 * Math.Max(1, Math.Round(factor))) { LineJoin = PenLineJoin.Round }, ink);
        dc.DrawGeometry(color, null, ink);
        if (s.CenterDot && !filled)
            dc.DrawEllipse(color, s.Outline ? new Pen(Brushes.Black, Math.Max(1, Math.Round(factor))) : null, new Point(), Math.Max(.5, s.Thickness / 2), Math.Max(.5, s.Thickness / 2));
        dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
    }
    private void DrawArtwork(DrawingContext dc, ReticleSettings settings, double expansion)
    {
        var art = settings.Artwork; if (art == null) return;
        if (art.PngBase64 is string png)
        {
            if (cachedPng != png)
            {
                cachedPng = png; cachedBitmap = null;
                try { cachedBitmap = PngCrosshair.Decode(Convert.FromBase64String(png)); }
                catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or FormatException or InvalidOperationException) { }
            }
            if (cachedBitmap == null) return;
            var factor = (settings.Size + expansion * 2) / Math.Max(cachedBitmap.PixelWidth, cachedBitmap.PixelHeight);
            var width = cachedBitmap.PixelWidth * factor; var height = cachedBitmap.PixelHeight * factor;
            dc.DrawImage(cachedBitmap, new Rect(-width / 2, -height / 2, width, height));
            return;
        }
        var ratio = settings.Size / art.ReferenceSize;
        dc.PushTransform(new ScaleTransform(ratio, ratio));
        var color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.Color));
        var motionScale = FitThumbnail ? 1 : ResolutionScale;
        var animatedMarks = art.Marks.ConvertAll(mark => settings.Dynamic
            ? mark.AtMotion(movement * settings.MovementSpread * motionScale, firing * settings.FiringSpread * motionScale, firing, ratio)
            : mark);
        // Keep source order: later outlines separate overlapping inner/outer arms.
        foreach (var mark in animatedMarks)
        {
            if (mark.Outline > 0 && mark.OutlineOpacity > 0)
            {
                var outside = new RectangleGeometry(new Rect(mark.X - mark.Outline, mark.Y - mark.Outline, mark.Width + mark.Outline * 2, mark.Height + mark.Outline * 2));
                var inside = new RectangleGeometry(new Rect(mark.X, mark.Y, mark.Width, mark.Height));
                dc.PushOpacity(mark.OutlineOpacity);
                dc.DrawGeometry(Brushes.Black, null, new CombinedGeometry(GeometryCombineMode.Exclude, outside, inside));
                dc.Pop();
            }
            dc.PushOpacity(mark.Opacity);
            dc.DrawRectangle(color, null, new Rect(mark.X, mark.Y, mark.Width, mark.Height));
            dc.Pop();
        }
        dc.Pop();
    }
    private static Geometry MakeGeometry(ReticleSettings s)
    {
        var r = s.Size / 2;
        if (s.Shape == ReticleShape.Dot || s.Shape == ReticleShape.Circle) return new EllipseGeometry(new Point(), r, r);
        var group = new GeometryGroup();
        void Line(double x1, double y1, double x2, double y2) => group.Children.Add(new LineGeometry(new Point(x1, y1), new Point(x2, y2)));
        var gap = Math.Min(Math.Ceiling(s.Gap / 2), Math.Max(0, r - 1));
        switch (s.Shape)
        {
            case ReticleShape.Cross:
            case ReticleShape.TShape:
            case ReticleShape.Hybrid:
                if (s.Shape == ReticleShape.Hybrid) gap = Math.Max(gap, Math.Ceiling(r * .55 + s.Thickness + 2));
                gap = Math.Min(gap, r - 1);
                Line(-r, 0, -gap, 0); Line(gap, 0, r, 0); Line(0, gap, 0, r);
                if (s.Shape != ReticleShape.TShape) Line(0, -r, 0, -gap);
                if (s.Shape == ReticleShape.Hybrid) group.Children.Add(new EllipseGeometry(new Point(), Math.Max(1, Math.Floor(r * .45)), Math.Max(1, Math.Floor(r * .45))));
                break;
            case ReticleShape.Chevron:
                group.Children.Add(Geometry.Parse(FormattableString.Invariant($"M {-r},{r * .7} L 0,0 {r},{r * .7}"))); break;
            case ReticleShape.Diamond:
                group.Children.Add(Geometry.Parse(FormattableString.Invariant($"M 0,{-r} L {r},0 0,{r} {-r},0 Z"))); break;
            case ReticleShape.Brackets:
                foreach (var x in new[] { -1, 1 }) foreach (var y in new[] { -1, 1 })
                { var inner = Math.Round(r * .65); group.Children.Add(Geometry.Parse(FormattableString.Invariant($"M {x * inner},{y * r} L {x * r},{y * r} {x * r},{y * inner}"))); }
                break;
        }
        return group;
    }
}
