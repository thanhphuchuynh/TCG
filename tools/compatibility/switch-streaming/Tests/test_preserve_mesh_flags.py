"""Real-bundle preservation tests; fixtures are synthetic and supplied explicitly.

TCG_MESH_FLAG_FIXTURE points to a Unity-built mesh-only LZ4 bundle with flag0.
TCG_MESH_FLAG_BAD_FIXTURE optionally points to a Mesh bundle with a Texture subasset.
"""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

TOOL = Path(__file__).resolve().parents[1] / 'preserve_mesh_flags.py'
FIXTURE = os.environ.get('TCG_MESH_FLAG_FIXTURE')


def inspect(path):
    import UnityPy
    from UnityPy.files.SerializedFile import SerializedFile
    env = UnityPy.load(str(path))
    bundle = next(iter(env.files.values()))
    objects = list(env.objects)
    meshes = [o for o in objects if o.type.name == 'Mesh']
    metadata = [o for o in objects if o.type.name == 'AssetBundle']
    return {'mesh': meshes[0].read_typetree(check_read=True),
            'metadata': metadata[0].read_typetree(check_read=True),
            'raw_resources': {name: child.bytes for name, child in bundle.files.items() if not isinstance(child, SerializedFile)},
            'object_types': sorted(o.type.name for o in objects),
            'container': sorted(env.container)}


