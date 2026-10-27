import os,struct,tempfile,unittest,json,hashlib
from pathlib import Path
from package_helpers import control_nacp,nro_nacp,private_keyset,safe_relative,prepare_payload,TITLE,AUTHOR,TITLE_ID
class PackageHelperTests(unittest.TestCase):
 def test_home_menu_title_is_application_not_applet(self):
  config=json.loads((Path(__file__).resolve().parent/'bootstrap/hbl.json').read_text())
  kinds=[row['value'] for row in config['kernel_capabilities'] if row['type']=='application_type']
  self.assertEqual(kinds,[1],'Standalone HOME title must get application context, not applet context')
 def test_control_identity_and_memory_flags(self):
  data=control_nacp(bytes(0x4000))
  self.assertEqual(len(data),0x4000);self.assertEqual(data.split(b'\0')[0].decode(),TITLE)
  self.assertEqual(data[0x200:].split(b'\0')[0].decode(),AUTHOR)
  self.assertEqual(struct.unpack_from('<Q',data,0x3038)[0],int(TITLE_ID,16))
  self.assertEqual(data[0x3035],0);self.assertEqual(struct.unpack_from('<I',data,0x302c)[0],1)
 def test_nro_metadata_bounds(self):
  with tempfile.TemporaryDirectory() as t:
   p=Path(t)/'runtime.nro';header=bytearray(0x80);header[0x10:0x14]=b'NRO0';struct.pack_into('<I',header,0x18,0x80)
   assets=bytearray(0x38);assets[:4]=b'ASET';struct.pack_into('<QQ',assets,0x18,0x38,0x4000);p.write_bytes(header+assets+bytes(0x4000))
   self.assertEqual(len(nro_nacp(p)),0x4000);p.write_bytes(b'bad')
   with self.assertRaises((ValueError,struct.error)):nro_nacp(p)
 def test_keyset_is_filtered_private_and_anonymous(self):
  with tempfile.TemporaryDirectory() as t:
   p=Path(t)/'test.keys';p.write_text('header_key = '+'11'*32+'\nkey_area_key_application_00 = '+'22'*16+'\ntitlekek_00 = '+'33'*16+'\nsecure_boot_key = '+'44'*16+'\n')
   with private_keyset(p) as f:
    self.assertEqual(os.fstat(f.fileno()).st_mode&0o777,0o600);f.seek(0);text=f.read().decode()
    self.assertNotIn('secure_boot_key',text);self.assertIsInstance(f.name,int)
 def test_identical_overlapping_profiles_are_listed_once(self):
  with tempfile.TemporaryDirectory() as t:
   root=Path(t); handoff=root/'handoff';(handoff/'Verification').mkdir(parents=True)
   names=['Runtime/switch/wine/drive_c/TCG_Autorun_Test/profile.args.txt','Game/TCG_Autorun_Test/profile.args.txt']
   entries=[]
   for name in names:
    f=handoff/name;f.parent.mkdir(parents=True,exist_ok=True);f.write_bytes(b'profile')
    entries.append({'path':name,'bytes':7,'sha256':hashlib.sha256(b'profile').hexdigest()})
   (handoff/'Verification/handoff-manifest.json').write_text(json.dumps({'files':entries}))
   result=prepare_payload(handoff,root/'romfs')
   self.assertEqual(result['files'],1);self.assertEqual(result['bytes'],7)
   self.assertEqual(len((root/'romfs/manifest.tsv').read_text().splitlines()),2)
 def test_conflicting_overlapping_profiles_fail_before_staging(self):
  with tempfile.TemporaryDirectory() as t:
   root=Path(t);handoff=root/'handoff';(handoff/'Verification').mkdir(parents=True)
   entries=[]
   for name,body in [('Runtime/switch/wine/drive_c/TCG_Autorun_Test/p.args.txt',b'one'),('Game/TCG_Autorun_Test/p.args.txt',b'two')]:
    f=handoff/name;f.parent.mkdir(parents=True,exist_ok=True);f.write_bytes(body)
    entries.append({'path':name,'bytes':len(body),'sha256':hashlib.sha256(body).hexdigest()})
   (handoff/'Verification/handoff-manifest.json').write_text(json.dumps({'files':entries}))
   with self.assertRaisesRegex(ValueError,'Conflicting'):prepare_payload(handoff,root/'romfs')
   self.assertFalse((root/'romfs/payload').exists())
 def test_no_private_keys_or_traversal_in_payload(self):
  for path in ['../escape','/absolute','prod.keys','nested/title.keys','a\\b','a\nb']:
   with self.subTest(path=path):
    with self.assertRaises(ValueError):safe_relative(path)
if __name__=='__main__':unittest.main()
