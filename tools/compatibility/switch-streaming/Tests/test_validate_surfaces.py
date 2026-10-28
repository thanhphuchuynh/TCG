"""Host proof plus optional tiny, actual Unity Texture2D bundle fixtures.

Set TCG_SURFACE_FIXTURES to the private surface-runtime-tests project to enable
native-format tests. No commercial source assets are embedded in this suite.
"""
import copy
import importlib.util
import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile
import unittest

MODULE = pathlib.Path(__file__).resolve().parents[1] / 'validate_surfaces.py'
spec = importlib.util.spec_from_file_location('validate_surfaces', MODULE)
validation = importlib.util.module_from_spec(spec) if MODULE.exists() else None
if validation:
    spec.loader.exec_module(validation)


class SurfaceValidationTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(validation, 'Surface validator must exist')

    def texture(self):
        return {'m_Name': 'Original finish', 'm_Width': 4, 'm_Height': 4,
                'm_TextureFormat': 10, 'm_CompleteImageSize': 24, 'm_MipCount': 3,
                'm_ImageCount': 1, 'm_TextureDimension': 2, 'm_MipsStripped': 0,
                'm_IsReadable': False, 'm_StreamingMipmaps': False, 'm_ColorSpace': 1,
                'm_TextureSettings': {'m_FilterMode': 2, 'm_Aniso': 3, 'm_MipBias': 0.,
                                      'm_WrapU': 1, 'm_WrapV': 1, 'm_WrapW': 1},
                'm_PlatformBlob': [], 'image data': bytes(range(24)),
                'm_StreamData': {'path': '', 'offset': 0, 'size': 0}}

    def record(self, guid='1' * 32, index=0):
        return {'key': 'surface:floor:' + str(index) + ':mainTexture',
                'property': 'm_FloorDecoDataList.Array.data[' + str(index) + '].mainTexture',
                'textureGuid': guid, 'texturePath': 'Assets/Finish/' + guid + '.png',
                'textureName': 'Original finish', 'bytes': 24}

    def report(self, records=None):
        records = [self.record()] if records is None else records
        unique = {r['textureGuid']: r for r in records}
        return {'status': 'prepared', 'eligibleRecords': records,
                'retained': [{'guid': '9' * 32, 'reason': 'Material default'}],
                'bundleCount': len(unique), 'recordCount': len(records),
                'imageBytes': sum(r['bytes'] for r in unique.values())}

    def catalog(self, records=None):
        records = [self.record()] if records is None else records
        return {'version': 1, 'entries': [{'key': r['key'], 'bundle': 'surface_' + r['textureGuid'],
                'asset': 'assets/tcgstreamedsurface/' + r['textureGuid'] + '.asset',
                'bytes': r['bytes'], 'crc': 123} for r in records]}

    def test_complete_texture_fields_and_raw_compressed_pixels_are_compared(self):
        tree = self.texture(); expected = validation.texture_signature(tree)
        for field in tree:
            if field in ('image data', 'm_StreamData'):
                continue
            changed = copy.deepcopy(tree); changed[field] = 'altered' if field == 'm_Name' else {'tampered': 1}
            self.assertNotEqual(expected, validation.texture_signature(changed), field)
        for index in range(24):
            changed = copy.deepcopy(tree); data = bytearray(changed['image data']); data[index] ^= 1
            changed['image data'] = data
            self.assertNotEqual(expected, validation.texture_signature(changed), str(index))
        changed = copy.deepcopy(tree); changed['future_unknown_field'] = {'setting': 1}
        self.assertNotEqual(expected, validation.texture_signature(changed))
        changed = copy.deepcopy(tree); changed['m_TextureSettings']['m_Aniso'] = 0
        self.assertNotEqual(expected, validation.texture_signature(changed))

    def test_only_stream_coordinates_and_build_ownership_are_normalized(self):
        tree = self.texture(); moved = copy.deepcopy(tree); payload = moved['image data']
        moved['image data'] = b''
        moved['m_StreamData'] = {'path': 'archive:/local/tex.resS', 'offset': 1234, 'size': 24}
        moved['m_CorrespondingSourceObject'] = {'m_FileID': 2, 'm_PathID': 91}
        moved['m_ObjectHideFlags'] = 1
        self.assertEqual(validation.texture_signature(tree), validation.texture_signature(moved, payload))
        moved['m_StreamData']['offset'] = 44
        self.assertEqual(validation.texture_signature(tree), validation.texture_signature(moved, payload))
        moved['unknown_pointer'] = {'m_FileID': 0, 'm_PathID': 1}
        with self.assertRaisesRegex(ValueError, 'pointer'):
            validation.texture_signature(moved, payload)

    def test_ambiguous_or_truncated_image_storage_is_rejected(self):
        tree = self.texture()
        for stream in [{'path': 'file', 'offset': 1, 'size': 24},
                       {'path': '', 'offset': 1, 'size': 0},
                       {'path': '', 'offset': 0, 'size': 24},
                       {'path': 'file', 'offset': -1, 'size': 24},
                       {'path': 'file', 'offset': 0, 'size': True},
                       {'path': '', 'offset': 0, 'size': 0, 'unknown': 2}]:
            changed = copy.deepcopy(tree); changed['m_StreamData'] = stream
            with self.assertRaises(ValueError): validation.texture_signature(changed)
        tree['image data'] = b''; tree['m_StreamData'] = {'path': 'file', 'offset': 2, 'size': 24}
        with self.assertRaises(ValueError): validation.texture_signature(tree, b'short')
        with self.assertRaises(ValueError): validation.texture_signature(self.texture(), b'extra')

    def test_image_bytes_use_native_format_and_every_mip(self):
        self.assertEqual(validation.texture_image_bytes(self.texture()), 24)
        t = self.texture(); t.update(m_Width=8, m_Height=8, m_MipCount=4, m_TextureFormat=4)
        self.assertEqual(validation.texture_image_bytes(t), 340)
        for field, value in [('m_TextureFormat', 999), ('m_MipCount', 0), ('m_Width', 0),
                             ('m_ImageCount', 2), ('m_TextureDimension', 3), ('m_MipsStripped', 1)]:
            changed = self.texture(); changed[field] = value
            with self.assertRaises(ValueError): validation.texture_image_bytes(changed)

    def test_catalog_aliases_deduplicate_guid_and_exact_property_paths(self):
        records = [self.record(), self.record(index=1)]
        self.assertEqual(validation.validate_catalog(self.report(records), self.catalog(records)), records)
        tables = {'floor': 'm_FloorDecoDataList', 'wall': 'm_WallDecoDataList',
                  'wallbar': 'm_WallBarDecoDataList', 'ceiling': 'm_CeilingDecoDataList'}
        for category, table in tables.items():
            for field in ['mainTexture', 'roughnessMap', 'normalMap']:
                row = self.record(); row['key'] = f'surface:{category}:7:{field}'
                row['property'] = f'{table}.Array.data[7].{field}'
                validation.validate_catalog(self.report([row]), self.catalog([row]))

    def test_catalog_rejects_missing_manifest_bad_counts_identity_and_conflicting_aliases(self):
        base_report, base_manifest = self.report(), self.catalog()
        cases = []
        for key, value in [('status', 'succeeded'), ('recordCount', 0), ('bundleCount', 2),
                           ('imageBytes', 48), ('eligibleRecords', []), ('retained', None)]:
            r = copy.deepcopy(base_report); r[key] = value; cases.append((r, base_manifest))
        for field, value in [('key', 'surface:floor:-1:mainTexture'), ('property', 'm_WallDecoDataList.Array.data[0].mainTexture'),
                             ('textureGuid', '1' * 31), ('texturePath', 'Assets/../evil.png'),
                             ('textureName', ''), ('bytes', True)]:
            r = copy.deepcopy(base_report); r['eligibleRecords'][0][field] = value; cases.append((r, base_manifest))
        for key, value in [('version', 2), ('entries', [])]:
            m = copy.deepcopy(base_manifest); m[key] = value; cases.append((base_report, m))
        for key, value in [('bundle', '../escape'), ('asset', 'assets/tcgstreamedsurface/wrong.asset'),
                           ('bytes', 25), ('crc', 0), ('crc', True)]:
            m = copy.deepcopy(base_manifest); m['entries'][0][key] = value; cases.append((base_report, m))
        m = copy.deepcopy(base_manifest); m['entries'].append(copy.deepcopy(m['entries'][0])); cases.append((base_report, m))
        records = [self.record(), self.record(index=1)]; r, m = self.report(records), self.catalog(records)
        m['entries'][1]['crc'] += 1; cases.append((r, m))
        for field in ['textureName', 'texturePath', 'bytes']:
            rows = copy.deepcopy(records); rows[1][field] = 25 if field == 'bytes' else 'different'
            cases.append((self.report(rows), self.catalog(rows)))
        r = self.report(); r['retained'][0]['guid'] = '1' * 32; cases.append((r, base_manifest))
        for r, m in cases:
            with self.subTest(report=r, manifest=m):
                with self.assertRaises(ValueError): validation.validate_catalog(r, m)

    def test_duplicate_names_require_complete_original_payload_sets(self):
        rows = [self.record(), self.record('2' * 32, 1), self.record(index=2)]
        before = {'Original finish': ['a', 'b', 'a']}
        bundles = [{'textureGuid': '1' * 32, 'name': 'Original finish', 'signature': 'b'},
                   {'textureGuid': '2' * 32, 'name': 'Original finish', 'signature': 'a'}]
        errors, evidence = validation.coverage_errors(before, bundles, rows)
        self.assertEqual(errors, []); self.assertTrue(evidence[0]['exact_coverage'])
        self.assertEqual(evidence[0]['eligible_guids'], 2)
        for changed in [bundles[:1], [dict(bundles[0], signature='a'), bundles[1]],
                        [dict(bundles[0], signature='new'), bundles[1]]]:
            self.assertTrue(validation.coverage_errors(before, changed, rows)[0])
        self.assertTrue(validation.coverage_errors({}, bundles, rows)[0])

    def test_resident_leak_noneligible_bytes_and_multiplicity_are_checked(self):
        before = {'Original finish': ['a', 'a'], 'retained': ['b'], 'unrelated': ['c']}
        after = {'retained': ['b'], 'unrelated': ['c']}
        self.assertEqual(validation.resident_errors(before, after, [self.record()]), [])
        for changed in [{'retained': ['changed'], 'unrelated': ['c']},
                        {'unrelated': ['c']}, {'retained': ['b', 'b'], 'unrelated': ['c']},
                        dict(after, new=['x']), dict(after, **{'Original finish': ['a']})]:
            self.assertTrue(validation.resident_errors(before, changed, [self.record()]))

    def metadata(self):
        return {'m_Name': 'surface_' + '1' * 32, 'm_AssetBundleName': 'surface_' + '1' * 32,
                'm_Dependencies': [], 'm_IsStreamedSceneAssetBundle': False,
                'm_Container': [('assets/tcgstreamedsurface/' + '1' * 32 + '.asset', {})]}

    def test_bundle_metadata_rejects_dependencies_scenes_and_duplicate_addresses(self):
        tree = self.metadata(); name = tree['m_Name']; address = tree['m_Container'][0][0]
        validation.validate_bundle_metadata(tree, name, address)
        for field, value in [('m_Name', 'renamed'), ('m_AssetBundleName', 'renamed'),
                             ('m_Dependencies', ['other']), ('m_IsStreamedSceneAssetBundle', True),
                             ('m_Container', []), ('m_Container', tree['m_Container'] * 2)]:
            changed = copy.deepcopy(tree); changed[field] = value
            with self.assertRaises(ValueError): validation.validate_bundle_metadata(changed, name, address)

    def test_resources_resolve_all_nonnull_identities_and_exact_canonical_bytes(self):
        refs = [('prefab', ('resources.assets', 1)), ('config', ('resources.assets', 2)),
                ('TCGStreaming/SurfaceCatalog', ('resources.assets', 3))]
        ids = {('resources.assets', 1): ('GameObject', 'Original'),
               ('resources.assets', 2): ('TextAsset', 'config'),
               ('resources.assets', 3): ('TextAsset', 'SurfaceCatalog')}
        resources, texts, canonical = validation.resource_evidence(refs, ids,
            {('resources.assets', 2): 'original\r\n', ('resources.assets', 3): '{"version":1}\r\n'})
        self.assertEqual(resources[('prefab', 'GameObject', 'Original')], 1)
        self.assertEqual(canonical, [b'{"version":1}\r\n'])
        self.assertEqual(texts[('config', b'original\r\n')], 1)
        with self.assertRaisesRegex(ValueError, 'unresolved'):
            validation.resource_evidence(refs, {}, {})
        ids[('resources.assets', 3)] = ('GameObject', 'Wrong')
        with self.assertRaisesRegex(ValueError, 'catalogue'):
            validation.resource_evidence(refs, ids, {('resources.assets', 2): 'original\r\n'})

    def inventory(self):
        return {'meshes': {'mesh': ['original-vertices']}, 'quality': [5], 'quality_signatures': ['quality'],
                'resource_objects': {('prefab', 'GameObject', 'Original'): 1},
                'resource_texts': {('config', b'original'): 1}, 'surface_catalogs': []}

    def test_global_invariants_preserve_mesh_geometry_quality_and_resources(self):
        before = self.inventory(); after = self.inventory(); after['surface_catalogs'] = [b'canonical']
        self.assertEqual(validation.invariant_errors(before, after, b'canonical'), [])
        for field, value in [('meshes', {'mesh': ['changed-vertices']}), ('meshes', {'mesh': ['original-vertices'] * 2}),
                             ('quality', [4]), ('quality_signatures', ['changed']),
                             ('resource_objects', {}), ('resource_texts', {}),
                             ('surface_catalogs', []), ('surface_catalogs', [b'canonical\n'])]:
            changed = copy.deepcopy(after); changed[field] = value
            self.assertTrue(validation.invariant_errors(before, changed, b'canonical'), field)

    def test_missing_manifest_cli_writes_failed_evidence_and_exits_nonzero(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp); report = root / 'surface-build.json'; output = root / 'proof.json'
            report.write_text(json.dumps(self.report()))
            process = subprocess.run([sys.executable, str(MODULE), '--baseline', str(root / 'baseline_Data'),
                '--player', str(root / 'player_Data'), '--report', str(report), '--output', str(output)],
                capture_output=True, text=True)
            self.assertEqual(process.returncode, 1)
            evidence = json.loads(output.read_text())
            self.assertEqual(evidence['status'], 'failed')
            self.assertIn('catalog.json', evidence['errors'][0])

    def test_fat32_boundary_is_checked_without_allocating_gigabytes(self):
        self.assertTrue(callable(getattr(validation, 'fat32_violations', None)), 'FAT32 file check must be independently verifiable')
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp); player = root / 'player_Data'; player.mkdir()
            boundary = player / 'at-limit.resS'
            with boundary.open('wb') as output: output.truncate((1 << 32) - 1)
            self.assertEqual(validation.fat32_violations(player), [])
            with boundary.open('ab') as output: output.write(b'x')
            self.assertEqual(validation.fat32_violations(player), ['player_Data/at-limit.resS'])

    def test_paths_reject_escape_and_symlinks(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp) / 'root'; root.mkdir()
            for name in ['../outside', '/outside', 'sub\\file', 'C:drive']:
                with self.assertRaises(ValueError): validation.relative_file(root, name)
            (root / 'escape').symlink_to(root.parent)
            with self.assertRaises(ValueError): validation.relative_file(root, 'escape/outside')


class RealSurfaceBundleTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(validation, 'Surface validator must exist')
        configured = os.environ.get('TCG_SURFACE_FIXTURES')
        if not configured:
            self.skipTest('Set TCG_SURFACE_FIXTURES to the actual Unity surface fixture project')
        self.root = pathlib.Path(configured) / 'fixture-bundles'
        self.guid = '0' * 31 + '1'; self.name = 'surface_' + self.guid
        self.path = self.root / self.name
        self.crc = int(re.search(r'^CRC: (\d+)', self.path.with_suffix('.manifest').read_text(), re.M)[1])
        self.entry = {'key': 'surface:floor:0:mainTexture', 'bundle': self.name,
                      'asset': 'assets/tcgstreamedsurface/' + self.guid + '.asset', 'bytes': 340, 'crc': self.crc}
        self.record = {'textureName': '1', 'textureGuid': self.guid, 'key': self.entry['key'], 'bytes': 340}

    def test_actual_unity_texture_lz4_payload_name_address_and_native_crc(self):
        result = validation.inspect_bundle(self.path, self.entry, self.record)
        self.assertEqual(result['crc'], self.crc); self.assertEqual(result['image_bytes'], 340)
        self.assertEqual(result['name'], '1'); self.assertEqual(len(result['signature']), 64)
        for change, pattern in [(dict(self.entry, crc=self.crc ^ 1), 'CRC mismatch'),
                                (dict(self.entry, bytes=1), 'image bytes'),
                                (dict(self.entry, asset='wrong'), 'address')]:
            with self.assertRaisesRegex(ValueError, pattern): validation.inspect_bundle(self.path, change, self.record)
        with self.assertRaisesRegex(ValueError, 'name changed'):
            validation.inspect_bundle(self.path, self.entry, dict(self.record, textureName='renamed'))

    def test_actual_unity_subasset_extra_texture_is_rejected(self):
        path = self.root / ('surface_' + '0' * 31 + '3')
        entry = dict(self.entry, crc=int(re.search(r'^CRC: (\d+)', path.with_suffix('.manifest').read_text(), re.M)[1]))
        with self.assertRaisesRegex(ValueError, 'exactly one'):
            validation.inspect_bundle(path, entry, self.record)

    def test_actual_unity_trailing_bytes_are_rejected_even_when_native_crc_matches(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = pathlib.Path(tmp) / self.name
            path.write_bytes(self.path.read_bytes() + b'unaccounted trailing payload')
            with self.assertRaisesRegex(ValueError, 'file size|trailing'):
                validation.inspect_bundle(path, self.entry, self.record)

    def test_actual_unity_material_shader_subasset_is_rejected(self):
        path = self.root / ('surface_' + '0' * 31 + '6')
        entry = dict(self.entry, crc=int(re.search(r'^CRC: (\d+)', path.with_suffix('.manifest').read_text(), re.M)[1]))
        with self.assertRaisesRegex(ValueError, 'Material|Shader'):
            validation.inspect_bundle(path, entry, self.record)

    def test_native_unity_chunked_lz4_raw_fallback_preserves_original_pixels(self):
        # Built by Unity's ChunkBasedCompression, rather than a Python repacker.
        root = self.root.parent / 'native-lz4-rawfallback-bundles'
        entry = json.loads((root / 'catalog.json').read_bytes())['entries'][0]
        record = {'textureName': '7', 'textureGuid': '0' * 31 + '7',
                  'key': entry['key'], 'bytes': 512 * 512 * 4}
        import UnityPy
        from UnityPy.files.BundleFile import BundleFile
        from UnityPy.streams import EndianBinaryReader
        class NativeBlockAudit(BundleFile):
            def read_fs(self, reader):
                self.blocks = []
                return super().read_fs(reader)
            def decompress_data(self, data, size, flags, index=0):
                self.blocks.append((int(flags) & 63, len(data), size))
                return super().decompress_data(data, size, flags, index)
        path = root / entry['bundle']
        native = NativeBlockAudit(EndianBinaryReader(path.read_bytes()), UnityPy.Environment(), path.name)
        self.assertIn(native.blocks[0][0], (2, 3))
        data_flags = [block[0] for block in native.blocks[1:]]
        self.assertIn(0, data_flags); self.assertTrue(any(flag in (2, 3) for flag in data_flags))
        for flag, stored, raw in native.blocks[1:]:
            if flag == 0: self.assertEqual(stored, raw)
        env = UnityPy.load(str(path))
        texture = next(obj for obj in env.objects if obj.type.name == 'Texture2D')
        source = (self.root.parent / 'Assets/SurfaceFixtures/7.asset').read_text()
        original_pixels = bytes.fromhex(re.search(r'^  _typelessdata: ([0-9a-fA-F]+)$', source, re.M)[1])
        self.assertEqual(bytes(texture.read().get_image_data()), original_pixels)
        # Native LZ4 chunks may keep incompressible data blocks verbatim.
        try:
            result = validation.inspect_bundle(path, entry, record)
        except ValueError as error:
            self.fail('Native Unity ChunkBasedCompression with exact pixels/CRC was rejected: ' + str(error))
        self.assertEqual(result['compression']['data_compressions'], data_flags)
        self.assertEqual(result['compression']['raw_fallback_blocks'], data_flags.count(0))
        self.assertEqual(result['image_bytes'], len(original_pixels))
        self.assertEqual(result['crc'], entry['crc'])

    def rewrite_bundle(self, mutation, packer='lz4'):
        """Repack tiny native Unity fixture and independently capture data CRC."""
        import UnityPy
        import zlib
        from UnityPy.files.BundleFile import BundleFile
        from UnityPy.streams import EndianBinaryReader
        env = UnityPy.load(str(self.path)); mutation(env)
        data = env.file.save(packer=packer)
        class CrcBundle(BundleFile):
            def read_files(self, reader, files):
                self.native_crc = zlib.crc32(reader.bytes) & 0xffffffff
                super().read_files(reader, files)
        parsed = CrcBundle(EndianBinaryReader(data), UnityPy.Environment(), self.name)
        tmp = tempfile.TemporaryDirectory(); self.addCleanup(tmp.cleanup)
        path = pathlib.Path(tmp.name) / self.name; path.write_bytes(data)
        return path, dict(self.entry, crc=parsed.native_crc)

    def test_actual_external_serialized_dependency_is_rejected(self):
        def add_external(env):
            from UnityPy.files.SerializedFile import FileIdentifier
            texture = next(o for o in env.objects if o.type.name == 'Texture2D')
            external = object.__new__(FileIdentifier)
            external.path = 'hidden_dependency.assets'; external.temp_empty = ''
            external.guid = bytes(16); external.type = 0
            texture.assets_file.externals.append(external)
        path, entry = self.rewrite_bundle(add_external)
        with self.assertRaisesRegex(ValueError, 'external'):
            validation.inspect_bundle(path, entry, self.record)

    def test_actual_unreferenced_blob_is_rejected(self):
        def add_blob(env):
            from UnityPy.streams import EndianBinaryReader
            blob = EndianBinaryReader(bytes(64) + b'unreferenced payload'); blob.flags = 0
            env.file.files['hidden.bytes'] = blob
        path, entry = self.rewrite_bundle(add_blob)
        with self.assertRaisesRegex(ValueError, 'unreferenced|resource'):
            validation.inspect_bundle(path, entry, self.record)

    def test_actual_streamed_and_inline_image_bytes_have_identical_full_signature(self):
        expected = validation.inspect_bundle(self.path, self.entry, self.record)
        def move_to_resource(env):
            from UnityPy.streams import EndianBinaryReader
            obj = next(o for o in env.objects if o.type.name == 'Texture2D')
            tree = obj.read_typetree(check_read=True); payload = tree['image data']
            tree['image data'] = b''
            tree['m_StreamData'] = {'path': 'archive:/local/pixels.resS', 'offset': 32, 'size': len(payload)}
            obj.save_typetree(tree)
            resource = EndianBinaryReader(bytes(32) + payload); resource.flags = 0
            env.file.files['pixels.resS'] = resource
        path, entry = self.rewrite_bundle(move_to_resource)
        streamed = validation.inspect_bundle(path, entry, self.record)
        self.assertEqual(expected['signature'], streamed['signature'])
        self.assertEqual(expected['image_bytes'], streamed['image_bytes'])

    def test_actual_native_metadata_dependencies_duplicate_address_and_compression_are_rejected(self):
        def change_metadata(field, value):
            def mutate(env):
                obj = next(o for o in env.objects if o.type.name == 'AssetBundle')
                tree = obj.read_typetree(check_read=True)
                tree[field] = tree['m_Container'] * 2 if field == 'm_Container' else value
                obj.save_typetree(tree)
            return mutate
        for mutation, pattern in [(change_metadata('m_Dependencies', ['other']), 'dependencies'),
                                  (change_metadata('m_Container', None), 'address')]:
            path, entry = self.rewrite_bundle(mutation)
            with self.assertRaisesRegex(ValueError, pattern): validation.inspect_bundle(path, entry, self.record)
        for packer in ('none', 'lzma', (194, 0)):
            path, entry = self.rewrite_bundle(lambda env: None, packer=packer)
            with self.assertRaisesRegex(ValueError, 'LZ4'):
                validation.inspect_bundle(path, entry, self.record)

    def test_actual_serialized_format_mips_sampler_readability_and_pixels_affect_signature(self):
        import UnityPy
        env = UnityPy.load(str(self.path)); obj = next(o for o in env.objects if o.type.name == 'Texture2D')
        original = obj.read_typetree(check_read=True); expected = validation.texture_signature(original)
        for field, value in [('m_TextureFormat', 5), ('m_MipCount', 1), ('m_IsReadable', False),
                             ('m_IsPreProcessed', True), ('m_ColorSpace', 0)]:
            changed = copy.deepcopy(original); changed[field] = value
            self.assertNotEqual(expected, validation.texture_signature(changed), field)
        changed = copy.deepcopy(original); changed['m_TextureSettings']['m_FilterMode'] = 0
        self.assertNotEqual(expected, validation.texture_signature(changed))
        changed = copy.deepcopy(original); data = bytearray(changed['image data']); data[9] ^= 1
        changed['image data'] = data; self.assertNotEqual(expected, validation.texture_signature(changed))
        # Independently observe the compressed-file CRC differs from Unity's CRC.
        import zlib
        self.assertNotEqual(zlib.crc32(self.path.read_bytes()) & 0xffffffff, self.crc)


if __name__ == '__main__': unittest.main()
