#!/usr/bin/env python3
"""Opt-in, guarded diagnostics for a private Unity localization implementation.

The transformer imports the preparation tool's C# lexer and block helpers. It
changes only the named getter/method bodies, retaining original call order,
returns, setters, exceptions and finally blocks. It supplies no language fallback.
"""
import argparse
import hashlib
import json
import pathlib
import re
import tempfile

from prepare_project import block_end, mask_csharp, method_bounds

TRACE = "TCG.SwitchStreaming.LocalizationTrace"
ATTRIBUTE = "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]"
MARKER = "TCG_LOCALIZATION_PROBE_V1"
HEADER = re.compile(r"\A\ufeff?// " + MARKER + r" original=([a-f0-9]{64}) body=([a-f0-9]{64})\n")
SNAPSHOT = "Sources == null ? -1 : Sources.Count, GlobalSources == null ? -1 : GlobalSources.Length, mCurrentLanguage == null ? -1 : mCurrentLanguage.Length, mLanguageCode"


def digest(source):
    return hashlib.sha256(source.encode("utf-8")).hexdigest()


def _validate_blocks(source):
    masked = mask_csharp(source)
    depth = 0
    for character in masked:
        if character == "{": depth += 1
        elif character == "}":
            depth -= 1
            if depth < 0: raise ValueError("Unbalanced C# source")
    if depth: raise ValueError("Unbalanced C# source")
    if not re.search(r"\bnamespace\s+I2\.Loc\b", masked) or not re.search(r"\bclass\s+LocalizationManager\b", masked):
        raise ValueError("Expected the private I2.Loc.LocalizationManager implementation")
    for field in ("Sources", "GlobalSources", "mCurrentLanguage", "mLanguageCode"):
        if not re.search(r"\b" + field + r"\b", masked): raise ValueError("Missing diagnostic field: " + field)


def _getter_bounds(source, name):
    masked = mask_csharp(source)
    declarations = list(re.finditer(r"\bpublic\s+static\s+string\s+" + re.escape(name) + r"\s*\{", masked))
    if len(declarations) != 1: raise ValueError("Expected exactly one block property: " + name)
    opening = declarations[0].end() - 1
    ending = block_end(masked, opening)
    getters = list(re.finditer(r"\bget\s*\{", masked[opening + 1:ending]))
    if len(getters) != 1: raise ValueError("Expected exactly one block getter: " + name)
    declaration = opening + 1 + getters[0].start()
    start = opening + 1 + getters[0].end() - 1
    return declaration, start, block_end(masked, start)


def _stage(label):
    return TRACE + '.Stage("' + label + '", ' + SNAPSHOT + ');'


def _instrument_calls(body, name, label, expected, qualifier="", declaration=False):
    """Bracket complete standalone calls, never rewrite condition evaluation or expressions."""
    masked = mask_csharp(body)
    prefix = r"(?:string\s+\w+\s*=\s*)" if declaration else ""
    pattern = re.compile(r"(?m)^([ \t]*)" + prefix + re.escape(qualifier + name) + r"\s*\(")
    matches = list(pattern.finditer(masked))
    if len(matches) != expected: raise ValueError("Unknown initialization call shape: " + qualifier + name)
    changes = []
    for number, match in enumerate(matches):
        opening = match.end() - 1
        depth, end = 1, opening + 1
        while end < len(masked) and depth:
            if masked[end] == "(": depth += 1
            elif masked[end] == ")": depth -= 1
            end += 1
        while end < len(masked) and masked[end].isspace(): end += 1
        if depth or end >= len(masked) or masked[end] != ";": raise ValueError("Expected a standalone initialization statement: " + name)
        end += 1
        indent = match.group(1)
        tag = label + ("." + str(number) if expected > 1 else "")
        original = body[match.start():end]
        changes.append((match.start(), end, indent + _stage(tag + ".before") + "\n" + original + "\n" + indent + _stage(tag + ".after")))
    for start, end, replacement in reversed(changes): body = body[:start] + replacement + body[end:]
    return body


def _instrument_body(body, name):
    calls = {
        "InitializeIfNeeded": [("AutoLoadGlobalParamManagers", "PARAMS", 1), ("UpdateSources", "SOURCES", 1), ("SelectStartupLanguage", "SELECT", 1)],
        "UpdateSources": [("UnregisterDeletededSources", "DELETE", 1), ("RegisterSourceInResources", "RESOURCE", 1), ("RegisterSceneSources", "SCENE", 1)],
        "SelectStartupLanguage": [("GetSetting_String", "PREFS", 1, "PersistentStorage.", True), ("GetCurrentDeviceLanguage", "DEVICE", 1, "", True), ("SetLanguageAndCode", "SET_LANGUAGE", 3)],
    }
    for call in calls.get(name, []): body = _instrument_calls(body, *call)
    return body


