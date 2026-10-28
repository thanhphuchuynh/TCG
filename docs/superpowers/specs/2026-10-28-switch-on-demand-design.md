# Switch on-demand artwork loading

Date: 2026-10-28. Status: approved by the user after builder review; implementation authorized.

## Intent and user decision

The user chose to keep the current visual settings and refactor asset loading
to load content on demand. The target remains the existing Windows x86 Unity
2021.3.45f2 / Mono / OpenGLCore player under Autorun on a retail-memory Switch.
This is a compatibility build, not a native Unity Switch export.

Preserve the current approved settings: Ultra quality level 5, world texture
cap 1024, the existing 242 Asc card-art textures capped at 1024, other artwork
at its current resolution, and font/atlas imports unchanged. No additional
compression, resolution reduction, geometry simplification or audio changes.

## Evidence and constraints

The local work folder is `/Users/tphuc/Downloads/TCG/switch-optimized`.
Its `optimization-result.json` and `optimization-progress.txt` record the builds.
The direct-scene player has 2,610,902,524 serialized texture image bytes.
It removes duplicate mesh copies: scene mesh data fell from 863,502,032 to
64,427,328 bytes, with the five largest meshes occurring once in Resources.
It reaches 18% loading at 4 GB and then fails an allocation. At 8 GB it reaches
22% and stops drawing frames with 2,613 MiB still free; the main worker stack
includes `wait_suspend`. The precise suspension deadlock cause is unproven.

`catalogue-art-dependency-audit.json` identifies 440 texture dependencies from
`SO_MonsterData`, totaling 861,840,832 image bytes. This is an initial migration
scope and a measured upper bound on those serialized image savings, not a
promise of equivalent physical-memory savings or a complete 4 GB solution.

Only isolated local game copies may be modified. Preserve the original project,
native Mac app, installed mods, saves and existing NSPs. The publication Git repo
may contain our source/tools/docs only: no recovered game source, art, mods,
firmware, keys or emulator data. No upstream messages or submissions are needed.
The FEX source checkout disallows AI-generated contributions; do not modify or
submit generated FEX code. Runtime investigation can target our Wine/Horizon
compatibility changes and their tests.

## Approaches

1. Further texture reduction: smaller implementation, but changes visuals. The
   user declined this in favor of on-demand loading.
2. Local on-demand compressed assets: chosen. Remove eager artwork references,
   keep authored sprites and current compression, and load only needed content.
   More lifecycle work and some SD reads; preserves the selected visual settings.
3. PNG loading or loading entire expansions: rejected implementation alternatives.
   PNG decode expands textures and reconstructing sprites risks the prior card
   frame/UV problem. Expansion-wide retention weakens the memory bound.

The builder's proposed implementation of option 2 uses Unity AssetBundles with
LZ4 chunk compression, direct Title/Start scenes, and no new package dependencies.

## Build and asset layout

Work in a new APFS clone of the optimized private Unity project. Retain the
direct-scene build; do not restore the old bootstrap that mounts all bundles.
Generate an artwork catalog keyed by stable source GUID plus local sprite ID.
Semantic entries identify monster, expansion, art role and variant index.
The catalog stores identifiers and sprite metadata for validation, never live
Unity object references that pull textures into the player.

For each migrated source texture, bundle its original Sprite assets and that
texture together. Multiple sprites sharing a texture share one bundle/cache
entry. Preserve authored rect, pivot, pixels-per-unit, border, mesh/UV data,
texture format, dimensions, mip chain and sampler settings. Do not recreate
sprites with `Sprite.Create` or change card-frame layout. Validate unexpected
atlas/secondary-texture dependencies rather than silently bundling an expansion.

Initially migrate the artwork reached through `MonsterData.Icon`, `GhostIcon`,
`IconList`, `BGList`, and applicable monster/special-icon lists. Leave small shared
frames, fonts and common UI elements resident when their retention is useful.
The builder settles the exact migration list from the dependency report and
records it, its bytes and every remaining eager reference.

Replace migrated serialized sprite references with catalog keys in the private
clone, preserving list lengths, indices, monster IDs, null entries and fallback
semantics. Audit scenes, prefabs and ScriptableObjects for other references to
these same sprites/textures, including default images on pooled CardUI prefabs.
Remove or adapt those references too; changing a getter alone cannot save memory.
The build must reject an unexplained eager reference to migrated artwork.

Store bundles and a versioned, local manifest under StreamingAssets. Use relative
validated paths and CRCs. No network requests for relocated artwork. An unknown
or missing entry reports a clear local content error; it must not silently fetch
commercial artwork from the web. Preserve unrelated existing streaming behavior.
All output files must fit FAT32. Build into a staging directory, validate it,
then install on the emulator SD with the previous data preserved by rename.

