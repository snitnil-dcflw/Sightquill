param([Parameter(Mandatory)][string]$GameDirectory)
. "$PSScriptRoot/scripts/toolchain.ps1"
Push-Location $PSScriptRoot
try {
    $managedPath = Join-Path $GameDirectory 'How to Fish_Data/Managed'
    if (-not (Test-Path -LiteralPath (Join-Path $managedPath 'UnityEngine.CoreModule.dll'))) { throw 'Répertoire Unity du jeu introuvable.' }
    if (-not (Test-Path -LiteralPath '.tools/bepinex/BepInEx/core/BepInEx.dll')) { throw 'Télécharge BepInEx 5.4.23.5 x64 officiel dans .tools/bepinex avant compilation (voir docs/how-to-fish.md).' }
    Invoke-Dotnet restore src/Sightquill.HowToFish --configfile NuGet.Config
    Invoke-Dotnet build src/Sightquill.HowToFish -c Release --no-restore "-p:GameManagedDir=$managedPath"
    New-Item -ItemType Directory -Force artifacts/HowToFish-Companion | Out-Null
    Copy-Item -LiteralPath src/Sightquill.HowToFish/bin/Release/netstandard2.1/Sightquill.HowToFish.dll -Destination artifacts/HowToFish-Companion/Sightquill.HowToFish.dll -Force
} finally { Pop-Location }
