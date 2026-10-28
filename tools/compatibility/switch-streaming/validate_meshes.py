#!/usr/bin/env python3
"""Read-only artifact proof for the approved character-choice Mesh migration.

Requires UnityPy on PYTHONPATH. --baseline and --player are actual *_Data folders,
not Unity source projects. Output is private build evidence. Mesh geometry is
never decoded, reconstructed, quantized or re-encoded. Every Mesh field is
compared; only stream coordinates and known editor/build ownership are omitted.
Run the independent artwork audit as well; this proof does not replace it.
"""
import argparse
import collections
import gc
import hashlib
import json
import math
import pathlib
import re
import struct
import sys
import zlib

FAT32_MAXIMUM = (1 << 32) - 1
BUILD_METADATA = frozenset(('m_ObjectHideFlags', 'm_CorrespondingSourceObject',
                            'm_PrefabInstance', 'm_PrefabAsset'))
RESOURCE_CATALOG = 'tcgstreaming/meshcatalog'
GUID = re.compile(r'[0-9a-f]{32}')
BODY_PATHS = frozenset('Assets/Mesh/' + gender + '_Combined_LOD' + str(lod) + '.asset'
                       for gender in ('Male', 'Female') for lod in range(4))


def combine_reports(choice, body=None):
    if body is None:
        if any(row.get('path') in BODY_PATHS for row in choice.get('eligible', [])):
            raise ValueError('Body entries require their native preparation/build proof')
        return choice
    if choice.get('status') != 'prepared' or choice.get('bundleCount') != 252 or len(choice.get('eligible', [])) != 252:
        raise ValueError('Body migration requires the complete252 choice catalogue')
    if body.get('status') != 'prepared' or body.get('bundleCount') != 8 or len(body.get('eligible', [])) != 8 or {r.get('path') for r in body['eligible']} != BODY_PATHS:
        raise ValueError('Body migration requires exactly the original eight body paths')
    if body.get('sourceSceneUnchanged') is not True or body.get('previousBundlesUnchanged') is not True or any(
            body.get(field) != value for field, value in [('sceneBindingCount', 224), ('sceneOwnerCount', 56), ('prefabCount', 6), ('previousBundleCount', 252)]):
        raise ValueError('Native body scene/bundle fidelity proof incomplete')
    return dict(choice, eligible=choice['eligible'] + body['eligible'], bundleCount=260)


def _hash_value(hasher, value):
    """Typed, length-delimited incremental hashing without a second giant JSON tree."""
    if value is None:
        hasher.update(b'n')
    elif isinstance(value, bool):
        hasher.update(b't' if value else b'f')
    elif isinstance(value, int):
        data = str(value).encode('ascii'); hasher.update(b'i' + struct.pack('>Q', len(data)) + data)
    elif isinstance(value, float):
        if math.isnan(value):
            raise ValueError('Unsupported NaN Mesh value (payload bits cannot be proven)')
        hasher.update(b'd' + struct.pack('>d', value))
    elif isinstance(value, str):
        data = value.encode('utf-8', 'surrogatepass'); hasher.update(b's' + struct.pack('>Q', len(data)) + data)
    elif isinstance(value, (bytes, bytearray, memoryview)):
        hasher.update(b'b' + struct.pack('>Q', len(value))); hasher.update(value)
    elif isinstance(value, dict):
        if any(not isinstance(key, str) for key in value):
            raise TypeError('Unsupported non-string serialized field')
        if ('m_FileID' in value or 'm_PathID' in value) and value != {'m_FileID': 0, 'm_PathID': 0}:
            raise ValueError('Unsupported Mesh pointer outside known build metadata')
        hasher.update(b'{' + struct.pack('>Q', len(value)))
        for key in sorted(value):
            _hash_value(hasher, key); _hash_value(hasher, value[key])
    elif isinstance(value, (list, tuple)):
        hasher.update(b'[' + struct.pack('>Q', len(value)))
        for child in value:
            _hash_value(hasher, child)
    else:
        raise TypeError('Unsupported serialized value: ' + type(value).__name__)


def digest(value):
    hasher = hashlib.sha256(); _hash_value(hasher, value)
    return hasher.hexdigest()


