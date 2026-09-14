$ErrorActionPreference = 'Stop'
$appPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/Sightquill-win-x64/Sightquill.exe'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ReopenProbe {
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int n);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
'@
if (Get-Process -Name Sightquill -ErrorAction SilentlyContinue) { throw 'Close Sightquill before running this test.' }
$primary = Start-Process -FilePath $appPath -PassThru
for ($i=0; $i -lt 60; $i++) { $primary.Refresh(); if ($primary.MainWindowHandle -ne 0) { break }; Start-Sleep -Milliseconds 100 }
$handle = $primary.MainWindowHandle
if ($handle -eq 0) { throw 'App window did not open.' }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
$condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Minimize to tray')
$button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
$button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 200
if ([ReopenProbe]::IsWindowVisible($handle)) { throw 'Tray test did not hide the window.' }
foreach ($scenario in @('hidden in tray','minimized')) {
    if ($scenario -eq 'minimized') { [void][ReopenProbe]::ShowWindow($handle,6); Start-Sleep -Milliseconds 100 }
    $second = Start-Process -FilePath $appPath -PassThru
    if (-not $second.WaitForExit(5000)) { throw 'Second launch did not exit automatically.' }
    for ($i=0; $i -lt 30; $i++) {
        if ([ReopenProbe]::IsWindowVisible($handle) -and -not [ReopenProbe]::IsIconic($handle) -and [ReopenProbe]::GetForegroundWindow() -eq $handle) { break }
        Start-Sleep -Milliseconds 100
    }
    if (-not [ReopenProbe]::IsWindowVisible($handle) -or [ReopenProbe]::IsIconic($handle)) { throw "Window was not restored from $scenario." }
    if ([ReopenProbe]::GetForegroundWindow() -ne $handle) { throw "Window was not activated from $scenario." }
    if (@(Get-Process -Name Sightquill).Count -ne 1) { throw 'Duplicate process remained.' }
    "PASS Shortcut restores the existing window from $scenario, foreground, one process"
}
'The restored application is left open.'
