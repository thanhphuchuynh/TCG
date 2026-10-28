#!/usr/bin/env python3
"""Read-only validation of a private direct-player artwork migration.

Requires UnityPy on PYTHONPATH. This tool never decodes/re-encodes artwork or
changes game files. Its JSON output belongs with private build evidence.
"""
import argparse
import collections
import gc
import hashlib
import json
import pathlib
import re
import sys

TEXTURE_FIELDS = ("m_Width", "m_Height", "m_TextureFormat", "m_CompleteImageSize",
                  "m_MipCount", "m_MipMap", "m_ImageCount", "m_TextureDimension",
                  "m_ColorSpace", "m_LightmapFormat", "m_TextureSettings", "m_IsReadable",
                  "m_ReadAllowed", "m_IgnoreMasterTextureLimit", "m_IgnoreMipmapLimit",
                  "m_MipsStripped", "m_StreamingMipmaps", "m_StreamingMipmapsPriority",
                  "m_DownscaleFallback", "m_IsAlphaChannelOptional", "m_PlatformBlob")
SPRITE_FIELDS = ("m_Rect", "m_Offset", "m_Pivot", "m_PixelsToUnits", "m_Border",
                 "m_Extrude", "m_IsPolygon", "m_Bones", "m_PhysicsShape")
RENDER_FIELDS = ("textureRect", "textureRectOffset", "atlasRectOffset", "settingsRaw",
                 "downscaleMultiplier", "uvTransform", "indices", "vertices",
                 "m_Bindpose", "m_BlendShapes", "m_IndexBuffer", "m_SourceSkin",
                 "m_SubMeshes", "m_VertexData")
FAT32_MAXIMUM = (1 << 32) - 1


def normalized(value):
    if isinstance(value, (bytes, bytearray)):
        return {"bytes": len(value), "sha256": hashlib.sha256(value).hexdigest()}
    if isinstance(value, dict):
        return {str(key): normalized(child) for key, child in value.items()}
    if isinstance(value, (list, tuple)):
        return [normalized(child) for child in value]
    if value is None or isinstance(value, (str, int, float, bool)):
        return value
    raise TypeError("Unsupported serialized value: " + type(value).__name__)


def digest(value):
    return hashlib.sha256(json.dumps(normalized(value), sort_keys=True, separators=(",", ":"), allow_nan=False).encode()).hexdigest()


def single_texture_sprite(tree):
    render = tree.get("m_RD", {})
    if render.get("secondaryTextures") or render.get("alphaTexture", {}).get("m_PathID", 0):
        raise ValueError("Sprite uses an additional texture payload that this one-texture audit cannot prove")


def sprite_geometry(tree):
    if not isinstance(tree.get("m_RD"), dict) or "m_Rect" not in tree:
        raise ValueError("Sprite lacks authored render data")
    geometry = {key: tree[key] for key in SPRITE_FIELDS if key in tree}
    geometry["m_RD"] = {key: tree["m_RD"][key] for key in RENDER_FIELDS if key in tree["m_RD"]}
    return digest(geometry)


def texture_signature(tree, payload):
    metadata = {key: tree[key] for key in TEXTURE_FIELDS if key in tree}
    return digest({"metadata": metadata, "compressed_image": bytes(payload)})


def unique_signature(values, label):
    unique = set(values)
    if len(unique) != 1:
        raise ValueError(label + (": missing baseline payload" if not unique else ": ambiguous baseline payloads"))
    return next(iter(unique))


def mesh_growth(before, after):
    return {name: count - before.get(name, 0) for name, count in after.items() if count > before.get(name, 0)}


def serialized_paths(root):
    return sorted(path for path in root.iterdir() if path.is_file() and
                  (path.suffix == ".assets" or re.fullmatch(r"level\d+", path.name) or path.name == "globalgamemanagers"))


