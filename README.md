<h1><img src="docs/images/sightquill-logo.png" alt="" width="48" height="48" align="absmiddle"> Sightquill</h1>

A native Windows crosshair overlay with 24 presets, transparent PNG and game-code imports, and an optional How to Fish companion. English interface. No account or telemetry.

## Install

Download **Sightquill-Setup-0.6.1-win-x64.exe** from [Releases](https://github.com/snitnil-dcflw/Sightquill/releases/latest). Run it, choose an installation folder and optionally create a desktop shortcut. Installation is per user and does not require administrator privileges. The .NET runtime is included.

A portable ZIP is also available: extract the complete archive and launch Sightquill.exe. SHA-256 checksums accompany each release. This release is not code-signed; Windows may show an unknown-publisher or SmartScreen prompt.

## VirusTotal checks

Look up the exact 0.6.1 release files by their SHA-256 fingerprints:

- [Windows installer — VirusTotal lookup](https://www.virustotal.com/gui/file/696f3657900192a8528c826eb81938749beb0f070a8345c12b3f8d546a5173bc)
- [Portable ZIP — VirusTotal lookup](https://www.virustotal.com/gui/file/f1b76dc8e32d264ca2e1d9a5b9bb2437b77d957733a0ce3c3d24e77c51dea340)

**Status: scan results have not been verified by the project.** These are file lookups, not a clean-scan badge. If VirusTotal has no report, upload the matching file from [Releases](https://github.com/snitnil-dcflw/Sightquill/releases/tag/v0.6.1) for analysis. Compare its hash with the release SHA256SUMS file. Antivirus results do not guarantee that a file is safe.

## Screenshots

The studio brings the preset library, live preview and crosshair controls together.

![Sightquill studio with the crosshair library, live preview and customization controls](docs/images/sightquill-studio.png)

<details>
<summary>Color wheel and crosshair imports</summary>

Choose a custom color with the wheel and brightness control.

![Sightquill color wheel and brightness control](docs/images/sightquill-color-wheel.png)

Import transparent PNGs, Sightquill JSON profiles, or Valorant / Counter-Strike codes, and preview the result before adding it.

![Crosshair import dialog showing a Valorant code and its preview](docs/images/sightquill-import.png)

</details>

## Use

1. Choose a preset or import a transparent PNG, Sightquill JSON, Valorant Primary code, or CS:GO / CS2 share code.
2. Adjust color, size, opacity and placement. Save named profiles to keep them.
3. Select Universal for a display-centered overlay. Use windowed or borderless fullscreen; exclusive fullscreen is not guaranteed.
4. Enable the crosshair, or press **Ctrl+Alt+X**. It always starts disabled.

Minimizing to the tray keeps the application running. Launching Sightquill again restores the editor. Closing the application also closes the overlay. The overlay passes clicks through to the game.

### Crosshair imports and animation

PNG input: transparent background, up to 2 MB / 2048 x 2048 pixels, embedded at up to 256 x 256. JSON preserves settings and embedded images. PNG export captures the resting appearance; game share-code export is not supported.

Valorant imports preserve Primary inner/outer lines, dot, outlines, layer movement/firing switches, multipliers and simulated firing fade. Animated codes enable Dynamic automatically. Use Preview animation to inspect movement, firing and recovery. Reimport codes imported before 0.6 to recover previously discarded animation settings.

Dynamic uses ZQSD/WASD and the left mouse button in How to Fish, Valorant or CS:GO / CS2. It simulates input; it does not measure weapon accuracy, recoil, velocity or ADS. Game chat/menu input can also trigger animation in Universal mode. Native and PNG crosshairs use generic animation. JSON sharing preserves animation parameters; recipients need 0.6 or later for Valorant layer rules.

Scale with resolution uses a 1080p reference and is independent of Windows UI scaling. Disable it for fixed physical pixel dimensions. Imported crosshairs default to fixed pixels.

### Optional How to Fish tracking

Official release packages include the companion under Integrations/HowToFish. Choose How to Fish, open Set up, close the game, and install the companion. The official BepInEx loader is downloaded and hash-verified only when requested. No game assemblies are distributed. See [the integration guide](docs/how-to-fish.md).

The companion follows the nominal aim direction, not random spread or bullet drop. It hides stale telemetry and menus. Other games use the external overlay. This project is not affiliated with the game publishers; compatibility with game updates and anti-cheat systems is not guaranteed.

## Data and uninstall

Preferences and profiles: `%LOCALAPPDATA%\Sightquill\settings.json`. Crash log: `%LOCALAPPDATA%\Sightquill\error.log`. There is no account, telemetry or keystroke history.

Uninstall Sightquill through Windows Installed apps. Preferences are retained. Remove the optional game companion through Set up **before** uninstalling Sightquill; application uninstall does not alter game folders.

## Build from source

Windows x64, PowerShell, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
./build.ps1
# Close any running Sightquill before the interactive desktop tests.
./test.ps1
./publish.ps1
./build-installer.ps1 -Compiler 'C:/Program Files/Inno Setup 7/ISCC.exe'
```

The default publish builds Universal functionality without requiring a game installation. The installer compiler is [Inno Setup](https://jrsoftware.org/). Outputs are under artifacts; binaries are distributed through Releases, not committed to Git.

To include the optional companion, extract the official BepInEx 5.4.23.5 x64 SDK into `.tools/bepinex` and supply your own installed Unity Mono game:

```powershell
./publish.ps1 -GameDirectory 'X:/SteamLibrary/steamapps/common/How to Fish/How to Fish'
./build-installer.ps1
```

The build uses game libraries only as non-copied references. Do not upload game DLLs, downloaded SDKs, personal profiles or signing keys. CI builds/tests the application and publishes a Universal portable artifact; official companion-inclusive release packages are built with local game references.

## Contributing and license

Bug reports should include the app version, display resolution/scaling, reproduction steps and crosshair code where applicable. See [CONTRIBUTING](CONTRIBUTING.md).

Sightquill is licensed under [MIT](LICENSE). See [third-party notices](THIRD-PARTY-NOTICES.md) for dependencies and trademarks.
