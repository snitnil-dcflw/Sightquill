. "$PSScriptRoot/scripts/toolchain.ps1"
Push-Location $PSScriptRoot
try {
    Invoke-Dotnet restore src/Sightquill --configfile NuGet.Config
    Invoke-Dotnet build src/Sightquill -c Release --no-restore
} finally { Pop-Location }
