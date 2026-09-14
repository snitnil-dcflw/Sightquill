$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class GameWindowProbe {
 public delegate bool Callback(IntPtr h, IntPtr l);
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback cb, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder text,int max);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h,ref Point p);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
}
'@
$gameProcess = Get-Process -Name 'How to Fish' -ErrorAction SilentlyContinue | Select-Object -First 1
$sightquillProcess = Get-Process -Name Sightquill -ErrorAction SilentlyContinue | Select-Object -First 1
$processIds = @($gameProcess.Id, $sightquillProcess.Id)
$script:windowData = @()
$previousDpi = [GameWindowProbe]::SetThreadDpiAwarenessContext([IntPtr](-4))
try {
 [GameWindowProbe]::EnumWindows({param($handle,$unused)
    [uint32]$ownerId = 0
    [void][GameWindowProbe]::GetWindowThreadProcessId($handle,[ref]$ownerId)
    if ($processIds -contains $ownerId) {
      $title = New-Object System.Text.StringBuilder 512
      [void][GameWindowProbe]::GetWindowText($handle,$title,512)
      $outer = New-Object GameWindowProbe+Rect; $client = New-Object GameWindowProbe+Rect; $origin = New-Object GameWindowProbe+Point
      [void][GameWindowProbe]::GetWindowRect($handle,[ref]$outer)
      [void][GameWindowProbe]::GetClientRect($handle,[ref]$client)
      [void][GameWindowProbe]::ClientToScreen($handle,[ref]$origin)
      $script:windowData += [PSCustomObject]@{ Pid=$ownerId; Handle=$handle.ToInt64(); Title=$title.ToString(); Visible=[GameWindowProbe]::IsWindowVisible($handle); Minimized=[GameWindowProbe]::IsIconic($handle); Dpi=[GameWindowProbe]::GetDpiForWindow($handle); Window=$outer; ClientOrigin=$origin; ClientSize=@($client.Right,$client.Bottom); ClientCenter=@(($origin.X+$client.Right/2),($origin.Y+$client.Bottom/2)) }
    }
    return $true
 },[IntPtr]::Zero) | Out-Null
 $script:windowData | Where-Object { $_.Title -match 'Fish|Sightquill' } | ConvertTo-Json -Depth 5
 if ($gameProcess -and [GameWindowProbe]::GetForegroundWindow() -eq $gameProcess.MainWindowHandle) {
    $rect = New-Object GameWindowProbe+Rect; [void][GameWindowProbe]::GetWindowRect($gameProcess.MainWindowHandle,[ref]$rect)
    $bitmap = New-Object System.Drawing.Bitmap ($rect.Right-$rect.Left),($rect.Bottom-$rect.Top)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,$bitmap.Size); $bitmap.Save((Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/how-to-fish-observed.png')); 'Game screenshot captured.' }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
 } else { 'Game is not foreground; no screen capture taken.' }
} finally { [void][GameWindowProbe]::SetThreadDpiAwarenessContext($previousDpi) }
