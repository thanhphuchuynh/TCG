# TCG — experimental macOS compatibility work

Source for custom plugins and compatibility tools developed during a local Apple Silicon reconstruction of **TCG Card Shop Simulator**.

This repository is **not a downloadable copy of the game or a complete standalone Unity project**. Recovered game code, game assets, third-party mod binaries, native player binaries and saved games are not included. Building these plugins requires a compatible local player and the original dependencies.

## Included

- **Native Shop Menu:** colorful six-column ShopOS home layout, larger labels, compact status bar and footer, with the original app handlers retained.
- **Credits:** creator names and ten original project links, registered through ShopOS's public screen API.
- **Clear Loading Tips:** eight short gameplay tips replacing EPL's rotating joke list; percentage handling is unchanged.
- **Compatibility helpers:** scoped macOS path/native-tool patches, verification utilities and pure helper tests. Original dependency DLLs are not distributed.
- **Optional diagnostics:** a save-loading exception logger, plus a Unity editor callback for an icon supplied locally.

## macOS installer

Download the [latest plugin installer prerelease](https://github.com/thanhphuchuynh/TCG/releases). It installs our menu, credits and loading-tip plugin into a compatible native Mac game setup supplied by you, with backups and restore. The installer requires Apple Silicon and macOS 13+. It is ad-hoc signed, without Developer ID signing or notarization. See [installation instructions](installer/INSTALL.txt). It does not port the base Windows game.

The only bundled DLL is our own compiled plugin in `installer/Payload`. Its manifest records the payload and public source hashes. Third-party reference assemblies are excluded. Rebuild it with `tools/build.py`, then refresh provenance with `python3 installer/prepare_payload.py --dll /path/to/NativeShopMenu.dll`. Build and test the installer using `python3 installer/build.py` on a Mac with Xcode command-line tools.

## Build a complete local Mac app

The [complete-app builder](complete-app/README.md) packages a native Apple Silicon game setup and installed mods supplied by you into one `.app`. Its launcher prepares a writable runtime in Application Support, preserves mod settings on repeat launches, and starts the modded game without Terminal. The current game requires macOS 26+.

```sh
python3 complete-app/build.py --game-dir /path/to/native-build --output-dir /path/to/local-release --zip
```

The complete game and mod payload stay local. This repository contains the launcher/build source and [verification notes](complete-app/VALIDATION.md); the public release remains the plugin-only installer.

## Build

The tested environment uses Unity **2021.3.45f2**, an **ARM64 Mono/Metal** Mac player, BepInEx **5.4.23.6**, ShopOS **1.1.4**, and Enhanced Prefab Loader **6.2.16**. Private member layouts are version-sensitive; the menu adapter checks its bindings and pinned drag constants.

```sh
python3 tools/build.py   --game-dir /path/to/native-build   --unity-editor /Applications/Unity/Hub/Editor/2021.3.45f2/Unity.app
```

`--game-dir` must contain `TCG Card Shop Simulator.app` and its `BepInEx` directory. The script compiles only our plugin and runs the geometry, drag-adapter and focus-scroll checks. It does not create the base Mac player, fetch game files, install dependencies or deploy to the game.

To add an icon, supply your own square PNG using `--icon /path/to/icon.png`. No game artwork is hosted here. Output is written to `out/` by default.

## Install or remove

Quit the game, copy `out/NativeShopMenu.dll` into `BepInEx/plugins/NativeShopMenu/`, and optionally place your supplied PNG there as `CreditsIcon.png`. Launch using your existing mod launcher. Open the shop and press **Tab**; the Credits tile opens the reference links.

To remove these UI extensions, quit the game and remove that plugin folder. Keep the original game and dependencies installed. Compatibility patch tools under `tools/compatibility/` are separate; read their notes before using them.

## Validation and limits

The local UI build passed 1,650 layout assertions, a 15-operand check against the installed ShopOS drag implementation, five focus-scroll regressions, and local runtime UI checks. See [validation notes](docs/VALIDATION.md).

The full game reconstruction is experimental. This source checkout does not reproduce that complete build. Physical controller behavior and arbitrary mod/version combinations are not verified. A previously reported save-loading failure was not reproduced in two controlled loads; the optional logger records the hidden exception rather than claiming to fix every save.

The Switch work remains an **untested Autorun compatibility experiment**, not a native Switch port or a verified playable release. No Switch package is published here.

## Credits and upstream projects

[TCG Card Shop Simulator / OPNeon Games](https://store.steampowered.com/app/3070070/TCG_Card_Shop_Simulator/), [Pokémon Shop / Real TCG Overhaul](https://www.nexusmods.com/tcgcardshopsimulator/mods/577), [ShopOS / Evers665](https://www.nexusmods.com/tcgcardshopsimulator/mods/1754), [Enhanced Prefab Loader / GhostNarwhal](https://www.nexusmods.com/tcgcardshopsimulator/mods/496), [EPL API](https://www.nexusmods.com/tcgcardshopsimulator/mods/1144), [TextureReplacer / Shaklin](https://www.nexusmods.com/tcgcardshopsimulator/mods/69), [Collection Tracker](https://www.nexusmods.com/tcgcardshopsimulator/mods/867), [RTCGO Holographics](https://www.nexusmods.com/tcgcardshopsimulator/mods/1676), [BepInEx](https://github.com/BepInEx/BepInEx).

Names and source links credit the upstream projects; this repository does not grant permission to redistribute their files. Obtain the original game and mods from their owners. This is unofficial community work.
