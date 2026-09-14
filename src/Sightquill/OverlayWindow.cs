using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Sightquill.Core;

namespace Sightquill;
internal sealed class OverlayWindow : Window
{
    private readonly ReticleView view = new() { PhysicalPixels = true, IsHitTestVisible = false };
    private ReticleSettings settings = new();
    private int extent = 256;
    internal IntPtr Handle { get; }
    internal OverlayWindow()
    {
        Title = "Sightquill Overlay"; Width = 256; Height = 256; WindowStyle = WindowStyle.None;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        ShowActivated = false; Focusable = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false; Content = view;
        Handle = new WindowInteropHelper(this).EnsureHandle();
        var style = NativeMethods.GetWindowLong(Handle, -20);
        NativeMethods.SetWindowLong(Handle, -20, style | 0x08000000 | 0x20 | 0x80);
        HwndSource.FromHwnd(Handle)?.AddHook(Hook);
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if (message == 0x0084) { handled = true; return new IntPtr(-1); }
        if (message == 0x0021) { handled = true; return new IntPtr(3); }
        return IntPtr.Zero;
    }
    internal void UpdateReticle(ReticleSettings settings) { this.settings = settings.Clone(); view.Settings = settings.Clone(); }
    internal void SetMotion(double movement, double firing) => view.SetMotion(movement, firing);
    internal void SetResolution(int height)
    {
        view.ResolutionScale = settings.ResolutionScale(height);
        var spread = settings.Dynamic ? (settings.MovementSpread + settings.FiringSpread) * 6 : 0;
        extent = Math.Max(256, (int)Math.Ceiling((settings.Size * 1.5 + spread + settings.Thickness + 8) * view.ResolutionScale / 2) * 2);
    }
    internal void Position(int centerX, int centerY)
    {
        // Native coordinates are physical pixels; rendering compensates for WPF DPI.
        NativeMethods.SetWindowPos(Handle, new IntPtr(-1), centerX - extent / 2, centerY - extent / 2, extent, extent, 0x0010);
    }
}
