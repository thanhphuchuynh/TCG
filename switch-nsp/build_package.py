#!/usr/bin/env python3
"""Package an offline standalone compatibility NSP locally; never uploads keys or game data."""
import argparse,json,shutil,struct,sys
from pathlib import Path
from package_helpers import TITLE_ID,TITLE,AUTHOR,digest,nro_nacp,control_nacp,private_keyset,run_packer,prepare_payload

NOTES='''TCG Card Shop Simulator — experimental Switch compatibility NSP

Version 0.1.1 fixes a launcher null-framebuffer crash before preparation.
After installing this version, the HOME menu should show the preparation screen.
Console execution of this correction still needs confirmation.

This NSP bundles the clean Windows-x86/Mono/OpenGL base game and pinned Autorun
test-build-4. It is not a native Unity Switch game and has not been console-tested.
The Pokemon overhaul and other mod plugins from the Mac build are not included.
Original game content, shaders/quality and normal Steam initialization are retained.
Autorun cannot keep Steam running alongside the game; compatibility is unverified.

Install using your existing unsigned-homebrew-compatible Switch setup. This package
does not install firmware, boot files or signature patches. On first launch it
prepares about 4.6 GB of files under sd:/switch/wine, using a checked staging folder,
then starts the game. Allow several minutes and keep the SD inserted.
Existing unknown switch/wine folders are preserved and cause a clear error instead
of being overwritten. Back up and move an existing Autorun installation aside first.
Repeat launches retain this runtime's settings/saves. If preparation was interrupted,
launch again to resume. Do not manually remove or mix staging contents while running.

The complete NSP can exceed FAT32's single-file limit; use an installation method
that streams large NSPs (for example your existing USB/MTP installer) or storage
that supports the file size. Extracted game files are individually below 4 GiB.
The installed title plus its writable runtime need roughly 9.2 GB in total.

If launch fails, preserve sd:/switch/wine/logs/autorun_runtime.log and the game log.
Controller fallback maps sticks to WASD/mouse and Minus to Tab; these are untested
on this game/console. No playable-game guarantee is made by structural validation.

Credits: game by OPNeon Games; Autorun/Wine/Box64/Mesa and their contributors;
bootstrap derived from switchbrew/nx-hbloader v2.4.5 (MIT). This is a private,
unofficial compatibility package. No console keys or original third-party loader
from the game archive are included.
https://github.com/autorunhq/autorun/releases/tag/test-build-4
https://github.com/switchbrew/nx-hbloader/tree/v2.4.5
'''

def one_nca(folder):
 files=list(folder.glob('*.nca'))
 if len(files)!=1:raise RuntimeError('Expected exactly one completed NCA in '+folder.name)
 return files[0]