def mesh_signature(tree, streamed_vertex_data=None):
    """Hash complete serialized payload, substituting only logical vertex bytes.

    UnityPy MeshHelper.process uses ResourceReader.get_resource_data for exactly
    this substitution. Calling that reader directly avoids geometry decoding and
    avoids MeshHelper's mutation of the imported vertex data.
    """
    if not isinstance(tree, dict) or not isinstance(tree.get('m_Name'), str):
        raise ValueError('Mesh lacks its original authored name')
    if not isinstance(tree.get('m_VertexData'), dict) or 'm_DataSize' not in tree['m_VertexData']:
        raise ValueError('Unsupported Mesh vertex layout (m_VertexData/m_DataSize required)')
    if 'm_SubMeshes' not in tree or 'm_IndexBuffer' not in tree:
        raise ValueError('Mesh lacks authored submesh/index data')
    vertex = tree['m_VertexData']
    inline = vertex['m_DataSize']
    if not isinstance(inline, (bytes, bytearray, memoryview, list, tuple)):
        raise TypeError('Unsupported serialized vertex bytes')
    if isinstance(inline, (list, tuple)):
        if any(type(v) is not int or not 0 <= v <= 255 for v in inline):
            raise ValueError('Invalid inline vertex byte array')
        inline = bytes(inline)
    stream = tree.get('m_StreamData', {'path': '', 'offset': 0, 'size': 0})
    if not isinstance(stream, dict) or set(stream) != {'path', 'offset', 'size'}:
        raise ValueError('Unsupported Mesh stream metadata')
    path, offset, size = stream['path'], stream['offset'], stream['size']
    if not isinstance(path, str) or type(offset) is not int or type(size) is not int or offset < 0 or size < 0:
        raise ValueError('Invalid Mesh stream coordinates')
    if path:
        if not size or len(inline) or streamed_vertex_data is None:
            raise ValueError('Missing or ambiguous inline/streamed vertex payload')
        if not isinstance(streamed_vertex_data, (bytes, bytearray, memoryview)) or len(streamed_vertex_data) != size:
            raise ValueError('Truncated or unsupported Mesh streamed vertex payload')
        payload = streamed_vertex_data
    else:
        if size or offset or streamed_vertex_data is not None:
            raise ValueError('Ambiguous Mesh stream metadata without resource path')
        payload = inline
    # Shallow maps only: large blendshape/index arrays remain single allocations.
    logical_vertex = dict(vertex); logical_vertex['m_DataSize'] = payload
    fields = {key: value for key, value in tree.items() if key not in BUILD_METADATA and key != 'm_StreamData'}
    fields['m_VertexData'] = logical_vertex
    return digest(fields)


def read_mesh_signature(obj):
    from UnityPy.helpers.ResourceReader import get_resource_data
    tree = obj.read_typetree(check_read=True)
    stream = tree.get('m_StreamData', {})
    payload = None
    if stream.get('path'):
        # Validate all coordinates before allowing the resource reader to seek.
        for field in ('offset', 'size'):
            if type(stream.get(field)) is not int or stream[field] < 0:
                raise ValueError('Invalid Mesh resource coordinate: ' + field)
        payload = get_resource_data(stream['path'], obj.assets_file, stream['offset'], stream['size'])
    try:
        result = mesh_signature(tree, payload)
    except (ValueError, TypeError) as error:
        raise ValueError('Mesh ' + repr(tree.get('m_Name')) + ': ' + str(error)) from error
    del tree, payload
    return result


def unique_signature(values, label):
    unique = set(values)
    if len(unique) != 1:
        raise ValueError(label + (': missing baseline payload' if not unique else ': ambiguous baseline payloads'))
    return next(iter(unique))


def relative_file(root, relative):
    if not isinstance(relative, str) or not relative:
        raise ValueError('Empty local mesh manifest path')
    path = pathlib.PurePosixPath(relative)
    if path.is_absolute() or '..' in path.parts or '\\' in relative or ':' in relative:
        raise ValueError('Unsafe mesh manifest path')
    resolved = (root / pathlib.Path(*path.parts)).resolve()
    if not resolved.is_relative_to(root.resolve()):
        raise ValueError('Mesh manifest path escapes local build')
    return resolved


