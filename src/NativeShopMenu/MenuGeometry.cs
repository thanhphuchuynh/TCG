using System;
public struct MenuPoint { public float X,Y; public MenuPoint(float x,float y){X=x;Y=y;} }
public struct MenuTileShape {public float Width,Height,BodySize,BodyY,Font; public MenuTileShape(float w,float h,float b,float y,float f){Width=w;Height=h;BodySize=b;BodyY=y;Font=f;}}
public struct MenuGeometry {
 public int Columns,Rows; public float Width,Height,Margin,Top,Bottom,PitchX,PitchY,TileWidth,TileHeight;
 public static MenuGeometry Create(float width,float height){
  if(float.IsNaN(width)||float.IsNaN(height)||float.IsInfinity(width)||float.IsInfinity(height)||width<600||height<400)throw new ArgumentOutOfRangeException("size");
  var g=new MenuGeometry{Width=width,Height=height,Columns=6,Rows=4,Margin=width*.035f,Top=height*.125f,Bottom=height*.15f};
  g.PitchX=(width-2*g.Margin)/g.Columns;g.PitchY=(height-g.Top-g.Bottom)/g.Rows;
  g.TileWidth=g.PitchX*.90f;g.TileHeight=g.PitchY*.89f;return g;
 }
 public float FooterScale {get{return Math.Min(.70f,(Bottom-32f)/150f);}}
 public MenuTileShape Shape(bool folder){return folder?new MenuTileShape(150,178,122,-69,21):new MenuTileShape(TileWidth,TileHeight,Math.Min(TileHeight*.60f,110f),-TileHeight*.34f,32);}
 public int Edge(float x){return x<Margin?-1:x>Width-Margin?1:0;}
 public int HitSlot(float x,float y){var a=Slot(0);int c=Math.Max(0,Math.Min(Columns-1,(int)Math.Round((x-a.X)/PitchX)));int r=Math.Max(0,Math.Min(Rows-1,(int)Math.Round((a.Y-y)/PitchY)));return r*Columns+c;}
 public MenuPoint Slot(int index){if(index<0||index>=Columns*Rows)throw new ArgumentOutOfRangeException("index");return new MenuPoint(Margin+PitchX*(index%Columns+.5f),-Top-PitchY*(index/Columns+.5f));}
}
