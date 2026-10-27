using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;

[BepInPlugin("local.tcg.native.loadingtips", "Clear Loading Tips", "1.0.0")]
[BepInDependency("EnhancedPrefabLoader", BepInDependency.DependencyFlags.HardDependency)]
public sealed class NativeLoadingTips:BaseUnityPlugin {
 public static readonly string[] Tips={
  "Use the tablet to order stock and furniture.",
  "Check your stock before placing a new order.",
  "Open Settings to change display and control options.",
  "Use Collection Tracker to review your collection.",
  "Check the Rent app for your shop bills.",
  "Keep popular products stocked on your shelves.",
  "You can move apps around on the tablet.",
  "Open Credits for links to the game and mod authors."
 };
 static FieldInfo messages,active;
 void Awake(){
  var h=new Harmony("local.tcg.native.loadingtips");
  try {
   var type=AccessTools.TypeByName("EnhancedPrefabLoader.Core.LoadingPipeline.LoadingScreenMessageService");
   if(type==null)throw new TypeLoadException("Loading tip service unavailable");
   messages=AccessTools.Field(type,"loadingMessages");active=AccessTools.Field(type,"activeRoutine");
   var build=AccessTools.Method(type,"BuildMessageList");var init=AccessTools.Method(type,"Initialize");var start=AccessTools.Method(type,"Start");
   if(messages==null||active==null||messages.FieldType!=typeof(List<string>)||build==null||build.ReturnType!=typeof(List<string>)||init==null||start==null)throw new MissingMemberException("Loading tip service signature changed");
   h.Patch(build,postfix:new HarmonyMethod(typeof(NativeLoadingTips),"ReplaceList"));
   h.Patch(init,postfix:new HarmonyMethod(typeof(NativeLoadingTips),"RefreshIdle"));
   h.Patch(start,prefix:new HarmonyMethod(typeof(NativeLoadingTips),"RefreshIdle"));
   Logger.LogInfo("8 clear gameplay tips enabled; loading percentage and progress behavior unchanged.");
  }catch(Exception e){h.UnpatchSelf();Logger.LogError("Loading tips unchanged: "+e);}
 }
 static void ReplaceList(ref List<string> __result){__result=new List<string>(Tips);}
 static void RefreshIdle(object __instance){if(active.GetValue(__instance)==null)messages.SetValue(__instance,new List<string>(Tips));}
}
