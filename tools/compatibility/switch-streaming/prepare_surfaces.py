#!/usr/bin/env python3
"""Guarded finish setters; original getter/clamp, colors, smoothness and bars remain."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re

ROOT=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('streaming_preparation',ROOT/'prepare_project.py')
preparation=importlib.util.module_from_spec(spec);spec.loader.exec_module(preparation)
SLOTS={'_MainTex':'mainTexture','_MetallicGlossMap':'roughnessMap','_BumpMap':'normalMap'}
GROUPS={'floorDecoData':('floor','Floor'),'wallDecoData':('wall','Wall'),'wallBarDecoData':('wallbar','WallBar'),'ceilingDecoData':('ceiling','Ceiling')}
SETTER=re.compile(r'CSingleton<ShopCustomizationManager>\.Instance\.(m_[A-Za-z]+MatB?)\.SetTexture\("([^"\n]+)", ([A-Za-z]+DecoData)\.([A-Za-z]+)\);')

def patch_surfaces(source):
    for category,expected in [('Floor',6),('Wall',12),('Ceiling',6)]:
        def transform(body, category=category, expected=expected):
            masked=preparation.mask_csharp(body)
            if masked.count('.SetTexture(')!=expected:
                raise ValueError('Unknown finish texture setter count: '+category)
            variables=[category[0].lower()+category[1:]+'DecoData']
            if category=='Wall':variables.append('wallBarDecoData')
            for variable in variables:
                semantic,title=GROUPS[variable]
                declaration='ShopDecoData '+variable+' = InventoryBase.Get'+title+'DecoData(index);'
                places=[m.start() for m in re.finditer(re.escape(declaration),body) if preparation.mask_csharp(body)[m.start():].startswith('ShopDecoData ')]
                if len(places)!=1:raise ValueError('Unknown finish getter declaration: '+title)
                position=places[0]+len(declaration)
                body=body[:position]+'\n\t\tint tcg'+title+'TextureIndex = CSingleton<InventoryBase>.Instance.m_ObjectData_SO.m_'+title+'DecoDataList.IndexOf('+variable+');'+body[position:]
            masked=preparation.mask_csharp(body)
            matches=[m for m in SETTER.finditer(body) if masked[m.start():m.start()+10]=='CSingleton']
            if len(matches)!=expected:raise ValueError('Unknown finish setter expression: '+category)
            for match in reversed(matches):
                material,slot,variable,field=match.groups()
                if variable not in variables or SLOTS.get(slot)!=field:raise ValueError('Unknown finish texture field/slot')
                semantic,title=GROUPS[variable]
                if material not in ('m_'+title+'Mat','m_'+title+'MatB'):raise ValueError('Unknown finish material owner')
                instance='CSingleton<ShopCustomizationManager>.Instance'
                key='"surface:'+semantic+':" + tcg'+title+'TextureIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":'+field+'"'
                replacement='TCG.SwitchStreaming.SurfaceTextures.SetTexture('+instance+'.'+material+', '+instance+'.gameObject, "'+slot+'", '+key+', '+variable+'.'+field+');'
                body=body[:match.start()]+replacement+body[match.end():]
            return body
        source=preparation.edit_method(source,'public static void Change'+category+'Material(int index, bool isShopLotB)','SURFACE_'+category.upper(),transform)
    return source


def prepare(project):
    project=Path(project).resolve()
    if project.name!='macos-port-work' or project.parent.name!='switch-on-demand':raise ValueError('Only the isolated on-demand clone may be prepared')
    source=project/'Assets/Scripts/Assembly-CSharp/ShopCustomizationManager.cs'
    saved=project.parent/'surface-source-backup/ShopCustomizationManager.cs'
    receipt=project/'surface-preparation.json'
    current_bytes=source.read_bytes();current=source.read_text()
    hash_bytes=lambda data:hashlib.sha256(data).hexdigest()
    original_bytes=current_bytes
    if receipt.exists():
        recorded=json.loads(receipt.read_text())
        if recorded.get('version')!=1 or recorded.get('source')!=str(source) or recorded.get('backup')!=str(saved) or not saved.is_file():
            raise ValueError('Incomplete or unrecognized surface source receipt')
        original_bytes=saved.read_bytes()
        if hash_bytes(original_bytes)!=recorded.get('original_sha256'):
            raise ValueError('Original surface source backup has changed')
        if hash_bytes(current_bytes) not in (recorded.get('sha256'),recorded.get('original_sha256')):
            raise ValueError('Prepared surface source has unrecorded edits')
        baseline=saved.read_text()
    else:
        if 'TCG_STREAMING_SURFACE_' in current:
            raise ValueError('Prepared surface source requires its verified receipt')
        if saved.exists() and saved.read_bytes()!=current_bytes:
            raise ValueError('Unrecorded surface source recovery state')
        baseline=current
    if 'TCG_STREAMING_SURFACE_' in baseline:
        raise ValueError('Original surface source backup contains preparation markers')
    patched=patch_surfaces(baseline).encode()
    result={'version':1,'source':str(source),'backup':str(saved),'original_sha256':hash_bytes(original_bytes),'sha256':hash_bytes(patched)}
    # Compute/verify all inputs before writes. Missing receipts never bless a
    # prepared source; an intact original backup before the first write is safe.
    if not saved.exists():saved.parent.mkdir(parents=True,exist_ok=True);saved.write_bytes(original_bytes)
    if current_bytes!=patched:
        temporary=source.with_name(source.name+'.tcg-surface-new');temporary.write_bytes(patched);os.replace(temporary,source)
    temporary=receipt.with_name(receipt.name+'.new');temporary.write_text(json.dumps(result,indent=2)+'\n');os.replace(temporary,receipt)
    return result

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('project',type=Path)
    print(json.dumps(prepare(parser.parse_args().project),indent=2))
