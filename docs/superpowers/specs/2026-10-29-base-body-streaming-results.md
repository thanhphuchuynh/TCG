# Base-body streaming verification — 29 October 2026

The source implementation moves the eight audited body meshes to the existing
on-demand cache. Source Start.unity remains unchanged; the player build rewrites
224 renderer slots on56 customizers. Six prefab assets bind ownership to their
actual customizer, including the four nested Customer/Worker customizers.

The fresh code review found the nested-owner issue. A native regression reproduced
inactive initialization without geometry, then passed after the ownership fix.
All16 native mesh-runtime cases, native scene/payload/recovery tests,97 Python
fixture tests and8 cache/manifest tests passed. Body preparation also refuses a
stale version1 initialization receipt or changed source/backup bytes.

The corrected isolated player build succeeded with0 errors and1135 warnings
from the recovered project. Independent artifact checks passed:

- All260 mesh bundles match complete original payloads and CRCs; all eight body
  meshes are absent from direct resident player data.
- All961 other resident mesh objects retain their payloads and counts.
- The1902 resident textures retain every inspected field, compressed pixel byte
  and object count:1566356236 image bytes before and after.
- All1478 inspected prior bundle/metadata files remain byte-identical.
- Native checks preserve the six prefabs'24 original renderer/owner identities,
  keys, weights, bounds, bones/root hierarchy, enabled/customizer/LOD state.
- Source-scene bytes, Resources identities, Ultra quality5, Mono/OpenGL/x86/LAA
  and FAT32 file limits pass.

These are build and data-fidelity results. They do not establish playable or
smooth4 GiB gameplay, runtime headroom, cold-spawn timing, save/load behavior or
physical Switch compatibility. Cold mesh loads remain synchronous. The4 GiB
replay and gameplay profiling remain open; no fullgame release or new NSP follows
from these checks.
