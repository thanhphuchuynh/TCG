# Base-body streaming implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the eight audited body meshes from eager player dependencies while preserving their complete authored data and character setup behavior.

**Architecture:** Reuse MeshBundleOwner and the shared mesh cache. Prepare the root owner before CharacterCustomization.Initialize, rewrite six prefab assets under a recovery journal, and rewrite Start scene instances only in the build callback.

**Tech Stack:** Unity 2021.3.45f2, C#, Python 3.12/UnityPy, Windows x86 Mono/OpenGL player through Autorun.

**Spec:** `docs/superpowers/specs/2026-10-29-base-body-streaming-design.md`

## Global Constraints

- Keep every original mesh byte, usage/readability flag, LOD, bindpose, skin bone, blendshape, bounds, renderer/material property, hierarchy and save/RNG identity.
- No lower quality, fewer characters, altered rules, hidden required content, or GC/suspension bypass.
- Reuse MeshBundleOwner/MeshService and the shared128 MiB soft cache; active pins win.
- The combined manifest contains the original252 entries plus exactly eight body entries, with identical resident and SD copies.
- Do not save or rewrite Start.unity.
- Existing art, choice-mesh and surface bundle payloads must remain unchanged.
- FEX upstream source and patches remain outside our edits.
- No fullgame release or new NSP until gameplay, visual and runtime gates pass.

## Review Focus

- Reused or inactive customizers must have body meshes before either Initialize branch; the hook must not prepare unrelated descendants (Task1).
- Raw source or backup drift must cause refusal before any write, including legacy-receipt upgrades and CRLF inputs (Task1).
- An unknown scene/prefab consumer or changed owner identity must stop migration, rather than silently removing its mesh (Task2).
- A mixed or interrupted prefab/manifest preparation must be recoverable only when every current and backup hash is known (Task2).
- A native bundle build must neither delete nor modify the existing252 bundles; player dependencies must exclude all eight body meshes (Task3).

---

### Task 1: Root ownership and guarded initialization hook

**Files:** Modify `Runtime/MeshBundleOwner.cs`, `prepare_character_meshes.py`, `Tests/test_prepare_character_meshes.py`, `Tests/Unity/MeshRuntimeProbe.cs` under `tools/compatibility/switch-streaming/`.

**Interfaces:** Consumes existing `MeshBundleOwner.Prepare()` lifecycle; produces `public static void PrepareRoot(GameObject root)` (root component only, absent owner no-op), plus `prepare(project)` receipt version2 with raw `baselineSha256`, `preparedSha256`, and compatible `sha256`.

- [ ] Write behavioral tests executing patched C#: first Initialize inspects ready geometry and reused Initialize enters its original branch after preparation. Test fresh/repeated prepare, legacy upgrade, source/backup drift, missing receipt, and raw CRLF preservation using temporary isolated project layouts.
- [ ] Run `python3 -m unittest discover -s tools/compatibility/switch-streaming/Tests -p test_prepare_character_meshes.py`; expect failure for missing initialization and receipt guards. Use bundled Python3.12 for subsequent complete suites.
- [ ] Add the root hook before original Initialize statements. Implement exact-byte receipt validation: legacy backup is trusted only when old two-hook transform recreates the recorded current bytes; version2 validates both current and backup. Stage calculations/validation before writes; atomic receipt replacement.
- [ ] Add native Unity runtime cases: inactive root with another child owner prepares only root, real blendshape names available before setup, reused preparation preserves weights, inactive cleanup releases pins, later activation restores state, absent owner no-op. Run native fixtures through licensed Hub editor. Expect complete mesh runtime suite PASS.
- [ ] Run full89+ Python fixture suite and8 host tests; commit source-only Task1 changes after verification.

### Task 2: Audited body preparation and in-memory scene conversion

**Files:** Create `Editor/BodyMeshReferenceAudit.cs`, `Editor/BodyMeshBuild.cs`, `Editor/BodyMeshSceneProcessor.cs`, `Tests/Unity/BodyMeshBuildTests.cs`; modify `Editor/CharacterMeshBuild.cs` under the same directory.

