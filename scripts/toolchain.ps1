$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$localDotnet = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
if (Test-Path -LiteralPath $localDotnet) { $script:Dotnet = $localDotnet }
elseif (Get-Command dotnet -ErrorAction SilentlyContinue) { $script:Dotnet = (Get-Command dotnet).Source }
else { throw 'Installe le SDK .NET 10 : https://dotnet.microsoft.com/download/dotnet/10.0' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools/packages'
# Keep build tooling configuration local; this does not change Windows settings.
$env:APPDATA = Join-Path $projectRoot '.tools/appdata'
New-Item -ItemType Directory -Force $env:APPDATA | Out-Null
function Invoke-Dotnet {
    & $script:Dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet a échoué (code $LASTEXITCODE)." }
}
