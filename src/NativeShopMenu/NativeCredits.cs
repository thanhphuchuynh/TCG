using System;
using System.IO;
using System.Linq;
using BepInEx;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[BepInPlugin("local.tcg.native.credits", "Game and Mod Credits", "1.0.0")]
[BepInDependency("com.qolmods.shopos", BepInDependency.DependencyFlags.HardDependency)]
public sealed class NativeCreditsPlugin:BaseUnityPlugin {
 public const string Guid="local.tcg.native.credits",AppId="credits";
 NativeCreditsScreen screen;Texture2D iconTexture;Sprite icon;
 void Start(){
  string file=Path.Combine(Path.GetDirectoryName(typeof(NativeCreditsPlugin).Assembly.Location),"CreditsIcon.png");
  if(File.Exists(file)){iconTexture=new Texture2D(2,2,TextureFormat.RGBA32,false);if(ImageConversion.LoadImage(iconTexture,File.ReadAllBytes(file)))icon=Sprite.Create(iconTexture,new Rect(0,0,iconTexture.width,iconTexture.height),new Vector2(.5f,.5f));}
  bool registered=ShopOS.ShopOSApi.RegisterScreenApp(Guid,AppId,"Credits",icon,GetScreen);
  Logger.LogInfo("Credits screen registered="+registered+"; "+CreditCatalog.Entries.Length+" original project links.");
 }
 UIScreenBase GetScreen(){
  if(!screen){var go=new GameObject("NativeCreditsScreen");go.transform.SetParent(transform,false);screen=go.AddComponent<NativeCreditsScreen>();screen.Initialize();}
  return screen;
 }
 void OnDestroy(){ShopOS.ShopOSApi.UnregisterApp(Guid,AppId);if(screen)Destroy(screen.gameObject);if(icon)Destroy(icon);if(iconTexture)Destroy(iconTexture);}
}

