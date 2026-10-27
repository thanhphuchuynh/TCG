using System;using System.IO;using TCG.NativeCompatibility;
class TestPathLiterals {
 static int checks;static void Eq(string a,string b){checks++;if(a!=b)throw new Exception(a+" != "+b);}
 static void Main(string[] a){string root=Path.Combine(Path.GetFullPath(a[0]),Guid.NewGuid().ToString("N"));string cardDir=Path.Combine(root,"objects_data/cards/Tetramon/Normal/");Directory.CreateDirectory(cardDir);string legacyRoot=Path.Combine(root,"objects_data/cards");File.WriteAllText(Path.Combine(legacyRoot,"1_NAME.txt"),"Legacy name");
  if(cardDir.Contains("\\cards"))throw new Exception("Fixture unexpectedly satisfies Windows literal");checks++;
  string needle=PathLiterals.SelectForPlatform("\\cards",true);if(!cardDir.Contains(needle))throw new Exception("Native card path not classified");checks++;
  string derived=cardDir.Substring(0,cardDir.IndexOf(needle,StringComparison.Ordinal)+needle.Length)+PathLiterals.SelectForPlatform("\\",true);
  Eq(File.ReadAllText(derived+"1_NAME.txt"),"Legacy name");
  Eq(PathLiterals.SelectForPlatform("\\cards",false),"\\cards");Eq(PathLiterals.SelectForPlatform("\\",false),"\\");Eq(PathLiterals.SelectForPlatform("Shader\\AssetName",true),"Shader\\AssetName");Eq(PathLiterals.SelectForPlatform("\\s+",true),"\\s+");Eq(PathLiterals.SelectForPlatform("Tetramon/Normal",true),"Tetramon/Normal");
  Eq(PathLiterals.PrepatchBaseForPlatform("C:\\Users\\x\\AppData\\Local","Low",false,"unused"),"C:\\Users\\x\\AppData\\LocalLow");
  Eq(Path.Combine(PathLiterals.PrepatchBaseForPlatform("ignored","Low",true,"/Users/example"),"OPNeonGames","Card Shop Simulator"),"/Users/example/Library/Application Support/OPNeonGames/Card Shop Simulator");
  Console.WriteLine(checks+" pure path/legacy file fixture assertions passed; no plugin code executed");
 }
}
