import hashlib
import json
import importlib.util
import pathlib
import subprocess
import tempfile
import unittest

ROOT=pathlib.Path(__file__).resolve().parents[1]
MONO=pathlib.Path("/Applications/Unity/Hub/Editor/2021.3.45f2/Unity.app/Contents/MonoBleedingEdge/bin")
spec=importlib.util.spec_from_file_location("prepare_character_meshes",ROOT/"prepare_character_meshes.py")
module=importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

SOURCE = r'''using System;
namespace TCG.SwitchStreaming { static class MeshBundleOwner {
 public static object Instantiate(object prefab,object parent) { Original.Calls++; Original.Ready=true;return prefab; }
 public static void PrepareRoot(object root) { if(root==null)throw new Exception("missing root");Original.Ready=true; }
}}
class Original { public static int Calls;public static bool Ready; }
class GO { public object transform=new object(); }
class Base { public GO gameObject=new GO(); }
class Part { public object Mesh=new object(); }
class Character : Base {
 public int Weights, Parsed, Reloaded; bool m_HasInit;
 public void Initialize() {
  if(m_HasInit){if(!Original.Ready)throw new Exception("reuse before body");Reloaded++;return;}
  m_HasInit=true;
  if(!Original.Ready)throw new Exception("parse before body");Parsed++;
 }
 public void setHair(int selection, int slot) {
  var hairstyle=new Part();
  var obj=Object.Instantiate(hairstyle.Mesh, base.gameObject.transform);
  if(!Original.Ready)throw new Exception("setup before native mesh");Weights++;
 }
 public void setApparel(int selection, int slot, int materialSelection) {
  var apparel=new Part();
  var obj=Object.Instantiate(apparel.Mesh, base.gameObject.transform);
  if(!Original.Ready)throw new Exception("setup before native mesh");Weights++;
 }
}
class Program { static void Main() {
 var c=new Character();c.Initialize();Original.Ready=false;c.Initialize();
 Original.Ready=false;c.setHair(0,0);Original.Ready=false;c.setApparel(0,0,0);
 Console.Write(Original.Calls+":"+c.Weights+":"+c.Parsed+":"+c.Reloaded);
}}
'''

def legacy_patch(source):
    # Hand-checked v1 format: only the two choice methods were transformed.
    for signature, name in [("public void setHair(int selection, int slot)", "hairstyle"),
                            ("public void setApparel(int selection, int slot, int materialSelection)", "apparel")]:
        source = source.replace(signature + " {", signature + " {\n\t\t// TCG_STREAMING_CHARACTER_" + name.upper() + "\n")
        source = source.replace("Object.Instantiate(" + name + ".Mesh", "TCG.SwitchStreaming.MeshBundleOwner.Instantiate(" + name + ".Mesh")
    return source

