param([string]$GameDirectory, [string]$OutputDirectory = 'artifacts/Sightquill-win-x64')
. "$PSScriptRoot/scripts/toolchain.ps1"
Push-Location $PSScriptRoot
try {
    $version = ([xml](Get-Content src/Sightquill/Sightquill.csproj)).Project.PropertyGroup.Version | Select-Object -First 1
    $stage = Join-Path $PSScriptRoot ('.tools/release-' + [guid]::NewGuid().ToString('N'))
    Invoke-Dotnet restore src/Sightquill -r win-x64 --configfile NuGet.Config '-p:SelfContained=true' '-p:PublishSingleFile=true'
    Invoke-Dotnet publish src/Sightquill -c Release -r win-x64 --self-contained true --no-restore -o $stage '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:EnableCompressionInSingleFile=true' '-p:DebugType=None' '-p:DebugSymbols=false'
    Copy-Item README.md,CONTRIBUTING.md,LICENSE,THIRD-PARTY-NOTICES.md -Destination $stage
    New-Item -ItemType Directory -Force "$stage/docs","$stage/licenses/dotnet" | Out-Null
    Copy-Item docs/how-to-fish.md "$stage/docs/"
    Get-ChildItem $env:NUGET_PACKAGES -Recurse -File -Include LICENSE,LICENSE.TXT,THIRD-PARTY-NOTICES.TXT | Where-Object { $_.FullName -match 'microsoft\.(netcore|windowsdesktop)\.app\.runtime\.win-x64' } | ForEach-Object {
        $package = $_.FullName.Substring($env:NUGET_PACKAGES.Length + 1).Split([IO.Path]::DirectorySeparatorChar)[0]
        Copy-Item $_.FullName (Join-Path "$stage/licenses/dotnet" ($package + '-' + $_.Name))
    }
    if (!(Get-ChildItem "$stage/licenses/dotnet")) { throw 'Runtime license notices not found.' }
    if ($GameDirectory) {
        & "$PSScriptRoot/build-companion.ps1" -GameDirectory $GameDirectory
        New-Item -ItemType Directory -Force "$stage/Integrations/HowToFish" | Out-Null
        Copy-Item artifacts/HowToFish-Companion/Sightquill.HowToFish.dll "$stage/Integrations/HowToFish/"
    }
    # Compile installers against this clean staging directory, never stale build files.
    New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
    Copy-Item "$stage/*" $OutputDirectory -Recurse -Force
    Set-Content artifacts/release-stage.txt $stage
    Compress-Archive -Path "$stage/*" -DestinationPath "artifacts/Sightquill-$version-win-x64.zip" -Force
    Get-FileHash "artifacts/Sightquill-$version-win-x64.zip" -Algorithm SHA256
} finally { Pop-Location }
