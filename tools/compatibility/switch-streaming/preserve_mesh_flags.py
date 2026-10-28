#!/usr/bin/env python3
"""Restore an explicitly recorded authored int32 Mesh usage flag in local bundles.

Requires UnityPy. The request is a JSON list of {bundle: absolute path,
sourceFlag: integer}. Every input is validated before any output is replaced.
Only the recorded flag may differ; all other object bytes, authored Mesh fields,
addresses, metadata, dependencies and embedded resource bytes must survive proof.
Each verified replacement is written atomically as UnityFS/LZ4. No flags are guessed.
"""
import argparse
import collections
import gc
import hashlib
import json
import math
import os
from pathlib import Path, PurePosixPath
import stat
import struct
import sys
import tempfile
import zlib


def _digest(value):
    result = hashlib.sha256()
    def add(item):
        if item is None: result.update(b'n')
        elif isinstance(item, bool): result.update(b't' if item else b'f')
        elif isinstance(item, int): result.update(b'i' + str(item).encode() + b';')
        elif isinstance(item, float):
            if math.isnan(item): raise ValueError('NaN Mesh field cannot be proved')
            result.update(b'd' + struct.pack('>d', item))
        elif isinstance(item, str):
            data = item.encode('utf-8', 'surrogatepass'); result.update(b's' + struct.pack('>Q', len(data)) + data)
        elif isinstance(item, (bytes, bytearray, memoryview)):
            result.update(b'b' + struct.pack('>Q', len(item))); result.update(item)
        elif isinstance(item, dict):
            result.update(b'{' + struct.pack('>Q', len(item)))
            for key in sorted(item): add(key); add(item[key])
        elif isinstance(item, (tuple, list)):
            result.update(b'[' + struct.pack('>Q', len(item)))
            for child in item: add(child)
        else: raise ValueError('Unsupported serialized field: ' + type(item).__name__)
    add(value)
    return result.hexdigest()


def _local_file(value, absolute=True):
    if not isinstance(value, str) or not value or '\x00' in value:
        raise ValueError('Expected an existing local file path')
    path = Path(value)
    if absolute and not path.is_absolute(): raise ValueError('Bundle path must be absolute')
    if '..' in path.parts: raise ValueError('Parent traversal is not permitted')
    path = path.absolute()
    if path.resolve(strict=True) != path: raise ValueError('Symlink paths are not permitted: ' + str(path))
    info = path.stat()
    if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1:
        raise ValueError('Expected a regular file with one link: ' + str(path))
    return path


