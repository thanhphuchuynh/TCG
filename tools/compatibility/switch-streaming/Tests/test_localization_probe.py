import importlib.util
import pathlib
import subprocess
import sys
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import localization_probe as probe

MONO = pathlib.Path('/Applications/Unity/Hub/Editor/2021.3.45f2/Unity.app/Contents/MonoBleedingEdge/bin')

# Synthetic implementation only. No recovered game source is embedded in this test.
FIXTURE = r'''using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
namespace UnityEngine {
 public static class Debug {
  public static readonly List<string> Lines = new List<string>();
  public static bool Throw;
  public static void Log(object value) { if(Throw) throw new Exception("diagnostic sink"); Lines.Add(value.ToString()); }
  public static void LogError(object value) { Log(value); }
 }
}
namespace I2.Loc {
 public class LanguageSourceData { }
 public static class PersistentStorage { public static string GetSetting_String(string key,string defaultValue) { return "saved"; } }
 public static class LocalizationManager {
  private static string mCurrentLanguage = "initial", mLanguageCode = "en";
  public static List<LanguageSourceData> Sources = new List<LanguageSourceData>();
  public static string[] GlobalSources = new [] { "synthetic" };
  public static bool Fail;
  public static int SetterCalls;
  public static string CurrentLanguage {
   get {
    // A misleading get { } and a literal "}" must not confuse the transformer.
    string example = @"} "" {";
    try { InitializeIfNeeded(); return mCurrentLanguage; }
    finally { Program.GetterFinally++; }
   }
   set { SetterCalls++; mCurrentLanguage = value; }
  }
  public static string CurrentLanguageCode {
   get { try { InitializeIfNeeded(); return mLanguageCode; } finally { Program.GetterFinally++; } }
   set { SetterCalls++; mLanguageCode = value; }
  }
  public static void InitializeIfNeeded() {
   try {
    AutoLoadGlobalParamManagers();
    UpdateSources();
    SelectStartupLanguage();
   } finally { Program.InitFinally++; }
  }
  static void AutoLoadGlobalParamManagers() { if(Fail) throw Program.Expected; }
  public static bool UpdateSources() {
   UnregisterDeletededSources();
   RegisterSourceInResources();
   RegisterSceneSources();
   return Sources.Count > 0;
  }
  static void UnregisterDeletededSources() { }
  static void RegisterSourceInResources() { if(Sources.Count==0) Sources.Add(new LanguageSourceData()); }
  static void RegisterSceneSources() { }
  private static void SelectStartupLanguage() {
   string setting_String = PersistentStorage.GetSetting_String("synthetic", string.Empty);
   string currentDeviceLanguage = GetCurrentDeviceLanguage();
   if(setting_String != null) {
    SetLanguageAndCode(setting_String, "sv");
    return;
   }
   if(currentDeviceLanguage != null) {
    SetLanguageAndCode(currentDeviceLanguage, "dv");
    return;
   }
   SetLanguageAndCode("first", "fv");
  }
  static string GetCurrentDeviceLanguage() { return "device"; }
  static void SetLanguageAndCode(string language,string code) { mCurrentLanguage=language; mLanguageCode=code; }
 }
}
class Program {
 public static int GetterFinally, InitFinally;
 public static readonly Exception Expected = new InvalidOperationException("sentinel");
 static void Main() {
  I2.Loc.LocalizationManager.CurrentLanguage="setter";
  string result=I2.Loc.LocalizationManager.CurrentLanguage;
  string code=I2.Loc.LocalizationManager.CurrentLanguageCode;
  I2.Loc.LocalizationManager.Fail=true;
  bool same=false; try { string unused=I2.Loc.LocalizationManager.CurrentLanguage; } catch(Exception e) { same=Object.ReferenceEquals(e,Expected); }
  int marked=0;
  foreach(var method in typeof(I2.Loc.LocalizationManager).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static))
   if((method.GetMethodImplementationFlags() & MethodImplAttributes.NoInlining)!=0) marked++;
  Console.Write(result+":"+code+":"+I2.Loc.LocalizationManager.SetterCalls+":"+GetterFinally+":"+InitFinally+":"+same+":"+marked);
 }
}'''


