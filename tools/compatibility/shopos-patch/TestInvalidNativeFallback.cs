using System;using System.IO;using TCG.NativeCompatibility;
class TestInvalidNativeFallback {
 static void Main(string[] a) {
  string dir=Path.Combine(Path.GetFullPath(a[0]),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  // This test must exercise rejection with no valid native fallback on this host.
  foreach(string root in new[]{"/opt/homebrew/bin","/usr/local/bin","/usr/bin","/bin"})
   if(NativeToolPaths.IsMacExecutableFormat(Path.Combine(root,"yt-dlp")))throw new Exception("Test precondition: yt-dlp unexpectedly installed in standard directory");
  int checks=0;
  foreach(byte[] magic in new[]{new byte[]{77,90,0,0},new byte[]{127,69,76,70},new byte[]{1},new byte[]{0,0,0,0}}) {
   string file=Path.Combine(dir,"yt-dlp");File.WriteAllBytes(file,magic);
   string result=NativeToolPaths.Resolve(dir,"yt-dlp.exe",true,"/nonexistent-only");
   if(File.Exists(result))throw new Exception("Rejected native-name file escaped resolver: "+result);
   if(result!=String.Empty)throw new Exception("Expected empty unavailable result");
   if(NativeToolPaths.IsMac && NativeToolPaths.CanSelfUpdate(file,dir))throw new Exception("Rejected file permitted self-update");
   checks+=3;
  }
  string lockedFile=Path.Combine(dir,"yt-dlp");File.WriteAllBytes(lockedFile,new byte[]{0xcf,0xfa,0xed,0xfe});
  using(var locked=new FileStream(lockedFile,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) {
   if(NativeToolPaths.IsMacExecutableFormat(lockedFile))throw new Exception("Test precondition: exclusive lock did not reject reading");
   string result=NativeToolPaths.Resolve(dir,"yt-dlp.exe",true,"/nonexistent-only");
   if(result!=String.Empty||File.Exists(result)||NativeToolPaths.CanSelfUpdate(lockedFile,dir))throw new Exception("Unreadable native helper escaped rejection");
   checks+=3;
  }
  if(NativeToolPaths.IsMac && (NativeToolPaths.CanSelfUpdate("",dir)||NativeToolPaths.CanSelfUpdate(null,dir)))throw new Exception("Unavailable helper permitted self-update");
  Console.WriteLine((checks+2)+" invalid native filename / unavailable self-update checks passed; no helper executed");
 }
}