public sealed class NativeCreditsScreen:UIScreenBase {
 public ScrollRect Scroll {get;private set;}
 public Button[] WebsiteButtons {get;private set;}
 public Button BackButton {get;private set;}
 TMP_FontAsset font;
 protected override void Awake(){base.Awake();m_FinishLoading=true;}
 // The factory may open this screen before its first Start; never hide it afterward.
 protected override void Start(){}
 public void Initialize(){
  font=Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f=>f.name=="FredokaOne-Regular SDF border");
  var canvas=Rect(transform,"CreditsCanvas");var c=canvas.gameObject.AddComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;c.sortingOrder=2000;
  var scale=canvas.gameObject.AddComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1920,1080);scale.matchWidthOrHeight=.5f;canvas.gameObject.AddComponent<GraphicRaycaster>();
  m_ScreenGroup=canvas.gameObject;
  var shade=Rect(canvas,"Dim");Stretch(shade,Vector2.zero,Vector2.zero);Panel(shade,new Color(.015f,.025f,.04f,.92f),true);
  var panel=Rect(canvas,"Panel");panel.anchorMin=new Vector2(.08f,.06f);panel.anchorMax=new Vector2(.92f,.94f);panel.offsetMin=panel.offsetMax=Vector2.zero;Panel(panel,new Color(.12f,.17f,.23f),true);
  var header=Rect(panel,"Header");header.anchorMin=new Vector2(0,1);header.anchorMax=Vector2.one;header.pivot=new Vector2(.5f,1);header.anchoredPosition=Vector2.zero;header.sizeDelta=new Vector2(0,100);Panel(header,new Color(.08f,.37f,.64f),false);
  Label(header,"Title","CREDITS & LINKS",42,Color.white,new Vector2(36,0),new Vector2(-36,0));
  var viewport=Rect(panel,"Viewport");Stretch(viewport,new Vector2(28,110),new Vector2(-28,-126));Panel(viewport,new Color(.10f,.14f,.19f),true);viewport.gameObject.AddComponent<RectMask2D>();
  var content=Rect(viewport,"Content");content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.anchoredPosition=Vector2.zero;content.sizeDelta=new Vector2(0,CreditCatalog.Entries.Length*84f+16);
  Scroll=viewport.gameObject.AddComponent<ScrollRect>();Scroll.viewport=viewport;Scroll.content=content;Scroll.horizontal=false;Scroll.vertical=true;Scroll.movementType=ScrollRect.MovementType.Clamped;Scroll.scrollSensitivity=48;
  WebsiteButtons=new Button[CreditCatalog.Entries.Length];
  for(int i=0;i<CreditCatalog.Entries.Length;i++){
   var entry=CreditCatalog.Entries[i];var row=Rect(content,"Credit_"+i);row.anchorMin=new Vector2(0,1);row.anchorMax=Vector2.one;row.pivot=new Vector2(.5f,1);row.anchoredPosition=new Vector2(0,-i*84-8);row.sizeDelta=new Vector2(0,78);Panel(row,i%2==0?new Color(.16f,.22f,.28f):new Color(.13f,.19f,.25f),false);
   var name=Label(row,"Name",entry.Name,31,Color.white,new Vector2(22,34),new Vector2(-250,-4));name.alignment=TextAlignmentOptions.MidlineLeft;
   var author=Label(row,"Author",entry.Author,23,new Color(.79f,.86f,.91f),new Vector2(22,4),new Vector2(-250,-44));author.alignment=TextAlignmentOptions.MidlineLeft;
   var button=Rect(row,"Website");button.anchorMin=new Vector2(1,.5f);button.anchorMax=button.anchorMin;button.pivot=new Vector2(1,.5f);button.anchoredPosition=new Vector2(-16,0);button.sizeDelta=new Vector2(210,58);WebsiteButtons[i]=Button(button,"Open website",()=>Application.OpenURL(entry.Url));
  }
  var note=Rect(panel,"BrowserNote");note.anchorMin=Vector2.zero;note.anchorMax=new Vector2(1,0);note.pivot=new Vector2(.5f,0);note.anchoredPosition=new Vector2(0,30);note.sizeDelta=new Vector2(0,48);Label(note,"Text","Links open in your browser.",23,new Color(.79f,.86f,.91f),new Vector2(36,0),new Vector2(-330,0)).alignment=TextAlignmentOptions.MidlineLeft;
  var back=Rect(panel,"Back");back.anchorMin=new Vector2(1,0);back.anchorMax=back.anchorMin;back.pivot=new Vector2(1,0);back.anchoredPosition=new Vector2(-28,24);back.sizeDelta=new Vector2(250,62);BackButton=Button(back,"Back to tablet",OnPressBack);
  m_ScreenGroup.SetActive(false);
 }
 protected override void OnOpenScreen(){base.OnOpenScreen();Canvas.ForceUpdateCanvases();Scroll.verticalNormalizedPosition=1;if(EventSystem.current)EventSystem.current.SetSelectedGameObject(WebsiteButtons[0].gameObject);}
 public void Reveal(Button button){if(!Scroll||!button)return;int index=Array.IndexOf(WebsiteButtons,button);if(index<0)return;Canvas.ForceUpdateCanvases();float y=CreditsScroll.Reveal(Scroll.content.anchoredPosition.y,Scroll.viewport.rect.height,index*84+8,index*84+86,Scroll.content.rect.height);Scroll.content.anchoredPosition=new Vector2(0,y);}
 static RectTransform Rect(Transform parent,string name){var go=new GameObject(name,typeof(RectTransform));go.layer=parent.gameObject.layer;var r=(RectTransform)go.transform;r.SetParent(parent,false);return r;}
 static void Stretch(RectTransform r,Vector2 lo,Vector2 hi){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=lo;r.offsetMax=hi;}
 static NativeMenuPanel Panel(RectTransform r,Color color,bool raycast){var g=r.gameObject.AddComponent<NativeMenuPanel>();g.color=color;g.raycastTarget=raycast;return g;}
 TextMeshProUGUI Label(Transform parent,string name,string text,float size,Color color,Vector2 lo,Vector2 hi){var r=Rect(parent,name);Stretch(r,lo,hi);var t=r.gameObject.AddComponent<TextMeshProUGUI>();if(font)t.font=font;t.text=text;t.color=color;t.fontSize=size;t.enableAutoSizing=true;t.fontSizeMin=size*.80f;t.fontSizeMax=size;t.enableWordWrapping=false;t.overflowMode=TextOverflowModes.Ellipsis;t.alignment=TextAlignmentOptions.MidlineLeft;t.raycastTarget=false;return t;}
 Button Button(RectTransform r,string text,UnityEngine.Events.UnityAction action){var graphic=Panel(r,new Color(.07f,.55f,.85f),true);var b=r.gameObject.AddComponent<Button>();b.targetGraphic=graphic;b.onClick.AddListener(action);var colors=b.colors;colors.highlightedColor=new Color(1.16f,1.16f,1.16f);colors.pressedColor=new Color(.75f,.75f,.75f);b.colors=colors;Label(r,"Label",text,27,Color.white,new Vector2(10,4),new Vector2(-10,-4)).alignment=TextAlignmentOptions.Center;var focus=r.gameObject.AddComponent<CreditsFocus>();focus.Screen=this;focus.Button=b;return b;}
}
public sealed class CreditsFocus:MonoBehaviour,ISelectHandler {
 public NativeCreditsScreen Screen;public Button Button;
 public void OnSelect(BaseEventData data){if(!(data is PointerEventData)&&Screen)Screen.Reveal(Button);}
}