class LocalizationProbeTests(unittest.TestCase):
    def run_csharp(self, source):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            fixture = root / 'fixture.cs'; fixture.write_text(source)
            output = root / 'fixture.exe'
            compiled = subprocess.run([str(MONO / 'mcs'), '-out:' + str(output), str(fixture), str(ROOT / 'Runtime/LocalizationTrace.cs')], text=True, capture_output=True)
            self.assertEqual(compiled.returncode, 0, compiled.stdout + compiled.stderr)
            run = subprocess.run([str(MONO / 'mono'), str(output)], text=True, capture_output=True)
            self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
            return run.stdout.strip()

    def test_instrumented_code_preserves_return_exception_identity_finally_and_setter(self):
        patched = probe.patch_localization(FIXTURE)
        self.assertEqual(self.run_csharp(patched), 'saved:sv:1:3:3:True:5')

    def test_patch_is_idempotent_and_preserves_setters_exactly(self):
        once = probe.patch_localization(FIXTURE)
        self.assertEqual(probe.patch_localization(once), once)
        for setter in ('set { SetterCalls++; mCurrentLanguage = value; }', 'set { SetterCalls++; mLanguageCode = value; }'):
            self.assertEqual(once.count(setter), 1)
        self.assertEqual(self.run_csharp(once), 'saved:sv:1:3:3:True:5')

    def test_trace_bounds_markers_and_reports_first_exception_after_budget(self):
        source = '''using System; using System.Collections.Generic;
namespace UnityEngine { public static class Debug { public static List<string> Lines=new List<string>(); public static void Log(object o){Lines.Add(o.ToString());} public static void LogError(object o){Log(o);} } }
class Program { static void Main() {
 for(int i=0;i<100;i++) TCG.SwitchStreaming.LocalizationTrace.Stage("stage"+i, -1, -1, -1, null);
 var error=new InvalidOperationException("sentinel");
 TCG.SwitchStreaming.LocalizationTrace.Failure("boundary",error,-1,-1,-1,null);
 TCG.SwitchStreaming.LocalizationTrace.Failure("again",error,3,4,5,"en");
 var lines=UnityEngine.Debug.Lines;
 Console.Write(lines.Count+":"+(lines.Count>0 && lines[0].Contains("sources=-1"))+":"+(lines.Count>64 && lines[64].Contains("sentinel"))+":"+(lines.Count>64 && lines[64].Contains("boundary"))+":"+(lines.Count>64 && lines[64].Contains("lastStage=stage99")));
} }'''
        self.assertEqual(self.run_csharp(source), '65:True:True:True:True')

    def test_early_exception_does_not_consume_marker_budget(self):
        source = '''using System; using System.Collections.Generic;
namespace UnityEngine { public static class Debug { public static List<string> Lines=new List<string>(); public static void Log(object o){Lines.Add(o.ToString());} public static void LogError(object o){Log(o);} } }
class Program { static void Main() {
 TCG.SwitchStreaming.LocalizationTrace.Failure("early",new Exception("sentinel"),1,1,2,"en");
 for(int i=0;i<100;i++) TCG.SwitchStreaming.LocalizationTrace.Stage("marker",1,1,2,"en");
 Console.Write(UnityEngine.Debug.Lines.Count);
} }'''
        self.assertEqual(self.run_csharp(source), '65')

    def test_diagnostic_sink_failure_cannot_replace_original_exception(self):
        source = probe.patch_localization(FIXTURE).replace('I2.Loc.LocalizationManager.Fail=true;', 'I2.Loc.LocalizationManager.Fail=true; UnityEngine.Debug.Throw=true;')
        self.assertEqual(self.run_csharp(source), 'saved:sv:1:3:3:True:5')

    def test_prepare_backup_and_repeat_are_guarded(self):
        with tempfile.TemporaryDirectory() as directory:
            project = pathlib.Path(directory)
            path = project / 'Assets/Scripts/Assembly-CSharp/I2/Loc/LocalizationManager.cs'
            path.parent.mkdir(parents=True); path.write_text(FIXTURE)
            receipt = probe.prepare_localization_probe(project)
            once = path.read_text()
            self.assertEqual(pathlib.Path(receipt['backup']).read_text(), FIXTURE)
            self.assertTrue((project / 'Assets/TCGSwitchStreaming/Runtime/LocalizationTrace.cs').is_file())
            probe.prepare_localization_probe(project)
            self.assertEqual(path.read_text(), once)
            path.write_text(once + '\n// unexpected edit\n')
            with self.assertRaises(ValueError): probe.prepare_localization_probe(project)
            self.assertEqual(pathlib.Path(receipt['backup']).read_text(), FIXTURE)

    def test_unknown_partial_or_unbalanced_source_rejected_before_writes(self):
        for source in (FIXTURE.replace('RegisterSceneSources();', 'ChangedSourceCall();'), FIXTURE[:-10], FIXTURE.replace('public static string CurrentLanguageCode', 'public static string RenamedCode')):
            with self.subTest(source=source[-40:]):
                with self.assertRaises(ValueError): probe.patch_localization(source)
        with tempfile.TemporaryDirectory() as directory:
            project = pathlib.Path(directory)
            path = project / 'Assets/Scripts/Assembly-CSharp/I2/Loc/LocalizationManager.cs'
            path.parent.mkdir(parents=True); path.write_text('class Unknown {}')
            with self.assertRaises(ValueError): probe.prepare_localization_probe(project)
            self.assertEqual(path.read_text(), 'class Unknown {}')
            self.assertFalse((project / 'Assets/TCGSwitchStreaming').exists())


if __name__ == '__main__': unittest.main()
