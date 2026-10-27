# Complete native macOS game app

## Intent
Build a local, self-contained native Apple Silicon app containing the existing reconstructed game and every installed production mod. The user requested the whole game app rather than the plugin-only installer. Preserve the current visuals, menu, loading tips and credits.

## Approaches
1. Plugin installer: small, but requires an existing separate game/mod installation. This was released as v0.1.0 and does not satisfy the latest request.
2. Complete app: embeds the current native game and all production mods. This matches the user's request to include everything. Recommended implementation: a native launcher prepares a writable runtime on first launch and starts the existing mod launcher without Terminal.

## Proposed implementation
- Local output: `local-release/TCG Card Shop Simulator Modded.app`, plus a compressed local archive if space permits.
- Native arm64 launcher, existing native Unity Mono/Metal game, and existing BepInEx/Doorstop startup. No Windows compatibility runtime.
- Include the existing game app, BepInEx core, production plugins, mod artwork/assets, current mod configurations, Doorstop library, startup script and Steam app ID.
- Exclude Switch experiments, caches, logs, diagnostic probes, recovery projects and user save files.
- Keep immutable payload inside the app. On first launch, prepare an independent writable runtime under `~/Library/Application Support/TCG Modded/`. This avoids changing the signed app when mods update configuration or custom objects. Stage the copy before moving it into place. The builder chooses progress details and safe disk-space checks.
- Launch the current production script from that runtime without opening Terminal. Preserve ordinary Steam initialization and ownership checks.
- Keep existing game save behavior and bundle identity; do not delete or overwrite saves. Do not start a new game during smoke testing.
- Preserve the existing original game icon and name, with a clear Modded suffix for the outer launcher.
- Ad-hoc sign and verify the complete local app. No Apple Developer ID or notarization is available. Current game requirements remain Apple Silicon and macOS 26+.

## Validation
Check payload inventory and native architectures; build/sign the launcher; verify initial signature; test staged preparation and repeated startup; confirm BepInEx production plugins load and the game reaches its normal menu. Confirm mutable runtime changes leave the outer app signature valid. Prior gameplay validation remains documented; do not claim exhaustive retesting of every mod.

## Publication
Build the complete app locally. Do not upload commercial game files or third-party mod assets to the public repository/release without established redistribution permission. The existing public plugin installer stays available.

## Storage
Bundled payload is approximately 11 GB. First-launch runtime needs approximately another 11 GB before filesystem compression or cloning. Preserve the current working build as a fallback. No automatic deletion of existing builds or saves.