def validate_catalog(report, manifest):
    if not isinstance(report, dict) or report.get('status') != 'prepared':
        raise ValueError('Mesh report must have status prepared')
    records = report.get('eligible')
    if not isinstance(records, list) or not records:
        raise ValueError('Mesh report lacks nonempty eligible catalogue')
    if not isinstance(report.get('retained'), (dict, list)):
        raise ValueError('Mesh report lacks explicit retained catalogue')
    seen_keys, seen_paths = set(), set()
    for row in records:
        if not isinstance(row, dict) or set(('key', 'guid', 'path', 'name', 'bytes')) - set(row):
            raise ValueError('Incomplete MeshRecord')
        guid = row['guid']
        if not isinstance(guid, str) or not GUID.fullmatch(guid) or row['key'] != 'mesh:' + guid:
            raise ValueError('Invalid semantic mesh GUID/key')
        if not isinstance(row['name'], str) or not row['name'] or not isinstance(row['path'], str) or not row['path'].startswith('Assets/'):
            raise ValueError('MeshRecord lacks authored identity')
        if any(part in ('', '.', '..') for part in row['path'].split('/')) or '\\' in row['path'] or ':' in row['path']:
            raise ValueError('Unsafe authored mesh path')
        if type(row['bytes']) is not int or row['bytes'] <= 0:
            raise ValueError('Invalid accounted mesh byte estimate')
        if row['key'] in seen_keys or row['path'].lower() in seen_paths:
            raise ValueError('Duplicate or ambiguous mesh key/path')
        seen_keys.add(row['key']); seen_paths.add(row['path'].lower())
    if type(report.get('bundleCount')) is not int or report['bundleCount'] != len(records):
        raise ValueError('One-mesh bundle count differs from eligible catalogue')
    if not isinstance(manifest, dict) or manifest.get('version') != 1 or not isinstance(manifest.get('entries'), list):
        raise ValueError('Unsupported local mesh manifest')
    entries = manifest['entries']
    if any(not isinstance(row, dict) or 'key' not in row for row in entries):
        raise ValueError('Incomplete mesh manifest entry')
    by_key = {row['key']: row for row in entries}
    if len(by_key) != len(entries) or set(by_key) != seen_keys:
        raise ValueError('Manifest semantic keys differ from approved catalogue')
    for record in records:
        entry = by_key[record['key']]
        if entry.get('bundle') != 'mesh_' + record['guid'] or entry.get('asset') != 'assets/tcgstreamedmesh/' + record['guid'] + '.asset':
            raise ValueError('Manifest bundle/stable asset identity changed: ' + record['key'])
        if type(entry.get('bytes')) is not int or entry['bytes'] != record['bytes']:
            raise ValueError('Manifest accounted byte estimate changed: ' + record['key'])
        if type(entry.get('crc')) is not int or not 0 < entry['crc'] <= 0xffffffff:
            raise ValueError('Missing or invalid Unity bundle CRC: ' + record['key'])
    return records


def serialized_paths(root):
    return sorted(path for path in root.iterdir() if path.is_file() and
                  (path.suffix == '.assets' or re.fullmatch(r'level\d+', path.name) or path.name == 'globalgamemanagers'))


def resource_identity(assets_name, external_names, pointer):
    file_id, path_id = pointer.get('m_FileID'), pointer.get('m_PathID')
    if type(file_id) is not int or type(path_id) is not int or file_id < 0 or file_id > len(external_names):
        raise ValueError('Unsupported Resources pointer')
    # Unity intentionally keeps null entries for stripped shader subassets.
    # They have no authored GameObject identity and are not prefab resources.
    if path_id == 0:
        return None
    return (assets_name if file_id == 0 else external_names[file_id - 1], path_id)


def _resource_references(root):
    import UnityPy
    path = root / 'globalgamemanagers'
    env = UnityPy.load(str(path)); references = []
    for obj in env.objects:
        if obj.type.name != 'ResourceManager':
            continue
        tree = obj.read_typetree(check_read=True)
        for name, pointer in tree['m_Container']:
            identity = resource_identity(path.name, [external.name for external in obj.assets_file.externals], pointer)
            if identity is not None:
                references.append((name, identity))
    del env
    if not references:
        raise ValueError('Direct player lacks Resources identity map')
    return references


def resource_evidence(references, identities, catalogs):
    prefabs, manifest_texts = collections.Counter(), []
    for name, identity in references:
        target = identities.get(identity)
        if target is None:
            raise ValueError('Resources identity unresolved: ' + name)
        if target[0] == 'GameObject':
            if not isinstance(target[1], str) or not target[1]:
                raise ValueError('Resources prefab lacks authored name: ' + name)
            prefabs[(name, target[1])] += 1
        if name.lower() == RESOURCE_CATALOG:
            if target[0] != 'TextAsset' or identity not in catalogs:
                raise ValueError('Resources mesh identifier catalogue cannot be resolved')
            manifest_texts.append(catalogs[identity])
    return prefabs, manifest_texts


