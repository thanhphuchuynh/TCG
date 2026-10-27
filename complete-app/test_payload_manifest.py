import pathlib, tempfile, unittest
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
if __name__=='__main__': unittest.main()
