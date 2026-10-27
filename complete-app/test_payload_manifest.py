import os, pathlib, tempfile, unittest, zipfile
from unittest.mock import patch
from zip_archive import create_archive
from payload_manifest import describe_payload

class PayloadIdentityTests(unittest.TestCase):
    def test_same_size_content_update_gets_new_runtime(self):
        with tempfile.TemporaryDirectory() as tmp:
            game=pathlib.Path(tmp)
            file=game/'Game.app/Contents/Resources/art.asset';file.parent.mkdir(parents=True);file.write_bytes(b'first')
            before=describe_payload(game,'Game.app','Player')
            file.write_bytes(b'other')
            after=describe_payload(game,'Game.app','Player')
            self.assertNotEqual(before['id'],after['id'])
    def test_config_changes_keep_runtime_and_existing_settings(self):
        with tempfile.TemporaryDirectory() as tmp:
            game=pathlib.Path(tmp)
            f=game/'Game.app/Contents/MacOS/Player';f.parent.mkdir(parents=True);f.write_bytes(b'player')
            config=game/'BepInEx/config/test.cfg';config.parent.mkdir(parents=True);config.write_bytes(b'initial')
            before=describe_payload(game,'Game.app','Player')
            config.write_bytes(b'changed configuration with another size')
            after=describe_payload(game,'Game.app','Player')
            self.assertEqual(before['id'],after['id'])
            self.assertNotEqual(before['byteCount'],after['byteCount'])
            self.assertNotIn('BepInEx/config/test.cfg',after['requiredSizes'])
class ArchiveTests(unittest.TestCase):
    def test_zip64_offsets_crc_and_executable_permissions(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=pathlib.Path(tmp);app=root/'Test Game.app';binary=app/'Contents/MacOS/Player'
            binary.parent.mkdir(parents=True);data=os.urandom(4096);binary.write_bytes(data);binary.chmod(0o755)
            archive=root/'complete.zip'
            # Exercise large-offset end records without allocating multi-gigabyte test files.
            with patch.object(zipfile,'ZIP64_LIMIT',1024): create_archive(app,archive)
            self.assertIn(b'PK\x06\x06',archive.read_bytes()[-100:])
            with zipfile.ZipFile(archive) as z:
                self.assertIsNone(z.testzip())
                info=z.getinfo('Test Game.app/Contents/MacOS/Player')
                self.assertEqual(z.read(info),data)
                self.assertTrue((info.external_attr>>16)&0o111)
                self.assertEqual(z.infolist()[0].header_offset,0)

if __name__=='__main__': unittest.main()
