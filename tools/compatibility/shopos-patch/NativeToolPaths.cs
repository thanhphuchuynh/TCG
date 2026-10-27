using System;using System.IO;using System.Collections.Generic;
namespace TCG.NativeCompatibility {
public static class NativeToolPaths {
 public static bool IsMac { get {return Environment.OSVersion.Platform==PlatformID.MacOSX || (Environment.OSVersion.Platform==PlatformID.Unix && File.Exists("/System/Library/CoreServices/SystemVersion.plist"));} }
 public static string FileManager(){return IsMac?"/usr/bin/open":"explorer.exe";}
 public static bool CanSelfUpdate(string tool,string directory){
  if(!IsMac)return true;
  if(String.IsNullOrEmpty(tool)||String.IsNullOrEmpty(directory)||!Directory.Exists(directory)||!IsMacExecutableFormat(tool))return false;
  try{return String.Equals(Path.GetFullPath(Path.GetDirectoryName(tool)),Path.GetFullPath(directory),StringComparison.Ordinal);}catch{return false;}
 }
 public static string Combine(string directory,string windowsName){return Resolve(directory,windowsName,IsMac,Environment.GetEnvironmentVariable("PATH"));}
 public static string Resolve(string directory,string windowsName,bool mac,string path){
  if(!mac)return Path.Combine(directory,windowsName);
  string name;switch(windowsName){case "yt-dlp.exe":name="yt-dlp";break;case "qjs.exe":name="qjs";break;case "ffmpeg.exe":name="ffmpeg";break;default:return Path.Combine(directory,windowsName);}
  if(!Directory.Exists(directory))return String.Empty;
  var dirs=new List<string>();dirs.Add(directory);if(path!=null)dirs.AddRange(path.Split(Path.PathSeparator));dirs.AddRange(new[]{"/opt/homebrew/bin","/usr/local/bin","/usr/bin","/bin"});
  foreach(var dir in dirs){if(String.IsNullOrEmpty(dir)||!Path.IsPathRooted(dir))continue;var candidate=Path.Combine(dir,name);if(IsMacExecutableFormat(candidate))return candidate;}
  return String.Empty;
 }
 public static bool IsMacExecutableFormat(string path){try{using(var f=File.OpenRead(path)){var b=new byte[4];if(f.Read(b,0,4)<2)return false;if(b[0]==35&&b[1]==33)return true;uint magic=(uint)(b[0]<<24|b[1]<<16|b[2]<<8|b[3]);return magic==0xfeedface||magic==0xfeedfacf||magic==0xcefaedfe||magic==0xcffaedfe||magic==0xcafebabe||magic==0xbebafeca||magic==0xcafebabf;}}catch{return false;}}
}}