def main():
 p=argparse.ArgumentParser(description=__doc__)
 p.add_argument('--handoff',type=Path,required=True);p.add_argument('--bootstrap',type=Path,required=True);p.add_argument('--packer',type=Path,required=True)
 p.add_argument('--keys',type=Path,required=True);p.add_argument('--out',type=Path,required=True);p.add_argument('--icon',type=Path)
 p.add_argument('--prepare-only',action='store_true');p.add_argument('--preflight',action='store_true')
 a=p.parse_args();out=a.out.resolve();out.mkdir(parents=True,exist_ok=True);work=out/'work';work.mkdir(exist_ok=True)
 romfs=work/'romfs';control=work/'control';exefs=work/'exefs'
 for f in [romfs,control,exefs]:f.mkdir(exist_ok=True)
 stage_report=work/'stage.json'
 if not stage_report.exists():
  if a.preflight:
   (romfs/'README.txt').write_text('Structural package preflight only. No game payload.')
   stage={'preflightOnly':True,'files':0,'bytes':0}
  else:
   print('Checking and staging the audited game/runtime files…',flush=True)
   stage=prepare_payload(a.handoff,romfs);(romfs/'README.txt').write_text(NOTES)
  stage_report.write_text(json.dumps(stage,indent=2)+'\n')
 else:
  stage=json.loads(stage_report.read_text())
  if bool(stage.get('preflightOnly')) != a.preflight:raise ValueError('Cannot reuse preflight staging for a full game build')
 for name in ['main','main.npdm']:
  source=a.bootstrap/name
  expected=b'NSO0' if name=='main' else b'META'
  if source.read_bytes()[:4]!=expected:raise ValueError('Invalid compiled loader '+name)
  shutil.copy2(source,exefs/name)
 nro=a.handoff/'Runtime/switch/wine/wine-nx-runtime.nro'
 (control/'control.nacp').write_bytes(control_nacp(nro_nacp(nro)))
 if a.icon:
  from PIL import Image
  with Image.open(a.icon) as image:image.convert('RGB').resize((256,256),Image.Resampling.LANCZOS).save(control/'icon_AmericanEnglish.dat',format='JPEG',quality=90,progressive=False)
 else:
  with nro.open('rb') as f:
   header=f.read(0x80);size=struct.unpack_from('<I',header,0x18)[0];f.seek(size);assets=f.read(0x38);offset,length=struct.unpack_from('<QQ',assets,8);f.seek(size+offset);icon=f.read(length)
  (control/'icon_AmericanEnglish.dat').write_bytes(icon)
 (out/'README.txt').write_text(NOTES)
 print('Payload staged:',json.dumps(stage),flush=True)
 if a.prepare_only:return 0
 output=out/'TCG-Card-Shop-Simulator-Switch-Compat-0.1.1.nsp'
 if output.exists():raise ValueError('Completed output already exists; refusing overwrite')
 ncas=work/'ncas';ncas.mkdir(exist_ok=True)
 with private_keyset(a.keys) as key_stream:
  common=['--type','nca','--titleid',TITLE_ID,'--plaintext','--keygeneration','1','--ncasig','zero','--noselfsignncasig2']
  produced={}
  for kind in ['program','control','meta']:
   folder=ncas/kind;folder.mkdir(exist_ok=True)
   if list(folder.glob('*.nca')):raise ValueError('Previous NCA output exists; preserve/move it before retrying')
   args=common+['--ncatype',kind,'-o',folder,'--tempdir',work/('temp-'+kind),'--backupdir',work/'backup']
   if kind=='program':args+=['--exefsdir',exefs,'--romfsdir',romfs]
   elif kind=='control':args+=['--romfsdir',control]
   else:args+=['--titletype','application','--programnca',produced['program'],'--controlnca',produced['control']]
   print('Creating '+kind+' NCA…',flush=True)
   run_packer(a.packer,key_stream,args,work,out/(kind+'-sanitized.log'));produced[kind]=one_nca(folder)
  combined=work/'nca-combined';combined.mkdir(exist_ok=True)
  for source in produced.values():
   destination=combined/source.name
   if destination.exists():raise ValueError('Combined NCA output exists')
   # Same-volume hardlinks avoid duplicating multi-gigabyte immutable completed NCAs.
   destination.hardlink_to(source)
  packaged=work/'nsp';packaged.mkdir(exist_ok=True)
  run_packer(a.packer,key_stream,['--type','nsp','--titleid',TITLE_ID,'--ncadir',combined,'-o',packaged],work,out/'nsp-sanitized.log')
  files=list(packaged.glob('*.nsp'))
  if len(files)!=1:raise RuntimeError('Expected one completed NSP')
  files[0].rename(output)
 report={'status':'built-needs-independent-verification','nsp':str(output),'bytes':output.stat().st_size,'titleID':TITLE_ID,'sha256':digest(output),'payload':stage,'nativeSwitchPort':False,'consoleTested':False,'keyMaterialIncluded':False}
 (out/'build.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2),flush=True)
 return 0
if __name__=='__main__':raise SystemExit(main())