def scan_player(root):
    import UnityPy
    paths = serialized_paths(root)
    if not paths:
        raise ValueError('No direct-player serialized files: ' + str(root))
    references = _resource_references(root)
    resource_targets = {identity for _, identity in references}
    identities, catalogs = {}, {}
    result = {'meshes': collections.defaultdict(list), 'mesh_serialized_bytes': 0,
              'quality': [], 'quality_signatures': [], 'files': [], 'resource_prefabs': collections.Counter(),
              'mesh_catalogs': []}
    for path in paths:
        print('Inspecting ' + str(path), file=sys.stderr, flush=True)
        env = UnityPy.load(str(path)); result['files'].append(path.name)
        # The environment may lazily load resource files: snapshot object readers
        # but release the environment before loading the next serialized file.
        for obj in list(env.objects):
            kind = obj.type.name
            identity = (path.name, obj.path_id)
            if identity in resource_targets:
                identities[identity] = (kind, obj.peek_name())
                if kind == 'TextAsset':
                    catalogs[identity] = obj.read_typetree(check_read=True).get('m_Script')
            if kind == 'Mesh':
                name = obj.peek_name()
                if not isinstance(name, str) or not name:
                    raise ValueError('Mesh authored name cannot be proven: ' + path.name)
                result['meshes'][name].append(read_mesh_signature(obj))
                result['mesh_serialized_bytes'] += obj.byte_size
            elif kind == 'QualitySettings':
                tree = obj.read_typetree(check_read=True)
                result['quality'].append(tree.get('m_CurrentQuality'))
                result['quality_signatures'].append(digest(tree))
                del tree
        obj = None
        del env
        gc.collect()
    result['resource_prefabs'], result['mesh_catalogs'] = resource_evidence(references, identities, catalogs)
    if not result['resource_prefabs']:
        raise ValueError('No original Resources prefab identities could be proven')
    return result


def resident_errors(before, after, records):
    errors = []
    eligible = {row['name'] for row in records}
    leaked = sorted(name for name in eligible if after.get(name))
    if leaked:
        errors.append('Eligible mesh names remain resident: ' + ', '.join(leaked))
    expected = collections.Counter((name, signature) for name, values in before.items() if name not in eligible for signature in values)
    actual = collections.Counter((name, signature) for name, values in after.items() for signature in values)
    missing, extra = expected - actual, actual - expected
    if missing:
        errors.append('Retained resident Mesh payload/count changed or lost: ' + ', '.join(sorted({name for name, _ in missing})))
    if extra:
        errors.append('Unexpected resident Mesh payload/count: ' + ', '.join(sorted({name for name, _ in extra})))
    return errors


def coverage_errors(before, bundles, records):
    """Prove complete per-name payload coverage without guessing baseline GUIDs.

    A direct player contains names and payloads, but omits source GUIDs. Several
    original source GUIDs can share a name. Compare the entire unique payload set
    for that name, requiring no missing or additional variants. The controlled
    builder proves GUID/source-path/address and renderer-binding association.
    """
    errors, coverage = [], []
    record_counts = collections.Counter(row['name'] for row in records)
    by_name = collections.defaultdict(list)
    for bundle in bundles:
        by_name[bundle['name']].append(bundle['signature'])
    for name, count in sorted(record_counts.items()):
        baseline = before.get(name, [])
        expected, actual = set(baseline), set(by_name.get(name, []))
        missing, extra = expected - actual, actual - expected
        exact = bool(expected) and not missing and not extra and len(by_name[name]) == count
        coverage.append({'name': name, 'eligible_records': count, 'baseline_objects': len(baseline),
                         'baseline_unique_payloads': len(expected), 'bundle_count': len(by_name[name]),
                         'bundle_unique_payloads': len(actual), 'exact_coverage': exact,
                         'missing_signatures': sorted(missing), 'extra_signatures': sorted(extra)})
        if not expected:
            errors.append('Mesh ' + name + ': missing direct-player baseline payload')
        if missing:
            errors.append('Mesh ' + name + ': uncovered original baseline payload variant(s): ' + str(len(missing)))
        if extra:
            errors.append('Mesh ' + name + ': bundle serialized Mesh payload/metadata changed: ' + str(len(extra)))
        if len(by_name[name]) != count:
            errors.append('Mesh ' + name + ': not every eligible GUID has an inspected bundle')
    unexpected = set(by_name) - set(record_counts)
    if unexpected:
        errors.append('Unexpected original mesh names in bundles: ' + ', '.join(sorted(unexpected)))
    return errors, coverage


