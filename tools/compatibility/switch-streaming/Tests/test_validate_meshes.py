import copy
import importlib.util
import json
import os
import re
import pathlib
import tempfile
import unittest

MODULE = pathlib.Path(__file__).resolve().parents[1] / 'validate_meshes.py'
spec = importlib.util.spec_from_file_location('validate_meshes', MODULE)
validation = importlib.util.module_from_spec(spec) if MODULE.exists() else None
if validation:
    spec.loader.exec_module(validation)


class MeshValidationTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(validation, 'Mesh validator must exist')

    def mesh(self):
        return {'m_Name': 'authored beard LOD0',
                'm_SubMeshes': [{'firstByte': 0, 'indexCount': 3, 'topology': 0}],
                'm_IndexBuffer': [0, 0, 1, 0, 2, 0],
                'm_VertexData': {'m_VertexCount': 3, 'm_Channels': [
                    {'stream': 0, 'offset': 0, 'format': 0, 'dimension': 3},
                    {'stream': 0, 'offset': 12, 'format': 0, 'dimension': 2}],
                    'm_DataSize': bytes(range(60))},
                'm_BindPose': [{'e00': 1., 'e11': 1., 'e22': 1., 'e33': 1.}],
                'm_BoneNameHashes': [34], 'm_RootBoneNameHash': 34,
                'm_BonesAABB': [{'m_Min': [0., 0., 0.], 'm_Max': [1., 1., 1.]}],
                'm_VariableBoneCountWeights': {'m_Data': [1, 2, 3]},
                'm_Shapes': {'vertices': [{'vertex': [1., 2., 3.], 'normal': [0., 1., 0.],
                            'tangent': [1., 0., 0.], 'index': 0}],
                            'shapes': [{'firstVertex': 0, 'vertexCount': 1}],
                            'channels': [{'name': 'smile', 'frameIndex': 0, 'frameCount': 1}],
                            'fullWeights': [100.]},
                'm_CompressedMesh': {'m_Vertices': {'m_NumItems': 0, 'm_Data': []}},
                'm_LocalAABB': {'m_Center': [0., 0., 0.], 'm_Extent': [1., 1., 1.]},
                'm_MeshCompression': 0, 'm_IsReadable': False, 'm_KeepVertices': False,
                'm_KeepIndices': False, 'm_IndexFormat': 0,
                'm_MeshLodInfo': {'m_Lods': [{'indexCount': 3}]},
                'm_StreamData': {'path': '', 'offset': 0, 'size': 0}}

    def record(self):
        guid = '1' * 32
        return {'key': 'mesh:' + guid, 'guid': guid, 'path': 'Assets/Original/Beard.asset',
                'name': 'authored beard LOD0', 'bytes': 1234}

    def catalog(self):
        r = self.record()
        return {'version': 1, 'entries': [{'key': r['key'], 'bundle': 'mesh_' + r['guid'],
                'asset': 'assets/tcgstreamedmesh/' + r['guid'] + '.asset', 'bytes': 1234, 'crc': 3}]}

    def report(self):
        return {'status': 'prepared', 'eligible': [self.record()], 'retained': [], 'bundleCount': 1}

    def test_inline_and_streamed_bytes_compare_without_storage_coordinates(self):
        inline = self.mesh(); moved = copy.deepcopy(inline)
        payload = moved['m_VertexData'].pop('m_DataSize')
        moved['m_VertexData']['m_DataSize'] = b''
        moved['m_StreamData'] = {'path': 'archive:/bundle/external.resS', 'offset': 914, 'size': len(payload)}
        self.assertEqual(validation.mesh_signature(inline), validation.mesh_signature(moved, payload))
        moved['m_StreamData']['path'] = 'different.resS'; moved['m_StreamData']['offset'] = 19
        self.assertEqual(validation.mesh_signature(inline), validation.mesh_signature(moved, payload))
        self.assertNotEqual(validation.mesh_signature(inline), validation.mesh_signature(moved, b'x' + payload[1:]))

    def test_every_authored_field_and_each_vertex_byte_affects_signature(self):
        source = self.mesh(); original = validation.mesh_signature(source)
        for field in source:
            if field in ('m_StreamData', 'm_VertexData'):
                continue
            changed = copy.deepcopy(source); changed[field] = 'renamed' if field == 'm_Name' else {'altered': 1}
            self.assertNotEqual(original, validation.mesh_signature(changed), field)
        for index in range(60):
            changed = copy.deepcopy(source)
            payload = bytearray(changed['m_VertexData']['m_DataSize']); payload[index] ^= 1
            changed['m_VertexData']['m_DataSize'] = payload
            self.assertNotEqual(original, validation.mesh_signature(changed), 'vertex byte ' + str(index))
        changed = copy.deepcopy(source); changed['m_VertexData']['m_Channels'][1]['offset'] = 16
        self.assertNotEqual(original, validation.mesh_signature(changed))
        for field in ['vertex', 'normal', 'tangent', 'index']:
            changed = copy.deepcopy(source); changed['m_Shapes']['vertices'][0][field] = 7
            self.assertNotEqual(original, validation.mesh_signature(changed), field)
        changed = copy.deepcopy(source); changed['new_unrecognized_mesh_field'] = [22]
        self.assertNotEqual(original, validation.mesh_signature(changed))

    def test_only_known_build_pointer_metadata_is_ignored(self):
        source = self.mesh(); moved = copy.deepcopy(source)
        moved['m_CorrespondingSourceObject'] = {'m_FileID': 7, 'm_PathID': 98}
        moved['m_PrefabInstance'] = {'m_FileID': 1, 'm_PathID': 4}
        self.assertEqual(validation.mesh_signature(source), validation.mesh_signature(moved))
        moved['null_pointer_field'] = {'m_FileID': 0, 'm_PathID': 0}
        self.assertNotEqual(validation.mesh_signature(source), validation.mesh_signature(moved))
        moved['unknown_geometry_pointer'] = {'m_FileID': 0, 'm_PathID': 7}
        with self.assertRaisesRegex(ValueError, 'pointer'):
            validation.mesh_signature(moved)

    def test_unsupported_or_ambiguous_payload_fails_closed(self):
        for stream in [{'path': 'file', 'offset': 1, 'size': 60},
                       {'path': '', 'offset': 1, 'size': 60},
                       {'path': 'file', 'offset': 1, 'size': 0}]:
            changed = self.mesh(); changed['m_StreamData'] = stream
            with self.assertRaises(ValueError): validation.mesh_signature(changed)
        changed = self.mesh(); changed['m_VertexData']['m_DataSize'] = b''
        changed['m_StreamData'] = {'path': 'file', 'offset': 0, 'size': 60}
        with self.assertRaises(ValueError): validation.mesh_signature(changed, b'short')
        changed = self.mesh(); changed['extra'] = object()
        with self.assertRaises(TypeError): validation.mesh_signature(changed)
        changed = self.mesh(); changed['m_LocalAABB']['bad'] = float('nan')
        with self.assertRaises(ValueError): validation.mesh_signature(changed)

    def test_uninfluenced_bone_infinite_aabb_limits_are_preserved_exactly(self):
        source = self.mesh(); source['m_BonesAABB'][0]['m_Min'] = [float('inf')] * 3
        source['m_BonesAABB'][0]['m_Max'] = [-float('inf')] * 3
        self.assertEqual(validation.mesh_signature(source), validation.mesh_signature(copy.deepcopy(source)))
        moved = copy.deepcopy(source); moved['m_BonesAABB'][0]['m_Min'][0] = -float('inf')
        self.assertNotEqual(validation.mesh_signature(source), validation.mesh_signature(moved))

    def test_manifest_contract_has_exact_keys_addresses_counts_and_estimates(self):
        self.assertEqual(validation.validate_catalog(self.report(), self.catalog())[0]['name'], self.record()['name'])
        cases = []
        for field in ['key', 'bundle', 'asset', 'bytes', 'crc']:
            altered = self.catalog(); altered['entries'][0][field] = 0 if field in ['bytes', 'crc'] else '../bad'
            cases.append((self.report(), altered))
        duplicate = self.catalog(); duplicate['entries'].append(copy.deepcopy(duplicate['entries'][0]))
        cases.append((self.report(), duplicate))
        for field, value in [('status', 'succeeded'), ('bundleCount', 2), ('eligible', []), ('retained', None)]:
            report = self.report(); report[field] = value; cases.append((report, self.catalog()))
        duplicate_name = self.report(); second = copy.deepcopy(self.record())
        second['guid'] = '2' * 32; second['key'] = 'mesh:' + second['guid']
        duplicate_name['eligible'].append(second)
        cases.append((duplicate_name, self.catalog()))
        for report, catalog in cases:
            with self.subTest(report=report, catalog=catalog), self.assertRaises(ValueError):
                validation.validate_catalog(report, catalog)

    def test_bundle_metadata_has_one_stable_address_and_no_dependencies(self):
        tree = {'m_Name': 'mesh_111', 'm_AssetBundleName': 'mesh_111', 'm_Dependencies': [],
                'm_IsStreamedSceneAssetBundle': False, 'm_Container': [('assets/original.asset', {})]}
        validation.validate_bundle_metadata(tree, 'mesh_111', 'assets/original.asset')
        for field, value in [('m_Name', 'wrong'), ('m_AssetBundleName', 'wrong'),
                             ('m_Dependencies', ['external']), ('m_IsStreamedSceneAssetBundle', True),
                             ('m_Container', [('assets/original.asset', {}), ('assets/original.asset', {})])]:
            altered = copy.deepcopy(tree); altered[field] = value
            with self.assertRaises(ValueError):
                validation.validate_bundle_metadata(altered, 'mesh_111', 'assets/original.asset')

    def test_duplicate_names_require_complete_baseline_payload_coverage(self):
        first = self.record(); second = copy.deepcopy(first)
        second.update(guid='2' * 32, key='mesh:' + '2' * 32, path='Assets/Original/Beard_0.asset')
        report = self.report(); report['eligible'].append(second); report['bundleCount'] = 2
        catalog = self.catalog(); extra = copy.deepcopy(catalog['entries'][0])
        extra.update(key=second['key'], bundle='mesh_' + second['guid'],
                     asset='assets/tcgstreamedmesh/' + second['guid'] + '.asset')
        catalog['entries'].append(extra)
        self.assertEqual(len(validation.validate_catalog(report, catalog)), 2)
        before = {first['name']: ['a', 'a', 'b']}
        bundles = [{'name': first['name'], 'signature': value, 'key': row['key']}
                   for row, value in [(first, 'a'), (second, 'b')]]
        errors, coverage = validation.coverage_errors(before, bundles, [first, second])
        self.assertEqual(errors, [])
        self.assertEqual(coverage[0]['baseline_objects'], 3)
        self.assertEqual(coverage[0]['baseline_unique_payloads'], 2)
        self.assertEqual(coverage[0]['bundle_count'], 2)
        self.assertTrue(coverage[0]['exact_coverage'])
        for altered in [bundles[:1], [bundles[0], {**bundles[1], 'signature': 'a'}],
                        [bundles[0], {**bundles[1], 'signature': 'extra'}],
                        [bundles[0], {**bundles[1], 'name': 'wrong original name'}]]:
            errors, _ = validation.coverage_errors(before, altered, [first, second])
            self.assertTrue(errors)
        errors, _ = validation.coverage_errors({}, bundles, [first, second])
        self.assertTrue(errors)

    def test_signature_swap_between_original_names_is_rejected(self):
        first = self.record(); second = copy.deepcopy(first)
        second.update(name='different original', guid='2' * 32, key='mesh:' + '2' * 32)
        errors, _ = validation.coverage_errors({first['name']: ['a'], second['name']: ['b']},
            [{'name': first['name'], 'signature': 'b', 'key': first['key']},
             {'name': second['name'], 'signature': 'a', 'key': second['key']}], [first, second])
        self.assertTrue(errors)

    def test_resident_inventory_is_exact_not_only_object_count(self):
        before = {'eligible': ['source', 'source'], 'retained': ['unchanged'], 'other': ['payload']}
        after = {'retained': ['unchanged'], 'other': ['payload']}
        # Use the source mesh's real name so the eligible identity is removed.
        record = self.record(); record['name'] = 'eligible'
        self.assertEqual(validation.resident_errors(before, after, [record]), [])
        for altered in [{'retained': ['unchanged']},
                        {'retained': ['changed'], 'other': ['payload']},
                        {'retained': ['unchanged', 'unchanged'], 'other': ['payload']},
                        {**after, 'new': ['payload']}, {**after, 'eligible': ['source']}]:
            self.assertTrue(validation.resident_errors(before, altered, [record]))
        with self.assertRaisesRegex(ValueError, 'ambiguous'):
            validation.unique_signature(['a', 'b'], 'same name')
        with self.assertRaises(ValueError): validation.unique_signature([], 'missing')

    def test_resource_prefab_evidence_must_resolve_every_nonnull_identity(self):
        references = [('hair/prefab', ('resources.assets', 1)), ('tcgstreaming/meshcatalog', ('resources.assets', 2))]
        identities = {('resources.assets', 1): ('GameObject', 'Original Hair'),
                      ('resources.assets', 2): ('TextAsset', 'MeshCatalog')}
        prefabs, catalogs = validation.resource_evidence(references, identities, {('resources.assets', 2): '{}'})
        self.assertEqual(dict(prefabs), {('hair/prefab', 'Original Hair'): 1})
        self.assertEqual(catalogs, ['{}'])
        with self.assertRaisesRegex(ValueError, 'unresolved'):
            validation.resource_evidence(references, {}, {})
        identities[('resources.assets', 2)] = ('GameObject', 'Wrong kind')
        with self.assertRaisesRegex(ValueError, 'catalogue'):
            validation.resource_evidence(references, identities, {})

    def test_null_resource_subassets_are_not_unresolved_prefabs(self):
        self.assertIsNone(validation.resource_identity('globalgamemanagers', ['resources.assets'],
                                                     {'m_FileID': 0, 'm_PathID': 0}))
        self.assertEqual(validation.resource_identity('globalgamemanagers', ['resources.assets'],
                                                     {'m_FileID': 1, 'm_PathID': 12}), ('resources.assets', 12))
        self.assertEqual(validation.resource_identity('resources.assets', [],
                                                     {'m_FileID': 0, 'm_PathID': 12}), ('resources.assets', 12))
        for pointer in [{'m_FileID': 2, 'm_PathID': 12}, {'m_FileID': -1, 'm_PathID': 12},
                        {'m_FileID': 0, 'm_PathID': 'unsupported'}]:
            with self.assertRaises(ValueError): validation.resource_identity('file', [], pointer)

    def test_path_escape_and_symlinks_are_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp) / 'root'; root.mkdir()
            for name in ['../outside', '/outside', 'sub\\file', 'C:drive']:
                with self.assertRaises(ValueError): validation.relative_file(root, name)
            (root / 'escape').symlink_to(root.parent)
            with self.assertRaises(ValueError): validation.relative_file(root, 'escape/outside')
            self.assertEqual(validation.relative_file(root, 'mesh_111'), (root / 'mesh_111').resolve())


