"""Derive runtime identity from immutable content, keeping user settings independent."""
import hashlib, json

def hash_file(path):
    digest=hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda:stream.read(4*1024*1024),b''): digest.update(block)
    return digest.hexdigest()

def describe_payload(game, game_app, executable):
    sizes={};critical={};immutable={};byte_count=0
    for path in sorted(p for p in game.rglob('*') if p.is_file()):
        relative=path.relative_to(game).as_posix();size=path.stat().st_size;byte_count+=size
        mutable=relative.startswith('BepInEx/config/') or relative.startswith('BepInEx/plugins/TextureReplacer/objects_custom/')
        if mutable: continue
        sizes[relative]=size
        digest=hash_file(path);immutable[relative]=digest
        if path.suffix.lower() in {'.dll','.dylib','.so'} or relative in {'run_bepinex.sh','steam_appid.txt',game_app+'/Contents/MacOS/'+executable,game_app+'/Contents/Info.plist'}:
            critical[relative]=digest
    identity=hashlib.sha256(json.dumps({'format':1,'gameApp':game_app,'immutableHashes':immutable},sort_keys=True).encode()).hexdigest()[:20]
    return {'format':1,'id':'runtime-'+identity,'gameApp':game_app,'byteCount':byte_count,'criticalHashes':critical,'requiredSizes':sizes}
