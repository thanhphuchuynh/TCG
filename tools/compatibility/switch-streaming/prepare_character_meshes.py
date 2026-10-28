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


def _patch_choices(source):
    for signature, item in [("public void setHair(int selection, int slot)", "hairstyle"),
                            ("public void setApparel(int selection, int slot, int materialSelection)", "apparel")]:
        def transform(body, item=item):
            expression = "Object.Instantiate(" + item + ".Mesh, base.gameObject.transform)"
            if preparation.mask_csharp(body).count(expression) != 1:
                raise ValueError("Unknown character part instantiation: " + item)
            return body.replace(expression, "TCG.SwitchStreaming.MeshBundleOwner.Instantiate(" + item + ".Mesh, base.gameObject.transform)")
        source = preparation.edit_method(source, signature, "CHARACTER_" + item.upper(), transform)
    return source


def patch_character(source):
    crlf = "\r\n" in source and "\n" not in source.replace("\r\n", "")
    if crlf:
        source = source.replace("\r\n", "\n")
    source = _patch_choices(source)
    source = preparation.prepend(source, "public void Initialize()",
                                 "\t\tTCG.SwitchStreaming.MeshBundleOwner.PrepareRoot(base.gameObject);",
                                 "CHARACTER_ROOT")
    return source.replace("\n", "\r\n") if crlf else source


def _sha(raw):
    return hashlib.sha256(raw).hexdigest()


def _atomic_write(path, raw):
    temporary = path.with_name(path.name + ".new")
    with temporary.open("xb") as stream:
        stream.write(raw)
    temporary.replace(path)


def prepare(project):
    project = pathlib.Path(project).resolve()
    if project.name != "macos-port-work" or project.parent.name != "switch-on-demand":
        raise ValueError("Only the isolated on-demand clone may be prepared")
    source = project / "Assets/Scripts/Assembly-CSharp/CC/CharacterCustomization.cs"
    saved = project.parent / "character-mesh-source-backup/CharacterCustomization.cs"
    receipt = project / "character-mesh-preparation.json"
    current = source.read_bytes()
    if receipt.is_file():
        data = json.loads(receipt.read_text())
        if data.get("source") != str(source) or data.get("backup") != str(saved) or not saved.is_file():
            raise ValueError("Character source receipt identity/backup changed")
        baseline = saved.read_bytes()
        if b"TCG_STREAMING_" in baseline:
            raise ValueError("Character backup is not an original source")
        version = data.get("version", 1)
        if version == 1:
            # V1 did not record its backup hash. Trust it only when replaying
            # the historical transform proves the exact recorded current bytes.
            if _sha(current) != data.get("sha256") or _patch_choices(baseline.decode("utf-8")).encode("utf-8") != current:
                raise ValueError("Legacy character backup does not reproduce recorded source")
        elif version == 2:
            if _sha(baseline) != data.get("baselineSha256") or data.get("sha256") != data.get("preparedSha256"):
                raise ValueError("Character source backup/receipt changed")
            if _sha(current) not in (data["baselineSha256"], data["preparedSha256"]):
                raise ValueError("Character source has unrecorded edits")
        else:
            raise ValueError("Unknown character source receipt version")
    else:
        if saved.exists() or b"TCG_STREAMING_" in current:
            raise ValueError("Character source/backup requires its original receipt")
        baseline = current
    patched = patch_character(baseline.decode("utf-8")).encode("utf-8")
    # Validate all destinations before writing any source or backup.
    if source.with_name(source.name + ".new").exists() or receipt.with_name(receipt.name + ".new").exists():
        raise ValueError("Unrecorded character source write requires inspection")
    if not saved.exists():
        saved.parent.mkdir(parents=True, exist_ok=True)
        with saved.open("xb") as stream:
            stream.write(baseline)
    if current != patched:
        _atomic_write(source, patched)
    result = {"version": 2, "source": str(source), "backup": str(saved),
              "baselineSha256": _sha(baseline), "preparedSha256": _sha(patched), "sha256": _sha(patched)}
    _atomic_write(receipt, (json.dumps(result, indent=2) + "\n").encode("utf-8"))
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("project", type=pathlib.Path)
    print(json.dumps(prepare(parser.parse_args().project), indent=2))