class RealMeshBundleTests(unittest.TestCase):
    def setUp(self):
        configured = os.environ.get('TCG_MESH_FIXTURES')
        if not configured:
            self.skipTest('Set TCG_MESH_FIXTURES to the local actual Unity Mesh fixture project')
        self.root = pathlib.Path(configured)
        scene = (self.root / 'Assets/MeshFixtures/Tests.unity').read_text()
        self.crc = int(re.search(r'^  Crc: (\d+)', scene, re.M)[1])
        self.bad_crc = int(re.search(r'^  BadPayloadCrc: (\d+)', scene, re.M)[1])
        self.entry = {'key': 'mesh:' + '1' * 32, 'asset': 'mesh:' + '1' * 32,
                      'crc': self.crc, 'bytes': 1024}
        self.record = {'name': 'rigged', 'key': self.entry['key'], 'bytes': 1024}

    def test_actual_unity_rigged_blendshape_bundle_and_unity_crc(self):
        result = validation.inspect_bundle(self.root / 'fixture-bundles/mesh_rigged', self.entry, self.record)
        self.assertEqual(result['crc'], self.crc)
        self.assertEqual(result['name'], 'rigged')
        self.assertEqual(len(result['signature']), 64)
        bad_entry = dict(self.entry); bad_entry['crc'] ^= 1
        with self.assertRaisesRegex(ValueError, 'CRC mismatch'):
            validation.inspect_bundle(self.root / 'fixture-bundles/mesh_rigged', bad_entry, self.record)
        wrong_name = dict(self.record); wrong_name['name'] = 'renamed'
        with self.assertRaisesRegex(ValueError, 'name changed'):
            validation.inspect_bundle(self.root / 'fixture-bundles/mesh_rigged', self.entry, wrong_name)
        wrong_asset = dict(self.entry); wrong_asset['asset'] = 'assets/wrong.asset'
        with self.assertRaisesRegex(ValueError, 'address set'):
            validation.inspect_bundle(self.root / 'fixture-bundles/mesh_rigged', wrong_asset, self.record)

    def test_actual_unity_hidden_texture_dependency_is_rejected(self):
        entry = dict(self.entry); entry['crc'] = self.bad_crc
        with self.assertRaisesRegex(ValueError, 'Texture2D'):
            validation.inspect_bundle(self.root / 'fixture-bundles/mesh_badpayload', entry, self.record)


if __name__ == '__main__': unittest.main()
