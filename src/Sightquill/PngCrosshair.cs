using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sightquill.Core;

namespace Sightquill;

public static class PngCrosshair
{
    public static SavedProfile Read(string path)
    {
        if (new FileInfo(path).Length > 2_000_000) throw new InvalidDataException("PNG files must be smaller than 2 MB.");
        var bytes = File.ReadAllBytes(path); ImportedArtwork.ValidatePngHeader(bytes, 2048);
        var image = Decode(bytes);
        var size = Math.Max(image.PixelWidth, image.PixelHeight);
        BitmapSource stored = image;
        if (size > 256) stored = new TransformedBitmap(image, new ScaleTransform(256d / size, 256d / size));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(stored));
        using var stream = new MemoryStream(); encoder.Save(stream);
        var art = new ImportedArtwork { Source = "PNG", PngBase64 = Convert.ToBase64String(stream.ToArray()), ReferenceSize = Math.Max(stored.PixelWidth, stored.PixelHeight) };
        art.Validate();
        return new SavedProfile { Name = AppSettings.CleanName(Path.GetFileNameWithoutExtension(path)), Reticle = new ReticleSettings { Shape = ReticleShape.Imported, Artwork = art, Size = Math.Clamp(size, 4, 160), ScaleWithResolution = false, Outline = false } };
    }
    public static BitmapSource Decode(byte[] bytes)
    {
        ImportedArtwork.ValidatePngHeader(bytes, 2048);
        try
        {
            using var stream = new MemoryStream(bytes);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var image = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            var visible = false; var transparent = false;
            for (var i = 3; i < pixels.Length; i += 4) { visible |= pixels[i] > 0; transparent |= pixels[i] < 255; }
            if (!visible) throw new InvalidDataException("This PNG is completely transparent.");
            if (!transparent) throw new InvalidDataException("This PNG has no transparency. Use an image with a transparent background.");
            image.Freeze(); return image;
        }
        catch (Exception ex) when (ex is FileFormatException or NotSupportedException or ArgumentException or OverflowException or System.Runtime.InteropServices.COMException)
        { throw new InvalidDataException("The PNG could not be decoded.", ex); }
    }
    public static void Validate(ReticleSettings settings)
    {
        if (settings.Artwork?.PngBase64 is string png) _ = Decode(Convert.FromBase64String(png));
    }
    public static void Export(string path, ReticleSettings settings)
    {
        var extent = (int)Math.Ceiling(settings.Size * 1.5 + settings.Thickness + 8);
        extent += extent % 2;
        var view = new ReticleView { Settings = settings.Clone(), Width = extent, Height = extent };
        view.Measure(new Size(extent, extent)); view.Arrange(new Rect(0, 0, extent, extent));
        var image = new RenderTargetBitmap(extent, extent, 96, 96, PixelFormats.Pbgra32); image.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        var full = Path.GetFullPath(path); var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var file = File.Create(temp)) encoder.Save(file); File.Move(temp, full, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
