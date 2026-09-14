$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$manifestPath = Join-Path $root 'artifacts/how-to-fish-installation.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Manifeste introuvable.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$gameRoot = [IO.Path]::GetFullPath($manifest.GameDirectory).TrimEnd('\')
if (Get-Process -Name 'How to Fish' -ErrorAction SilentlyContinue) { throw 'Ferme How to Fish normalement avant de désinstaller le compagnon.' }
$remaining = @()
$pluginPath = [IO.Path]::GetFullPath((Join-Path $gameRoot 'BepInEx/plugins/Sightquill/Sightquill.HowToFish.dll'))
$pluginsRoot = Join-Path $gameRoot 'BepInEx/plugins'
$hasOtherPlugins = $false
if (Test-Path -LiteralPath $pluginsRoot) { $hasOtherPlugins = @(Get-ChildItem -LiteralPath $pluginsRoot -Recurse -File -Filter '*.dll' | Where-Object { $_.FullName -ne $pluginPath }).Count -gt 0 }
foreach ($entry in $manifest.Files) {
    $target = [IO.Path]::GetFullPath($entry.Path)
    if (-not $target.StartsWith($gameRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Chemin sortant du dossier de jeu refusé.' }
    if ($hasOtherPlugins -and $target -ne $pluginPath) { $remaining += $entry; continue }
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.SHA256) { Write-Warning "Fichier modifié depuis installation, conservé : $target"; $remaining += $entry; continue }
        Remove-Item -LiteralPath $target
    }
}
$manifest.Files = $remaining
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
'Les fichiers installés et inchangés ont été retirés. Les sauvegardes du jeu, configurations et journaux sont conservés.'
if ($hasOtherPlugins) { 'Le chargeur BepInEx est conservé car des plugins tiers sont installés.' }
