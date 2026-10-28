# Character-choice mesh streaming refinement

Continuation of approved switch-on-demand-design.md. User authorized preservation
of visuals and loading unused content on demand, including the next persistent
catalogue after a dependency/lifetime audit. No new visual/gameplay choice is asked.

The unlocked dcbbc77 4GB test reached21% then ENOMEM (heap3108/free75MiB). Title and
controller activation worked. First381texture migration is insufficient. Private
next-catalogue-audit.txt documents exact CC roots/setter/pooling call sites.

Chosen refinement: stream authored Mesh assets only from the approved CC hair/
apparel choice prefabs. Keep their prefab hierarchy, material/shadow/mask/print
references and all table/preset/save strings resident. This is more conservative
than moving whole appearance prefabs/dependencies, and avoids texture duplication.
Base body meshes and any unexplained consumer remain resident. No LOD/geometry/
compression/readability changes, no FEX edits or suspension semantics workaround.

Read-only inventory lead:13CCtables,231name-matched choice meshes (~455MB;
12beard LOD meshes alone~368MB). Exact UnityGUID audit subsequently found252
unique eligibleMeshes across80choiceprefabs,0retained,560502804 estimated runtime
meshbytes. First artifact removes467269072serializedresidentbytes (445.623MiB);
that is artifact reduction, not promised runtime headroom.
Build audit permits only m_Mesh on SkinnedMeshRenderer/MeshFilter in exact approved
part prefabs; unknown direct references retain that mesh. Explicitly relocate only
eligible Mesh assets out of Resources with GUID preserved; keep original character
Resources prefab names and all string-based loads unchanged. No blanket deletion.

One original Mesh per local LZ4 bundle, stable address/key mesh:{guid}. Attach own
MeshBundleOwner component to each affected part prefab. Its serialized MeshBinding
contains a renderer/filter component reference, string key, original default
blendshape weights and bounds; never a live Mesh reference. Clear eligible renderer
mesh references only after bundles built and metadata captured. Canonical identifier
manifest in Resources plus CRC/local external manifest enforce required local data.
Unity's mesh-only bundle pipeline rewrites source m_MeshUsageFlags1 to0. Capture
its exact signedint32 value before bundling, then restore only those4serializedbytes
with complete unchanged Mesh/metadata/resource proof and LZ4 repack. Do not guess
semantics or omit it from validation. The helper's decompressedpayloadCRC is checked
by native Unity LoadFromFile; GetCRCForAssetBundle reads stale buildsidecars, which
are discarded along with the unused aggregatehashmanifest after preservation.

Runtime synchronously acquires deduplicated leases before the original character
setup reads mesh/blendshape data. Two guarded Instantiate expressions in original
setHair/setApparel call our wrapper, which uses ordinary Object.Instantiate then
ensures its MeshBundleOwner is prepared even under an inactive parent. Awake/
OnEnable also ensure preparation. All original preset selection, RNG, bones,
blendshape/color/material application, object activation and saved IDs stay intact.
This can add an SD stall at first use; it avoids introducing async spawn semantics.

On disable/destroy clear renderer/filter sharedMesh references before unpinning;
preserve current blendshape weights, bones/rootBone/bounds and rebind on enable
without rerolling or resetting m_HasInit. Instantiated-but-still-inactive owners
must release after the setup frame via the service's next-frame sweep. Destroyed
parts release only after references are cleared; shared active users retain pins.
Use existing ArtCache with128MiB soft accounted mesh budget; pins win. Accounted
sizes are build estimates, not actual application headroom. Log that distinction.

Acceptance: actual Unity rigged/blendshape Mesh bundle tests, two shared owners,
inactive-parent Instantiate, repeated deactivate/rehydrate/switch/destroy, local
failure/CRC and no leaked pins. Source integration fixtures prove unchanged setter
flow and exactly one original Instantiate. Recovery uses version2 GUID/original/prepared SHA256 receipts. Preflight the entire
batch before moving/copying. Original and committed prepared states are safe; a
changed identity, backup or unrecorded write refuses recovery. Legacy path-only
journals require independent verification, never silent adoption.

Build proof of no eligible resident
mesh, exact authored mesh payload including vertices/indices/bones/blendshapes/UVs,
unchanged textures/LOD/materials, preserved Resources names and FAT32. Then repeat
unlocked4GB gameplay. No release/playable claim before main approved gates pass.
