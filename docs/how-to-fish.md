# How to Fish integration

## Enable tracking

1. In Sightquill, select How to Fish under Game & display.
2. Open Set up. Steam libraries are detected automatically; use Choose game folder if needed.
3. Save your game and close it before clicking Install / update companion.
4. Start How to Fish normally, then enable your crosshair with the button or Ctrl+Alt+X.

Switch to Universal to stop using game telemetry and return to a screen-centered overlay. New Sightquill installations default to Universal; existing mode preferences are retained. The crosshair always starts disabled.

## Start Sightquill after the game

Once the companion is installed and loaded by the game, either launch order works. You can also close and reopen Sightquill while How to Fish stays running.

1. Start or resume How to Fish.
2. Open Sightquill and select How to Fish under Game & display.
3. Click Enable crosshair, or press Ctrl+Alt+X.
4. Return to the game. The crosshair hides while the editor or a game menu has focus, and returns during gameplay.

The companion retries its connection automatically. Activation is separate from the connection: Sightquill starts with the crosshair off, even if the game is already running. First-time companion installation or a disabled loader requires restarting the game; starting the desktop app cannot load a missing companion into an existing game process.

## Installation and removal

The companion package is beside Sightquill.exe under Integrations/HowToFish. Extract the whole application ZIP before setup.

Setup uses the official [BepInEx 5.4.23.5 x64 release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5). If no loader exists, it downloads BepInEx_win_x64_5.4.23.5.zip and verifies SHA-256 82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4 before extracting. An existing BepInEx 5 installation is reused. Conflicting files are preserved and reported.

The plugin is installed as BepInEx/plugins/Sightquill/Sightquill.HowToFish.dll. Original game assemblies and saves are not replaced. The setup journal is BepInEx/config/Sightquill.installation.json. Previous companion files are backed up under BepInEx/config/Sightquill-backups.

To remove it, close the game, open Set up and click Remove companion. Only recorded, unchanged files are removed. Shared loader files are retained when other plugins or patchers are present. When adopting an older manually installed companion, its pre-existing loader remains untouched.

## Behavior

The companion samples the final weapon pose each game frame and projects its nominal firing axis onto the first surface ahead. Full sniper scope aiming uses the camera direction. Without a weapon, the marker indicates camera center. It does not predict random spread, bullet drop or target motion.

The marker hides in menus, while dead/inactive, outside the game, outside the viewport, and when samples expire. The enabled state stays on during temporary hiding. The status explains what is happening.

Communication uses a local Windows named pipe restricted to the same user. No network port is opened. Sightquill checks sender process identity, frame order and freshness. A game update can change the internals the companion reads; unsupported states hide the marker rather than retaining stale positions.

The loader log is BepInEx/LogOutput.log in the game folder. The companion does not modify weapon behavior, recoil, dispersion or physics, and does not control the mouse or keyboard.