**Interfaces:** Consumes Task1 root owner and existing `MeshRecord`, `MeshBinding`, `MeshManifest`, usage preservation. Produces `BodyMeshBuild.Prepare()`, `Restore()`, `ProcessScene(Scene,BuildReport)` and strict testable audit/conversion helpers. Extract `CharacterMeshBuild.RewriteHierarchy(GameObject,IList<MeshRecord>) -> int` and expose whole-batch `ValidatePrefabSnapshots` without changing old prefab semantics.

- [ ] Write synthetic native tests for exact body renderer ownership, all GUID occurrence accounting, unknown consumer rejection, inactive scene binding conversion, bones/materials/LOD/bounds/weights retention, original scene bytes, and whole-batch restore refusing late drift. Run before implementation; expect missing body API failure.
- [ ] Implement strict native body audit: eight standalone meshes, exactly248 skinned slots/62 roots, Start224/56 and six prefabs4 each, actual CharacterCustomization.MainMesh link verified; scan all Assets for unexplained direct/GUID refs. Fail closed on changed identity/counts. Fixture helper accepts explicit test expectations; production pins the audited counts/paths.
- [ ] Implement six-prefab snapshots plus both manifest snapshots and source scene stamp in one versioned journal. Validate complete batches before writes/restores; atomically record each known prepared hash. Build only eight independent bundles, preserve flags/CRC, append eight entries to existing252 canonical manifest, preserve all old bundle hashes.
- [ ] Implement build callback for the prepared Start scene on StandaloneWindows only: validate journal/scene identity/hash, match every audited slot/root, rewrite in-memory hierarchy with original binding data. No source scene save. Record callback proof and require expected scene accounting.
- [ ] Run all native synthetic tests, including actual baseline/converted Win32 payload proof and failed recovery cases. Expect no source hash changes and unchanged control mesh; run existing mesh build/audit/restore suites; commit Task2 source-only changes.

### Task 3: Full player integration and exact payload validation

**Files:** Modify `Editor/StreamingBuild.cs`, `validate_meshes.py`, relevant Python tests; add body preparation instructions to `README.md` under the streaming tool.

**Interfaces:** Consumes Task2 guarded preparation and callback; produces combined260 manifest, exact body payload proof, direct scene player and build evidence.

- [ ] Write failing validator fixtures:260 manifest allowed only with complete expected original8 body identities; body resident payload or changed non-body mesh/texture multiset is rejected. Run RED before implementation.
- [ ] Restore body before choice restoration/regeneration; prepare body after art/choice/surfaces. Verify build callback actually ran, source scene bytes unchanged, all old bundle hashes unchanged, exact resident/SD manifest, Mono/OpenGL/quality5/x86/LAA/FAT32 checks.
- [ ] Copy only reviewed source tools and guarded hook into private clone; rebuild using the legitimate licensed Hub session. Keep previous player preserved. Expect successful Unity report and independent260 bundle payload/CRC/flags proof, body absent from resident player, non-body mesh/texture signatures equal previous verified player.
- [ ] Run complete Python/host/native regression suites and record exact commands/counts. Commit source-only changes and documentation.

### Task 4: 4 GiB replay and gameplay profiling

**Files:** Private evidence in `switch-on-demand/basebody-4gb-*`; public measured summary in `docs/` only after verified results.

**Interfaces:** Consumes Task3 verified player and existing diagnostic runtime. Produces honest replay evidence and either usable shop measurements or a narrowed next blocker.

- [ ] Preserve installed player, install verified test player, keep `dram_size=0`, use same runtime and current visual settings. Run Title/new shop with controlled title idle, measure peak/free heap and ENOMEM alongside load progress; compare repeated runs where possible.
- [ ] If playable, measure cold/repeated actor activation and frame times before changing logic. Exercise movement/interactions, pack, album, save/quit/relaunch/load with representative original visuals. Target at least256 MiB headroom; record unmet gates explicitly.
- [ ] If still stalled, preserve logs and investigate the first measured failure. Do not declare smooth/playable, release, or alter game rules to make this pass.
- [ ] Request one fresh whole-change review after implementation; fix Critical/Important findings with RED→GREEN regression coverage. Keep main goal active until actual gameplay/performance gates pass.
