#!/usr/bin/env python3
"""Build a local complete Mac app using game and mod files supplied by the user.
Never publishes/uploads its payload. Run on macOS with Swift command-line tools.
"""
import argparse, hashlib, json, os, pathlib, plistlib, shutil, subprocess, tempfile, zipfile
from payload_manifest import describe_payload
from zip_archive import create_archive
ROOT = pathlib.Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--game-dir',type=pathlib.Path,required=True)
parser.add_argument('--output-dir',type=pathlib.Path,required=True)
parser.add_argument('--zip',action='store_true')
a = parser.parse_args()
source = a.game_dir.expanduser().resolve(); out = a.output_dir.expanduser().resolve()
out.mkdir(parents=True,exist_ok=True)
run = lambda *args: subprocess.run(list(map(str,args)),check=True)
def sha(path):
    h=hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda:f.read(4*1024*1024),b''): h.update(block)
    return h.hexdigest()
app_name = 'TCG Card Shop Simulator.app'
player = source/app_name
assert player.is_dir(), 'Native game app missing'
info = plistlib.loads((player/'Contents/Info.plist').read_bytes())
executable = info['CFBundleExecutable']
assert pathlib.PurePath(executable).name == executable and executable not in ('.','..'), 'Unsafe executable name'
run('codesign','--verify','--deep','--strict',player)
archs = subprocess.check_output(['lipo','-archs',str(player/'Contents/MacOS'/executable)],text=True)
assert 'arm64' in archs.split(), 'Game must contain native Apple Silicon code'
assert (source/'BepInEx/core/BepInEx.Preloader.dll').is_file(), 'BepInEx missing'
assert (source/'BepInEx/plugins/NativeShopMenu/NativeShopMenu.dll').is_file(), 'Our production menu plugin missing'
assert not list((source/'BepInEx/plugins').rglob('*Probe.dll')), 'Remove diagnostic test probes before packaging'
for p in [player, source/'BepInEx']:
    assert not any(f.is_symlink() for f in p.rglob('*')), 'Resolve external links before packaging'
module_cache = out/'module-cache'; module_cache.mkdir(exist_ok=True)
swift = ['swiftc','-swift-version','5','-module-cache-path',str(module_cache)]
sources = [ROOT/'complete-app'/p for p in ['RuntimeCore.swift','RuntimeTests.swift','App.swift','main.swift']]
run(*swift,*sources,'-o',out/'runtime-tests')
run(out/'runtime-tests','--self-test')
run('python3',ROOT/'complete-app/test_payload_manifest.py')
final = out/'TCG Card Shop Simulator Modded.app'
assert not final.exists(), 'Output already exists; move it aside before rebuilding'
app = out/('building-'+next(tempfile._get_candidate_names())+'.app')
contents = app/'Contents'; (contents/'MacOS').mkdir(parents=True); resources = contents/'Resources'; resources.mkdir()
payload = resources/'Payload'; game = payload/'Game'; game.mkdir(parents=True)
def copy_tree(src,dst):
    # APFS clone copies are independent; fall back to ordinary ditto on other volumes.
    result = subprocess.run(['/bin/cp','-cR',str(src),str(dst)],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    if result.returncode:
        if dst.exists(): shutil.rmtree(dst)
        run('/usr/bin/ditto',src,dst)
print('Copying native game and all production mods…',flush=True)
copy_tree(player,game/app_name)
(game/'BepInEx').mkdir()
for folder in ['core','patchers','plugins','config']: copy_tree(source/'BepInEx'/folder,game/'BepInEx'/folder)
for name in ['run_bepinex.sh','libdoorstop.dylib','steam_appid.txt']: shutil.copy2(source/name,game/name)
excluded=[]
for f in list(game.rglob('*')):
    if not f.is_file(): continue
    rel = f.relative_to(game).as_posix()
    personal = (rel.startswith('BepInEx/config/ShopOS/') and '_slot' in f.name) or (rel.startswith('BepInEx/plugins/TextureReplacer/objects_custom/placed_custom_objects_saveslot_') and f.suffix == '.json')
    if f.name == '.DS_Store' or personal:
        excluded.append(rel); f.unlink()
# Sign local native tools before recording hashes; managed DLLs and game content are preserved.
for f in (game/'BepInEx/plugins/ShopOS/tools').iterdir():
    if f.is_file() and subprocess.check_output(['file','-b',str(f)],text=True).startswith('Mach-O'):
        run('codesign','--force','--sign','-',f)
run('codesign','--force','--sign','-',game/'libdoorstop.dylib')
files=sorted(p for p in game.rglob('*') if p.is_file())
print('Hashing immutable game/mod content for runtime identity…',flush=True)
manifest=describe_payload(game,app_name,executable)
byte_count=manifest['byteCount'];critical=manifest['criticalHashes']
inventory=[{'path':p.relative_to(game).as_posix(),'size':p.stat().st_size} for p in files]
(payload/'runtime.json').write_text(json.dumps(manifest,sort_keys=True,indent=2)+'\n')
(payload/'inventory.json').write_text(json.dumps({'files':inventory,'excludedUserState':excluded,'modFolders':sorted(p.name for p in (game/'BepInEx/plugins').iterdir() if p.is_dir())},indent=2)+'\n')
icon = player/'Contents/Resources'/info['CFBundleIconFile']
if not icon.suffix: icon=icon.with_suffix('.icns')
shutil.copy2(icon,resources/'PlayerIcon.icns')
shutil.copy2(ROOT/'complete-app/README.md',resources/'README.md')
launcher_info={'CFBundleName':'TCG Card Shop Simulator Modded','CFBundleDisplayName':'TCG Card Shop Simulator Modded','CFBundleIdentifier':'community.tcg.complete-macos','CFBundleExecutable':'TCGModdedLauncher','CFBundlePackageType':'APPL','CFBundleIconFile':'PlayerIcon.icns','CFBundleShortVersionString':'1.0','CFBundleVersion':'1','LSMinimumSystemVersion':'26.0','LSApplicationCategoryType':'public.app-category.games','NSHighResolutionCapable':True}
(contents/'Info.plist').write_bytes(plistlib.dumps(launcher_info))
run(*swift,'-O','-target','arm64-apple-macos26.0',*sources,'-o',contents/'MacOS/TCGModdedLauncher')
print('Signing and verifying complete app…',flush=True)
run('codesign','--force','--deep','--sign','-',app)
run('codesign','--verify','--deep','--strict',app)
# Deep signing must never silently invalidate the inventory.
for relative,digest in critical.items(): assert sha(game/relative)==digest, 'Signing changed runtime file: '+relative
app.rename(final)
report={'app':str(final),'runtimeID':manifest['id'],'byteCount':byte_count,'files':len(files),'modFolders':json.loads((final/'Contents/Resources/Payload/inventory.json').read_text())['modFolders'],'excludedUserState':excluded,'architectures':archs.strip(),'signed':'ad-hoc, deep strict verified'}
(out/'build-report.json').write_text(json.dumps(report,indent=2)+'\n')
shutil.copy2(ROOT/'complete-app/README.md',out/'README.md')
if a.zip:
    archive=out/'TCG-Card-Shop-Simulator-Modded-macOS-arm64.zip'
    create_archive(final,archive)
    (out/'SHA256SUMS').write_text(sha(archive)+'  '+archive.name+'\n')
print(json.dumps(report,indent=2),flush=True)
