using UnityEngine;using UnityEngine.UI;
// Native UI geometry: a rounded rectangle, with no raster artwork or texture dependency.
public sealed class NativeMenuPanel:MaskableGraphic {
 protected override void OnPopulateMesh(VertexHelper vh){
  vh.Clear();Rect r=rectTransform.rect;float radius=Mathf.Min(16,Mathf.Min(r.width,r.height)*.5f);if(radius<=0)return;
  vh.AddVert(new Vector3(r.center.x,r.center.y,0),color,Vector2.zero);
  for(int corner=0;corner<4;corner++){float cx=corner==0||corner==3?r.xMax-radius:r.xMin+radius;float cy=corner<2?r.yMax-radius:r.yMin+radius;
   for(int step=0;step<=6;step++){float a=(corner*90+step*15)*Mathf.Deg2Rad;vh.AddVert(new Vector3(cx+Mathf.Cos(a)*radius,cy+Mathf.Sin(a)*radius,0),color,Vector2.zero);}
  }
  for(int i=1;i<=28;i++)vh.AddTriangle(0,i==28?1:i+1,i);
 }
}
