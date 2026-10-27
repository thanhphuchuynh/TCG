using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;
[BepInPlugin("local.tcg.saveload.exceptionlogger", "Save Load Exception Logger", "1.0")]
public class SaveLoadExceptionLogger : BaseUnityPlugin {
 void Awake(){
  var h=new Harmony("local.tcg.saveload.exceptionlogger");
  h.Patch(AccessTools.Method(typeof(CGameData),"PropagateLoadData"),transpiler:new HarmonyMethod(typeof(SaveLoadExceptionLogger),"TraceCatch"));
  h.Patch(AccessTools.Method(typeof(ShelfManager),"LoadInteractableObjectData"),finalizer:new HarmonyMethod(typeof(SaveLoadExceptionLogger),"ShelfError"));
  Logger.LogInfo("Save-load exception logging enabled; normal loading and saving preserved.");
 }
 static Exception ShelfError(Exception __exception){if(__exception!=null)Debug.LogError("SAVE_LOAD_SHELF_EXCEPTION: "+__exception);return __exception;}
 static IEnumerable<CodeInstruction> TraceCatch(IEnumerable<CodeInstruction> input){
  int count=0;
  foreach(var c in input){
   if(c.blocks.Any(b=>b.blockType==ExceptionBlockType.BeginCatchBlock)){
    var d=new CodeInstruction(OpCodes.Dup);d.blocks.AddRange(c.blocks);c.blocks.Clear();d.labels.AddRange(c.labels);c.labels.Clear();yield return d;
    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(SaveLoadExceptionLogger),"Report")); count++;
   }
   yield return c;
  }
  if(count!=1)throw new InvalidOperationException("Expected exactly one load catch; found "+count);
 }
 static void Report(object e){Debug.LogError("SAVE_LOAD_EXCEPTION: "+e);}
}
