using System;using System.IO;
namespace TCG.NativeCompatibility {
 public static class PathLiterals {
  public static bool IsMac {get{return Environment.OSVersion.Platform==PlatformID.MacOSX||(Environment.OSVersion.Platform==PlatformID.Unix&&File.Exists("/System/Library/CoreServices/SystemVersion.plist"));}}
  public static string Select(string literal){return SelectForPlatform(literal,IsMac);}
  public static string SelectForPlatform(string literal,bool mac){if(mac&&literal=="\\cards")return "/cards";if(mac&&literal=="\\")return "/";return literal;}
  public static string PrepatchDataBase(string localData,string suffix){return PrepatchBaseForPlatform(localData,suffix,IsMac,Environment.GetFolderPath(Environment.SpecialFolder.Personal));}
  public static string PrepatchBaseForPlatform(string localData,string suffix,bool mac,string home){return mac?Path.Combine(home,"Library","Application Support"):String.Concat(localData,suffix);}
 }
}