def scan_player(root, texture_names, sprite_names):
    import UnityPy
    paths = serialized_paths(root)
    if not paths:
        raise ValueError("No direct-player serialized files: " + str(root))
    result = {"textures": collections.defaultdict(list), "sprites": collections.defaultdict(list),
              "meshes": collections.Counter(), "mesh_serialized_bytes": 0,
              "texture_objects": 0, "texture_image_bytes": 0, "quality": [], "files": []}
    for path in paths:
        print("Inspecting " + str(path), flush=True)
        env = UnityPy.load(str(path))
        result["files"].append(path.name)
        # Resolve a sprite's external texture only for selected artwork names.
        # Never expand every mesh's vertex arrays merely to count duplicates.
        for obj in list(env.objects):
            kind = obj.type.name
            if kind == "Texture2D":
                tree = obj.read_typetree()
                name = tree["m_Name"]
                result["texture_objects"] += 1
                result["texture_image_bytes"] += tree["m_CompleteImageSize"]
                if name in texture_names:
                    texture = obj.read()
                    result["textures"][name].append(texture_signature(tree, texture.get_image_data()))
            elif kind == "Sprite":
                name = obj.peek_name()
                if name in sprite_names:
                    sprite = obj.read()
                    texture_name = sprite.m_RD.texture.deref().peek_name()
                    if texture_name in texture_names:
                        sprite_tree = obj.read_typetree()
                        single_texture_sprite(sprite_tree)
                        result["sprites"][(name, texture_name)].append(sprite_geometry(sprite_tree))
            elif kind == "Mesh":
                name = obj.peek_name()
                if name is None:
                    raise ValueError("Mesh name could not be inspected: " + str(path))
                result["meshes"][name] += 1
                result["mesh_serialized_bytes"] += obj.byte_size
            elif kind == "QualitySettings":
                result["quality"].append(obj.read_typetree().get("m_CurrentQuality"))
        del env
        gc.collect()
    return result


def relative_file(root, relative):
    path = pathlib.PurePosixPath(relative)
    if path.is_absolute() or ".." in path.parts or "\\" in relative or ":" in relative:
        raise ValueError("Unsafe artwork manifest path")
    resolved = (root / pathlib.Path(*path.parts)).resolve()
    if not resolved.is_relative_to(root.resolve()):
        raise ValueError("Artwork manifest path escapes local build")
    return resolved


def inspect_bundle(path, records):
    import UnityPy
    env = UnityPy.load(str(path))
    textures = [obj for obj in env.objects if obj.type.name == "Texture2D"]
    if len(textures) != 1:
        raise ValueError(path.name + ": expected exactly one underlying texture, got " + str(len(textures)))
    if any(obj.type.name == "Mesh" for obj in env.objects):
        raise ValueError(path.name + ": unexpected resident geometry in artwork bundle")
    texture = textures[0].read()
    texture_tree = textures[0].read_typetree()
    texture_hash = texture_signature(texture_tree, texture.get_image_data())
    expected_names = {record["textureName"] for record in records}
    if expected_names != {texture.m_Name}:
        raise ValueError(path.name + ": texture identity differs from source catalogue")
    sprites = {}
    container = {key.lower(): value for key, value in env.container.items()}
    for record in records:
        source = record["spritePath"].lower()
        if source not in container:
            raise ValueError(path.name + ": missing original authored asset " + source)
        obj = container[source].deref()
        if obj.type.name != "Sprite":
            raise ValueError(path.name + ": original asset is no longer a Sprite")
        sprite = obj.read()
        if sprite.m_Name != record["spriteName"] or sprite.m_RD.texture.deref().path_id != textures[0].path_id:
            raise ValueError(path.name + ": sprite/source texture association changed")
        sprite_tree = obj.read_typetree()
        single_texture_sprite(sprite_tree)
        sprites[record["key"]] = sprite_geometry(sprite_tree)
    return {"texture": texture.m_Name, "signature": texture_hash, "image_bytes": texture.m_CompleteImageSize,
            "sprite_signatures": sprites, "file_bytes": path.stat().st_size}