def validate_bundle_metadata(tree, bundle_name, asset):
    if tree.get('m_Name') != bundle_name or tree.get('m_AssetBundleName') != bundle_name:
        raise ValueError(bundle_name + ': internal bundle identity changed')
    if tree.get('m_Dependencies') != [] or tree.get('m_IsStreamedSceneAssetBundle') is not False:
        raise ValueError(bundle_name + ': external dependencies or scene bundle unsupported')
    container = tree.get('m_Container')
    if not isinstance(container, list) or len(container) != 1 or container[0][0] != asset:
        raise ValueError(bundle_name + ': stable address set differs from manifest')


def inspect_bundle(path, entry, record):
    """Parse an actual UnityFS bundle, including Unity's uncompressed-data CRC."""
    import UnityPy
    from UnityPy.files.BundleFile import BundleFile
    from UnityPy.streams import EndianBinaryReader

    class AuditedBundle(BundleFile):
        def read_fs(self, reader):
            self.compressions = []
            return super().read_fs(reader)

        def decompress_data(self, compressed_data, uncompressed_size, flags, index=0):
            self.compressions.append(int(flags) & 0x3f)
            return super().decompress_data(compressed_data, uncompressed_size, flags, index)

        def read_files(self, reader, files):
            self.payload_crc = zlib.crc32(reader.bytes) & 0xffffffff
            super().read_files(reader, files)

    env = UnityPy.Environment()
    bundle = AuditedBundle(EndianBinaryReader(path.read_bytes()), env, path.name)
    env.files[path.name] = bundle
    if bundle.signature != 'UnityFS' or len(bundle.compressions) < 2 or any(value not in (2, 3) for value in bundle.compressions[1:]):
        raise ValueError(path.name + ': expected local LZ4 Mesh bundle')
    if bundle.payload_crc != entry['crc']:
        raise ValueError(path.name + ': Unity uncompressed bundle CRC mismatch')
    objects = list(env.objects)
    meshes = [obj for obj in objects if obj.type.name == 'Mesh']
    if len(meshes) != 1:
        raise ValueError(path.name + ': expected exactly one original Mesh')
    metadata = [obj for obj in objects if obj.type.name == 'AssetBundle']
    if len(metadata) != 1:
        raise ValueError(path.name + ': expected exactly one AssetBundle metadata object')
    extras = sorted({obj.type.name for obj in objects if obj.type.name not in ('Mesh', 'AssetBundle')})
    if extras:
        raise ValueError(path.name + ': unexpected bundled dependencies: ' + ', '.join(extras))
    validate_bundle_metadata(metadata[0].read_typetree(check_read=True), path.name, entry['asset'])
    container = dict(env.container)
    if set(container) != {entry['asset']}:
        raise ValueError(path.name + ': stable address set differs from manifest')
    addressed = container[entry['asset']].deref()
    if addressed.type.name != 'Mesh' or addressed.path_id != meshes[0].path_id or addressed.assets_file is not meshes[0].assets_file:
        raise ValueError(path.name + ': stable address does not resolve the single original Mesh')
    if meshes[0].peek_name() != record['name']:
        raise ValueError(path.name + ': original authored mesh name changed')
    result = {'bundle': path.name, 'key': record['key'], 'name': record['name'],
              'signature': read_mesh_signature(meshes[0]), 'crc': bundle.payload_crc,
              'file_bytes': path.stat().st_size, 'serialized_mesh_bytes': meshes[0].byte_size,
              'accounted_bytes': record['bytes']}
    del env, bundle, objects, meshes, metadata, container, addressed
    gc.collect()
    return result


