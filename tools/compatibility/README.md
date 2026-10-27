# Scoped compatibility tools

These are standalone C# sources for local analysis/patching of dependency DLLs you supply. They are not the original dependency sources or binaries. Compile helpers with the matching Unity Mono C# compiler; patch/verification tools additionally require Mono.Cecil supplied locally (for example, the version shipped with the tested BepInEx install).

- `shopos-patch/`: native tool-path resolution, six scoped ShopOS dispatch changes, an external-tool update guard and regression verification. The patcher rejects an unexpected original DLL hash and in-place output; keep your original file. This targets the exact original ShopOS 1.1.4 input used in the investigation, not an arbitrary current Nexus download.
- `path-audit/`: Windows-literal/macOS-path dispatch, TextureReplacer/EPL path patching, structural checks and pure path fixture tests.

Run each command-line program only with the input/output arguments its `Main` method specifies. Read the source and work on copies; no patched third-party DLL is distributed. These helpers do not change Steam authentication or provide a Steam emulator.
