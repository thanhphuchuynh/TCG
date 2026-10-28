# Character-choice mesh streaming Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline. Independent
> runtime ownership and editor reference-audit domains may run under the existing
> parallel-agent skill with disjoint files/fixture projects. No new approval gate;
> this refines the explicitly authorized next-catalogue path.

**Goal:** Reduce eagerly loaded optional character meshes while preserving visuals.
**Architecture:** Original per-mesh LZ4 bundles and explicit renderer ownership;
resident choice metadata/prefab/materials, synchronous prepare before existing setup.
**Tech Stack:** Unity2021.3.45f2,C#,Python,existingArtCache; private clone only.
**Spec:** ../specs/2026-10-28-character-mesh-streaming.md

## Constraints
- Only /Users/tphuc/Downloads/TCG/switch-on-demand/macos-port-work game edits.
- Public own sources under tools/compatibility/switch-streaming only; no gamefiles.
- Preserve Ultra5, all imports, geometry/LODs, materials, RNG/save IDs and Resources
  prefab names. No FEX changes, fake context success or GC bypass.
- Keep baseline snapshots/move journal and previous installed/build data.
- FullNSP/release remains prohibited until original gameplay/fidelity gates pass.

## Review Focus
- Inactive-parent setup must read real mesh/blendshape data before original setter.
- Shared meshes stay alive for another visible owner; clearing precedes final unpin.
- Reenable preserves weights/bones/bounds without rerolling saved appearance.
- Unknown references and Resources roots never cause resident/bundle duplication.
- Switching/destroy/failure paths do not retain discarded parts or overwrite new ones.

## Task 1 Runtime mesh contract/owner
- [x] RED actual Unity synthetic rigged/blendshape fixture, shared owners, inactive
  instantiate, disable/destroy/reload, malformed/missing bundle and sweep.
- [x] MeshEntry/MeshManifest, MeshBinding/MeshBundleOwner and synchronous MeshService
  using ArtCache. One-mesh/no-material bundle guard. Canonical local manifest.
- [x] Preserve defaults/current weights/bones/rootBone/bounds; service next-frame
  inactive sweep. GREEN entire own Unity suite + existing cache tests.

## Task 2 Editor reference audit and build
- [x] RED own synthetic known prefab vs unknown scene/asset/direct ref cases.
- [x] Collect13table Mesh prefab paths via SerializedObject. Dedup meshes/GUIDs;
  MeshRecord key/guid/path/name/bytes/mesh; only approved renderer m_Mesh may strip.
- [x] Build per-mesh bundles, explicit stable addresses; snapshot prefab originals,
  add owner bindings/defaults, clear eligible mesh refs, move exact eligible assets
  outside Resources with GUID-preserving journal. Restore safely on retries.
- [x] Integrate restore before existing art preparation and mesh preparation before
  direct player build; private reports and canonical manifest. GREEN source tests.

## Task 3 Source integration/artifact proof/gameplay
- [x] RED executable fixture proving same Instantiate/setter order under inactive
  parent; guarded two-expression wrapper, backup/receipt/idempotence.
- [x] Fresh private build; exact mesh payload/artifact comparisons, resident leakage,
  textures/LOD/Resources compatibility, no duplicate dependencies/FAT32.
- [x] Install only verified private build; preserve prior installed player.
- [ ] Unlocked4GB shop/pack/album/save-load, measured cache/application memory;
  investigate independent runtime stalls honestly. Mac locked again during build.
- [ ] Fresh whole-change review, important fixes RED/GREEN, publish own source only.
