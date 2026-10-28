# Shop finish texture streaming refinement

Continues explicitly approved switch-on-demand-design.md next-catalogue scope.
User requires original visuals; loading unused content on demand is authorized.
No new user-facing choice or approval gate: preserve current quality and rules.

fc96445 unlocked4GB test reached39% then ENOMEM, heap3117MiB/free66. The previous
artwork-only test stopped21%. This remains a memory failure, not proof of a
suspension fix. Emulator stopped and evidence character-unlocked4gb-first/ saved.

Read-only exact Unity audit surface-catalogue-probe.json:430 raw finish field
references,238uniqueTexture2D assets;229 have only the authorized catalogue slot
references,319122736 compressed image bytes. Nine referenced by existing materials
stay resident; no material default is stripped. This is artifact potential, not
application headroom. All finish texture sources are outside Resources. Furniture
lifetime audit separately found poster candidates; those are outside this first
implementation, as are whole-prefab streaming and material/mesh changes.

Choose synchronous per-Texture2D LZ4 bundles and persistent material-slot leases.
Keep SO_ObjectDataList structure, counts, prices, iconSprites, colors, smoothness,
showBar values, prefab refs and saved indices. Strip only audited raw mainTexture/
roughnessMap/normalMap fields in floor/wall/wallbar/ceiling rows. Do not alter imports,
mips, formats, readability, sampler state or pixels. Unknown refs retain the texture.
Back up the catalogue with GUID/original/prepared SHA256 receipts and whole-batch
preflight. Refuse authored/backup/GUID drift and legacy ambiguity before overwriting.

Public Runtime interface SurfaceTextures.SetTexture(Material material, GameObject
owner, string slot, string key, Texture fallback). Source preparation replaces24
existing SetTexture calls in ShopCustomizationManager; original float/color/bar
logic stays intact. Keys surface:{floor|wall|wallbar|ceiling}:{actualIndex}:{field}.
Resolve actualIndex via the returned data object's original list.IndexOf, preserving
InventoryBase's high-index clamp-to-zero (and negative-index exception). Keep
all original data getter calls and ordering. A non-null fallback is an authored
resident value or a runtime mod override; honor it, including for an eligible key.
If fallback null and key in canonical manifest, load required original texture.
If key absent and fallback null, preserve original null assignment. Required asset
failure throws before changing material/pins and shows a local error; no substitutes.

Acquire new texture before Material.SetTexture; release old pin only after binding.
Use ArtCache keyed by bundle for alias dedup,64MiB soft accounted budget; visible
pins win and actual application memory is measured separately. All payloads are
one original Texture2D, CRC checked, no dependencies/subassets; use ArtImageSize
for actual imagebytes validation. Canonical Resources/TCGStreaming/SurfaceCatalog
text equals StreamingAssets/TCGStreamedSurfaces/catalog.json exactly. Manifestv1
entries key/bundle/asset/bytes/crc, bundle surface_{guid}, stable address
assets/tcgstreamedsurface/{guid}.asset. CRC comes from unmodified Unity build output.

SurfaceMaterialOwner attaches to the manager GO. OnDisable keeps pins: world
renderers are separate and can stay visible. OnDestroy clears only owned slot
references still pointing at our texture before final unpin. Support multiple
owners sharing same material/slot/key so destroying one never blanks another.
Changing a slot to a different value supersedes old bindings after assignment;
old owner teardown never clears a newer binding. External material override must
not be clobbered on release. Dead materials release slot references/pins even while
the manager remains alive; borrowed resident fallback properties remain intact. Scene exit/service teardown releases all owned slots;
lowMemory trims only unpinned cache. Runtime mods setting a non-null data override
keep their original value. No borrowed material or fallback Texture is destroyed.

Acceptance: RED/GREEN actual Unity textures/materials shared owners, replace,
destroy, disabled-owner retention, external override, null/retained fallbacks,
failures/CRC/canonical mismatch, cache eviction/native unload/reload; sourcefixture
proves all24 setters, clamped index, unchanged color/showBar logic. Audit covers
unknown scene/material/Sprite/binary/Resources refs. Strict actual artifact proof
compares all original texture fields and raw compressed pixels (stream coordinate
normalization only), removes229eligible resident textures, preserves all other
textures/meshes, material and quality configuration, catalogue unrelated fields,
Resources and FAT32. Then unlocked4GB gameplay; no playable/release claim before
main approved gates pass. If insufficient, audit next catalogue without reducing
quality. Keep commercial sources/assets private; publish only own tooling.

Source receipts include version, exact source/backup paths and original/prepared
byte hashes. Edited backups or marked sources missing a receipt refuse preparation;
known unmarked original backup before the first write can recover safely. The
private initial receipt was upgraded only after exact untouched-source and full
transform comparison. No generic legacy adoption is supported.
