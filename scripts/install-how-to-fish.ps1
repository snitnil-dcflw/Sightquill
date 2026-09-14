param([Parameter(Mandatory)][string]$GameDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$gameRoot = (Resolve-Path -LiteralPath $GameDirectory).Path.TrimEnd('\')
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'How to Fish.exe'))) { throw 'Le dossier ne contient pas How to Fish.exe.' }
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'How to Fish_Data/Managed/Assembly-CSharp.dll'))) { throw 'Version Unity Mono du jeu introuvable.' }
$pluginSource = Join-Path $root 'artifacts/HowToFish-Companion/Sightquill.HowToFish.dll'
$loaderRoot = Join-Path $root '.tools/bepinex'
if (-not (Test-Path -LiteralPath $pluginSource) -or -not (Test-Path -LiteralPath (Join-Path $loaderRoot 'winhttp.dll'))) { throw 'Compile le compagnon et prépare BepInEx avant installation.' }
$manifestPath = Join-Path $root 'artifacts/how-to-fish-installation.json'
$ownedFiles = @()
if (Test-Path -LiteralPath $manifestPath) {
    $previous = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($previous.GameDirectory -ne $gameRoot) { throw 'Une installation existe pour un autre dossier. Désinstalle-la avant de continuer.' }
    $ownedFiles = @($previous.Files)
}
$existingLoader = Join-Path $gameRoot 'BepInEx/core/BepInEx.dll'
$copyList = @()
if (Test-Path -LiteralPath $existingLoader) {
    if ([Reflection.AssemblyName]::GetAssemblyName($existingLoader).Version.Major -ne 5) { throw 'Un autre chargeur de mods est installé. Aucun fichier modifié.' }
} else {
    foreach ($entry in Get-ChildItem -LiteralPath $loaderRoot -File -Recurse) {
        $relative = $entry.FullName.Substring($loaderRoot.Length).TrimStart('\','/')
        $target = [IO.Path]::GetFullPath((Join-Path $gameRoot $relative))
        if (-not $target.StartsWith($gameRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Chemin sortant du dossier du jeu.' }
        if (Test-Path -LiteralPath $target) { throw "Fichier existant préservé : $target. Aucun fichier modifié." }
        $copyList += [PSCustomObject]@{ Source=$entry.FullName; Target=$target }
    }
}
$pluginTarget = [IO.Path]::GetFullPath((Join-Path $gameRoot 'BepInEx/plugins/Sightquill/Sightquill.HowToFish.dll'))
if (-not $pluginTarget.StartsWith($gameRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Chemin du module invalide.' }
if (Test-Path -LiteralPath $pluginTarget) {
    if (-not ($ownedFiles | Where-Object { $_.Path -eq $pluginTarget })) { throw 'Un compagnon non géré existe déjà. Aucun fichier modifié.' }
    $backup = Join-Path $root ('artifacts/backups/companion-' + (Get-Date -Format yyyyMMdd-HHmmss))
    New-Item -ItemType Directory -Force $backup | Out-Null
    Copy-Item -LiteralPath $pluginTarget -Destination $backup
}
$copyList += [PSCustomObject]@{ Source=$pluginSource; Target=$pluginTarget }
foreach ($entry in $copyList) {
    New-Item -ItemType Directory -Force (Split-Path $entry.Target -Parent) | Out-Null
    Copy-Item -LiteralPath $entry.Source -Destination $entry.Target -Force
    $ownedFiles = @($ownedFiles | Where-Object { $_.Path -ne $entry.Target })
    $ownedFiles += [PSCustomObject]@{ Path=$entry.Target; SHA256=(Get-FileHash -LiteralPath $entry.Target -Algorithm SHA256).Hash }
    # Journal each completed copy so an interrupted installation remains removable.
    [PSCustomObject]@{ GameDirectory=$gameRoot; InstalledAt=(Get-Date).ToString('o'); Files=$ownedFiles } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}
"Compagnon installé dans $pluginTarget"
'Chargement au prochain lancement du jeu. Aucun fichier original du jeu remplacé.'
