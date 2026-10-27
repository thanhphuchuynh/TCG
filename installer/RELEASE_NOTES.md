Native Apple Silicon macOS installer for this project's custom plugins.

- Colorful ShopOS menu, creator/reference links and clear loading tips.
- Checks the selected game structure and required dependencies.
- Backs up existing plugin files and offers Restore Previous Files.
- Installs only our `NativeShopMenu.dll`; game files and third-party mod assets are excluded.

Download the **macOS-arm64.zip**, unzip it, then open **TCG Plugin Installer.app**. Read **INSTALL.txt** for requirements and restore instructions.

Requires macOS 13+ for the installer and an existing compatible ARM64 Mono Mac game setup with BepInEx 5.4.23.6, ShopOS 1.1.4 and Enhanced Prefab Loader 6.2.16. Users supply the game and mods. This does not build or convert the base game into a native Mac player.

**Experimental prerelease:** the installer is ad-hoc signed and is not Developer ID signed or notarized. The full game has only been tested locally on the reconstructed player described in the README. Other game/mod versions are unverified.

`SHA256SUMS` records download checksums. Plugin source and payload provenance are included in the repository.
