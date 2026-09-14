using System;
using Sightquill.Core;
using System.Linq;
using System.Windows.Interop;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Diagnostics;
using Sightquill.Bridge;
using System.Threading;
using System.Windows.Media;

namespace Sightquill;
public sealed class OverlayController : IDisposable
{
    private readonly OverlayWindow window = new();
    private readonly HwndSource messages;
    private AppSettings current = new();
    private bool disposed;
    private AimReceiver? receiver;
    private readonly DispatcherTimer trackingTimer;
    private int verifiedGamePid;
    private int placementQueued;
    private readonly DynamicMotion motion = new();
    private readonly Stopwatch motionClock = new();
    private bool animating;
    private IntPtr motionTarget;
    private uint motionPid;
    private bool supportedMotionTarget;
    public string TrackingStatus { get; private set; } = "Screen center";
    public bool IsEnabled { get; private set; }
    public bool HotkeyRegistered { get; private set; }
    public IntPtr WindowHandle => window.Handle;
    public IntPtr HotkeyHandle => messages.Handle;
    public event EventHandler? Changed;
    public event EventHandler? DisplaysChanged;
    public OverlayController()
    {
        messages = new HwndSource(new HwndSourceParameters("Sightquill Shortcuts") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        messages.AddHook(Hook);
        HotkeyRegistered = NativeMethods.RegisterHotKey(messages.Handle, NativeMethods.HotkeyId, 0x0001 | 0x0002 | 0x4000, 0x58);
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        trackingTimer = new DispatcherTimer(DispatcherPriority.Render, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
        trackingTimer.Tick += (_, _) => { if (IsEnabled) Place(); };
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if (message == 0x0312 && w.ToInt32() == NativeMethods.HotkeyId) { Toggle(); handled = true; }
        return IntPtr.Zero;
    }
    private void OnDisplaysChanged(object? sender, EventArgs args)
    {
        if (disposed) return;
        window.Dispatcher.BeginInvoke(() => { if (!disposed) { Place(); DisplaysChanged?.Invoke(this, EventArgs.Empty); } }, DispatcherPriority.Normal);
    }
    public void Update(AppSettings state)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        current = new AppSettings { Current = state.Current.Clone(), MonitorId = state.MonitorId, OffsetX = state.OffsetX, OffsetY = state.OffsetY, HowToFishMode = state.HowToFishMode };
        if (current.HowToFishMode && receiver == null) { receiver = new AimReceiver(); receiver.Updated += QueuePlacement; }
        if (!current.HowToFishMode && receiver != null) { receiver.Updated -= QueuePlacement; receiver.Dispose(); receiver = null; verifiedGamePid = 0; }
        if (current.HowToFishMode && IsEnabled) trackingTimer.Start(); else trackingTimer.Stop();
        current.Normalize(); window.UpdateReticle(current.Current); if (IsEnabled) Place();
        RefreshAnimation();
    }
    private void RefreshAnimation()
    {
        var needed = IsEnabled && current.Current.Dynamic && !disposed;
        if (needed == animating) return;
        animating = needed;
        if (needed) { motionClock.Restart(); CompositionTarget.Rendering += Animate; }
        else { CompositionTarget.Rendering -= Animate; motion.Reset(); window.SetMotion(0, 0); motionClock.Stop(); }
    }
    private void Animate(object? sender, EventArgs e)
    {
        var elapsed = motionClock.Elapsed.TotalSeconds; motionClock.Restart();
        var target = NativeMethods.GetForegroundWindow(); NativeMethods.GetWindowThreadProcessId(target, out var pid);
        if (target != motionTarget || pid != motionPid)
        {
            motionTarget = target; motionPid = pid; supportedMotionTarget = false;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                supportedMotionTarget = new[] { "How to Fish", "VALORANT-Win64-Shipping", "csgo", "cs2" }.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        if (!window.IsVisible || !supportedMotionTarget || NativeMethods.IsIconic(target)) { motion.Reset(); window.SetMotion(0, 0); return; }
        bool Down(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
        var moving = Down(current.Current.AzertyMovement ? 0x5A : 0x57) || Down(current.Current.AzertyMovement ? 0x51 : 0x41) || Down(0x53) || Down(0x44);
        motion.Step(moving, Down(0x01), elapsed);
        window.SetMotion(motion.Movement, motion.Firing);
    }
    private void QueuePlacement()
    {
        // Keep at most one pending update; consume the newest sample when the UI runs it.
        if (Interlocked.Exchange(ref placementQueued, 1) != 0) return;
        window.Dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref placementQueued, 0);
            if (!disposed && IsEnabled && current.HowToFishMode) Place();
        }, DispatcherPriority.Render);
    }
    private void Place()
    {
        if (!IsEnabled) return;
        if (current.HowToFishMode) { TrackGame(); return; }
        SetTrackingStatus("Screen center");
        var screen = Screen.AllScreens.FirstOrDefault(x => x.DeviceName == current.MonitorId) ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
        var bounds = screen.Bounds;
        window.SetResolution(bounds.Height);
        var x = Math.Clamp(bounds.Left + bounds.Width / 2 + current.OffsetX, bounds.Left, bounds.Right - 1);
        var y = Math.Clamp(bounds.Top + bounds.Height / 2 + current.OffsetY, bounds.Top, bounds.Bottom - 1);
        window.Position(x, y);
        if (!window.IsVisible) { window.Show(); window.Position(x, y); }
    }
    private void SetTrackingStatus(string value) { if (TrackingStatus == value) return; TrackingStatus = value; Changed?.Invoke(this, EventArgs.Empty); }
    private void HideTracked(string reason) { if (window.IsVisible) window.Hide(); SetTrackingStatus(reason); }
    private void TrackGame()
    {
        var frame = receiver?.Latest;
        if (frame == null || !frame.IsFresh(DateTime.UtcNow.Ticks)) { HideTracked(frame == null ? receiver?.Status ?? "Waiting for companion" : "Signal expired · crosshair hidden"); return; }
        if (verifiedGamePid != frame.ProcessId)
        {
            try { using var process = Process.GetProcessById(frame.ProcessId); if (!process.ProcessName.Equals("How to Fish", StringComparison.OrdinalIgnoreCase)) { HideTracked("Unrecognized companion"); return; } verifiedGamePid = frame.ProcessId; }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { HideTracked("Game closed or unavailable"); return; }
        }
        var target = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(target, out var foregroundPid);
        if (foregroundPid != frame.ProcessId || NativeMethods.IsIconic(target)) { HideTracked("Game in background · crosshair hidden"); return; }
        if (frame.Kind == AimKind.Hidden) { HideTracked("Menu or pause · crosshair hidden"); return; }
        if (!NativeMethods.GetClientRect(target, out var rect) || rect.Right <= 0 || rect.Bottom <= 0) { HideTracked("Game window unavailable"); return; }
        var origin = new NativeMethods.Point();
        if (!NativeMethods.ClientToScreen(target, ref origin)) { HideTracked("Game position unavailable"); return; }
        var point = AimProtocol.ToScreen(frame, origin.X, origin.Y, rect.Right, rect.Bottom, current.OffsetX, current.OffsetY);
        window.SetResolution(rect.Bottom);
        window.Position(point.X, point.Y);
        if (!window.IsVisible) { window.Show(); window.Position(point.X, point.Y); }
        SetTrackingStatus(frame.Kind switch { AimKind.Barrel => "Connected · barrel tracking", AimKind.Scope => "Connected · sniper scope", _ => "Connected · camera center" });
    }
    public void Toggle()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        IsEnabled = !IsEnabled;
        if (IsEnabled) { Place(); if (current.HowToFishMode) trackingTimer.Start(); } else { trackingTimer.Stop(); window.Hide(); }
        RefreshAnimation();
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        RefreshAnimation();
        SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        trackingTimer.Stop();
        if (receiver != null) { receiver.Updated -= QueuePlacement; receiver.Dispose(); receiver = null; }
        if (HotkeyRegistered) NativeMethods.UnregisterHotKey(messages.Handle, NativeMethods.HotkeyId);
        messages.RemoveHook(Hook); messages.Dispose(); window.Close(); IsEnabled = false;
    }
}
