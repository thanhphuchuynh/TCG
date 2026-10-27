using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[BepInPlugin("local.tcg.native.shopmenu", "Native Shop Menu", "1.0.0")]
[BepInDependency("com.qolmods.shopos", BepInDependency.DependencyFlags.HardDependency)]
public sealed class NativeShopMenu : BaseUnityPlugin {
 static readonly ConditionalWeakTable<object, Home> Homes=new ConditionalWeakTable<object, Home>();
 static Type Desktop,Tile;
 static readonly Dictionary<string,FieldInfo> Fields=new Dictionary<string,FieldInfo>();
 static readonly Dictionary<string,MethodInfo> Methods=new Dictionary<string,MethodInfo>();
 static TMP_FontAsset GameFont; static float AssetsAt;
 static readonly Color[] Colors={new Color(.07f,.55f,.85f),new Color(.95f,.43f,.06f),new Color(.27f,.63f,.13f),new Color(.60f,.31f,.77f)};
 class Home {public RectTransform Root;public MenuGeometry Geometry;public bool LaidOut;public float LastStyle;public readonly HashSet<int> Styled=new HashSet<int>();}
 void Awake(){
  var h=new Harmony("local.tcg.native.shopmenu");
  try {
   Desktop=AccessTools.TypeByName("ShopOS.OsDesktop");Tile=Desktop==null?null:Desktop.GetNestedType("Tile",BindingFlags.Public|BindingFlags.NonPublic);
   if(Desktop==null||Tile==null)throw new MissingMemberException("ShopOS home types unavailable");
   foreach(var n in new[]{"_root","_viewport","_cols","_rows","_pageW","_sized","_shownTiles","_apps","_folders","_pattern","_picture","_lift","_fade","_backLogo","_infoCard","_barClock","_shop","_money","_lightsCard","_nowCard","_blobs"})BindField(Desktop,n);
   foreach(var n in new[]{"Rect","Body","Slot","InFolder","Placed"})BindField(Tile,n);
   foreach(var n in new[]{"Build","Layout","SlotPos","Overflow","Arrange","Refresh","Animate","SyncApps","DragTick","OpenFolder","CloseFolder","ReturnToGrid"}){
    var m=AccessTools.Method(Desktop,n);if(m==null)throw new MissingMethodException(n);Methods.Add(n,m);
   }
   if(Methods["Layout"].GetParameters()[0].ParameterType!=typeof(Vector2)||Methods["SlotPos"].ReturnType!=typeof(Vector2))throw new NotSupportedException("ShopOS layout signature changed");
   h.Patch(Methods["Layout"],prefix:new HarmonyMethod(typeof(NativeShopMenu),"Layout"));
   h.Patch(Methods["SlotPos"],prefix:new HarmonyMethod(typeof(NativeShopMenu),"SlotPos"));
   h.Patch(Methods["Build"],postfix:new HarmonyMethod(typeof(NativeShopMenu),"Built"));
   h.Patch(Methods["SyncApps"],postfix:new HarmonyMethod(typeof(NativeShopMenu),"Changed"));
   h.Patch(Methods["Refresh"],postfix:new HarmonyMethod(typeof(NativeShopMenu),"Changed"));
   h.Patch(Methods["Animate"],postfix:new HarmonyMethod(typeof(NativeShopMenu),"Changed"));
   h.Patch(Methods["DragTick"],transpiler:new HarmonyMethod(typeof(NativeShopMenu),"DragGeometry"));
   foreach(var method in new[]{"OpenFolder","CloseFolder","ReturnToGrid"})h.Patch(Methods[method],postfix:new HarmonyMethod(typeof(NativeShopMenu),"ForceChanged"));
   Logger.LogInfo("Colorful ShopOS home style enabled; original app tiles and handlers retained.");
  }catch(Exception e){h.UnpatchSelf();Logger.LogError("Menu adapter unavailable; original ShopOS retained: "+e);}
 }
 static void BindField(Type t,string n){var f=AccessTools.Field(t,n);if(f==null)throw new MissingFieldException(t.Name,n);Fields.Add(t.FullName+"/"+n,f);}
 static object Get(object o,string n){return Fields[o.GetType().FullName+"/"+n].GetValue(o);}
 static T Get<T>(object o,string n){return (T)Get(o,n);}
 static void Set(object o,string n,object v){Fields[o.GetType().FullName+"/"+n].SetValue(o,v);}
 static Home State(object o){return Homes.GetValue(o,k=>new Home{Root=Get<RectTransform>(k,"_root")});}
 static bool Layout(object __instance,Vector2 __0){
  if(__0.x<600||__0.y<400)return true;
  var s=State(__instance);s.Geometry=MenuGeometry.Create(__0.x,__0.y);s.LaidOut=true;
  Set(__instance,"_cols",s.Geometry.Columns);Set(__instance,"_rows",s.Geometry.Rows);Set(__instance,"_pageW",__0.x);Set(__instance,"_sized",true);
  var viewport=Get<RectTransform>(__instance,"_viewport");viewport.sizeDelta=__0;
  Methods["Overflow"].Invoke(__instance,null);Methods["Arrange"].Invoke(__instance,null);
  foreach(var t in (IEnumerable)Get(__instance,"_shownTiles"))Set(t,"Placed",false);
  Style(__instance,s);return false;
 }
 static bool SlotPos(object __instance,int __0,ref Vector2 __result){Home s;if(!Homes.TryGetValue(__instance,out s)||!s.LaidOut||__0<0||__0>=24)return true;var p=s.Geometry.Slot(__0);__result=new Vector2(p.X,p.Y);return false;}
 static void Built(object __result){if(__result==null)return;var s=State(__result);Style(__result,s);}
 static void Changed(object __instance){Home s;if(!Homes.TryGetValue(__instance,out s)||!s.LaidOut)return;if(Time.realtimeSinceStartup-s.LastStyle>.25f)Style(__instance,s);else Frame(__instance,s);}
 static void Frame(object desktop,Home s){
  if(!s.Root)return;Hide(Get(desktop,"_infoCard") as UnityEngine.Object);Hide(Get(desktop,"_nowCard") as UnityEngine.Object);
  var lights=Get<RectTransform>(desktop,"_lightsCard");if(lights){lights.localScale=Vector3.one*s.Geometry.FooterScale;Box(lights,new Vector2(0,0),new Vector2(0,0),new Vector2(s.Geometry.Margin,16),new Vector2(470,150));}
 }

