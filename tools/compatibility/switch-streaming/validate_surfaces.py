#!/usr/bin/env python3
"""Read-only proof of the approved finish Texture2D migration in direct players.

Requires UnityPy on PYTHONPATH. Inputs are actual *_Data folders plus the private
surface-build.json receipt. Never decode/re-encode pixels or modify game files.
Source GUID and original material/catalogue wiring require the independent
controlled builder/source proof; serialized player file IDs do not prove GUIDs.
"""
import argparse
import collections
import gc
import importlib.util
import json
import pathlib
import re
import sys
import zlib

# Reuse complete Mesh payload proof and typed incremental hashing, including its
# conservative rejection of unknown nonnull pointers. No geometry is decoded.
_mesh_spec = importlib.util.spec_from_file_location('_tcg_mesh_validation', pathlib.Path(__file__).with_name('validate_meshes.py'))
meshes = importlib.util.module_from_spec(_mesh_spec)
_mesh_spec.loader.exec_module(meshes)
BUILD_METADATA = meshes.BUILD_METADATA
FAT32_MAXIMUM = meshes.FAT32_MAXIMUM
GUID = re.compile(r'[0-9a-f]{32}')
KEY = re.compile(r'surface:(floor|wall|wallbar|ceiling):(0|[1-9][0-9]*):(mainTexture|roughnessMap|normalMap)')
TABLES = {'floor': 'm_FloorDecoDataList', 'wall': 'm_WallDecoDataList',
          'wallbar': 'm_WallBarDecoDataList', 'ceiling': 'm_CeilingDecoDataList'}
RESOURCE_CATALOG = 'tcgstreaming/surfacecatalog'


def _byte_array(value):
    if isinstance(value, (list, tuple)):
        if any(type(child) is not int or not 0 <= child <= 255 for child in value):
            raise ValueError('Invalid inline Texture2D image byte array')
        return bytes(value)
    if not isinstance(value, (bytes, bytearray, memoryview)):
        raise ValueError('Unsupported Texture2D image bytes')
    return value


def texture_signature(tree, streamed_data=None):
    """Hash every original field and compressed byte; normalize storage only."""
    if not isinstance(tree, dict) or 'm_Name' not in tree or 'image data' not in tree:
        raise ValueError('Texture2D lacks original serialized name/image data')
    inline = _byte_array(tree['image data'])
    stream = tree.get('m_StreamData', {'path': '', 'offset': 0, 'size': 0})
    if not isinstance(stream, dict) or set(stream) != {'path', 'offset', 'size'}:
        raise ValueError('Unsupported Texture2D stream metadata')
    path, offset, size = stream['path'], stream['offset'], stream['size']
    if not isinstance(path, str) or type(offset) is not int or type(size) is not int or offset < 0 or size < 0:
        raise ValueError('Invalid Texture2D stream coordinates')
    if path:
        if not size or len(inline) or streamed_data is None:
            raise ValueError('Missing or ambiguous inline/streamed Texture2D image payload')
        payload = _byte_array(streamed_data)
        if len(payload) != size:
            raise ValueError('Truncated Texture2D streamed image payload')
    else:
        if size or offset or streamed_data is not None:
            raise ValueError('Ambiguous Texture2D stream metadata without resource path')
        payload = inline
    fields = {key: value for key, value in tree.items() if key not in BUILD_METADATA and key != 'm_StreamData'}
    fields['image data'] = payload
    return meshes.digest(fields)


