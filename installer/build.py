#!/usr/bin/env python3
"""Build and test a native Apple Silicon installer, including only our plugin."""
import hashlib, json, pathlib, plistlib, shutil, struct, subprocess
root = pathlib.Path(__file__).resolve().parent.parent
out = root/'dist'
out.mkdir(exist_ok=True)
run = lambda *args: subprocess.run([str(a) for a in args], check=True)
payload = root/'installer/Payload'
assert {p.name for p in payload.iterdir()} == {'manifest.json','NativeShopMenu.dll'}, 'Unexpected payload files'
manifest = json.loads((payload/'manifest.json').read_text())
assert hashlib.sha256((payload/'NativeShopMenu.dll').read_bytes()).hexdigest() == manifest['sha256'], 'Plugin hash mismatch'
expected = {str(p.relative_to(root)) for p in (root/'src/NativeShopMenu').glob('*.cs')}
assert set(manifest['sources']) == expected, 'Plugin source inventory changed; rebuild payload'
for name, digest in manifest['sources'].items():
    assert hashlib.sha256((root/name).read_bytes()).hexdigest() == digest, 'Plugin source changed; rebuild payload: '+name
sources = [root/'installer'/p for p in ['InstallerCore.swift','InstallerTests.swift','App.swift','main.swift']]
cache = out/'module-cache'; cache.mkdir(exist_ok=True)
common = ['-swift-version','5','-module-cache-path',cache]
run('swiftc', *common, *sources, '-o',out/'installer-tests')
run(out/'installer-tests','--self-test')
app = out/'TCG Plugin Installer.app'
if app.exists(): shutil.rmtree(app)
contents = app/'Contents'
(contents/'MacOS').mkdir(parents=True)
(contents/'Resources').mkdir()
run('swiftc',*common,'-O','-target','arm64-apple-macos13.0',*sources,'-o',contents/'MacOS/TCGPluginInstaller')
shutil.copytree(payload, contents/'Resources/Payload')
run('swiftc',*common,root/'installer/MakeIcon.swift','-o',out/'make-icon')
run(out/'make-icon',out/'Installer.iconset')
# ICNS supports PNG representations directly; avoid host iconutil conversion services.
chunks = []
for kind, name in [('ic07','128x128'),('ic08','256x256'),('ic09','512x512'),('ic10','512x512@2x')]:
    png = (out/'Installer.iconset'/('icon_'+name+'.png')).read_bytes()
    chunks.append(kind.encode()+struct.pack('>I',len(png)+8)+png)
icon = b''.join(chunks)
(contents/'Resources/Installer.icns').write_bytes(b'icns'+struct.pack('>I',len(icon)+8)+icon)
info = {'CFBundleIconFile':'Installer.icns','CFBundleName':'TCG Plugin Installer','CFBundleDisplayName':'TCG Plugin Installer','CFBundleIdentifier':'community.tcg.plugininstaller','CFBundleExecutable':'TCGPluginInstaller','CFBundlePackageType':'APPL','CFBundleShortVersionString':'0.1.0','CFBundleVersion':'1','LSMinimumSystemVersion':'13.0','NSHighResolutionCapable':True,'NSHumanReadableCopyright':'Unofficial community plugin installer. See project credits.'}
with (contents/'Info.plist').open('wb') as f: plistlib.dump(info,f)
run('codesign','--force','--deep','--sign','-',app)
run('codesign','--verify','--deep','--strict',app)
archive = out/'TCG-Plugin-Installer-v0.1.0-macOS-arm64.zip'
if archive.exists(): archive.unlink()
run('ditto','-c','-k','--sequesterRsrc','--keepParent',app,archive)
shutil.copyfile(root/'installer/INSTALL.txt', out/'INSTALL.txt')
(out/'SHA256SUMS').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name+'\n' for p in [archive,out/'INSTALL.txt']))
print('Built '+str(archive))