def validate(baseline, player, build_report):
    report = json.loads(build_report.read_text())
    if report.get("status") != "succeeded":
        raise ValueError("Input build has not succeeded")
    records = report.get("artwork", [])
    if not records or any(not row.get("textureName") or not row.get("spriteName") for row in records):
        raise ValueError("Build report must contain eligible artwork with authored textureName and spriteName")
    if len({row["key"] for row in records}) != len(records):
        raise ValueError("Duplicate semantic artwork keys in build report")
    art_root = player / "StreamingAssets/TCGStreamedArt"
    manifest = json.loads((art_root / "catalog.json").read_text())
    entries = manifest.get("entries", [])
    by_key = {entry["key"]: entry for entry in entries}
    if len(by_key) != len(entries) or set(by_key) != {row["key"] for row in records}:
        raise ValueError("Manifest semantic keys differ from audited eligible catalogue")
    groups = collections.defaultdict(list)
    for record in records:
        entry = by_key[record["key"]]
        expected_bundle = "art_" + record["textureGuid"]
        if entry["bundle"] != expected_bundle or entry["asset"] != record["spritePath"].lower() or entry["bytes"] != record["bytes"]:
            raise ValueError("Manifest source identity differs from audited catalogue: " + record["key"])
        groups[expected_bundle].append(record)
    if len(groups) != report["textureBundles"]:
        raise ValueError("Texture bundle count differs from audited catalogue")
    texture_names = {row["textureName"] for row in records}
    sprite_names = {row["spriteName"] for row in records}
    before = scan_player(baseline, texture_names, sprite_names)
    after = scan_player(player, texture_names, sprite_names)
    errors, bundles = [], []
    for name, group in sorted(groups.items()):
        try:
            path = relative_file(art_root, name)
            current = inspect_bundle(path, group)
            expected = unique_signature(before["textures"][current["texture"]], "Texture " + current["texture"])
            if current["signature"] != expected:
                raise ValueError(name + ": compressed texture bytes/imported sampler or mip metadata changed")
            if current["image_bytes"] != group[0]["bytes"]:
                raise ValueError(name + ": accounted image bytes differ from serialized texture payload")
            for record in group:
                expected_sprite = unique_signature(before["sprites"][(record["spriteName"], record["textureName"])], "Sprite " + record["spriteName"])
                if current["sprite_signatures"][record["key"]] != expected_sprite:
                    raise ValueError(name + ": authored sprite geometry changed for " + record["key"])
            bundles.append({"bundle": name, "texture": current["texture"], "image_bytes": current["image_bytes"],
                            "file_bytes": current["file_bytes"], "semantic_keys": len(group), "texture_signature": current["signature"]})
        except (ValueError, KeyError, OSError) as error:
            errors.append(str(error))
    leaked = sorted(name for name, values in after["textures"].items() if values)
    if leaked: errors.append("Migrated texture names remain in resident player data: " + ", ".join(leaked))
    increased = mesh_growth(before["meshes"], after["meshes"])
    if increased: errors.append("Resident mesh object multiplicity increased: " + json.dumps(increased, sort_keys=True))
    if after["mesh_serialized_bytes"] > before["mesh_serialized_bytes"]:
        errors.append("Resident serialized mesh bytes increased")
    if not after["quality"] or any(level != 5 for level in after["quality"]):
        errors.append("Built player does not prove Ultra quality level 5")
    large_files = [str(path.relative_to(player.parent)) for path in player.parent.rglob("*") if path.is_file() and path.stat().st_size > FAT32_MAXIMUM]
    if large_files: errors.append("Files exceed FAT32 maximum: " + ", ".join(large_files))
    if len(bundles) != len(groups): errors.append("Not all texture bundles matched baseline artwork")
    return {"status": "passed" if not errors else "failed", "errors": errors,
            "baseline": str(baseline), "player": str(player), "checked_bundles": len(bundles),
            "expected_bundles": len(groups), "semantic_keys": len(records), "quality": after["quality"],
            "baseline_texture_image_bytes": before["texture_image_bytes"], "resident_texture_image_bytes": after["texture_image_bytes"],
            "baseline_mesh_objects": sum(before["meshes"].values()), "resident_mesh_objects": sum(after["meshes"].values()),
            "baseline_mesh_serialized_bytes": before["mesh_serialized_bytes"], "resident_mesh_serialized_bytes": after["mesh_serialized_bytes"],
            "resident_migrated_texture_names": leaked, "mesh_growth": increased, "fat32_violations": large_files,
            "bundles": bundles,
            "limits": ["Serialized image bytes exclude driver copies and runtime allocations.",
                       "Mesh multiplicities and serialized byte totals check duplication; mesh vertex payload equality is outside this artwork migration check.",
                       "No emulator/hardware gameplay or runtime memory claim follows from this artifact audit."]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=pathlib.Path, required=True)
    parser.add_argument("--player", type=pathlib.Path, required=True)
    parser.add_argument("--report", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    try:
        result = validate(args.baseline.resolve(), args.player.resolve(), args.report.resolve())
    except Exception as error:
        result = {"status": "failed", "errors": [type(error).__name__ + ": " + str(error)]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps({key: value for key, value in result.items() if key != "bundles"}, indent=2))
    return 0 if result["status"] == "passed" else 1


if __name__ == "__main__": sys.exit(main())