class CharacterPreparationTests(unittest.TestCase):
    def test_original_instantiation_and_setup_order_unchanged(self):
        source = SOURCE
        patched=module.patch_character(source)
        self.assertEqual(module.patch_character(patched),patched)
        with tempfile.TemporaryDirectory() as directory:
            path=pathlib.Path(directory);(path/"test.cs").write_text(patched)
            subprocess.run([str(MONO/"mcs"),"-out:"+str(path/"test.exe"),str(path/"test.cs")],check=True,capture_output=True)
            self.assertEqual(subprocess.check_output([str(MONO/"mono"),str(path/"test.exe")],text=True),"2:2:1:1")

    def setup_project(self, directory, raw=None):
        project=(pathlib.Path(directory)/"switch-on-demand/macos-port-work").resolve()
        source=project/"Assets/Scripts/Assembly-CSharp/CC/CharacterCustomization.cs"
        source.parent.mkdir(parents=True);source.write_bytes(raw or SOURCE.encode())
        backup=project.parent/"character-mesh-source-backup/CharacterCustomization.cs"
        return project, source, backup, project/"character-mesh-preparation.json"

    def test_fresh_prepare_is_raw_byte_guarded_and_repeatable(self):
        with tempfile.TemporaryDirectory() as directory:
            original=SOURCE.replace("\n", "\r\n").encode()
            project,source,backup,receipt=self.setup_project(directory,original)
            result=module.prepare(project)
            self.assertEqual(backup.read_bytes(),original)
            self.assertEqual(result["version"],2)
            self.assertEqual(result["baselineSha256"],hashlib.sha256(original).hexdigest())
            self.assertEqual(result["preparedSha256"],hashlib.sha256(source.read_bytes()).hexdigest())
            self.assertEqual(result["sha256"],result["preparedSha256"])
            self.assertNotIn(b"\n",source.read_bytes().replace(b"\r\n",b""))
            before=(source.read_bytes(),backup.read_bytes(),receipt.read_bytes())
            self.assertEqual(module.prepare(project),result)
            self.assertEqual((source.read_bytes(),backup.read_bytes(),receipt.read_bytes()),before)

    def test_legacy_receipt_only_upgrades_when_backup_reproduces_recorded_source(self):
        for corrupt in (False,True):
            with self.subTest(corrupt=corrupt), tempfile.TemporaryDirectory() as directory:
                project,source,backup,receipt=self.setup_project(directory)
                backup.parent.mkdir(parents=True);backup.write_bytes(SOURCE.encode())
                old=legacy_patch(SOURCE).encode();source.write_bytes(old)
                receipt.write_text(json.dumps({"source":str(source),"backup":str(backup),"sha256":hashlib.sha256(old).hexdigest()}))
                if corrupt:backup.write_bytes((SOURCE+"\n// authored drift").encode())
                before=(source.read_bytes(),backup.read_bytes(),receipt.read_bytes())
                if corrupt:
                    with self.assertRaises(ValueError):module.prepare(project)
                    self.assertEqual((source.read_bytes(),backup.read_bytes(),receipt.read_bytes()),before)
                else:
                    self.assertEqual(module.prepare(project)["version"],2)
                    self.assertEqual(backup.read_bytes(),SOURCE.encode())

    def test_recorded_drift_never_changes_any_file(self):
        for which in ("source","backup","identity","missing_receipt"):
            with self.subTest(which=which), tempfile.TemporaryDirectory() as directory:
                project,source,backup,receipt=self.setup_project(directory)
                module.prepare(project)
                if which=="source":source.write_bytes(source.read_bytes()+b"\n// external edit")
                if which=="backup":backup.write_bytes(backup.read_bytes()+b"\n// external edit")
                if which=="identity":
                    data=json.loads(receipt.read_text());data["backup"]="/tmp/foreign.cs";receipt.write_text(json.dumps(data))
                if which=="missing_receipt":receipt.unlink()
                before=(source.read_bytes(),backup.read_bytes(),receipt.read_bytes() if receipt.exists() else None)
                with self.assertRaises(ValueError):module.prepare(project)
                self.assertEqual((source.read_bytes(),backup.read_bytes(),receipt.read_bytes() if receipt.exists() else None),before)

    def test_unreceipted_backup_or_marker_refused_without_write(self):
        for old_backup in (False,True):
            with self.subTest(old_backup=old_backup), tempfile.TemporaryDirectory() as directory:
                project,source,backup,receipt=self.setup_project(directory)
                if old_backup:
                    backup.parent.mkdir(parents=True);backup.write_bytes(SOURCE.encode())
                else:source.write_bytes(legacy_patch(SOURCE).encode())
                before=source.read_bytes()
                with self.assertRaises(ValueError):module.prepare(project)
                self.assertEqual(source.read_bytes(),before)
                self.assertFalse(receipt.exists())

    def test_unknown_instantiation_rejected(self):
        with self.assertRaises(ValueError):module.patch_character("class Unknown {}")

if __name__=="__main__":unittest.main()