## Runtime ownership and presentation

Use a main-thread asset service with deduplicated in-flight requests and explicit
leases. A lease pins its texture bundle while any visible consumer uses it.
Integrate the service at CardUI and the audited inventory/icon callers rather
than assigning loaded sprites back to the permanent monster catalog.
All call sites for migrated artwork must participate in ownership; the builder
audits direct field access and getter calls, including previous-evolution icons.
Existing APIs for non-migrated resident art retain their behavior.

Each UI image binding carries a generation token. A late completion for a card
that has been reused, disabled or destroyed must release its lease and must not
overwrite the current card. Clear the owned Image.sprite reference before
releasing the last lease. Reactivation re-acquires the same semantic key.

Tie world-card ownership to the existing Card3dUISpawner visibility and pooling
lifecycle. Its current culling behavior must not change. Prefetch on approach or
before a card becomes visible so newly exposed cards display complete artwork.
Do not retain an expansion merely because an inactive pooled card once used it.

Preload the selected pack's upcoming visible art before beginning its reveal.
Album/deck page transitions wait for that page's requested art. Use existing
loading/progress presentation where needed; avoid half-drawn cards and permanent
placeholders. The builder chooses the smallest necessary presentation changes;
no menu redesign, branding or new startup text is part of this work.

Cache unpinned entries with LRU eviction. Proposed initial image budget: 128 MiB,
counted once per source texture, with manifest sizes checked against loaded assets.
This is a tuning choice, not a hard cap on visible art: pinned content must never
be destroyed to meet it. Log pinned bytes, cached bytes, pending loads and peak
bytes. If visible demand exceeds the budget, report it and measure the total
application memory; do not lower quality or hide required cards automatically.
Unload a bundle with its assets only after all leases and UI references are
released. Test that SD reload and Unity asset destruction reclaim memory.

## Independent runtime suspension defect

Keep the build-7 runtime as a preserved baseline. Reproduce the startup/load
stall with matched DLLs, ELF symbols and fresh thread logs. Instrument suspend
and resume ownership/counts, target thread IDs, doorbells and context handoff in
our Wine/Horizon layer; identify the actual wait cycle before changing semantics.
Do not fake successful suspension/context capture, disable garbage collection,
or bypass the faulting instruction. Regression tests must cover any demonstrated
race and repeated Mono/GC activity. If the cause lies outside a change we can
faithfully make, report that boundary explicitly. Streaming success alone is not
evidence that the runtime stall is fixed.

## Sequence and acceptance

1. Prove catalog extraction and dependency removal in an isolated editor build.
   Confirm streamed art is absent from resident player data and appears once in
   the intended bundle. Compare compressed texture payloads and sprite geometry
   against the current direct player. Keep current quality and import settings.
2. Test service ownership: overlapping requests, shared textures, cancellation,
   pooled reuse, disabled/destroyed owners, cache eviction, reload, missing files
   and asset failure. Include actual Unity load/unload tests, not just mocks.
3. Fix or clearly isolate the demonstrated runtime suspension defect with fresh
   reproduction and regression evidence. Do not count extra-RAM tests as Switch
   passes. Restore 4 GB after diagnostic runs.
4. Measure 4 GB title -> new shop -> movement/interactions -> pack reveal -> album
   paging -> save -> quit -> relaunch -> load. Repeat enough transitions to expose
   retention, stale-art and suspension races. Capture fresh memory/frame logs
   and representative visuals, including foil, alternate art, previous evolution,
   transparent borders and world-card culling. Physical Switch remains unverified
   until actually tested there.

If artwork migration alone is insufficient, profile the next persistent catalog
and apply the same on-demand architecture to its unused content. Do not revert to
blanket scene bundles, duplicate Resources geometry, further visual reductions,
or speculate that the first 822 MiB solves the whole budget. Additional categories
need a documented dependency/lifetime audit before implementation.

Pass means a usable shop plus save/load within retail memory, correct current
visuals, no unbounded cache growth and no demonstrated runtime suspension stall.
Target at least 256 MiB measured application headroom in the tested shop; record
limits of the chosen scene/card population. A compiler success, menu, 22% progress
or successful mocked tests do not establish gameplay.

No new full NSP, public release or upload of game material until these checks
pass. A future public artifact may contain only our installer/plugins/tools.

## Choices left to the implementer

Use existing Unity/C# APIs and project conventions. Choose catalog serialization,
bundle naming, binding component names and exact prefetch margins; document those
choices. Tune cache size using measured 4 GB headroom. Preserve every externally
visible game rule and save-data identity. Keep the two tracks independently
testable, and record blockers without presenting an incomplete port as playable.
