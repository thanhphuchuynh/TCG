#!/usr/bin/env python3
"""Build our managed plugin against explicitly supplied local references."""
import argparse, os, shutil, subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--game-dir', type=Path, required=True)
parser.add_argument('--unity-editor', type=Path, required=True, help='Path to Unity.app')
parser.add_argument('--output', type=Path, default=ROOT/'out')
parser.add_argument('--icon', type=Path, help='Optional original/user-supplied square PNG')
a = parser.parse_args()
install = a.game_dir.expanduser().resolve()
managed = install/'TCG Card Shop Simulator.app/Contents/Resources/Data/Managed'
mono = a.unity_editor.expanduser().resolve()/'Contents/MonoBleedingEdge/bin'
out = a.output.expanduser().resolve()
refs = ['mscorlib.dll','System.dll','System.Core.dll','netstandard.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.UIModule.dll','UnityEngine.UI.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.ImageConversionModule.dll','Unity.TextMeshPro.dll','Assembly-CSharp.dll']
libraries = [managed/n for n in refs] + [install/'BepInEx/core'/n for n in ['BepInEx.dll','0Harmony.dll']] + [install/'BepInEx/plugins/ShopOS/ShopOS.dll']
for p in libraries + [mono/'mcs',mono/'mono']:
    if not p.is_file(): parser.error('Missing local dependency: '+str(p))
if a.icon and not a.icon.expanduser().is_file(): parser.error('Icon file not found')
out.mkdir(parents=True,exist_ok=True)
subprocess.run([str(mono/'mcs'),'-nostdlib','-target:library','-out:'+str(out/'NativeShopMenu.dll')] + ['-r:'+str(p) for p in libraries] + [str(p) for p in sorted((ROOT/'src/NativeShopMenu').glob('*.cs'))],check=True)
def test(name, sources, references=(), args=(), env=None):
    exe=out/(name+'.exe')
    subprocess.run([str(mono/'mcs'),'-out:'+str(exe)] + ['-r:'+str(p) for p in references] + [str(ROOT/p) for p in sources],check=True)
    subprocess.run([str(mono/'mono'),str(exe)] + list(map(str,args)),check=True,env=env)
test('MenuGeometryTests',['src/NativeShopMenu/MenuGeometry.cs','tests/MenuGeometryTests.cs'])
test('AdapterTests',['src/NativeShopMenu/DragMetricIds.cs','tests/AdapterTests.cs'],[install/'BepInEx/core/Mono.Cecil.dll'],[install/'BepInEx/plugins/ShopOS/ShopOS.dll'],dict(os.environ,MONO_PATH=str(install/'BepInEx/core')))
test('CreditsScrollTests',['src/NativeShopMenu/CreditsScroll.cs','tests/CreditsScrollTests.cs'])
if a.icon: shutil.copy2(a.icon.expanduser(),out/'CreditsIcon.png')
print('Built and verified:',out/'NativeShopMenu.dll')
