# Local asset streaming experiment

These are source-only tools for an isolated, user-supplied Unity project. They
contain no game or mod files. The target is the existing Windows x86 player under
Autorun, not a native Switch export. A successful build does not establish a
playable Switch port.

`prepare_project.py PROJECT` backs up and applies guarded ownership hooks in the
private recovered project. It computes every replacement before writing sources
and is safe to repeat. Also run `prepare_character_meshes.py PROJECT` for the two
guarded character-part Instantiate hooks. Copy Runtime and Editor sources into
the project. Also run `prepare_surfaces.py PROJECT` for the 24 original shop
finish setters. All three preparation receipts are required by the combined build.

The Unity batch entry point `TCG.SwitchStreaming.Editor.StreamingBuild.Run` is
restricted to the prepared `switch-on-demand/macos-port-work` clone. Start Unity
2021.3.45f2 with `-buildTarget Win`. It audits direct references and Resources
roots, builds one LZ4 bundle per eligible texture using original Sprite assets,
strips only verified catalogue/default-card references, and builds direct
Title/Start scenes. Character retries preflight prefab GUIDs and original/prepared hashes before restoring
any backed-up prefab. Mixed known original/prepared states recover safely; an
unrecorded interrupted write or authored edit refuses recovery for inspection.
The legacy path-only character journal is rejected. The older artwork preparation
still uses its existing isolated-clone backup flow.
The character audit allows only renderer Mesh references in the exact hair/apparel
choice prefabs. Eligible original Mesh assets move out of Resources with GUIDs
preserved; prefab names, choice lists and materials remain resident. One Mesh per
LZ4 bundle is acquired synchronously before the original appearance setup. Disable
clears references before unpinning; reenable restores current appearance data.
The 128 MiB mesh cache budget is a soft accounting estimate, not measured headroom.

Unity's mesh-only bundle pipeline resets `m_MeshUsageFlags`; the build restores the
exact captured source value with `preserve_mesh_flags.py`. It does not infer flag
semantics or omit the field from validation. Configure absolute paths in
`TCG_STREAMING_PYTHON` (Python with UnityPy available) and `TCG_MESH_FLAG_TOOL`
(the preservation script). The helper verifies every other serialized payload and
metadata field before atomically replacing a local LZ4 bundle. The helper computes the
post-restoration decompressed-data CRC, which native Unity load tests verify.
The runtime catalogue uses it; obsolete build sidecars/hashes are discarded.

Current resolution, compression, sampler, authored geometry and Ultra settings
stay unchanged. Reports and game outputs belong outside this public repository.

The cache deduplicates shared artwork and pins active/pending consumers. Inactive
cards release their image references before the final lease. Pack preloads and
album/deck gates wait for complete bindings; errors stay local. A resident copy
of the identifier-only manifest detects missing or altered external metadata.

## Verification

Run `sh Tests/run-host-tests.sh` and
`python3 -m unittest discover -s Tests -p 'test_*.py' -v` from this directory.
The cache runner uses the installed Unity Mono compiler without package restores.

For real Unity tests, use a separate empty 2021.3.45f2 project. Copy `Runtime/*.cs`
and `Tests/Unity/{StreamingProbe,CardUI}.cs` under Assets, and `Editor/*.cs` plus
the other `Tests/Unity/*.cs` into Assets/Editor. Provide the standard Unity UI
assembly from the local Unity installation/player. Run batch mode with
`-executeMethod StreamingSuite.Run`; the harness exits and writes
`unity-test-result.txt`. These fixtures create their own tiny test images only.
Do not copy the test sources into the real game project.

`validate_artwork.py --baseline DATA --player DATA --report REPORT --output JSON`
uses UnityPy to compare compressed texture payloads and authored sprite geometry,
check resident leakage, mesh duplication, quality and FAT32 limits. It fails for
missing or ambiguous comparisons. It is read-only; its private reports must not
be uploaded with the game assets.

`localization_probe.py PROJECT` is an optional investigation tool for the separate
startup initialization exception. It preserves the original source in
`.tcg-localization-probe`, adds five no-inline boundaries and bounded primitive
stage markers, and logs then rethrows the original exception. It supplies no
fallback language or game-state bypass. Do not treat installing the probe as a
startup fix.

`validate_meshes.py --baseline DATA --player DATA --report MESH_REPORT --output JSON`
checks the complete authored Mesh payload, resident leakage and retained meshes,
Resources prefab identities, quality settings and one-Mesh bundle contents/CRCs.
Duplicate authored names require complete per-name payload-set coverage; baseline
player files do not identify source GUIDs. The controlled builder enforces source
GUID/address/renderer association separately. This limitation is in each report.
Run both artwork and mesh audits; neither establishes emulator/hardware gameplay.

Additional actual Unity entry points are `MeshRuntimeTests.Run`,
`TCG.SwitchStreaming.Tests.MeshReferenceAuditTests.Run`, `CharacterBuildTests.Run`
and `CharacterRestoreTests.Run`; use separate fixture
projects so their generated assets never enter the game. Preservation tests need
UnityPy and the synthetic mesh fixture (`TCG_MESH_FIXTURES` points at its project).

Finish textures use `SurfaceTextures.SetTexture` and a 64 MiB soft cache. The
original data getter runs first; the hook maps its actual returned list index,
preserving the game's high-index clamp. Non-null resident/runtime mod overrides
remain intact. Material slots share pins; disabled managers retain them because
world renderers live elsewhere. Destroyed owners or materials release cache pins,
and borrowed material/fallback properties survive teardown.

Only exclusive floor/wall/ceiling catalogue texture fields are stripped. Existing
material defaults and all unknown references stay resident. The catalogue and
source preparation use versioned original/prepared hashes and GUIDs; ambiguous
or edited backups/receipts are rejected. No automatic legacy receipt adoption.

`validate_surfaces.py --baseline DATA --player DATA --report REPORT --output JSON`
compares against the previous character-streaming player, including complete
Texture2D fields/pixels, retained textures, mesh payloads, quality and Resources.
Run the artwork/mesh validators as well. Native Unity's chunked LZ4 output can
contain raw fallback blocks for incompressible data; those preserve the same CRC
and raw texture bytes. Full uncompressed/LZMA payloads remain unsupported.
