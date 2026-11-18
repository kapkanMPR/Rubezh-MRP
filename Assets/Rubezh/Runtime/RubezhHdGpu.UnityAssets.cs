using System;
using System.Collections.Generic;
using UnityEngine;
namespace Rubezh.UnityPort {
 [Serializable] public sealed class UnityTestAtlasEntry { public int id,page,x,y,w,h,ox,oy,ow,oh; }
 [Serializable] public sealed class UnityTestAtlasCatalog { public int version=1; public string sourceVersion; public UnityTestAtlasEntry[] entries; }
 public sealed partial class HdGpuRenderer {
  public readonly bool UnitySceneMode;
  // Editor preparation only. Kept null throughout gameplay.
  public static Action<int,byte[],int,int> AtlasExport;
  readonly HashSet<Texture2D> addressableTextures=new HashSet<Texture2D>();
  readonly Dictionary<Texture2D,int> nativeTexturePages=new Dictionary<Texture2D,int>();
  bool nativeAtlasMode;
  readonly HashSet<Texture2D> importedUnityTextures=new HashSet<Texture2D>();
  readonly Dictionary<int,UnityTestAtlasEntry> unityEntries=new Dictionary<int,UnityTestAtlasEntry>();
  readonly Dictionary<int,int> unityPages=new Dictionary<int,int>();
  public int ImportedUnityPages=>unityPages.Count+nativeTexturePages.Count;
  void LoadUnityAtlasCatalog(){
   if(NativeBattleAssets.Active!=null){
    nativeAtlasMode=true;
    var white=new Texture2D(4,4,TextureFormat.RGBA32,false,true);var pixels=new byte[64];for(int i=0;i<pixels.Length;i++)pixels[i]=255;
    white.LoadRawTextureData(pixels);white.Apply(false,true);atlases.Add(white);
    var mat=new Material(shader);mat.SetTexture("_MainTex",white);materials.Add(mat);return;
   }
   throw new InvalidOperationException("Нативные атласы не загружены. Выполните подготовку контента.");
  }
  bool TryUnitySprite(int id){
   UnityTestAtlasEntry e;
   if(nativeAtlasMode){
    Texture2D texture;
    if(!Rubezh.Graphics.HasSprite(id))return false;
    if(!NativeBattleAssets.Active.TrySprite(id,out texture,out e))throw new InvalidOperationException("Нет спрайта в загруженных атласах Unity: "+id);
    int slot;
    if(!nativeTexturePages.TryGetValue(texture,out slot)){
     slot=atlases.Count;atlases.Add(texture);nativeTexturePages.Add(texture,slot);addressableTextures.Add(texture);
     var material=new Material(shader);material.SetTexture("_MainTex",texture);material.SetVector("_CanvasSize",new Vector4(canvas.Width,canvas.Height,0,0));materials.Add(material);
    }
    regions[id]=new[]{new Region{Page=slot,X=e.x,Y=e.y,W=e.w,H=e.h,OX=e.ox,OY=e.oy,OW=e.ow,OH=e.oh}};return true;
   }
   return false;
  }
  public UnityTestAtlasCatalog ExportUnityCatalog(){
   var result=new List<UnityTestAtlasEntry>();
   for(int id=0;id<regions.Length;id++)if(regions[id]!=null&&regions[id].Length>0){var e=regions[id][0];result.Add(new UnityTestAtlasEntry{id=id,page=e.Page,x=e.X,y=e.Y,w=e.W,h=e.H,ox=e.OX,oy=e.OY,ow=e.OW,oh=e.OH});}
   return new UnityTestAtlasCatalog{sourceVersion=RubezhVersion.Current,entries=result.ToArray()};
  }
 }
}
