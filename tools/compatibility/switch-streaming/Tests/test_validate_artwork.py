import copy
import importlib.util
import pathlib
import unittest

MODULE = pathlib.Path(__file__).resolve().parents[1] / "validate_artwork.py"
spec = importlib.util.spec_from_file_location("validate_artwork", MODULE)
validation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validation)


class ArtworkValidationTests(unittest.TestCase):
    def sprite(self):
        return {"m_Name": "sample", "m_Rect": {"x": 2, "y": 3, "width": 4, "height": 5},
                "m_Pivot": {"x": .2, "y": .7}, "m_PixelsToUnits": 32,
                "m_Border": {"x": 1, "y": 2, "z": 3, "w": 4},
                "m_RenderDataKey": ["build-local", 12],
                "m_SpriteAtlas": {"m_FileID": 2, "m_PathID": 5},
                "m_RD": {"texture": {"m_FileID": 1, "m_PathID": 90},
                         "alphaTexture": {"m_FileID": 0, "m_PathID": 0},
                         "textureRect": {"x": 2, "y": 3, "width": 4, "height": 5},
                         "uvTransform": {"x": 1, "y": 0}, "settingsRaw": 1,
                         "m_IndexBuffer": [0, 1, 2],
                         "m_VertexData": {"m_VertexCount": 3, "m_DataSize": b"vertices"}}}

    def test_build_local_pointers_do_not_change_authored_geometry(self):
        source = self.sprite(); moved = copy.deepcopy(source)
        moved["m_RenderDataKey"] = ["other-build", 999]
        moved["m_SpriteAtlas"] = {"m_FileID": 7, "m_PathID": 123}
        moved["m_RD"]["texture"] = {"m_FileID": 0, "m_PathID": 45}
        self.assertEqual(validation.sprite_geometry(source), validation.sprite_geometry(moved))

    def test_every_visual_geometry_field_affects_comparison(self):
        source = self.sprite()
        for container, field in [("", "m_Pivot"), ("", "m_PixelsToUnits"), ("", "m_Rect"),
                                 ("", "m_Border"), ("m_RD", "textureRect"),
                                 ("m_RD", "uvTransform"), ("m_RD", "m_IndexBuffer"),
                                 ("m_RD", "m_VertexData")]:
            moved = copy.deepcopy(source)
            (moved[container] if container else moved)[field] = "altered"
            self.assertNotEqual(validation.sprite_geometry(source), validation.sprite_geometry(moved), field)

    def test_sampler_mips_and_compressed_payload_are_preserved(self):
        source = {"m_Width": 16, "m_Height": 16, "m_TextureFormat": 12, "m_MipCount": 5,
                  "m_CompleteImageSize": 128, "m_TextureSettings": {"m_FilterMode": 1, "m_WrapU": 0},
                  "m_StreamData": {"path": "baseline.resS", "offset": 12, "size": 128}}
        moved = copy.deepcopy(source); moved["m_StreamData"]["path"] = "bundle.resS"
        self.assertEqual(validation.texture_signature(source, b"compressed"), validation.texture_signature(moved, b"compressed"))
        self.assertNotEqual(validation.texture_signature(source, b"compressed"), validation.texture_signature(moved, b"different"))
        for field in ["m_Width", "m_Height", "m_TextureFormat", "m_MipCount", "m_TextureSettings"]:
            moved = copy.deepcopy(source); moved[field] = "changed"
            self.assertNotEqual(validation.texture_signature(source, b"compressed"), validation.texture_signature(moved, b"compressed"), field)

    def test_unaccounted_secondary_texture_payload_is_rejected(self):
        source = self.sprite()
        validation.single_texture_sprite(source)
        source["m_RD"]["secondaryTextures"] = [{"name": "normal", "texture": {"m_FileID": 0, "m_PathID": 2}}]
        with self.assertRaises(ValueError): validation.single_texture_sprite(source)
        source = self.sprite(); source["m_RD"]["alphaTexture"]["m_PathID"] = 123
        with self.assertRaises(ValueError): validation.single_texture_sprite(source)

    def test_missing_or_ambiguous_baseline_fails(self):
        with self.assertRaises(ValueError): validation.unique_signature([], "missing")
        with self.assertRaises(ValueError): validation.unique_signature(["a", "b"], "ambiguous")
        self.assertEqual(validation.unique_signature(["a", "a"], "duplicated equivalent"), "a")

    def test_mesh_duplication_growth_is_rejected(self):
        self.assertEqual(validation.mesh_growth({"mesh": 1}, {"mesh": 1}), {})
        self.assertEqual(validation.mesh_growth({"mesh": 1}, {"mesh": 2, "new": 1}), {"mesh": 1, "new": 1})


if __name__ == "__main__": unittest.main()
