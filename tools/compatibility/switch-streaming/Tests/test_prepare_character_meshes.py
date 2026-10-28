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

class CharacterPreparationTests(unittest.TestCase):
    def test_original_instantiation_and_setup_order_unchanged(self):
        source = r'''using System;
namespace TCG.SwitchStreaming { static class MeshBundleOwner {
 public static object Instantiate(object prefab,object parent) { Original.Calls++; Original.Ready=true;return prefab; }
}}
class Original { public static int Calls;public static bool Ready; }
class GO { public object transform=new object(); }
class Base { public GO gameObject=new GO(); }
class Part { public object Mesh=new object(); }
class Character : Base {
 public int Weights;
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
 var c=new Character();c.setHair(0,0);Original.Ready=false;c.setApparel(0,0,0);
 Console.Write(Original.Calls+":"+c.Weights);
}}
'''
        patched=module.patch_character(source)
        self.assertEqual(module.patch_character(patched),patched)
        with tempfile.TemporaryDirectory() as directory:
            path=pathlib.Path(directory);(path/"test.cs").write_text(patched)
            subprocess.run([str(MONO/"mcs"),"-out:"+str(path/"test.exe"),str(path/"test.cs")],check=True,capture_output=True)
            self.assertEqual(subprocess.check_output([str(MONO/"mono"),str(path/"test.exe")],text=True),"2:2")
    def test_unknown_instantiation_rejected(self):
        with self.assertRaises(ValueError):module.patch_character("class Unknown {}")

if __name__=="__main__":unittest.main()
