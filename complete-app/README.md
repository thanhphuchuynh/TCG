# TCG Card Shop Simulator Modded — complete local Mac app

Open **TCG Card Shop Simulator Modded.app**. On its first launch, it prepares the native game and all installed production mods in `~/Library/Application Support/TCG Modded/`. Keep about 12 GB free for preparation; subsequent launches reuse that runtime and retain mod settings. The app opens the modded game directly, without Terminal.

Requires **Apple Silicon and macOS 26 or newer**. Both the launcher and game run natively. Use your ordinary Steam account and ownership; original Steam initialization is preserved. Startup can take about a minute while the mods load.

The app includes the existing native player, BepInEx/Doorstop, Pokémon overhaul assets, ShopOS, Enhanced Prefab Loader/API, TextureReplacer, Collection Tracker, Holographics, Configuration Manager, installed grading/phone assets, our menu/credits/loading-tip plugin and the existing save exception logger. Installed assets are included even when they have no separate plugin DLL. Switch experiments, caches and test probes are excluded.

Game saves keep their existing location: `~/Library/Application Support/OPNeonGames/Card Shop Simulator/`. Saves and per-slot mod state are not embedded in the app. For this local build, existing ShopOS slot state and TextureReplacer custom placements are migrated separately into the prepared runtime without overwriting existing files. The original working build remains intact.

Runtime logs: `Launcher.log` within the versioned runtime folder, plus `~/Library/Logs/OPNeonGames/Card Shop Simulator/Player.log`. `BepInEx/LogOutput.log` is also available if disk logging is enabled. Preparation failures leave the existing ready runtime and saves alone. An integrity error names a missing/modified program file; preserve the runtime folder and its settings before attempting repair. To open another copy, quit the current game first.

This is an experimental local reconstruction, ad-hoc signed without Apple Developer ID signing or notarization. Keep the app on your writable Mac disk rather than running it from a read-only disk image. The bundled payload stays unchanged while mods write to the prepared runtime. First-run crashes or power loss can leave a `staging-*` folder under Application Support; only remove an abandoned staging folder after the game/launcher has stopped.

Full game and third-party mod content stays local. Public plugin source/credits: https://github.com/thanhphuchuynh/TCG

## Rebuild using files you supply

```sh
python3 complete-app/build.py --game-dir /path/to/native-build --output-dir /path/to/local-release
```

The build validates native architecture and signatures, runs runtime tests, clones/copies the game and all installed mods, excludes personal slot state, compiles the native launcher and signs/verifies the complete app. `--zip` additionally makes a local archive and checksum. This tool never uploads its payload.
