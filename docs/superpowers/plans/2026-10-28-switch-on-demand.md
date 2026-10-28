# Switch on-demand artwork Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. The user explicitly approved implementation; execute inline without another execution-mode approval.

**Goal:** Preserve current visuals while loading and releasing artwork on demand, then validate shop gameplay and save/load at 4 GB.

**Architecture:** Keep direct Title/Start scenes and shared Resources geometry. Build authored Sprite assets into small per-texture LZ4 bundles, replace eager catalogue references with semantic keys, and pin a bounded cache through CardUI visibility/ownership. Separately investigate the runtime using the companion suspension plan.

**Tech Stack:** Unity 2021.3.45f2, C# compatible with Unity's Mono, Python 3, local .NET 9 host tests, existing Autorun/Wine and patched Ryujinx.

**Spec:** `docs/superpowers/specs/2026-10-28-switch-on-demand-design.md` (approved 2026-10-28).

## Global Constraints

- Preserve Ultra quality 5, current 1024 world/Asc caps, all other import settings, authored sprite geometry, fonts, frames, geometry and audio.
- Private project: APFS clone at `/Users/tphuc/Downloads/TCG/switch-on-demand/macos-port-work` of `switch-optimized/macos-port-work`; never edit original game or Mac app.
- Source-only deliverables: `tools/compatibility/switch-streaming/` in publication; never commit recovered game files, assets, mods, firmware, keys or emulator data.
- Reuse clean `codex/switch-on-demand` publication branch; private assets are isolated by the approved APFS clone. Do not use the unrelated attached commercial worktree.
- Local bundles, validated relative paths/CRCs, one texture's sprites per bundle; no PNG reconstruction or network fallback for relocated art.
- Soft unpinned cache budget initially 128 MiB; active/pending leases prevent eviction. Count shared textures once.
- Game files must fit FAT32; keep previous SD data/users/registry backups. Restore 4 GB after diagnostics.
- No FEX modifications, fake suspension/context success, disabled GC, public game upload or premature playable claim.

## Review Focus

- Cached inactive cards: clearing image references precedes lease release; reactivation reloads correct art.
- Variant/previous evolution/null slots: catalogue identity and fallback semantics remain unchanged.
- SD errors and repeated requests: failures complete ownership cleanly and do not trigger web downloads.
- Shared textures and pinned over-budget content: no duplicate loads or destruction of visible artwork.
- Build dependency leakage and atlas geometry: migrated art is absent from resident data and original sprite/texture payloads are preserved.

## Task 1: Cache contracts and deterministic ownership tests

**Files:** `Runtime/ArtCache.cs`, `Runtime/ArtManifest.cs`, `Tests/HostTests.cs`, `Tests/HostTests.csproj` under the source-only tool directory.

**Interfaces:** `ArtEntry` contains key/bundle/asset/bytes/crc; `ArtCache<T>` takes budget, async loader callback, unloader; `Acquire(bundle, bytes, changed)` returns disposable `Lease` with `Ready`, `Value`, `Error`; `Trim()` evicts unpinned LRU entries. Counters expose loaded/pinned/pending/peak bytes.

- [ ] Write host assertions first: shared requests invoke loader once; two leases protect same payload; last release allows LRU eviction; disposed pending waiter receives no callback; completed abandoned loads can be evicted; failed loads release budget and can retry; pinned >128 MiB survives; duplicate Dispose is harmless; callbacks may acquire/release safely.
- [ ] Run host console test project with local .NET 9, observe expected missing-type/behavior failure, then implement cache and manifest path validation.
- [ ] Re-run complete host suite; require all assertions pass. Record RED/GREEN output in local ledger and commit own sources/tests.

## Task 2: Unity bundle backend and CardUI ownership session

**Files:** `Runtime/UnityArtService.cs`, `Runtime/CardArtSession.cs`, `Editor/StreamingTests.cs`.

**Interfaces:** service initializes from StreamingAssets manifest; `TryResolve(monster, role, index, out Sprite)` resolves within a current session. `CardArtSession.Begin(Component, Action refresh)` returns IDisposable; `Disable(Component)`, `Reactivate(Component)`, `Prefetch(Component)` manage lifecycle. Original getter fallback runs only for absent manifest keys. Existing authored resident loading sprite is used during a hidden pending card.