@unittest.skipUnless(FIXTURE, 'set TCG_MESH_FLAG_FIXTURE to an actual synthetic Unity bundle')
class MeshFlagPreservationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.bundle = self.root / 'usage_mesh'; shutil.copyfile(FIXTURE, self.bundle)

    def invoke(self, rows):
        request = self.root / 'request.json'; request.write_text(json.dumps(rows))
        return subprocess.run([sys.executable, str(TOOL), '--request', str(request)], capture_output=True, text=True)

    def test_restores_source_flag_and_preserves_every_other_mesh_field(self):
        before = inspect(self.bundle)
        self.assertEqual(before['mesh']['m_MeshUsageFlags'], 0, 'fixture must demonstrate the real Unity flag loss')
        result = self.invoke([{'bundle': str(self.bundle), 'sourceFlag': 1}])
        self.assertEqual(result.returncode, 0, result.stderr)
        after = inspect(self.bundle)
        self.assertEqual(after['mesh'].pop('m_MeshUsageFlags'), 1)
        before['mesh'].pop('m_MeshUsageFlags')
        self.assertEqual(after, before)
        report = json.loads(result.stdout)
        self.assertTrue(report['entries'][0]['changed'])
        self.assertGreater(report['entries'][0]['crc'], 0)

    def test_unknown_authored_bits_are_copied_without_interpreting_them(self):
        result = self.invoke([{'bundle': str(self.bundle), 'sourceFlag': -2147483643}])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(inspect(self.bundle)['mesh']['m_MeshUsageFlags'], -2147483643)

    def test_equal_flag_leaves_original_bundle_bytes_untouched(self):
        before = self.bundle.read_bytes()
        result = self.invoke([{'bundle': str(self.bundle), 'sourceFlag': 0}])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.bundle.read_bytes(), before)
        self.assertFalse(json.loads(result.stdout)['entries'][0]['changed'])

    def test_invalid_later_entry_cannot_change_first_bundle(self):
        before = self.bundle.read_bytes()
        result = self.invoke([{'bundle': str(self.bundle), 'sourceFlag': 1}, {'bundle': str(self.root / 'missing'), 'sourceFlag': 1}])
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.bundle.read_bytes(), before)

    def test_ambiguous_paths_values_and_duplicate_entries_are_rejected(self):
        for flag in (True, -2147483649, 2147483648, 1.5, '1'):
            with self.subTest(flag=flag):
                before = self.bundle.read_bytes()
                self.assertNotEqual(self.invoke([{'bundle': str(self.bundle), 'sourceFlag': flag}]).returncode, 0)
                self.assertEqual(self.bundle.read_bytes(), before)
        row = {'bundle': str(self.bundle), 'sourceFlag': 1}
        self.assertNotEqual(self.invoke([row, row]).returncode, 0)
        link = self.root / 'link'; link.symlink_to(self.bundle)
        self.assertNotEqual(self.invoke([{'bundle': str(link), 'sourceFlag': 1}]).returncode, 0)
        self.assertNotEqual(self.invoke([{'bundle': 'usage_mesh', 'sourceFlag': 1}]).returncode, 0)

    def test_existing_signed_infinities_survive_without_normalization(self):
        import UnityPy
        env = UnityPy.load(str(self.bundle)); archive = next(iter(env.files.values()))
        mesh = next(obj for obj in env.objects if obj.type.name == 'Mesh')
        tree = mesh.read_typetree(check_read=True)
        tree['m_LocalAABB']['m_Center']['x'] = float('inf')
        tree['m_LocalAABB']['m_Center']['y'] = float('-inf')
        mesh.save_typetree(tree); self.bundle.write_bytes(archive.save(packer='lz4'))
        before = inspect(self.bundle)
        result = self.invoke([{'bundle': str(self.bundle), 'sourceFlag': 1}])
        self.assertEqual(result.returncode, 0, result.stderr)
        after = inspect(self.bundle)
        self.assertEqual(after['mesh'].pop('m_MeshUsageFlags'), 1)
        before['mesh'].pop('m_MeshUsageFlags')
        self.assertEqual(after, before)

    def test_streamed_vertex_resource_bytes_and_coordinates_survive(self):
        import UnityPy
        from UnityPy.streams import EndianBinaryReader
        env = UnityPy.load(str(self.bundle)); archive = next(iter(env.files.values()))
        mesh = next(obj for obj in env.objects if obj.type.name == 'Mesh')
        tree = mesh.read_typetree(check_read=True)
        vertices = bytes(tree['m_VertexData']['m_DataSize'])
        resource_name = 'CAB-synthetic-vertex.resS'
        resource = EndianBinaryReader(b'padding-preserve!' + vertices + b'trailing-preserve')
        resource.flags = 0; archive.files[resource_name] = resource
        tree['m_VertexData']['m_DataSize'] = b''
        tree['m_StreamData'] = {'path': 'archive:/CAB-synthetic/' + resource_name, 'offset': 17, 'size': len(vertices)}
        mesh.save_typetree(tree); self.bundle.write_bytes(archive.save(packer='lz4'))
        before = inspect(self.bundle)
        result = self.invoke([{'bundle': str(self.bundle), 'sourceFlag': 1}])
        self.assertEqual(result.returncode, 0, result.stderr)
        after = inspect(self.bundle)
        self.assertEqual(after['mesh'].pop('m_MeshUsageFlags'), 1)
        before['mesh'].pop('m_MeshUsageFlags')
        self.assertEqual(after, before)
        self.assertEqual(after['raw_resources'][resource_name], resource.bytes)

    def test_unaccounted_trailing_payload_is_rejected(self):
        before = self.bundle.read_bytes() + b'unaccounted-trailing-payload'
        self.bundle.write_bytes(before)
        result = self.invoke([{'bundle': str(self.bundle), 'sourceFlag': 1}])
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.bundle.read_bytes(), before)

    def test_texture_payload_is_rejected_without_modifying_bundle(self):
        fixture = os.environ.get('TCG_MESH_FLAG_BAD_FIXTURE')
        if not fixture: self.skipTest('set TCG_MESH_FLAG_BAD_FIXTURE')
        shutil.copyfile(fixture, self.bundle); before = self.bundle.read_bytes()
        result = self.invoke([{'bundle': str(self.bundle), 'sourceFlag': 1}])
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.bundle.read_bytes(), before)


if __name__ == '__main__':
    unittest.main()
