using System;using System.Collections.Generic;using SilentOrders.Core;using UnityEngine;
namespace Rubezh.UnityPort {
 public static class StandaloneWallMaterials {
  public const string MaterialId="c68531d1147509105a3d4aa9";
  public static bool UsesMaterial(string key)=>key=="S30-01"||key=="S30-02"||key=="wide-S12-02-x";
  public static HashSet<int> AllSourceSprites(){var ids=new HashSet<int>();foreach(var a in StudioCatalog.Assets.Values){if(!UsesMaterial(a.Id))continue;foreach(var s in a.Sprites)ids.Add(Rubezh.SpritePackIndex.Resolve(s.Id));if(a.Caps!=null)foreach(var c in a.Caps)ids.Add(Rubezh.SpritePackIndex.Resolve(c.Sprite.Id));}return ids;}
  // Preparation-only pixel recipe: exact legacy recolour, including premultiplied alpha.
  // Coordinates refer to the complete uncropped source, never the imported trimmed rectangle.
  public static void BakePixels(byte[] pixels,int width,int height,uint[] material,int mw,int mh){
   if(pixels==null||pixels.Length!=checked(width*height*4)||material==null||mw<=0||mh<=0||material.Length!=checked(mw*mh))throw new ArgumentException("Invalid wall bake dimensions");
   for(int y=0;y<height;y++)for(int x=0;x<width;x++){
    int i=(y*width+x)*4,a=pixels[i+3];if(a==0)continue;
    int u=Math.Min(mw-1,(int)((.04f+.92f*(x+.5f)/width)*mw)),v=Math.Min(mh-1,(int)((.06f+.88f*(y+.5f)/height)*mh));uint c=material[v*mw+u];
    int light=(pixels[i]*77+pixels[i+1]*150+pixels[i+2]*29)*255/Math.Max(1,a)/256;float shade=Math.Max(.55f,Math.Min(1.15f,.55f+light/230f));
    pixels[i]=(byte)Math.Min(a,(int)((c&255)*shade*a/255));pixels[i+1]=(byte)Math.Min(a,(int)(((c>>8)&255)*shade*a/255));pixels[i+2]=(byte)Math.Min(a,(int)(((c>>16)&255)*shade*a/255));
   }
  }
  public static HashSet<int> SourceSprites(BattleMap map){var ids=new HashSet<int>();if(map.Studio==null)return ids;foreach(var o in map.Studio.Objects){string key=o.Asset.Id;if(!UsesMaterial(key))continue;foreach(var s in o.Asset.Sprites)ids.Add(s.Id);if(o.Asset.Caps!=null)foreach(var c in o.Asset.Caps)ids.Add(c.Sprite.Id);}return ids;}
  public static void Apply(BattleMap map){if(NativeBattleAssets.Active!=null)return;var ids=SourceSprites(map);if(ids.Count==0)return;
   var material=BuildingMaterialPixels.Read(MaterialId);
   var src=material.pixels;var rgba=new uint[src.Length];for(int y=0;y<material.height;y++)for(int x=0;x<material.width;x++){var c=src[(material.height-1-y)*material.width+x];rgba[y*material.width+x]=(uint)(c.r|(c.g<<8)|(c.b<<16)|(c.a<<24));}Rubezh.Graphics.ApplyWallMaterial(ids,rgba,material.width,material.height);
  }
 }
}
