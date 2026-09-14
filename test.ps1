. "$PSScriptRoot/scripts/toolchain.ps1"
Push-Location $PSScriptRoot
try {
    Invoke-Dotnet restore tests/Sightquill.Tests --configfile NuGet.Config
    Invoke-Dotnet run --project tests/Sightquill.Tests -c Release --no-restore
    Invoke-Dotnet restore tests/Sightquill.DesktopTests --configfile NuGet.Config
    Invoke-Dotnet run --project tests/Sightquill.DesktopTests -c Release --no-restore
} finally { Pop-Location }
