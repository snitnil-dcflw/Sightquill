param([string]$Compiler, [string]$SourceDirectory)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (!$Compiler) {
        $candidates = @("$PSScriptRoot/.tools/inno/ISCC.exe", "${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe", "$env:ProgramFiles/Inno Setup 7/ISCC.exe")
        $Compiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (!$Compiler) { $Compiler = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
        if (!$Compiler) { throw 'Install Inno Setup or pass -Compiler with the ISCC.exe path.' }
    }
    if (!$SourceDirectory) { $SourceDirectory = (Get-Content artifacts/release-stage.txt -Raw).Trim() }
    if (!(Test-Path "$SourceDirectory/Sightquill.exe")) { throw 'Run publish.ps1 first.' }
    $version = ([xml](Get-Content src/Sightquill/Sightquill.csproj)).Project.PropertyGroup.Version | Select-Object -First 1
    & $Compiler "/DAppVersion=$version" "/DSourceDir=$SourceDirectory" packaging/Sightquill.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $files = @("artifacts/Sightquill-Setup-$version-win-x64.exe", "artifacts/Sightquill-$version-win-x64.zip")
    $files | ForEach-Object { (Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path $_ -Leaf) } | Set-Content "artifacts/Sightquill-$version-SHA256SUMS.txt"
} finally { Pop-Location }