def _load(data, name):
    import UnityPy
    from UnityPy.files.BundleFile import BundleFile
    from UnityPy.files.SerializedFile import SerializedFile
    from UnityPy.streams import EndianBinaryReader
    class AuditedBundle(BundleFile):
        def read_fs(self, reader):
            self.compressions = []
            header_position = reader.Position
            declared_size = reader.read_long(); reader.Position = header_position
            if declared_size != reader.Length: raise ValueError('UnityFS file size disagrees with payload')
            return super().read_fs(reader)
        def decompress_data(self, compressed_data, uncompressed_size, flags, index=0):
            self.compressions.append(int(flags) & 0x3f)
            return super().decompress_data(compressed_data, uncompressed_size, flags, index)
        def read_files(self, reader, files):
            self.payload_crc = zlib.crc32(reader.bytes) & 0xffffffff
            return super().read_files(reader, files)
    if not data.startswith(b'UnityFS\0'): raise ValueError('Expected UnityFS bundle')
    env = UnityPy.Environment()
    bundle = AuditedBundle(EndianBinaryReader(data), env, name)
    env.files[name] = bundle
    if len(bundle.compressions) < 2 or any(c not in (2, 3) for c in bundle.compressions):
        raise ValueError('Expected LZ4-compressed UnityFS bundle')
    objects = list(env.objects)
    if collections.Counter(obj.type.name for obj in objects) != {'Mesh': 1, 'AssetBundle': 1}:
        raise ValueError('Expected exactly one Mesh and one AssetBundle; no other payload')
    meshes = [obj for obj in objects if obj.type.name == 'Mesh']; mesh = meshes[0]
    metadata = next(obj for obj in objects if obj.type.name == 'AssetBundle')
    serialized = [child for child in bundle.files.values() if isinstance(child, SerializedFile)]
    if len(serialized) != 1 or mesh.assets_file is not metadata.assets_file or serialized[0].externals:
        raise ValueError('Ambiguous serialized files or external dependencies')
    tree = mesh.read_typetree(check_read=True); meta = metadata.read_typetree(check_read=True)
    flag = tree.get('m_MeshUsageFlags')
    if type(flag) is not int or not -0x80000000 <= flag <= 0x7fffffff: raise ValueError('Missing int32 Mesh usage field')
    if not isinstance(tree.get('m_Name'), str) or not tree['m_Name']: raise ValueError('Missing authored Mesh name')
    if not meta.get('m_Name') or meta.get('m_Name') != meta.get('m_AssetBundleName') or meta.get('m_Dependencies') != [] or meta.get('m_IsStreamedSceneAssetBundle') is not False:
        raise ValueError('Unsupported bundle identity/dependencies/scene payload')
    container = meta.get('m_Container')
    if not isinstance(container, list) or len(container) != 1 or not isinstance(container[0][0], str):
        raise ValueError('Expected one stable authored Mesh address')
    addressed = dict(env.container)
    if set(addressed) != {container[0][0]}: raise ValueError('Ambiguous Mesh addresses')
    target = addressed[container[0][0]].deref()
    if target.path_id != mesh.path_id or target.assets_file is not mesh.assets_file:
        raise ValueError('Bundle address does not refer to its sole Mesh')
    resources = {key: child.bytes for key, child in bundle.files.items() if not isinstance(child, SerializedFile)}
    if any(not hasattr(child, 'bytes') for child in bundle.files.values() if not isinstance(child, SerializedFile)):
        raise ValueError('Unsupported nested bundle file')
    stream = tree.get('m_StreamData', {})
    if not isinstance(stream, dict): raise ValueError('Unsupported Mesh stream metadata')
    stream_path = stream.get('path', '')
    if stream_path:
        if not isinstance(stream_path, str) or '\\' in stream_path or '..' in PurePosixPath(stream_path).parts:
            raise ValueError('Unsafe Mesh resource path')
        resource = PurePosixPath(stream_path).name
        if set(resources) != {resource}: raise ValueError('Mesh vertex resource is external or ambiguous')
        offset, size = stream.get('offset'), stream.get('size')
        if type(offset) is not int or type(size) is not int or offset < 0 or size <= 0 or offset + size > len(resources[resource]):
            raise ValueError('Truncated Mesh vertex resource')
    elif resources: raise ValueError('Unreferenced resource payload in Mesh-only bundle')
    fields = {key: value for key, value in tree.items() if key != 'm_MeshUsageFlags'}
    snapshot = {
        'mesh_fields': _digest(fields), 'metadata_raw': metadata.get_raw_data(),
        'resources': {key: hashlib.sha256(value).hexdigest() for key, value in resources.items()},
        'files': [(key, child.flags) for key, child in bundle.files.items()],
        'object_ids': sorted((obj.type.name, obj.path_id, obj.type_id) for obj in objects),
        'unity': (bundle.version, bundle.version_player, bundle.version_engine, serialized[0].unity_version, serialized[0].header.version, serialized[0].header.endian, serialized[0]._m_target_platform),
        'address': container[0][0], 'mesh_name': tree['m_Name'],
    }
    return bundle, mesh, tree, snapshot


def _prove_raw_flag_only(before, after, old, new, endian):
    if len(before) != len(after): raise ValueError('Mesh serialization size changed')
    differences = [i for i, (a, b) in enumerate(zip(before, after)) if a != b]
    if not differences: raise ValueError('Recorded flag change was not serialized')
    first, last = differences[0], differences[-1]
    if last - first >= 4: raise ValueError('Mesh bytes changed outside the usage flag')
    old_bytes, new_bytes = struct.pack(endian + 'i', old), struct.pack(endian + 'i', new)
    candidates = [offset for offset in range(max(0, last - 3), min(first, len(before) - 4) + 1)
                  if before[offset:offset + 4] == old_bytes and after[offset:offset + 4] == new_bytes]
    if len(candidates) != 1: raise ValueError('Usage field byte location is ambiguous')


