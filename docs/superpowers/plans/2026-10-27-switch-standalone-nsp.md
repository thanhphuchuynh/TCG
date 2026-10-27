# Standalone Switch Compatibility NSP Build

User requests one standalone NSP, no console test, copied to their OneDrive temp folder. Keys are provided locally and must never be printed, committed or embedded. Existing clean Windows-x86/Mono/OpenGL TCG build and pinned Autorun test-build-4 are the payload; this is a compatibility package, not a native Unity Switch export.

1. Build an MIT nx-hbloader-derived ExeFS with a first-run RomFS-to-SD preparation step. Use staged, checksummed extraction; preserve any existing unknown Autorun installation. Launch the bundled runtime with the game selected. Host-test interruption recovery, repeated preparation, path bounds and preservation; cross-compile only source in GitHub Actions (no keys or game assets in CI).
2. Locally stage the verified player/runtime and a manifest. Create control metadata and native game icon, then program/control/meta NCAs and an NSP using the user's key file through a local, filtered, private temporary keyset. Keep logs free of key values. Preserve original game visuals, assets and normal Steam initialization.
3. Independently verify PFS0 bounds, NCA headers, FS integrity, ExeFS/NPDM identity, CNMT content hashes, NACP and embedded payload hashes. This is structural verification only; do not claim console playability.
4. Copy only the verified NSP and checksum to the requested OneDrive temp directory, refuse unrelated-file overwrites, verify destination hash, and provide launch/storage notes.

No firmware, boot configuration, signature settings or console files are changed by this offline build. A HOME-installed unsigned homebrew title needs an already-compatible CFW setup. First launch needs about 4.6 GB extra SD space, separate from installed NSP storage. The existing native macOS build and saves stay unchanged. Base-game compatibility is untested on hardware; Pokémon/mod packaging remains a separate phase from the previously approved base-first Switch experiment.
