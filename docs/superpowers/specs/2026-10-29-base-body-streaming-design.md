# Base-body mesh loading: implementation of the approved on-demand design

This extends the approved `2026-10-28-switch-on-demand-design.md` instruction to
profile the next persistent catalogue when earlier migrations are insufficient.
The user also authorized game-logic optimization. Rules, saves and current visuals
stay the same. The implementation choices below are the builder's, not additional
preferences attributed to the user.

## Evidence and purpose

The latest 4 GiB emulator run displayed100% loading but stopped before a usable
shop; the Player log reached97%. Native heap peaked at3175 MiB with8 MiB free.
Unique live NvMap source ranges in the physical heap totalled1194.625 MiB;
anonymous guest backing requests were about1.5 GiB and JIT arenas112 MiB.
These overlapping/incomplete categories are not an additive RAM accounting.
The rebuilt emulator and its long title idle are comparison confounds, so this
is not a proven performance fix. Preserve both the original and diagnostic apps.

A native Unity audit accounts for eight standalone body Mesh assets,248 exact
SkinnedMeshRenderer.m_Mesh references,62 customizers, and seven source files:
Start.unity (224 references/56 inactive customizers), Customer/Worker male/female
prefabs, and male/female Resources base prefabs (four references each). No other
native direct dependency was found. Every serialized GUID occurrence matches an
inspected renderer slot. All source bytes stayed unchanged. The eight meshes
have authored usage flag1, with147 blendshapes in each LOD0/1 and one in LOD2/3.
Historical serialized geometry is307544400 bytes; editor estimated memory is
320437176 bytes. Neither is a measured application saving.

Private proofs: `switch-on-demand/basebody-native-audit.json`,
`basebody-reference-accounting.json`, and `body-scene-spike/payload-proof.json`.
The synthetic Win32 player proves IProcessSceneWithReport removes a mesh from
an inactive renderer's player dependencies while retaining the control mesh,
its serialized ownership/renderer/bounds/weight42, and the source scene hash.

## Approaches and choice

1. Reuse the exact-mesh cache/owner and rewrite scene instances only during the
   player build. This keeps source-scene lighting and object identities intact,
   uses the established payload proofs, and removes unused eager geometry.
   This is the selected implementation under the already-approved architecture.
2. Stream whole character prefabs. This would require another dependency policy
   for shared materials, scripts, skeletons and Resources identities. It is a
   larger change than the measured eight-mesh graph needs.
3. Change allocator or GPU-cache behavior. Current observations establish memory
   pressure but do not identify a safe allocator/driver semantic fix. Keep this
   as a separate evidence-led investigation.

## Runtime contract

Keep every original mesh byte, usage/readability flag, LOD, bindpose, skin bone,
blendshape, bounds, renderer/material property, hierarchy and save/RNG identity.
No lower quality, fewer characters, altered rules, hidden required content, or
GC/suspension bypass. The existing 381 art,252 choice-mesh and229 surface bundles
remain unchanged. FEX upstream source and patches remain outside our edits.

Reuse MeshBundleOwner/MeshService and the shared128 MiB soft cache; active pins
win. Body meshes use the existing mesh:<GUID> keys and mesh_<GUID> filenames.
The combined manifest contains the original252 entries plus exactly eight body
entries, with identical resident and SD copies. Each bundle contains one original
Mesh and no other payload/dependency, using the existing exact usage-flag/CRC proof.

Attach an owner to each body customizer root. Preserve all four authored LOD
bindings for that gender. Inactive scene roots must not acquire anything merely
because the scene is deserialized. Before the original Initialize() first parses
BlendshapeManager names, prepare only that customizer's root owner. Do so before
its m_HasInit early branch too. The original initialization, preset selection,
RNG, saved fields and subsequent setup order remain unchanged. OnEnable restores
complete geometry before display; OnDisable clears owned Unity references before
unpinning. Reuse preserves the last weights, bones and bounds. Existing one-frame
inactive ownership cleanup handles Initialize calls on an inactive actor.

Cold loads remain synchronous in this first ownership migration. This is an
explicit temporary limitation, not the smooth-gameplay acceptance result. Measure
cold actor activation; introduce asynchronous prefetch only with an independently
verified scheduling contract if measurements require it.

## Preparation and recovery

Do not save or rewrite Start.unity. A guarded IProcessSceneWithReport callback
rewrites only its audited body slots in the in-memory build scene. It verifies
source identity/hash, target platform, prepared journal and binding accounting;
unknown consumers fail closed. Editor-only code does not ship. Source-scene
bytes and lighting settings remain unchanged after each build.

Rewrite the six prefab assets with the existing native renderer-binding logic.
Capture their original bytes/GUIDs and original/prepared hashes in a separate
body journal. Preflight all prefabs and both manifest copies before any restore.
Reject backup drift, user edits, foreign destinations and unknown interrupted
writes. Never overwrite unknown source changes to make a retry succeed.
Body assets are already outside Resources, so no asset move is needed.

Extend character source preparation with one root-preparation call at Initialize
entry. Upgrade its legacy receipt only when reconstructing the old two-hook
transform from the backup exactly reproduces the current recorded source.
Record raw baseline/prepared hashes thereafter. Retain the existing sha256 field
for current private rebuild tooling. No marked source without a receipt is trusted.

Full pipeline order: restore body state before regenerating the choice manifest;
prepare existing art/choice/surfaces; prepare body bundles, prefab bindings and
combined manifest; build the same direct Title/Start player with the scene callback.
Native/editor tests run on synthetic fixtures. A currently supported licensed
Hub-launched editor may be used when direct batch licensing cannot obtain a token;
no license-file fabrication, token extraction, entitlement bypass, or new terms
acceptance is part of the work.

## Verification and remaining goal

Require actual Unity tests for scene callback dependency removal, root preparation
before first/reused initialization, inactive cleanup, overlapping owners, state
restore, failure cleanup and source/journal recovery. Require exact equality of
all original mesh fields/payloads and original non-body mesh/texture multisets.
The eight migrated meshes must be absent from direct resident player data;
all260 independent bundles must pass CRC/payload proof. Original six-prefab fields
and local IDs must be preserved except cleared slots and added ownership data.
Scene source hashes, quality5, Mono/OpenGL/x86/LAA, existing manifests/bundles and
FAT32 file limits must still pass. No mocked/compilation-only gameplay claim.

Then replay4 GiB Title -> new shop -> movement/interactions -> pack -> album ->
save -> quit -> relaunch/load, with representative original visuals, measured
headroom (target at least256 MiB), frame times and repeated actor activation.
Profile live shop logic after it is playable; optimize measured work while
preserving game rules. Normal play, loading, visual and runtime-stall gates all
remain required. No fullgame release or new NSP until they pass. Physical Switch
behavior remains unverified until actually tested there.
