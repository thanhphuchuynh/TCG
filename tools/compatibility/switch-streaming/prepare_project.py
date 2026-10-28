#!/usr/bin/env python3
"""Guarded integration of our loader in a private recovered Unity project.

No game sources or artwork are part of this tool. All original files remain in
the user's private project and are backed up before replacement.
"""
import argparse
import hashlib
import json
import pathlib
import re
import shutil


def mask_csharp(source):
    chars = list(source)
    index = 0
    while index < len(source):
        start = index
        if source.startswith("//", index):
            end = source.find("\n", index)
            index = len(source) if end < 0 else end
        elif source.startswith("/*", index):
            end = source.find("*/", index + 2)
            if end < 0:
                raise ValueError("Unclosed C# comment")
            index = end + 2
        elif source.startswith('@"', index):
            index += 2
            while index < len(source):
                if source.startswith('""', index):
                    index += 2
                elif source[index] == '"':
                    index += 1
                    break
                else:
                    index += 1
            else:
                raise ValueError("Unclosed verbatim string")
        elif source[index] in "\"'":
            quote = source[index]
            index += 1
            while index < len(source):
                if source[index] == "\\":
                    index += 2
                elif source[index] == quote:
                    index += 1
                    break
                else:
                    index += 1
            else:
                raise ValueError("Unclosed C# literal")
        else:
            index += 1
            continue
        for at in range(start, min(index, len(chars))):
            if chars[at] not in "\r\n":
                chars[at] = " "
    return "".join(chars)


def block_end(masked, start):
    depth = 0
    for index in range(start, len(masked)):
        if masked[index] == "{":
            depth += 1
        elif masked[index] == "}":
            depth -= 1
            if depth == 0:
                return index
    raise ValueError("Unbalanced C# body")


def method_bounds(source, signature):
    masked = mask_csharp(source)
    matches = list(re.finditer(re.escape(signature), masked))
    if len(matches) != 1:
        raise ValueError("Expected exactly one method: " + signature)
    start = matches[0].end()
    while start < len(masked) and masked[start].isspace():
        start += 1
    if start >= len(masked) or masked[start] != "{":
        raise ValueError("Expected a block method: " + signature)
    return start, block_end(masked, start)


def edit_method(source, signature, marker, transform):
    start, end = method_bounds(source, signature)
    body = source[start + 1:end]
    tag = "// TCG_STREAMING_" + marker
    if tag in body:
        return source
    return source[:start + 1] + "\n\t\t" + tag + "\n" + transform(body) + source[end:]


def wrap_method(source, signature, expression, marker):
    return edit_method(source, signature, marker, lambda body: "\t\tusing (" + expression + ")\n\t\t{" + body + "\n\t\t}\n\t")


def prepend(source, signature, code, marker):
    return edit_method(source, signature, marker, lambda body: code + "\n" + body)


def append(source, signature, code, marker):
    return edit_method(source, signature, marker, lambda body: body + "\n" + code + "\n\t")


def refresh_callback(source, signature):
    def apply(body):
        original = "m_CenterFrameImage.sprite = m_MonsterData.GetIcon(m_CardData.expansionType);"
        if body.count(original) != 1:
            raise ValueError("Unknown web completion refresh")
        return body.replace(original, "SetCardUI(m_CardData);")
    return edit_method(source, signature, "FULL_STREAM_REFRESH", apply)


