using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SilentOrders.Core;
namespace Rubezh.UnityPort {
 // Metadata and imported GPU pages only. Original RGBA files are Editor inputs.
 public static class ExteriorSpriteLoader {
  public const string Root="NativeExterior";
  [Serializable] public sealed class Entry {public string key,sourceSha256;public int page,x,y,w,h;public float left,top,width,height;}
  [Serializable] public sealed class Catalog {public int version=1,size=2048;public Entry[] entries;}
  static readonly Dictionary<string,ExteriorSprite> cache=new Dictionary<string,ExteriorSprite>();
  static readonly Dictionary<int,Texture2D> pages=new Dictionary<int,Texture2D>();
  static Catalog catalog;
  public static void Initialize(){ExteriorSprites.Load=Load;}
  public static int CachedCount=>cache.Count;
  static void ReadCatalog(){
   if(catalog!=null)return;
   var asset=Resources.Load<TextAsset>(Root+"/catalog");
   if(asset==null)throw new InvalidOperationException("Нет NativeExterior: подготовьте нативный контент.");
   try {
    var next=JsonUtility.FromJson<Catalog>(asset.text);
    if(next==null||next.version!=1||next.size!=2048||next.entries==null)throw new InvalidOperationException("Повреждён NativeExterior/catalog");
    foreach(var e in next.entries){
     if(e.page<0||e.w<1||e.h<1||e.x<0||e.y<0||e.x+e.w>next.size||e.y+e.h>next.size)throw new InvalidOperationException("Exterior bounds: "+e.key);
     cache.Add(e.key,new ExteriorSprite{PixelWidth=e.w,PixelHeight=e.h,Left=e.left,Top=e.top,Width=e.width,Height=e.height,NativePage=e.page,U0=(float)e.x/next.size,V0=(float)e.y/next.size,U1=(float)(e.x+e.w)/next.size,V1=(float)(e.y+e.h)/next.size});
    }
    catalog=next;
   }catch{cache.Clear();throw;}finally{Resources.UnloadAsset(asset);}
  }
  public static IEnumerator Preload(Func<bool> cancelled){
   ReadCatalog();var needed=new HashSet<int>();foreach(var e in catalog.entries)needed.Add(e.page);
   foreach(int page in needed){
    if(cancelled())yield break;if(pages.ContainsKey(page))continue;
    var request=Resources.LoadAsync<Texture2D>(Root+"/page-"+page.ToString("D2"));while(!request.isDone)yield return null;
    var texture=request.asset as Texture2D;
    if(cancelled()){if(texture!=null)Resources.UnloadAsset(texture);yield break;}
    if(texture==null)throw new InvalidOperationException("Нет Exterior page "+page);
    pages.Add(page,texture);
   }
  }
  public static Texture2D Page(int id){
   Texture2D page;if(!pages.TryGetValue(id,out page))throw new InvalidOperationException("Exterior page not preloaded: "+id);
   return page;
  }
  public static void Release(){foreach(var page in pages.Values)if(page!=null)Resources.UnloadAsset(page);pages.Clear();cache.Clear();catalog=null;ExteriorSprites.Load=null;}
  static ExteriorSprite Load(string key){ReadCatalog();ExteriorSprite result;return cache.TryGetValue(key,out result)?result:null;}
 }
}
