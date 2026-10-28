import hashlib
import importlib.util
import pathlib
import subprocess
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("prepare_project", ROOT / "prepare_project.py")
prepare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prepare)
MONO = pathlib.Path("/Applications/Unity/Hub/Editor/2021.3.45f2/Unity.app/Contents/MonoBleedingEdge/bin")


class PreparationTests(unittest.TestCase):
    def run_csharp(self, source):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "test.cs").write_text(source)
            subprocess.run([str(MONO / "mcs"), "-out:" + str(root / "test.exe"), str(root / "test.cs")], check=True, capture_output=True)
            return subprocess.check_output([str(MONO / "mono"), str(root / "test.exe")], text=True).strip()

    def fixture(self):
        return '''using System;
class Scope : IDisposable {
 public static int Entered, Exited;
 public static Scope Begin() { Entered++; return new Scope(); }
 public void Dispose() { Exited++; }
}
class Program {
 static int Calculate(int number) {
  // } stray comment
  string literal = "{ not a block }";
  string verbatim = @"quoted "" } """;
  if (number < 0) throw new Exception(literal + verbatim);
  return number + 3;
 }
 static void Main() {
  int result = Calculate(4); try { Calculate(-1); } catch (Exception) { }
  Console.Write(result + ":" + Scope.Entered + ":" + Scope.Exited);
 }
}'''

    def test_wrap_preserves_return_exception_and_literal_braces(self):
        wrapped = prepare.wrap_method(self.fixture(), "static int Calculate(int number)", "Scope.Begin()", "calculation")
        self.assertEqual(self.run_csharp(wrapped), "7:2:2")

    def test_repeat_preparation_does_not_double_acquire(self):
        once = prepare.wrap_method(self.fixture(), "static int Calculate(int number)", "Scope.Begin()", "calculation")
        twice = prepare.wrap_method(once, "static int Calculate(int number)", "Scope.Begin()", "calculation")
        self.assertEqual(self.run_csharp(twice), "7:2:2")

    def test_unknown_and_unbalanced_source_is_rejected(self):
        with self.assertRaises(ValueError):
            prepare.wrap_method(self.fixture(), "void Unknown()", "Scope.Begin()", "unknown")
        with self.assertRaises(ValueError):
            prepare.wrap_method("class A { void Run() {", "void Run()", "Scope.Begin()", "broken")

    def test_web_refresh_performs_full_owner_rebind(self):
        source = '''using System;
namespace TCG.SwitchStreaming { static class CardArtSession { public static void RefreshPreservingPresentation(object owner, Action render) { render(); } } }
class Program {
 static int renders;
 static object m_CardData = new object();
 static void SetCardUI(object data) { renders++; }
 void Refresh() { m_CenterFrameImage.sprite = m_MonsterData.GetIcon(m_CardData.expansionType); }
 static void Main() { new Program().Refresh(); Console.Write(renders); }
}'''
        patched = prepare.refresh_callback(source, "void Refresh()")
        self.assertEqual(self.run_csharp(patched), "1")

    def test_single_pack_early_return_preloads_before_ready_gate(self):
        source = r'''using System; using System.Collections.Generic;
enum ECollectionPackType { Basic }
class View { public object m_CardUI = new object(); }
class Binder { public void SetCanUpdateSort(bool canSort) {} }
class InteractionPlayerController { public Binder m_CollectionBinderFlipAnimCtrl = new Binder(); }
class CSingleton<T> where T:new() { public static T Instance = new T(); }
namespace TCG.SwitchStreaming { static class CardArtSession {
 public static int Primes;
 public static void Prime(object view) { Primes++; }
 public static bool Ready(object view) { return Primes > 0; }
 public static void CancelPrime(object view) {}
}}
class Opening {
 List<object> m_ManualOpenCardDataList = new List<object>{ new object() };
 List<View> m_Card3dUIList = new List<View>{new View()};
 bool m_IsScreenActive=true; int m_StateIndex=1; public int Adds,Steps;
 public void OpenScreen(ECollectionPackType collectionPackType, bool isMultiPack, bool isPremiumPack = false) {
  Adds++;
  CSingleton<InteractionPlayerController>.Instance.m_CollectionBinderFlipAnimCtrl.SetCanUpdateSort(canSort: true);
  if (!isMultiPack) { return; }
  m_StateIndex=101;
 }
 private void Update() { Steps++; }
 public void Step() { Update(); }
}
class Program { static void Main() {
 var single=new Opening();single.OpenScreen(ECollectionPackType.Basic,false);single.Step();
 Console.Write(TCG.SwitchStreaming.CardArtSession.Primes+":"+single.Steps+":"+single.Adds);
}}
'''
        self.assertEqual(self.run_csharp(prepare.patch_opening(source)), "1:1:1")

    def test_page_hook_preserves_body_and_is_idempotent(self):
        original = "class Page { void Render() { original(); } }"
        once = prepare.page_gate(original, "void Render()", "cards", "item.view")
        twice = prepare.page_gate(once, "void Render()", "cards", "item.view")
        self.assertEqual(once, twice)
        self.assertEqual(once.count("original();"), 1)
        self.assertIn("tcgOwners.Add(item.view)", once)
        self.assertIn("ArtPageGate.Watch(this, tcgOwners)", once)

    def test_foil_setter_hooks_keep_last_direct_and_settings_choices(self):
        source = r'''using System; using System.Collections.Generic;
class Material { public string Name; public Material(string name) { Name=name; } }
namespace TCG.SwitchStreaming { static class CardArtSession {
 static Dictionary<string,Action> saved = new Dictionary<string,Action>();
 public static void RememberPresentation(object owner,string channel,Action apply) { saved[channel]=apply; }
 public static Action Get(string channel) { return saved[channel]; }
}}
class Card {
 public string Foil,Blend;
 public void SetFoilMaterialList(List<Material> mat) { Foil=mat[0].Name; }
 public void SetFoilBlendedMaterialList(List<Material> mat) { Blend=mat[0].Name; }
 public void SetFoilMaterialListFromSettingData(bool isWorldView) { Foil=isWorldView ? "world" : "tangent"; }
 public void SetFoilBlendedMaterialListFromSettingData(bool isWorldView) { Blend=isWorldView ? "world" : "tangent"; }
}
class Program { static void Main() {
 var card=new Card();card.SetFoilMaterialList(new List<Material>{new Material("custom-world")});
 card.SetFoilBlendedMaterialListFromSettingData(true);
 var foil=TCG.SwitchStreaming.CardArtSession.Get("foil");var blend=TCG.SwitchStreaming.CardArtSession.Get("foil-blended");
 card.SetFoilMaterialListFromSettingData(false);card.SetFoilBlendedMaterialListFromSettingData(false);
 foil();blend();Console.Write(card.Foil+":"+card.Blend);
}}
'''
        patched = prepare.patch_presentation_setters(source)
        self.assertEqual(prepare.patch_presentation_setters(patched), patched)
        self.assertEqual(self.run_csharp(patched), "custom-world:world")

    def test_only_managed_pool_members_defer_initial_world_art(self):
        source = r'''using System; using System.Collections.Generic;
class Card3dUIGroup {}
class Card3dUISpawner {
 public static Card3dUISpawner m_Instance;
 List<Card3dUIGroup> m_Card3dUIList = new List<Card3dUIGroup>();
 public Card3dUISpawner(Card3dUIGroup pooled) { m_Instance=this; m_Card3dUIList.Add(pooled); }
}
class Program { static void Main() {
 var pooled=new Card3dUIGroup();var fixedReveal=new Card3dUIGroup();new Card3dUISpawner(pooled);
 Console.Write(Card3dUISpawner.TCG_IsManagedPoolCard(pooled)+":"+Card3dUISpawner.TCG_IsManagedPoolCard(fixedReveal));
}}
'''
        patched = prepare.patch_pool_membership(source)
        self.assertEqual(prepare.patch_pool_membership(patched), patched)
        self.assertEqual(self.run_csharp(patched), "True:False")

    def test_culling_pass_skips_pooled_prefetch_but_keeps_allocated_card(self):
        source = r'''using System; using System.Collections.Generic;
namespace TCG.SwitchStreaming { static class CardArtSession {
 public static int Loads;
 public static void Prefetch(object owner) { Loads++; }
 public static void Prefetch(object owner, bool allocated) { if(allocated) Loads++; }
 public static void SetVisible(object owner, bool visible) {}
}}
class GO { public bool activeInHierarchy; public void SetActive(bool value) {} }
class Anim { public GO gameObject=new GO(); }
class Card3dUIGroup {
 public object m_CardUI = new object(); public Anim m_CardUIAnimGrp=new Anim();
 public GO gameObject=new GO(); public bool m_IgnoreCulling,allocated,far;
 public bool IsActive(){return allocated;} public bool GetAlwaysCulling(){return false;}
 public static implicit operator bool(Card3dUIGroup group) { return group!=null; }
}
class Card3dUISpawner {
 public static Card3dUISpawner m_Instance;
 List<Card3dUIGroup> m_Card3dUIList = new List<Card3dUIGroup> {new Card3dUIGroup(), new Card3dUIGroup{allocated=true,gameObject=new GO{activeInHierarchy=true}}};
 int m_CullIndex;
 private void Update() {
  if ((bool)m_Card3dUIList[m_CullIndex] && m_Card3dUIList[m_CullIndex].GetAlwaysCulling()) {
   m_Card3dUIList[m_CullIndex].m_CardUIAnimGrp.gameObject.SetActive(value: false);
  } else if ((bool)m_Card3dUIList[m_CullIndex] && !m_Card3dUIList[m_CullIndex].m_IgnoreCulling) {
   float magnitude = 0f;
   if(m_Card3dUIList[m_CullIndex].far) {
    m_Card3dUIList[m_CullIndex].m_CardUIAnimGrp.gameObject.SetActive(value: false);
   } else { m_Card3dUIList[m_CullIndex].m_CardUIAnimGrp.gameObject.SetActive(value: true); }
  }
			m_CullIndex++;
 }
 public void Pass() { Update(); Update(); }
}
class Program { static void Main() { new Card3dUISpawner().Pass();Console.Write(TCG.SwitchStreaming.CardArtSession.Loads); } }
'''
        self.assertEqual(self.run_csharp(prepare.patch_spawner(source)), "1")

    def test_tool_upgrade_uses_guarded_original_and_rejects_user_edits(self):
        original = self.fixture()
        old = prepare.wrap_method(original, "static int Calculate(int number)", "Scope.Begin()", "old")
        with tempfile.TemporaryDirectory() as directory:
            saved = pathlib.Path(directory) / "original.cs"; saved.write_text(original)
            expected = hashlib.sha256(old.encode()).hexdigest()
            self.assertEqual(prepare.upgrade_baseline(old, saved, expected), original)
            with self.assertRaises(ValueError): prepare.upgrade_baseline(old + "// local edit", saved, expected)
            self.assertEqual(prepare.upgrade_baseline(original, saved, expected), original)

    def test_wrong_project_is_not_modified(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            marker = root / "keep.txt"; marker.write_text("untouched")
            with self.assertRaises(ValueError):
                prepare.prepare_project(root, ROOT)
            self.assertEqual(marker.read_text(), "untouched")
            self.assertEqual(sorted(p.name for p in root.iterdir()), ["keep.txt"])

    def test_missing_late_source_does_not_patch_earlier_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            scripts = root / "Assets/Scripts/Assembly-CSharp"; scripts.mkdir(parents=True)
            original = "class CardUI { public void SetCardUI(CardData cardData) {} }"
            (scripts / "CardUI.cs").write_text(original)
            with self.assertRaises(ValueError):
                prepare.prepare_project(root, ROOT)
            self.assertEqual((scripts / "CardUI.cs").read_text(), original)
            self.assertFalse((root / "Assets/TCGSwitchStreaming").exists())


if __name__ == "__main__":
    unittest.main()