def patch_card_ui(source):
    scope = "TCG.SwitchStreaming.CardArtSession.Begin(this, () => SetCardUI(m_CardData), m_Card3dUIGroup == null || m_Card3dUIGroup.m_IgnoreCulling)"
    source = wrap_method(source, "public void SetCardUI(CardData cardData)", scope, "CARD_SCOPE")
    source = refresh_callback(source, "private void LoadStreamTextureCompleted(CEventPlayer_LoadStreamTextureCompleted evt)")
    source = append(source, "private void OnDisable()", "\t\tTCG.SwitchStreaming.CardArtSession.Disable(this);", "DISABLE")
    masked = mask_csharp(source)
    if "private void OnEnable()" in masked:
        return append(source, "private void OnEnable()", "\t\tTCG.SwitchStreaming.CardArtSession.Reactivate(this);", "ENABLE")
    match = re.search(r"\bclass\s+CardUI\b", masked)
    if not match:
        raise ValueError("Missing CardUI class")
    end = block_end(masked, masked.index("{", match.end()))
    return source[:end] + "\n\tprivate void OnEnable()\n\t{\n\t\t// TCG_STREAMING_ENABLE\n\t\tTCG.SwitchStreaming.CardArtSession.Reactivate(this);\n\t}\n" + source[end:]


def patch_monster(source):
    source = prepend(source, "public Sprite GetIcon(ECardExpansionType cardExpansionType)",
        '\t\tSprite tcgArt;\n\t\tif (TCG.SwitchStreaming.CardArtSession.TryResolve((int)MonsterType, cardExpansionType == ECardExpansionType.Ghost ? "ghost" : "icon", 0, out tcgArt)) return tcgArt;', "ICON")
    for name, role in [("GetIconFromList", "variant-icon"), ("GetBGFromList", "variant-bg")]:
        source = prepend(source, "public Sprite " + name + "(int index)",
            '\t\tSprite tcgArt;\n\t\tif (index > 0 && TCG.SwitchStreaming.CardArtSession.TryResolve((int)MonsterType, "' + role + '", index, out tcgArt)) return tcgArt;', role.upper())
    return source


def patch_inventory(source):
    signature = "public static Sprite GetTetramonIconSprite(EMonsterType monsterType)"
    def apply(body):
        match = re.search(r"\bint index\s*=.*?;", body)
        if not match:
            raise ValueError("Unknown Tetramon list indexing")
        code = '\n\t\tSprite tcgArt;\n\t\tif (TCG.SwitchStreaming.CardArtSession.TryResolveKey("tetramon:" + index, out tcgArt)) return tcgArt;'
        return body[:match.end()] + code + body[match.end():]
    return edit_method(source, signature, "TETRAMON", apply)


def patch_spawner(source):
    def apply(body):
        owner = "m_Card3dUIList[m_CullIndex].m_CardUI"
        for value, expected in [("false", 2), ("true", 1)]:
            call = "m_Card3dUIList[m_CullIndex].m_CardUIAnimGrp.gameObject.SetActive(value: " + value + ");"
            if body.count(call) != expected:
                raise ValueError("Unknown original world-card culling calls")
            body = body.replace(call, "TCG.SwitchStreaming.CardArtSession.SetVisible(" + owner + ", " + value + ");\n\t\t\t\t" + call)
        magnitude = list(re.finditer(r"float magnitude = [^;]+;", body))
        if len(magnitude) != 1:
            raise ValueError("Unknown original culling distance")
        at = magnitude[0].end()
        body = body[:at] + "\n\t\t\t\tif (magnitude < 10f) TCG.SwitchStreaming.CardArtSession.Prefetch(" + owner + ");" + body[at:]
        anchor = "\n\t\t\tm_CullIndex++;"
        if body.count(anchor) != 1:
            raise ValueError("Unknown cull loop")
        return body.replace(anchor, "\n\t\t\telse if ((bool)m_Card3dUIList[m_CullIndex] && m_Card3dUIList[m_CullIndex].m_IgnoreCulling)\n\t\t\t{\n\t\t\t\tTCG.SwitchStreaming.CardArtSession.SetVisible(" + owner + ", true);\n\t\t\t}" + anchor)
    return edit_method(source, "private void Update()", "WORLD_VISIBILITY", apply)


