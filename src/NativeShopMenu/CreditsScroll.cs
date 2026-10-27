using System;
public static class CreditsScroll {
 public static float Reveal(float current,float viewport,float top,float bottom,float total){float next=current;if(top<current)next=top;else if(bottom>current+viewport)next=bottom-viewport;return Math.Max(0,Math.Min(Math.Max(0,total-viewport),next));}
}
