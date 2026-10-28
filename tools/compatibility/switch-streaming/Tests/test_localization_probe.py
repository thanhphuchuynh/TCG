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
 public class LanguageSourceAsset { public LanguageSourceData mSource = new LanguageSourceData(); }
 public class ResourceManager {
  public static int SingletonCalls, AssetCalls;
  public static bool FailSingleton, FailAsset;
  static ResourceManager instance = new ResourceManager();
  public static ResourceManager pInstance { get { SingletonCalls++; if(FailSingleton) throw Program.Expected; return instance; } }
  public T GetAsset<T>(string key) where T:class { AssetCalls++; if(FailAsset) throw Program.Expected; return new LanguageSourceAsset() as T; }
 }
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
  private static void RegisterSourceInResources() {
   string[] globalSources = GlobalSources;
   foreach(string name in globalSources) {
    LanguageSourceAsset asset = ResourceManager.pInstance.GetAsset<LanguageSourceAsset>(name);
    if(asset != null && !Sources.Contains(asset.mSource)) Sources.Add(asset.mSource);
   }
  }
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
    def run_csharp(self, source, trace_override=None):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            fixture = root / 'fixture.cs'; fixture.write_text(source)
            output = root / 'fixture.exe'
            helper = ROOT / 'Runtime/LocalizationTrace.cs'
            if trace_override is not None:
                helper = root / 'trace.cs'; helper.write_text(trace_override)
            compiled = subprocess.run([str(MONO / 'mcs'), '-out:' + str(output), str(fixture), str(helper)], text=True, capture_output=True)
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
 Console.Write(lines.Count+":"+(lines.Count>0 && lines[0].Contains("sources=-1"))+":"+(lines.Count>65 && lines[65].Contains("sentinel"))+":"+(lines.Count>65 && lines[65].Contains("boundary"))+":"+(lines.Count>65 && lines[65].Contains("lastStage=stage99")));
} }'''
        self.assertEqual(self.run_csharp(source), '66:True:True:True:True')

    def test_early_exception_does_not_consume_marker_budget(self):
        source = '''using System; using System.Collections.Generic;
namespace UnityEngine { public static class Debug { public static List<string> Lines=new List<string>(); public static void Log(object o){Lines.Add(o.ToString());} public static void LogError(object o){Log(o);} } }
class Program { static void Main() {
 TCG.SwitchStreaming.LocalizationTrace.Failure("early",new Exception("sentinel"),1,1,2,"en");
 for(int i=0;i<100;i++) TCG.SwitchStreaming.LocalizationTrace.Stage("marker",1,1,2,"en");
 Console.Write(UnityEngine.Debug.Lines.Count);
} }'''
        self.assertEqual(self.run_csharp(source), '66')

    def test_diagnostic_sink_failure_cannot_replace_original_exception(self):
        source = probe.patch_localization(FIXTURE).replace('I2.Loc.LocalizationManager.Fail=true;', 'I2.Loc.LocalizationManager.Fail=true; UnityEngine.Debug.Throw=true;')
        self.assertEqual(self.run_csharp(source), 'saved:sv:1:3:3:True:5')

    def test_snapshot_failure_does_not_change_return_original_exception_or_finally(self):
        source = FIXTURE.replace(' public class LanguageSourceData { }', ''' public class LanguageSourceData { }
 public class ObserverList : List<LanguageSourceData> { public new int Count { get { throw new Exception("observer count failed"); } } }''')
        source = source.replace('public static List<LanguageSourceData> Sources = new List<LanguageSourceData>();', 'public static ObserverList Sources = new ObserverList();')
        source = source.replace('try { InitializeIfNeeded(); return mCurrentLanguage; }', 'try { if(Fail) throw Program.Expected; return mCurrentLanguage; }')
        source = source.replace('try { InitializeIfNeeded(); return mLanguageCode; }', 'try { return mLanguageCode; }')
        source = probe.patch_localization(source)
        self.assertEqual(self.run_csharp(source), 'setter:en:1:3:0:True:5')

    def test_sink_entry_faults_do_not_replace_production_semantics(self):
        helper = '''using System; namespace TCG.SwitchStreaming { public static class LocalizationTrace {
 public static void Stage(string s,int a,int b,int c,string d){throw new Exception("observer entry");}
 public static void Snapshot(string s,int a,int b,int c,string d){throw new Exception("observer entry");}
 public static void Boundary(string s){throw new Exception("observer entry");}
 public static void ObjectState(string s,bool a,bool b){throw new Exception("observer entry");}
 public static void Failure(string s,Exception e,int a,int b,int c,string d){throw new Exception("observer entry");}
} }'''
        self.assertEqual(self.run_csharp(probe.patch_localization(FIXTURE), helper), 'saved:sv:1:3:3:True:5')

    def test_counter_observer_failure_cannot_escape_sink(self):
        source = '''using System;
