# Complete native Mac app validation — 2026-10-27

The delivered local app contains the native ARM64 Unity Mono/Metal player and all 11 installed production mod folders. BepInEx loaded 11 production plugins, including the three plugins in NativeShopMenu.dll and the existing save exception logger. The final app opened to the Pokémon-themed main menu with New Game, Load Game, Setting and Quit; no startup overlay remained. A live second-launch attempt refused safely.

- 24 runtime fixture assertions passed: staged preparation, failed-copy cleanup, lock handling, insufficient space, repeated preparation/settings preservation, moved-app reuse, safe arguments, immutable-file changes, symlink refusal, unready-folder preservation and settings-only rebuild compatibility.
- Three packaging regression tests passed, covering immutable same-size content changes, config-independent identity and ZIP64 offsets/large members/CRC/executable modes.
- The arm64 launcher and complete app passed strict deep ad-hoc signature verification. Post-launch verification confirmed the immutable app remained valid.
- Nine existing game save files retained identical SHA-256 hashes. Seven personal mod slot-state files were excluded from the app and separately transferred into the writable runtime without overwrites; all seven still matched their originals after startup.
- The 7,582,081,499-byte ZIP passed complete CRC validation, 864 payload-file size checks and 245 critical program hashes. Its 5,491,027,952-byte resource asset retained its full ZIP64 size, launcher executable permissions were retained, and archived launcher/signature/Info.plist bytes matched the verified app.
- The archive SHA-256 is `4e97551d8ac99db151c406033bc7c83ee37b00923e1b4de1cbbe12c17995622b`.

The native archive tools initially produced invalid large-member/offset metadata. Those generated archives were removed. The delivered archive was recreated with Python's ZIP64 writer; the reusable builder now uses the same writer and verifies CRCs.

The original native-build was retained. The superseded smoke-test runtime and temporary compiler outputs were removed; a small backup of that test runtime's mod settings remains in local validation evidence. The full game package stays local and is ignored by Git. No full-game GitHub release was created.

## Limits

This session verified packaging and menu startup, without loading a shop save or repeating every earlier gameplay/mod test. Steam was not running, so ordinary Steam initialization reported failure; ownership checks and initialization behavior were preserved. The app requires Apple Silicon and macOS 26+, is ad-hoc signed and has no Developer ID notarization.

Two minor review findings remain: launch rechecks about 849 MB of program files on the main thread after background preparation, which can briefly pause the launcher; missing-file errors can show a basename instead of the full runtime-relative path. Runtime settings are preserved on failure in both cases.
