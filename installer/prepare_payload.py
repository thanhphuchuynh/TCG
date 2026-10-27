#!/usr/bin/env python3
"""Record provenance for our own compiled plugin; never copies dependencies."""
import argparse, hashlib, json, pathlib, shutil
root = pathlib.Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser()
parser.add_argument('--dll', type=pathlib.Path, required=True)
args = parser.parse_args()
target = root/'installer/Payload/NativeShopMenu.dll'
if args.dll.resolve() != target.resolve():
    shutil.copyfile(args.dll, target)
hash_file = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
manifest = {'sha256':hash_file(target), 'sources': {str(p.relative_to(root)):hash_file(p) for p in sorted((root/'src/NativeShopMenu').glob('*.cs'))}, 'description':'Our NativeShopMenu, NativeCredits and NativeLoadingTips plugin only. Third-party reference assemblies excluded.'}
(root/'installer/Payload/manifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