- [ ] Create failing Unity editor harness assertions for actual bundle load/unload, payload identity, two owners sharing one texture, old completion after pooled reuse, disable/destroy while pending, late failure, nested sessions and eviction/reload.
- [ ] Implement AssetBundle.LoadFromFileAsync backend with deduplicated bundle ownership. Resolve original Sprite asset by manifest asset name, never Sprite.Create. Complete callbacks on main thread.
- [ ] Session generation tokens suppress obsolete refreshes. Retain old leases through synchronous rebinding; clear obsolete owned Image.sprite references before releasing. Hide the pending card through a CanvasGroup and restore its prior alpha once a complete refresh is ready.
- [ ] Run host suite and actual Unity harness; prove native objects are reclaimed and repeat loads work. Commit sources and evidence references only.

## Task 3: Build extraction and source integration

**Files:** `Editor/StreamingBuild.cs`, `prepare_project.py`, `Tests/test_prepare_project.py`; private generated changes to MonsterData, InventoryBase, CardUI, Card3dUISpawner and reveal/page entry points as required.

**Interfaces:** `StreamingBuild.Run` builds catalog/bundles/direct player and emits build/dependency report. `prepare_project.py` copies own source and applies idempotent, guarded C# hooks in the private clone; source fixture tests contain synthetic code only.

- [ ] Create clone and confirm baseline settings, no running editor for that project, current authored placeholder sprite and source-only branch state.
- [ ] Write failing preparation tests: clean/idempotent application, brace/comment/string handling, unknown source rejection and unchanged save/gameplay code outside hooks.
- [ ] Generate keys `monster:{id}:{icon|ghost|variant-icon|variant-bg}:{index}` plus explicitly migrated list slots. Read original sprites through SerializedObject/AssetDatabase; preserve null slots and ordering. Build one bundle per underlying texture, record source identity, texture import/payload and sprite metadata.
- [ ] Guarded hooks wrap CardUI.SetCardUI and stream-completion refresh in a session, intercept only migrated MonsterData getters, and release/reacquire on disable/enable. Inventory call audit must not leave unleased migrated getters.
- [ ] Remove migrated catalogue refs and explain/adapt default Image references under CardUI in scenes/prefabs; fail for other unexplained strong refs. Preserve common resident UI and original online behavior for non-migrated content.
- [ ] Use original card spawner culling and pooled lifecycle for release/prefetch, without changing culling thresholds. Preload reveal/page art before display; do not show incomplete cards.
- [ ] Run Python tests, host suite, Unity harness and build. Verify direct scenes, quality 5, x86/Mono/OpenGL, FAT32 limits, no migrated resident texture leakage, no duplicate geometry, and matching authored art. Commit only own tools/tests and minimal integration logic.

## Task 4: Controlled integration test and completion

**Files:** own validation tooling as needed; private build/log/status reports outside Git.

**Interfaces:** complete source-only runtime investigation follows companion plan `2026-10-28-switch-suspension-diagnostics.md`; no uncontrolled emulator/runtime swaps while Unity build or agent operates.

- [ ] Preserve current test SD by rename and install only a verified build with matching dependencies. Verify 4 GB before launch.
- [ ] Exercise title, shop load, movement/interactions, pack reveal, album paging, culling, save, quit, relaunch/load. Capture generation/cache/total-memory logs and representative correct artwork; repeat transitions to test retention.
- [ ] If initial migration is insufficient, audit the next retained catalogue and apply the same lifecycle; no further quality reduction. If runtime still stalls, use the separately proven diagnostic/fix path and retain a truthful blocker report.
- [ ] Require no demonstrated suspension stall, bounded cache, usable shop/save-load, at least 256 MiB headroom in the tested scene, unchanged visuals. Hardware remains unverified until physically tested.
- [ ] Run a fresh whole-change review, fix important findings with regression coverage, and record tests/limits. Do not release a full NSP before these gates pass.

## Execution rulings

The user approved design and explicitly said implement; proceed with the native inline execution method. Source branch already exists and private APFS isolation is approved. The runtime investigation is delegated as an independent failure domain under dispatching-parallel-agents; it owns no Unity files, SD/runtime installation or shared Git operations. Main agent integrates and verifies any changes.