def _wrap_body(body, name):
    body = _instrument_body(body, name)
    return ("\n// TCG_LOCALIZATION_BOUNDARY_" + name + "\n" + _stage(name + ".enter") +
            "\ntry\n{" + body + "\n}\ncatch (System.Exception tcgLocalizationProbeError)\n{\n" +
            TRACE + '.Failure("' + name + '", tcgLocalizationProbeError, ' + SNAPSHOT + ");\nthrow;\n}\nfinally\n{\n" +
            _stage(name + ".exit") + "\n}\n")


def patch_localization(source):
    header = HEADER.match(source)
    if header:
        if digest(source[header.end():]) != header.group(2): raise ValueError("Instrumented source was modified; refusing partial/double patch")
        if source.count("// TCG_LOCALIZATION_BOUNDARY_") != 5 or source.count(ATTRIBUTE) != 5: raise ValueError("Incomplete diagnostic patch")
        return source
    if MARKER in source or "TCG_LOCALIZATION_BOUNDARY_" in source: raise ValueError("Unknown or partial diagnostic patch")
    _validate_blocks(source)
    original_hash = digest(source)
    bom = "\ufeff" if source.startswith("\ufeff") else ""
    body = source[len(bom):]
    for name in ("CurrentLanguage", "CurrentLanguageCode"):
        declaration, start, end = _getter_bounds(body, name)
        replacement = ATTRIBUTE + "\n" + body[declaration:start + 1] + _wrap_body(body[start + 1:end], name) + "}"
        body = body[:declaration] + replacement + body[end + 1:]
    for name, signature in (("InitializeIfNeeded", "public static void InitializeIfNeeded()"), ("UpdateSources", "public static bool UpdateSources()"), ("SelectStartupLanguage", "private static void SelectStartupLanguage()")):
        start, end = method_bounds(body, signature)
        declaration = mask_csharp(body).rfind(signature, 0, start)
        replacement = ATTRIBUTE + "\n" + body[declaration:start + 1] + _wrap_body(body[start + 1:end], name) + "}"
        body = body[:declaration] + replacement + body[end + 1:]
    return bom + "// " + MARKER + " original=" + original_hash + " body=" + digest(body) + "\n" + body


def _atomic_write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=path.parent, prefix=path.name + ".", suffix=".tmp", delete=False) as stream:
        temporary = pathlib.Path(stream.name)
        try:
            stream.write(data)
            stream.flush()
        except BaseException:
            temporary.unlink(missing_ok=True)
            raise
    try: temporary.replace(path)
    finally: temporary.unlink(missing_ok=True)


def prepare_localization_probe(project):
    project = pathlib.Path(project).resolve()
    source_path = project / "Assets/Scripts/Assembly-CSharp/I2/Loc/LocalizationManager.cs"
    helper_path = project / "Assets/TCGSwitchStreaming/Runtime/LocalizationTrace.cs"
    backup = project / ".tcg-localization-probe/LocalizationManager.cs.original"
    helper = pathlib.Path(__file__).resolve().parent / "Runtime/LocalizationTrace.cs"
    for path in (source_path, helper_path, backup):
        if path.is_symlink() or not path.resolve().is_relative_to(project): raise ValueError("Diagnostic paths must stay inside the private project")
    if not source_path.is_file(): raise ValueError("Not the private Unity localization project")
    current = source_path.read_bytes().decode("utf-8")
    patched = patch_localization(current)
    if backup.exists():
        original = backup.read_bytes().decode("utf-8")
        expected = patch_localization(original)
        if current not in (original, expected): raise ValueError("Source differs from the preserved diagnostic baseline")
        patched = expected
    elif HEADER.match(current): raise ValueError("Instrumented source has no preserved original backup")
    helper_bytes = helper.read_bytes()
    if helper_path.exists() and helper_path.read_bytes() != helper_bytes: raise ValueError("Existing diagnostic helper differs; refusing replacement")
    # Validate every input and collision before the first mutation. Back up original bytes exactly once.
    if not backup.exists():
        backup.parent.mkdir(parents=True, exist_ok=True)
        with backup.open("xb") as stream: stream.write(current.encode("utf-8"))
    if not helper_path.exists(): _atomic_write(helper_path, helper_bytes)
    if current != patched: _atomic_write(source_path, patched.encode("utf-8"))
    return {"source": str(source_path), "backup": str(backup), "helper": str(helper_path), "sha256": digest(patched)}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("project", type=pathlib.Path, help="Explicit private Unity project; never the original installation")
    args = parser.parse_args()
    print(json.dumps(prepare_localization_probe(args.project), indent=2))
