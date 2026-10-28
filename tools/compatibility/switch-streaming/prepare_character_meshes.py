#!/usr/bin/env python3
"""Guarded, private integration before the original appearance setup reads meshes."""
import argparse
import hashlib
import importlib.util
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("streaming_preparation", ROOT / "prepare_project.py")
preparation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preparation)


def patch_character(source):
    for signature, item in [("public void setHair(int selection, int slot)", "hairstyle"),
                            ("public void setApparel(int selection, int slot, int materialSelection)", "apparel")]:
        def transform(body, item=item):
            expression = "Object.Instantiate(" + item + ".Mesh, base.gameObject.transform)"
            if preparation.mask_csharp(body).count(expression) != 1:
                raise ValueError("Unknown character part instantiation: " + item)
            return body.replace(expression, "TCG.SwitchStreaming.MeshBundleOwner.Instantiate(" + item + ".Mesh, base.gameObject.transform)")
        source = preparation.edit_method(source, signature, "CHARACTER_" + item.upper(), transform)
    return source


def prepare(project):
    project = pathlib.Path(project).resolve()
    if project.name != "macos-port-work" or project.parent.name != "switch-on-demand":
        raise ValueError("Only the isolated on-demand clone may be prepared")
    source = project / "Assets/Scripts/Assembly-CSharp/CC/CharacterCustomization.cs"
    saved = project.parent / "character-mesh-source-backup/CharacterCustomization.cs"
    receipt = project / "character-mesh-preparation.json"
    current = source.read_text()
    old = json.loads(receipt.read_text())["sha256"] if receipt.is_file() else None
    baseline = preparation.upgrade_baseline(current, saved, old)
    patched = patch_character(baseline)
    if not saved.exists():
        saved.parent.mkdir(parents=True, exist_ok=True)
        saved.write_text(current)
    if current != patched:
        source.write_text(patched)
    result = {"source": str(source), "backup": str(saved), "sha256": hashlib.sha256(patched.encode()).hexdigest()}
    receipt.write_text(json.dumps(result, indent=2) + "\n")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("project", type=pathlib.Path)
    print(json.dumps(prepare(parser.parse_args().project), indent=2))