def validate(baseline, player, build_report, body_report=None):
    report = combine_reports(json.loads(build_report.read_text()), json.loads(body_report.read_text()) if body_report else None)
    mesh_root = player / 'StreamingAssets/TCGStreamedMeshes'
    manifest_text = (mesh_root / 'catalog.json').read_text()
    manifest = json.loads(manifest_text)
    records = validate_catalog(report, manifest)
    by_key = {entry['key']: entry for entry in manifest['entries']}
    before = scan_player(baseline)
    after = scan_player(player)
    errors = resident_errors(before['meshes'], after['meshes'], records)
    bundles = []
    for record in sorted(records, key=lambda row: row['key']):
        try:
            entry = by_key[record['key']]
            current = inspect_bundle(relative_file(mesh_root, entry['bundle']), entry, record)
            bundles.append(current)
        except (ValueError, TypeError, KeyError, OSError) as error:
            errors.append(str(error))
    coverage_failures, coverage = coverage_errors(before['meshes'], bundles, records)
    errors.extend(coverage_failures)
    if not after['quality'] or any(level != 5 for level in after['quality']):
        errors.append('Player does not prove intended Ultra quality level 5')
    if collections.Counter(before['quality_signatures']) != collections.Counter(after['quality_signatures']):
        errors.append('Original quality/LOD/texture quality configuration changed')
    if before['resource_prefabs'] != after['resource_prefabs']:
        errors.append('Original Resources prefab names/identities/counts changed')
    if after['mesh_catalogs'] != [manifest_text]:
        errors.append('Resources identifier catalogue differs from local external manifest text')
    expected_files = {entry['bundle'] for entry in manifest['entries']}
    extra_files = sorted(path.name for path in mesh_root.iterdir() if path.is_file() and
                         path.name.startswith('mesh_') and not path.name.endswith('.manifest') and path.name not in expected_files)
    if extra_files:
        errors.append('Unexpected unmanifested local mesh bundles: ' + ', '.join(extra_files))
    large_files = sorted(str(path.relative_to(player.parent)) for path in player.parent.rglob('*')
                         if path.is_file() and path.stat().st_size > FAT32_MAXIMUM)
    if large_files:
        errors.append('Files exceed FAT32 maximum: ' + ', '.join(large_files))
    if len(bundles) != len(records):
        errors.append('Not every eligible Mesh bundle matched the direct-player baseline')
    return {'status': 'passed' if not errors else 'failed', 'errors': errors,
            'baseline': str(baseline), 'player': str(player), 'expected_bundles': len(records),
            'checked_bundles': len(bundles), 'semantic_keys': len(records), 'quality': after['quality'],
            'payload_coverage': coverage,
            'matched_payload_groups': sum(row['exact_coverage'] for row in coverage),
            'duplicate_name_groups': [row for row in coverage if row['eligible_records'] > 1],
            'baseline_mesh_objects': sum(map(len, before['meshes'].values())),
            'resident_mesh_objects': sum(map(len, after['meshes'].values())),
            'baseline_mesh_serialized_bytes': before['mesh_serialized_bytes'],
            'resident_mesh_serialized_bytes': after['mesh_serialized_bytes'],
            'resources_prefab_count': sum(after['resource_prefabs'].values()),
            'fat32_violations': large_files, 'bundles': bundles,
            'limits': ['Accounted mesh bytes are build estimates, not actual runtime headroom.',
                       'Original Mesh payload is compared without decoding/reconstructing geometry.',
                       'Direct-player source GUIDs are unavailable. Complete per-name payload sets are compared; source-path/GUID/address and renderer binding association is enforced by the controlled builder, not inferred from baseline names.',
                       'Run the independent artwork audit and source/reference tests for textures, materials and renderer/LOD wiring.',
                       'No emulator/hardware gameplay, native allocation or runtime memory claim follows from this artifact audit.']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('baseline', 'player', 'report', 'output'):
        parser.add_argument('--' + name, type=pathlib.Path, required=True)
    parser.add_argument('--body-report', type=pathlib.Path)
    args = parser.parse_args()
    try:
        result = validate(args.baseline.resolve(), args.player.resolve(), args.report.resolve(), args.body_report.resolve() if args.body_report else None)
    except Exception as error:
        result = {'status': 'failed', 'errors': [type(error).__name__ + ': ' + str(error)]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({key: value for key, value in result.items() if key not in ('bundles', 'payload_coverage', 'duplicate_name_groups')}, indent=2))
    return 0 if result['status'] == 'passed' else 1


if __name__ == '__main__':
    sys.exit(main())
