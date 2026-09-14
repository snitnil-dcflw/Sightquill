$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$target = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/Sightquill/Assets'
New-Item -ItemType Directory -Force $target | Out-Null
$bitmap = New-Object System.Drawing.Bitmap 256,256
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(27,35,48))
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(128,216,247)),10
$graphics.DrawEllipse($pen,64,64,128,128)
$graphics.DrawLine($pen,128,26,128,94)
$graphics.DrawLine($pen,128,162,128,230)
$graphics.DrawLine($pen,26,128,94,128)
$graphics.DrawLine($pen,162,128,230,128)
$buffer = New-Object System.IO.MemoryStream
$bitmap.Save($buffer,[System.Drawing.Imaging.ImageFormat]::Png)
$bytes = $buffer.ToArray()
$stream = [System.IO.File]::Create((Join-Path $target 'Sightquill.ico'))
$writer = New-Object System.IO.BinaryWriter $stream
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]1)
$writer.Write([Byte]0); $writer.Write([Byte]0); $writer.Write([Byte]0); $writer.Write([Byte]0)
$writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$bytes.Length); $writer.Write([UInt32]22)
$writer.Write($bytes)
$writer.Dispose(); $buffer.Dispose(); $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