def patch_opening(source):
    source = append(source, "public void OpenScreen(ECollectionPackType collectionPackType, bool isMultiPack, bool isPremiumPack = false)",
        "\t\tfor (int tcg = 0; tcg < m_ManualOpenCardDataList.Count; tcg++)\n\t\t\tTCG.SwitchStreaming.CardArtSession.Prime(m_Card3dUIList[tcg].m_CardUI);", "PACK_PRIME")
    return prepend(source, "private void Update()", """\t\tif (!m_IsScreenActive)
\t\t{
\t\t\tforeach (var tcgView in m_Card3dUIList) TCG.SwitchStreaming.CardArtSession.CancelPrime(tcgView.m_CardUI);
\t\t}
\t\telse if (m_StateIndex >= 1 && m_StateIndex <= 3)
\t\t{
\t\t\tfor (int tcg = 0; tcg < m_ManualOpenCardDataList.Count; tcg++)
\t\t\t\tif (!TCG.SwitchStreaming.CardArtSession.Ready(m_Card3dUIList[tcg].m_CardUI)) return;
\t\t}""", "PACK_READY")


def page_gate(source, signature, collection, card):
    code = ("\t\tvar tcgOwners = new System.Collections.Generic.List<UnityEngine.Component>();\n"
            "\t\tforeach (var item in " + collection + ") tcgOwners.Add(" + card + ");\n"
            "\t\tTCG.SwitchStreaming.ArtPageGate.Watch(this, tcgOwners);")
    return append(source, signature, code, "PAGE_READY")


def patch_binder(source):
    source = page_gate(source, "public void SetCard(int index, ECardExpansionType cardExpansionType, bool isDimensionCard)", "m_CardList", "item.m_CardUI")
    return page_gate(source, "public void SetSingleCard(int cardIndex, CardData cardData, int cardCount, ECollectionSortingType sortingType)", "m_CardList", "item.m_CardUI")


def patch_deck(source):
    return page_gate(source, "private void EvaluateCardPanelUI()", "m_DeckCardEditPanelUIList", "item.m_CardUI")


def prepare_project(project, tools):
    project, tools = pathlib.Path(project).resolve(), pathlib.Path(tools).resolve()
    scripts = project / "Assets/Scripts/Assembly-CSharp"
    transforms = {"CardUI.cs": patch_card_ui, "MonsterData.cs": patch_monster, "InventoryBase.cs": patch_inventory,
                  "Card3dUISpawner.cs": patch_spawner, "CardOpeningSequence.cs": patch_opening,
                  "BinderPageGrp.cs": patch_binder, "DeckEditScreen.cs": patch_deck}
    if not scripts.is_dir():
        raise ValueError("Not the private recovered game project")
    replacements = {}
    for name, transform in transforms.items():
        path = scripts / name
        if not path.is_file():
            raise ValueError("Missing integration source: " + name)
        original = path.read_text()
        replacements[path] = (original, transform(original))
    for folder in ["Runtime", "Editor"]:
        if not (tools / folder).is_dir():
            raise ValueError("Missing own source folder: " + folder)
    backup = project.parent / "streaming-source-backup"
    receipt = {"files": []}
    for path, (original, patched) in replacements.items():
        relative = path.relative_to(project)
        if original != patched:
            saved = backup / relative
            saved.parent.mkdir(parents=True, exist_ok=True)
            if not saved.exists():
                saved.write_text(original)
            path.write_text(patched)
        receipt["files"].append({"path": str(relative), "sha256": hashlib.sha256(patched.encode()).hexdigest()})
    for folder in ["Runtime", "Editor"]:
        destination = project / "Assets/TCGSwitchStreaming" / folder
        destination.mkdir(parents=True, exist_ok=True)
        for path in (tools / folder).glob("*.cs"):
            shutil.copy2(path, destination / path.name)
    (project / "streaming-preparation.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return receipt


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("project", type=pathlib.Path)
    args = parser.parse_args()
    print(json.dumps(prepare_project(args.project, pathlib.Path(__file__).resolve().parent), indent=2))