def texture_image_bytes(tree):
    """Match runtime ArtImageSize.Of without decoding the native texture format."""
    for field in ('m_Width', 'm_Height', 'm_MipCount', 'm_TextureFormat'):
        if type(tree.get(field)) is not int or tree[field] <= 0:
            raise ValueError('Invalid Texture2D image layout: ' + field)
    if tree.get('m_ImageCount') != 1 or tree.get('m_TextureDimension') != 2 or tree.get('m_MipsStripped', 0) != 0:
        raise ValueError('Unsupported Texture2D array/dimension or stripped mip layout')
    width, height, mips = tree['m_Width'], tree['m_Height'], tree['m_MipCount']
    if mips > max(width, height).bit_length():
        raise ValueError('Invalid Texture2D mip count')
    # Values are UnityEngine.TextureFormat, also declared by UnityPy's enum.
    block = {10: 8, 12: 16, 24: 16, 25: 16}.get(tree['m_TextureFormat'])
    pixel = {1: 1, 2: 2, 3: 3, 4: 4, 5: 4, 7: 2, 13: 2, 14: 4, 63: 1}.get(tree['m_TextureFormat'])
    if block is None and pixel is None:
        raise ValueError('Unaccounted native Texture2D format: ' + str(tree['m_TextureFormat']))
    total = 0
    for _ in range(mips):
        total += ((width + 3) // 4) * ((height + 3) // 4) * block if block else width * height * pixel
        width, height = max(1, width // 2), max(1, height // 2)
    return total


def read_texture_signature(obj):
    from UnityPy.helpers.ResourceReader import get_resource_data
    tree = obj.read_typetree(check_read=True)
    stream = tree.get('m_StreamData', {})
    payload = None
    if stream.get('path'):
        for field in ('offset', 'size'):
            if type(stream.get(field)) is not int or stream[field] < 0:
                raise ValueError('Invalid Texture2D resource coordinate: ' + field)
        payload = get_resource_data(stream['path'], obj.assets_file, stream['offset'], stream['size'])
    result = texture_signature(tree, payload)
    size = len(payload) if payload is not None else len(_byte_array(tree['image data']))
    del tree, payload
    return result, size


def relative_file(root, relative):
    return meshes.relative_file(root, relative)


def validate_catalog(report, manifest):
    if not isinstance(report, dict) or report.get('status') != 'prepared':
        raise ValueError('Surface report must have status prepared')
    records = report.get('eligibleRecords')
    if not isinstance(records, list) or not records:
        raise ValueError('Surface report lacks nonempty eligibleRecords')
    retained = report.get('retained')
    if not isinstance(retained, list):
        raise ValueError('Surface report lacks explicit retained catalogue')
    retained_guids = set()
    for row in retained:
        if (not isinstance(row, dict) or not isinstance(row.get('guid'), str) or not GUID.fullmatch(row['guid'])
                or not isinstance(row.get('reason'), str) or not row['reason'].strip() or row['guid'] in retained_guids):
            raise ValueError('Invalid or duplicate retained Surface texture/reason')
        retained_guids.add(row['guid'])
    keys, by_guid, paths = set(), {}, {}
    for row in records:
        if not isinstance(row, dict) or not isinstance(row.get('key'), str):
            raise ValueError('Incomplete SurfaceRecord')
        match = KEY.fullmatch(row['key'])
        if not match:
            raise ValueError('Invalid semantic surface category/index/field')
        category, index, field = match.groups()
        if row.get('property') != TABLES[category] + '.Array.data[' + index + '].' + field:
            raise ValueError('SurfaceRecord property differs from semantic key: ' + row['key'])
        guid = row.get('textureGuid')
        if not isinstance(guid, str) or not GUID.fullmatch(guid) or guid in retained_guids:
            raise ValueError('Invalid or retained eligible surface GUID')
        name, path, size = row.get('textureName'), row.get('texturePath'), row.get('bytes')
        if not isinstance(name, str) or not name or not isinstance(path, str) or not path.startswith('Assets/'):
            raise ValueError('SurfaceRecord lacks authored texture identity')
        if any(part in ('', '.', '..') for part in path.split('/')) or '\\' in path or ':' in path:
            raise ValueError('Unsafe authored surface path')
        if type(size) is not int or size <= 0:
            raise ValueError('Invalid original surface image bytes')
        if row['key'] in keys:
            raise ValueError('Duplicate semantic surface key')
        identity = (path, name, size)
        if guid in by_guid and by_guid[guid] != identity:
            raise ValueError('Conflicting aliased surface GUID metadata')
        if path.lower() in paths and paths[path.lower()] != guid:
            raise ValueError('Ambiguous surface source path/GUID')
        keys.add(row['key']); by_guid[guid] = identity; paths[path.lower()] = guid
    for field, expected in [('recordCount', len(records)), ('bundleCount', len(by_guid)),
                            ('imageBytes', sum(identity[2] for identity in by_guid.values()))]:
        if type(report.get(field)) is not int or report[field] != expected:
            raise ValueError('Surface report ' + field + ' differs from eligible catalogue')
    if not isinstance(manifest, dict) or type(manifest.get('version')) is not int or manifest['version'] != 1 or not isinstance(manifest.get('entries'), list):
        raise ValueError('Unsupported local surface manifest')
    entries = manifest['entries']
    if any(not isinstance(row, dict) or not isinstance(row.get('key'), str) for row in entries):
        raise ValueError('Incomplete surface manifest entry')
    by_key = {row['key']: row for row in entries}
    if len(by_key) != len(entries) or set(by_key) != keys:
        raise ValueError('Manifest semantic keys differ from approved surface catalogue')
    aliases = {}
    for row in records:
        entry, guid = by_key[row['key']], row['textureGuid']
        if entry.get('bundle') != 'surface_' + guid or entry.get('asset') != 'assets/tcgstreamedsurface/' + guid + '.asset':
            raise ValueError('Manifest bundle/stable asset identity changed: ' + row['key'])
        if type(entry.get('bytes')) is not int or entry['bytes'] != row['bytes']:
            raise ValueError('Manifest image bytes differ from original surface: ' + row['key'])
        if type(entry.get('crc')) is not int or not 0 < entry['crc'] <= 0xffffffff:
            raise ValueError('Missing or invalid native Unity surface bundle CRC')
        identity = (entry['bundle'], entry['asset'], entry['bytes'], entry['crc'])
        if guid in aliases and aliases[guid] != identity:
            raise ValueError('Conflicting surface alias manifest metadata')
        aliases[guid] = identity
    return records


def resource_evidence(references, identities, catalogs):
    resources, texts, canonical = collections.Counter(), collections.Counter(), []
    for name, identity in references:
        target = identities.get(identity)
        if target is None:
            raise ValueError('Resources identity unresolved: ' + name)
        kind, authored_name = target
        if name.lower() == RESOURCE_CATALOG:
            if kind != 'TextAsset' or identity not in catalogs:
                raise ValueError('Resources surface catalogue cannot be resolved')
            value = catalogs[identity]
            canonical.append(value.encode('utf-8', 'surrogatepass') if isinstance(value, str) else bytes(value))
            continue
        if kind == 'GameObject' and (not isinstance(authored_name, str) or not authored_name):
            raise ValueError('Resources prefab lacks original authored name: ' + name)
        resources[(name, kind, authored_name)] += 1
        if kind == 'TextAsset':
            if identity not in catalogs:
                raise ValueError('Resources TextAsset payload unresolved: ' + name)
            value = catalogs[identity]
            texts[(name, value.encode('utf-8', 'surrogatepass') if isinstance(value, str) else bytes(value))] += 1
    return resources, texts, canonical


def scan_player(root):
    import UnityPy
    paths = meshes.serialized_paths(root)
    if not paths:
        raise ValueError('No direct-player serialized files: ' + str(root))
    references = meshes._resource_references(root)
    targets = {identity for _, identity in references}
    identities, catalogs = {}, {}
    result = {'textures': collections.defaultdict(list), 'texture_image_bytes': 0,
              'meshes': collections.defaultdict(list), 'mesh_serialized_bytes': 0,
              'quality': [], 'quality_signatures': [], 'files': []}
    for path in paths:
        print('Inspecting ' + str(path), file=sys.stderr, flush=True)
        env = UnityPy.load(str(path)); result['files'].append(path.name)
        for obj in list(env.objects):
            kind, identity = obj.type.name, (path.name, obj.path_id)
            if identity in targets:
                identities[identity] = (kind, obj.peek_name())
                if kind == 'TextAsset':
                    catalogs[identity] = obj.read_typetree(check_read=True).get('m_Script')
            if kind == 'Texture2D':
                name = obj.peek_name()
                if not isinstance(name, str):
                    raise ValueError('Texture2D authored name cannot be inspected: ' + path.name)
                signature, size = read_texture_signature(obj)
                result['textures'][name].append(signature)
                result['texture_image_bytes'] += size
            elif kind == 'Mesh':
                name = obj.peek_name()
                if not isinstance(name, str) or not name:
                    raise ValueError('Mesh authored name cannot be proven: ' + path.name)
                result['meshes'][name].append(meshes.read_mesh_signature(obj))
                result['mesh_serialized_bytes'] += obj.byte_size
            elif kind == 'QualitySettings':
                tree = obj.read_typetree(check_read=True)
                result['quality'].append(tree.get('m_CurrentQuality'))
                result['quality_signatures'].append(meshes.digest(tree))
        obj = None
        del env
        gc.collect()
    result['resource_objects'], result['resource_texts'], result['surface_catalogs'] = resource_evidence(references, identities, catalogs)
    if not result['resource_objects']:
        raise ValueError('No original Resources identities could be proven')
    return result


def resident_errors(before, after, records):
    errors = []
    eligible_names = {row['textureName'] for row in records}
    leaked = sorted(name for name in eligible_names if after.get(name))
    if leaked:
        errors.append('Eligible Texture2D names remain resident (or ambiguous retained name): ' + ', '.join(leaked))
    expected = collections.Counter((name, sig) for name, values in before.items() if name not in eligible_names for sig in values)
    actual = collections.Counter((name, sig) for name, values in after.items() for sig in values)
    for delta, label in [(expected - actual, 'Original noneligible Texture2D payload/count changed or lost'),
                         (actual - expected, 'Unexpected resident Texture2D payload/count')]:
        if delta:
            errors.append(label + ': ' + ', '.join(sorted({name for name, _ in delta})))
    return errors


def coverage_errors(before, bundles, records):
    """Compare complete per-name payload sets; never invent baseline GUIDs."""
    errors, coverage = [], []
    eligible = collections.defaultdict(set)
    for row in records:
        eligible[row['textureName']].add(row['textureGuid'])
    by_name = collections.defaultdict(list)
    for bundle in bundles:
        by_name[bundle['name']].append(bundle)
    for name, guids in sorted(eligible.items()):
        baseline = before.get(name, [])
        expected = set(baseline); actual = {row['signature'] for row in by_name[name]}
        actual_guids = [row['textureGuid'] for row in by_name[name]]
        missing, extra = expected - actual, actual - expected
        exact = bool(expected) and not missing and not extra and len(actual_guids) == len(guids) and set(actual_guids) == guids
        coverage.append({'name': name, 'eligible_guids': len(guids), 'baseline_objects': len(baseline),
                         'baseline_unique_payloads': len(expected), 'bundle_count': len(actual_guids),
                         'bundle_unique_payloads': len(actual), 'exact_coverage': exact,
                         'missing_signatures': sorted(missing), 'extra_signatures': sorted(extra)})
        if not expected:
            errors.append('Texture2D ' + name + ': missing direct-player baseline payload')
        if missing:
            errors.append('Texture2D ' + name + ': uncovered original baseline payload variants: ' + str(len(missing)))
        if extra:
            errors.append('Texture2D ' + name + ': serialized fields or raw compressed pixels changed: ' + str(len(extra)))
        if len(actual_guids) != len(guids) or set(actual_guids) != guids:
            errors.append('Texture2D ' + name + ': not every eligible GUID has exactly one inspected bundle')
    if set(by_name) - set(eligible):
        errors.append('Unexpected texture names in surface bundles')
    return errors, coverage


def invariant_errors(before, after, manifest_bytes):
    errors = []
    def inventory(values):
        return collections.Counter((name, signature) for name, signatures in values.items() for signature in signatures)
    if inventory(before['meshes']) != inventory(after['meshes']):
        errors.append('Original Mesh serialized payload/geometry/count changed')
    if not after['quality'] or any(level != 5 for level in after['quality']):
        errors.append('Player does not prove intended Ultra quality level 5')
    if collections.Counter(before['quality_signatures']) != collections.Counter(after['quality_signatures']):
        errors.append('Original quality/LOD/texture quality configuration changed')
    if before['resource_objects'] != after['resource_objects']:
        errors.append('Original Resources path/type/authored-name/count changed')
    if before['resource_texts'] != after['resource_texts']:
        errors.append('Original Resources TextAsset bytes/count changed')
    if after['surface_catalogs'] != [manifest_bytes]:
        errors.append('Resources surface catalogue differs from external manifest bytes')
    return errors


def validate_bundle_metadata(tree, bundle_name, asset):
    # Mesh proof enforces exact identity, one address, zero external dependencies.
    meshes.validate_bundle_metadata(tree, bundle_name, asset)


def inspect_bundle(path, entry, record):
    import UnityPy
    from UnityPy.files.BundleFile import BundleFile
    from UnityPy.files.SerializedFile import SerializedFile
    from UnityPy.streams import EndianBinaryReader

    class AuditedBundle(BundleFile):
        def read_fs(self, reader):
            self.compressions = []
            position = reader.Position
            declared_size = reader.read_long(); reader.Position = position
            if declared_size != reader.Length:
                raise ValueError(path.name + ': UnityFS file size disagrees with payload/trailing bytes')
            return super().read_fs(reader)

        def decompress_data(self, data, size, flags, index=0):
            compression = int(flags) & 0x3f
            self.compressions.append(compression)
            if compression == 0 and len(data) != size:
                raise ValueError(path.name + ': raw fallback block length differs from uncompressed size')
            return super().decompress_data(data, size, flags, index)

        def read_files(self, reader, files):
            self.payload_crc = zlib.crc32(reader.bytes) & 0xffffffff
            return super().read_files(reader, files)

    env = UnityPy.Environment()
    bundle = AuditedBundle(EndianBinaryReader(path.read_bytes()), env, path.name)
    env.files[path.name] = bundle
    # Unity ChunkBasedCompression keeps incompressible chunks verbatim (flag0).
    # Require LZ4 metadata and at least one LZ4 data chunk, allowing only native
    # raw fallback chunks alongside them; uncompressed and LZMA remain rejected.
    if (bundle.signature != 'UnityFS' or len(bundle.compressions) < 2
            or bundle.compressions[0] not in (2, 3)
            or not any(value in (2, 3) for value in bundle.compressions[1:])
            or any(value not in (0, 2, 3) for value in bundle.compressions[1:])):
        raise ValueError(path.name + ': expected local LZ4 surface bundle')
    if bundle.payload_crc != entry['crc']:
        raise ValueError(path.name + ': Unity uncompressed bundle CRC mismatch')
    objects = list(env.objects)
    textures = [obj for obj in objects if obj.type.name == 'Texture2D']
    metadata = [obj for obj in objects if obj.type.name == 'AssetBundle']
    if len(textures) != 1:
        raise ValueError(path.name + ': expected exactly one original Texture2D; subassets unsupported')
    if len(metadata) != 1:
        raise ValueError(path.name + ': expected exactly one AssetBundle metadata object')
    extras = sorted({obj.type.name for obj in objects if obj.type.name not in ('Texture2D', 'AssetBundle')})
    if extras:
        raise ValueError(path.name + ': unexpected bundled dependencies/subassets: ' + ', '.join(extras))
    validate_bundle_metadata(metadata[0].read_typetree(check_read=True), path.name, entry['asset'])
    container = dict(env.container)
    if set(container) != {entry['asset']}:
        raise ValueError(path.name + ': stable address set differs from manifest')
    addressed = container[entry['asset']].deref()
    texture = textures[0]
    if addressed.type.name != 'Texture2D' or addressed.path_id != texture.path_id or addressed.assets_file is not texture.assets_file:
        raise ValueError(path.name + ': stable address does not resolve the single original Texture2D')
    if texture.peek_name() != record['textureName']:
        raise ValueError(path.name + ': original authored texture name changed')
    serialized_files = [file for file in bundle.files.values() if isinstance(file, SerializedFile)]
    if len(serialized_files) != 1 or texture.assets_file is not serialized_files[0]:
        raise ValueError(path.name + ': expected exactly one self-contained serialized payload')
    if serialized_files[0].externals:
        raise ValueError(path.name + ': unexpected external serialized dependencies')
    if serialized_files[0].script_types or serialized_files[0].ref_types:
        raise ValueError(path.name + ': unexpected serialized script dependencies')
    tree = texture.read_typetree(check_read=True)
    stream_path = tree.get('m_StreamData', {}).get('path', '')
    resource_names = {name for name, file in bundle.files.items() if not isinstance(file, SerializedFile)}
    expected_resources = {stream_path.rsplit('/', 1)[-1]} if stream_path else set()
    if resource_names != expected_resources:
        raise ValueError(path.name + ': unreferenced or missing bundled image resource payload')
    signature, actual_bytes = read_texture_signature(texture)
    accounted_bytes = texture_image_bytes(tree)
    if (actual_bytes != accounted_bytes or type(tree.get('m_CompleteImageSize')) is not int
            or tree['m_CompleteImageSize'] != actual_bytes or entry['bytes'] != actual_bytes or record['bytes'] != actual_bytes):
        raise ValueError(path.name + ': actual native image bytes differ from serialized/manifest accounting')
    result = {'bundle': path.name, 'textureGuid': record['textureGuid'], 'name': record['textureName'],
              'signature': signature, 'crc': bundle.payload_crc, 'image_bytes': actual_bytes,
              'file_bytes': path.stat().st_size,
              'compression': {'metadata_flags': int(bundle.dataflags),
                              'metadata_compression': bundle.compressions[0],
                              'data_compressions': bundle.compressions[1:],
                              'raw_fallback_blocks': bundle.compressions[1:].count(0)}}
    del env, bundle, objects, textures, metadata, container, addressed, texture, tree
    gc.collect()
    return result


def fat32_violations(player):
    return sorted(str(path.relative_to(player.parent)) for path in player.parent.rglob('*')
                  if path.is_file() and path.stat().st_size > FAT32_MAXIMUM)


def validate(baseline, player, build_report):
    report = json.loads(build_report.read_bytes())
    surface_root = player / 'StreamingAssets/TCGStreamedSurfaces'
    manifest_bytes = (surface_root / 'catalog.json').read_bytes()
    manifest = json.loads(manifest_bytes)
    records = validate_catalog(report, manifest)
    by_key = {entry['key']: entry for entry in manifest['entries']}
    by_guid = {}
    for row in records:
        by_guid.setdefault(row['textureGuid'], row)
    before, after = scan_player(baseline), scan_player(player)
    errors = resident_errors(before['textures'], after['textures'], records)
    errors.extend(invariant_errors(before, after, manifest_bytes))
    bundles = []
    for guid, record in sorted(by_guid.items()):
        try:
            entry = by_key[record['key']]
            bundles.append(inspect_bundle(relative_file(surface_root, entry['bundle']), entry, record))
        except (ValueError, TypeError, KeyError, OSError) as error:
            errors.append(str(error))
    failures, coverage = coverage_errors(before['textures'], bundles, records)
    errors.extend(failures)
    expected_files = {entry['bundle'] for entry in manifest['entries']}
    extra_files = sorted(path.name for path in surface_root.iterdir() if path.is_file() and
                         path.name.startswith('surface_') and not path.name.endswith('.manifest') and path.name not in expected_files)
    if extra_files:
        errors.append('Unexpected unmanifested surface bundles: ' + ', '.join(extra_files))
    large_files = fat32_violations(player)
    if large_files:
        errors.append('Files exceed FAT32 maximum: ' + ', '.join(large_files))
    if len(bundles) != len(by_guid):
        errors.append('Not every eligible surface bundle matched the direct-player baseline')
    return {'status': 'passed' if not errors else 'failed', 'errors': errors,
            'baseline': str(baseline), 'player': str(player), 'expected_bundles': len(by_guid),
            'checked_bundles': len(bundles), 'semantic_keys': len(records), 'quality': after['quality'],
            'payload_coverage': coverage, 'matched_payload_groups': sum(row['exact_coverage'] for row in coverage),
            'duplicate_name_groups': [row for row in coverage if row['eligible_guids'] > 1],
            'baseline_texture_objects': sum(map(len, before['textures'].values())),
            'resident_texture_objects': sum(map(len, after['textures'].values())),
            'baseline_texture_image_bytes': before['texture_image_bytes'],
            'resident_texture_image_bytes': after['texture_image_bytes'],
            'verified_bundle_image_bytes': sum(row['image_bytes'] for row in bundles),
            'baseline_mesh_objects': sum(map(len, before['meshes'].values())),
            'resident_mesh_objects': sum(map(len, after['meshes'].values())),
            'baseline_mesh_serialized_bytes': before['mesh_serialized_bytes'],
            'resident_mesh_serialized_bytes': after['mesh_serialized_bytes'],
            'resources_object_count': sum(after['resource_objects'].values()),
            'fat32_violations': large_files, 'bundles': bundles,
            'limits': ['Every original Texture2D field and compressed image byte is compared; only stream coordinates and known build ownership are normalized.',
                       'Direct-player source GUIDs are unavailable. Complete per-name payload sets are compared. Controlled builder/source proof must separately establish original source GUID/path, catalogue unrelated fields and material wiring; serialized file IDs are not original GUID proof.',
                       'A migrated name that also remains resident is conservatively rejected because its eligible/retained source identity is ambiguous.',
                       'Resources comparison covers path/type/authored-name/count and TextAsset bytes. Source proof is required for original prefab/material wiring.',
                       'Run the independent artwork and mesh validators as well.',
                       'Serialized image bytes are artifact data, not runtime headroom. No gameplay, native allocation or release claim follows from this audit.']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('baseline', 'player', 'report', 'output'):
        parser.add_argument('--' + name, type=pathlib.Path, required=True)
    args = parser.parse_args()
    try:
        result = validate(args.baseline.resolve(), args.player.resolve(), args.report.resolve())
    except Exception as error:
        result = {'status': 'failed', 'errors': [type(error).__name__ + ': ' + str(error)]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({key: value for key, value in result.items() if key not in ('bundles', 'payload_coverage', 'duplicate_name_groups')}, indent=2))
    return 0 if result['status'] == 'passed' else 1


if __name__ == '__main__':
    sys.exit(main())