 static void ForceChanged(object __instance){Home s;if(Homes.TryGetValue(__instance,out s))Style(__instance,s);}
 static IEnumerable<CodeInstruction> DragGeometry(IEnumerable<CodeInstruction> input){
  int changed=0;foreach(var c in input){int metric=c.opcode==OpCodes.Ldc_R4?DragMetricIds.Get((float)c.operand):-1;
   if(metric<0){yield return c;continue;}
   c.opcode=OpCodes.Ldarg_0;c.operand=null;yield return c;yield return new CodeInstruction(OpCodes.Ldc_I4,metric);yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(NativeShopMenu),"DragMetric"));changed++;
  }if(changed!=15)throw new NotSupportedException("ShopOS drag layout changed: "+changed+" metrics");
 }
 static float DragMetric(object desktop,int key){var g=State(desktop).Geometry;var p=g.Slot(0);var b=g.Shape(false);switch(key){case 0:return g.Margin;case 1:return p.X;case 2:return g.PitchX;case 3:return g.PitchX*.5f;case 4:return p.Y;case 5:return g.PitchY;case 6:return g.TileHeight*.5f;case 7:return -b.BodyY-b.BodySize*.5f;case 8:return b.BodySize*.5f;default:return b.BodySize*.36f;}}

 static void Assets(){
  if(GameFont||Time.realtimeSinceStartup<AssetsAt)return;AssetsAt=Time.realtimeSinceStartup+1;
  if(!GameFont)GameFont=Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(x=>x.name=="FredokaOne-Regular SDF border");
 }
 static void Hide(UnityEngine.Object o){var c=o as Component;if(c)c.gameObject.SetActive(false);}
 static RectTransform Child(Transform parent,string name){var t=parent.Find(name) as RectTransform;if(t)return t;var go=new GameObject(name,typeof(RectTransform));go.layer=parent.gameObject.layer;t=(RectTransform)go.transform;t.SetParent(parent,false);return t;}
 static void Stretch(RectTransform r,Vector2 lo,Vector2 hi){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=lo;r.offsetMax=hi;}
 static MaskableGraphic Paint(RectTransform r,Color color){var existing=r.GetComponent<Image>();if(existing){existing.sprite=null;existing.type=Image.Type.Simple;existing.color=color;existing.raycastTarget=false;return existing;}var panel=r.GetComponent<NativeMenuPanel>()??r.gameObject.AddComponent<NativeMenuPanel>();panel.color=color;panel.raycastTarget=false;return panel;}
 static void Box(RectTransform r,Vector2 anchor,Vector2 pivot,Vector2 pos,Vector2 size){r.anchorMin=r.anchorMax=anchor;r.pivot=pivot;r.anchoredPosition=pos;r.sizeDelta=size;}
 static void Text(TextMeshProUGUI t,float size,TextAlignmentOptions alignment){if(!t)return;if(GameFont)t.font=GameFont;t.enableAutoSizing=true;t.fontSize=size;t.fontSizeMin=size*.80f;t.fontSizeMax=size;t.alignment=alignment;t.color=Color.white;t.enableWordWrapping=false;t.overflowMode=TextOverflowModes.Ellipsis;t.raycastTarget=false;}
 static void Style(object desktop,Home s){
  if(!s.Root||!s.LaidOut)return;
  s.LastStyle=Time.realtimeSinceStartup;Assets();var g=s.Geometry;
  var background=Child(s.Root,"NativeMenuBackground");Stretch(background,Vector2.zero,Vector2.zero);Paint(background,new Color(.13f,.18f,.23f));background.SetAsFirstSibling();
  foreach(var name in new[]{"_pattern","_lift","_fade","_backLogo","_infoCard","_nowCard"})Hide(Get(desktop,name) as UnityEngine.Object);
  foreach(var blob in (IEnumerable)Get(desktop,"_blobs")){
   var f=AccessTools.Field(blob.GetType(),"Rect");if(f!=null)Hide(f.GetValue(blob) as UnityEngine.Object);
  }
  var bar=s.Root.Find("TopBar") as RectTransform;
  if(bar){
   Box(bar,new Vector2(.5f,1),new Vector2(.5f,1),Vector2.zero,new Vector2(g.Width,g.Top*.77f));Paint(bar,new Color(.08f,.37f,.64f));
   Hide(bar.Find("Logo"));Hide(bar.Find("Hairline"));
   var title=bar.Find("OsName");if(title){var text=title.GetComponent<TextMeshProUGUI>();if(text){text.text="SHOP MENU";Text(text,38,TextAlignmentOptions.MidlineLeft);Box(text.rectTransform,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(g.Margin,0),new Vector2(g.Width*.21f,64));}}
   var shop=Get<TextMeshProUGUI>(desktop,"_shop");var money=Get<TextMeshProUGUI>(desktop,"_money");var clock=Get<TextMeshProUGUI>(desktop,"_barClock");
   if(shop){if(shop.transform.parent!=bar)shop.transform.SetParent(bar,false);Text(shop,31,TextAlignmentOptions.MidlineLeft);Box(shop.rectTransform,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(g.Width*.25f,0),new Vector2(g.Width*.25f,62));}
   if(clock){Text(clock,31,TextAlignmentOptions.MidlineRight);Box(clock.rectTransform,new Vector2(1,.5f),new Vector2(1,.5f),new Vector2(-g.Width*.22f,0),new Vector2(g.Width*.29f,62));}
   if(money){if(money.transform.parent!=bar)money.transform.SetParent(bar,false);Text(money,36,TextAlignmentOptions.MidlineRight);money.color=new Color(.83f,1f,.36f);Box(money.rectTransform,new Vector2(1,.5f),new Vector2(1,.5f),new Vector2(-g.Margin,0),new Vector2(g.Width*.18f,62));}
  }
  var apps=(IDictionary)Get(desktop,"_apps");foreach(DictionaryEntry e in apps)StyleTile(e.Value,s);
  foreach(var t in (IEnumerable)Get(desktop,"_folders"))StyleTile(t,s);
  var lights=Get<RectTransform>(desktop,"_lightsCard");if(lights){lights.localScale=Vector3.one*g.FooterScale;Box(lights,new Vector2(0,0),new Vector2(0,0),new Vector2(g.Margin,16),new Vector2(470,150));}
  var dock=s.Root.Find("Dock") as RectTransform;if(dock){Box(dock,new Vector2(1,0),new Vector2(1,0),new Vector2(-g.Margin,24),new Vector2(340,58));}
  Frame(desktop,s);
 }
 static void StyleTile(object tile,Home home){
  var rect=Get<RectTransform>(tile,"Rect");var body=Get<RectTransform>(tile,"Body");if(!rect||!body)return;
  var g=home.Geometry;bool folder=Get<bool>(tile,"InFolder");var shape=g.Shape(folder);rect.sizeDelta=new Vector2(shape.Width,shape.Height);
  body.sizeDelta=new Vector2(shape.BodySize,shape.BodySize);body.anchoredPosition=new Vector2(0,shape.BodyY);
  if(home.Styled.Add(rect.GetInstanceID())){
   foreach(var n in new[]{"Back","Plate"}){var t=body.Find(n);if(t)Hide(t);}
   var button=Child(rect,"NativeButton");Stretch(button,new Vector2(0,0),Vector2.zero);button.SetAsFirstSibling();Paint(button,new Color(.025f,.04f,.055f));
   var rim=Child(button,"Rim");Stretch(rim,new Vector2(4,5),new Vector2(-4,-4));Paint(rim,new Color(.85f,.91f,.94f));
   var color=Colors[Math.Abs(Get<int>(tile,"Slot"))%Colors.Length];
   var fill=Child(rim,"Fill");Stretch(fill,new Vector2(4,4),new Vector2(-4,-4));Paint(fill,color);
   var highlight=Child(fill,"Highlight");highlight.anchorMin=new Vector2(0,1);highlight.anchorMax=Vector2.one;highlight.offsetMin=new Vector2(12,-12);highlight.offsetMax=new Vector2(-12,-4);Paint(highlight,new Color(1,1,1,.35f));
  }
  foreach(var n in new[]{"Name","NameShadow"}){
   var t=rect.Find(n);if(!t)continue;var label=t.GetComponent<TextMeshProUGUI>();if(!label)continue;
   if(n=="NameShadow"){t.gameObject.SetActive(false);continue;}
   Text(label,shape.Font,TextAlignmentOptions.Center);label.fontStyle=FontStyles.Normal;Box(label.rectTransform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,folder?0:7),new Vector2(shape.Width-18,folder?38:shape.Height*.26f));
  }
 }
}