namespace UnityEngine { public static class Debug { public static void Log(object o){} public static void LogError(object o){} } }
namespace System.Threading { public static class Interlocked {
 public static int Increment(ref int value){throw new Exception("counter failed");}
 public static int CompareExchange(ref int value,int next,int old){throw new Exception("counter failed");}
 public static int Exchange(ref int value,int next){throw new Exception("counter failed");}
} }
class Program { static void Main() {
 TCG.SwitchStreaming.LocalizationTrace.Stage("entry",-1,-1,-1,null);
 TCG.SwitchStreaming.LocalizationTrace.Failure("boundary",new Exception("original"),-1,-1,-1,null);
 Console.Write("survived");
} }'''
        self.assertEqual(self.run_csharp(source), 'survived')

    def test_failed_error_sink_does_not_consume_only_exception_report(self):
        source = '''using System; using System.Collections.Generic;
namespace UnityEngine { public static class Debug { public static bool Throw=true; public static List<string> Lines=new List<string>(); public static void Log(object o){if(Throw) throw new Exception("observer"); Lines.Add(o.ToString());} public static void LogError(object o){Log(o);} } }
class Program { static void Main() {
 var error=new Exception("original");
 TCG.SwitchStreaming.LocalizationTrace.Failure("first",error,-1,-1,-1,null);
 UnityEngine.Debug.Throw=false;
 TCG.SwitchStreaming.LocalizationTrace.Failure("second",error,-1,-1,-1,null);
 Console.Write(UnityEngine.Debug.Lines.Count);
} }'''
        self.assertEqual(self.run_csharp(source), '1')

    def test_resource_lookup_runs_once_in_original_order_and_failure_identity_survives(self):
        source = FIXTURE[:FIXTURE.index(' static void Main() {')] + ''' static void Main() {
 I2.Loc.LocalizationManager.UpdateSources();
 bool singleton=false,asset=false;
 I2.Loc.ResourceManager.FailSingleton=true;
 try{I2.Loc.LocalizationManager.UpdateSources();}catch(Exception e){singleton=Object.ReferenceEquals(e,Expected);}
 I2.Loc.ResourceManager.FailSingleton=false; I2.Loc.ResourceManager.FailAsset=true;
 try{I2.Loc.LocalizationManager.UpdateSources();}catch(Exception e){asset=Object.ReferenceEquals(e,Expected);}
 Console.Write(I2.Loc.ResourceManager.SingletonCalls+":"+I2.Loc.ResourceManager.AssetCalls+":"+singleton+":"+asset+":"+
 UnityEngine.Debug.Lines.Exists(x=>x.Contains("RESOURCE.singleton.before"))+":"+UnityEngine.Debug.Lines.Exists(x=>x.Contains("RESOURCE.asset.after")));
} }'''
        self.assertEqual(self.run_csharp(probe.patch_localization(source)), '3:2:True:True:True:True')

    def test_first_bare_marker_is_protected_and_before_snapshot_read(self):
        verifier = r'''static bool EntryProtected() {
  var getter=typeof(I2.Loc.LocalizationManager).GetProperty("CurrentLanguage").GetGetMethod();
  var body=getter.GetMethodBody(); var bytes=body.GetILAsByteArray();
  var table=new Dictionary<short,System.Reflection.Emit.OpCode>();
  foreach(var field in typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static))
   if(field.FieldType==typeof(System.Reflection.Emit.OpCode)) { var op=(System.Reflection.Emit.OpCode)field.GetValue(null); table[op.Value]=op; }
  for(int index=0;index<bytes.Length;) {
   int offset=index; short value=bytes[index++]; if(value==254) value=(short)(0xfe00|bytes[index++]);
   var op=table[value];
   if(op==System.Reflection.Emit.OpCodes.Ldsfld || op==System.Reflection.Emit.OpCodes.Ldfld || op==System.Reflection.Emit.OpCodes.Ldlen) return false;
   if(op==System.Reflection.Emit.OpCodes.Call) {
    var called=getter.Module.ResolveMethod(BitConverter.ToInt32(bytes,index)); if(called.Name!="Stage") return false;
    int regions=0; foreach(var region in body.ExceptionHandlingClauses) if(region.TryOffset<=offset && offset<region.TryOffset+region.TryLength) regions++;
    return regions>=2;
   }
   switch(op.OperandType) {
    case System.Reflection.Emit.OperandType.InlineNone: break;
    case System.Reflection.Emit.OperandType.ShortInlineI: case System.Reflection.Emit.OperandType.ShortInlineBrTarget: case System.Reflection.Emit.OperandType.ShortInlineVar: index++; break;
    case System.Reflection.Emit.OperandType.InlineVar: index+=2; break;
    case System.Reflection.Emit.OperandType.InlineI8: case System.Reflection.Emit.OperandType.InlineR: index+=8; break;
    case System.Reflection.Emit.OperandType.InlineSwitch: int count=BitConverter.ToInt32(bytes,index); index+=4+count*4; break;
    default: index+=4; break;
   }
  }
  return false;
 }