def _restore_bytes(data, name, desired):
    bundle, mesh, tree, before = _load(data, name)
    previous = tree['m_MeshUsageFlags']
    if previous == desired:
        return data, {'previousFlag': previous, 'sourceFlag': desired, 'changed': False, 'crc': bundle.payload_crc, 'meshName': before['mesh_name'], 'asset': before['address']}
    raw = mesh.get_raw_data(); tree['m_MeshUsageFlags'] = desired
    changed_raw = mesh.save_typetree(tree)
    _prove_raw_flag_only(raw, changed_raw, previous, desired, mesh.reader.endian)
    output = bundle.save(packer='lz4')
    verified, new_mesh, new_tree, after = _load(output, name)
    if new_tree['m_MeshUsageFlags'] != desired or before != after or new_mesh.get_raw_data() != changed_raw:
        raise ValueError('Repacked bundle failed complete unchanged-payload proof')
    return output, {'previousFlag': previous, 'sourceFlag': desired, 'changed': True, 'crc': verified.payload_crc, 'meshName': before['mesh_name'], 'asset': before['address'], 'unchangedMeshFieldsSha256': before['mesh_fields'], 'resourceSha256': before['resources']}


def preserve_request(request):
    """Validate a request file; return evidence after individually atomic replacements."""
    request = _local_file(str(request), absolute=False)
    rows = json.loads(request.read_text(encoding='utf-8'))
    if isinstance(rows, dict) and set(rows) == {'entries'}: rows = rows['entries']
    if not isinstance(rows, list) or not rows: raise ValueError('Request must contain a nonempty entry list')
    entries, seen = [], set()
    for row in rows:
        if not isinstance(row, dict) or set(row) != {'bundle', 'sourceFlag'}: raise ValueError('Expected bundle/sourceFlag entry')
        flag = row['sourceFlag']
        if type(flag) is not int or not -0x80000000 <= flag <= 0x7fffffff: raise ValueError('sourceFlag must be an authored int32 integer')
        path = _local_file(row['bundle'])
        if path in seen: raise ValueError('Duplicate bundle path')
        seen.add(path); entries.append((path, flag))
    staged, evidence = [], []
    try:
        # Verify and stage the complete batch before changing any input.
        for path, flag in entries:
            original = path.read_bytes(); original_hash = hashlib.sha256(original).hexdigest()
            output, report = _restore_bytes(original, path.name, flag)
            report['bundle'] = str(path); report['fileBytes'] = len(output)
            report['sha256'] = hashlib.sha256(output).hexdigest(); evidence.append(report)
            if report['changed']:
                descriptor, temporary = tempfile.mkstemp(prefix='.' + path.name + '.flags-', dir=path.parent)
                staged.append((path, Path(temporary), original_hash))
                with os.fdopen(descriptor, 'wb') as writer:
                    os.fchmod(writer.fileno(), stat.S_IMODE(path.stat().st_mode))
                    writer.write(output); writer.flush(); os.fsync(writer.fileno())
            del original, output
            gc.collect()
        for path, temporary, original_hash in staged:
            if _local_file(str(path)) != path or hashlib.sha256(path.read_bytes()).hexdigest() != original_hash:
                raise ValueError('Bundle changed during preservation: ' + str(path))
        for path, temporary, original_hash in staged: os.replace(temporary, path)
        return {'entries': evidence}
    finally:
        for path, temporary, original_hash in staged:
            if temporary.exists(): temporary.unlink()


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--request', required=True, type=Path)
    args = parser.parse_args(argv)
    try:
        print(json.dumps(preserve_request(args.request), sort_keys=True))
    except Exception as error:
        print('Mesh flag preservation failed: ' + str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
