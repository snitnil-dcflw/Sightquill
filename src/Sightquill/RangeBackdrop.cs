using System.Windows;
using System.Windows.Media;

namespace Sightquill;
public sealed class RangeBackdrop : FrameworkElement
{
    public int Mode { get; set; }
    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        if (Mode != 0)
        {
            dc.DrawRectangle(Brush(Mode == 1 ? "#253244" : "#E5E9E8"), null, new Rect(0, 0, w, h));
            var grid = new Pen(Brush(Mode == 1 ? "#344459" : "#D1D9D9"), .6);
            for (var x = w / 2 % 28; x < w; x += 28) dc.DrawLine(grid, new Point(x, 0), new Point(x, h));
            for (var y = h / 2 % 28; y < h; y += 28) dc.DrawLine(grid, new Point(0, y), new Point(w, y));
            return;
        }
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(57, 81, 101), Color.FromRgb(118, 142, 148), 90), null, new Rect(0, 0, w, h));
        var horizon = h * .54;
        Polygon(dc, "#506775", new Point(0, horizon), new Point(0, h * .38), new Point(w * .15, h * .27), new Point(w * .29, h * .38), new Point(w * .45, h * .31), new Point(w * .63, h * .42), new Point(w * .83, h * .24), new Point(w, h * .35), new Point(w, horizon));
        dc.DrawRectangle(Brush("#394A50"), null, new Rect(0, horizon, w, h - horizon));
        Polygon(dc, "#455961", new Point(w * .32, horizon), new Point(w * .68, horizon), new Point(w, h), new Point(0, h));
        var line = new Pen(Brush("#78908E"), 1);
        for (var i = -3; i <= 3; i++) dc.DrawLine(line, new Point(w * .5 + i * 13, horizon), new Point(w * .5 + i * w * .28, h));
        foreach (var y in new[] { .60, .70, .85 }) dc.DrawLine(new Pen(Brush("#617779"), .8), new Point(0, h * y), new Point(w, h * y));
        Polygon(dc, "#2E424F", new Point(0, h * .30), new Point(w * .17, h * .37), new Point(w * .17, h * .69), new Point(0, h * .82));
        Polygon(dc, "#55717D", new Point(w * .17, h * .37), new Point(w * .25, h * .42), new Point(w * .25, h * .63), new Point(w * .17, h * .69));
        Polygon(dc, "#344853", new Point(w, h * .31), new Point(w * .81, h * .37), new Point(w * .81, h * .68), new Point(w, h * .80));
        dc.DrawRectangle(Brush("#8F9990"), new Pen(Brush("#B0B5A4"), 1), new Rect(w / 2 - 23, h / 2 - 32, 46, 64));
        dc.DrawEllipse(null, new Pen(Brush("#535F5D"), 2), new Point(w / 2, h / 2), 15, 21);
        dc.DrawEllipse(null, new Pen(Brush("#535F5D"), 1), new Point(w / 2, h / 2), 8, 12);
        dc.DrawRectangle(Brush("#283B44"), null, new Rect(w / 2 - 2, h / 2 + 32, 4, h * .13));
    }
    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
    private static void Polygon(DrawingContext dc, string color, params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open()) { g.BeginFigure(points[0], true, true); for (var i = 1; i < points.Length; i++) g.LineTo(points[i], true, false); }
        dc.DrawGeometry(Brush(color), null, geometry);
    }
}