'''
        source = FIXTURE.replace('class Program {', 'class Program { '+verifier)
        source = source.replace('Console.Write(result+', '''bool firstBare = UnityEngine.Debug.Lines.Count>0 && UnityEngine.Debug.Lines[0].Contains("CurrentLanguage.enter") && UnityEngine.Debug.Lines[0].Contains("sources=-1");
  if(!firstBare || !EntryProtected()) throw new Exception("first bare marker is not protected before field reads");
  foreach(string name in new[]{"Stage","Failure"}) if((typeof(TCG.SwitchStreaming.LocalizationTrace).GetMethod(name).GetMethodImplementationFlags() & MethodImplAttributes.NoInlining)==0) throw new Exception("sink can inline");
  Console.Write(result+''')
        self.assertEqual(self.run_csharp(probe.patch_localization(source)), 'saved:sv:1:3:3:True:5')

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

    def test_legacy_probe_requires_explicit_restore_and_preserves_backup(self):
        with tempfile.TemporaryDirectory() as directory:
            project = pathlib.Path(directory)
            path = project / 'Assets/Scripts/Assembly-CSharp/I2/Loc/LocalizationManager.cs'
            path.parent.mkdir(parents=True)
            previous = '// TCG_LOCALIZATION_PROBE_V1 original=' + probe.digest(FIXTURE) + ' body=' + probe.digest(FIXTURE) + '\n' + FIXTURE
            path.write_text(previous)
            backup = project / '.tcg-localization-probe/LocalizationManager.cs.original'
            backup.parent.mkdir(); backup.write_text(FIXTURE)
            with self.assertRaisesRegex(ValueError, 'requires restoration'):
                probe.prepare_localization_probe(project)
            self.assertEqual(path.read_text(), previous)
            self.assertEqual(backup.read_text(), FIXTURE)
            # The operator restores the verified preserved original explicitly.
            path.write_text(FIXTURE)
            probe.prepare_localization_probe(project)
            self.assertEqual(path.read_text(), probe.patch_localization(FIXTURE))
            self.assertEqual(backup.read_text(), FIXTURE)

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
