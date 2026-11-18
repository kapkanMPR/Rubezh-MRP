using System;using System.Collections.Generic;
namespace Rubezh {
 public static partial class Graphics {
  static readonly Dictionary<int,WeakReference<Tex[]>> wallMaterialApplied=new Dictionary<int,WeakReference<Tex[]>>();
  public static bool HasWallMaterial(int raw){int id=SpritePackIndex.Resolve(raw);WeakReference<Tex[]> weak;Tex[] source;return wallMaterialApplied.TryGetValue(id,out weak)&&weak.TryGetTarget(out source)&&atlas!=null&&id>=0&&id<atlas.Length&&ReferenceEquals(source,atlas[id]);}
  // Preserve the authored silhouette and shading, replace the old surface colour/texture.
  public static void ApplyWallMaterial(IEnumerable<int> ids,uint[] material,int mw,int mh){
   foreach(int raw in ids){int id=SpritePackIndex.Resolve(raw);if(!HasSprite(id))continue;var source=atlas[id];WeakReference<Tex[]> weak;Tex[] done;if(wallMaterialApplied.TryGetValue(id,out weak)&&weak.TryGetTarget(out done)&&ReferenceEquals(source,done))continue;
    foreach(var t in source){var p=t.P;for(int y=0;y<t.H;y++)for(int x=0;x<t.W;x++){int i=(y*t.W+x)*4,a=p[i+3];if(a==0)continue;
     int u=Math.Min(mw-1,(int)((.04f+.92f*(x+.5f)/t.W)*mw)),v=Math.Min(mh-1,(int)((.06f+.88f*(y+.5f)/t.H)*mh));uint c=material[v*mw+u];
     int light=(p[i]*77+p[i+1]*150+p[i+2]*29)*255/Math.Max(1,a)/256;float shade=Math.Max(.55f,Math.Min(1.15f,.55f+light/230f));
     p[i]=(byte)Math.Min(a,(int)((c&255)*shade*a/255));p[i+1]=(byte)Math.Min(a,(int)(((c>>8)&255)*shade*a/255));p[i+2]=(byte)Math.Min(a,(int)(((c>>16)&255)*shade*a/255));
    }}wallMaterialApplied[id]=new WeakReference<Tex[]>(source);
   }ClearStampCache();
  }
 }
}
