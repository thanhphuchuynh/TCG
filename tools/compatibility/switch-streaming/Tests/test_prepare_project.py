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
class Program {
 static int renders;
 static object m_CardData = new object();
 static void SetCardUI(object data) { renders++; }
 static void Refresh() { m_CenterFrameImage.sprite = m_MonsterData.GetIcon(m_CardData.expansionType); }
 static void Main() { Refresh(); Console.Write(renders); }
}'''
        patched = prepare.refresh_callback(source, "static void Refresh()")
        self.assertEqual(self.run_csharp(patched), "1")

    def test_page_hook_preserves_body_and_is_idempotent(self):
        original = "class Page { void Render() { original(); } }"
        once = prepare.page_gate(original, "void Render()", "cards", "item.view")
        twice = prepare.page_gate(once, "void Render()", "cards", "item.view")
        self.assertEqual(once, twice)
        self.assertEqual(once.count("original();"), 1)
        self.assertIn("tcgOwners.Add(item.view)", once)
        self.assertIn("ArtPageGate.Watch(this, tcgOwners)", once)

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
