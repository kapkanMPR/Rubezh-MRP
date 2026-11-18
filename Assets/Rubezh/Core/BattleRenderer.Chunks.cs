using System;using System.Collections.Generic;using SilentOrders.Core;
namespace SilentOrders.Rendering {
 public abstract partial class BattleVisualRecipes {
  const int ChunkSide=15;
  sealed class SceneChunk {public Area Bounds;public readonly List<Item> Items=new List<Item>();}
  SceneChunk[] sceneChunks;
  readonly Dictionary<ProceduralPicture,Area> pictureBounds=new Dictionary<ProceduralPicture,Area>();
  readonly List<Item> visibleItems=new List<Item>();
  readonly HashSet<Item> visibleSet=new HashSet<Item>();
  public int ChunkCount=>sceneChunks==null?0:sceneChunks.Length;
  public int LastDirtyChunks{get;private set;} public int VisibleChunks{get;private set;} public long ChunkUpdates{get;private set;}
  Area PictureArea(ProceduralPicture p){var o=P(new V2(0,0));return ClampArea(new Area{X=(int)Math.Floor((o.X+p.Left)*Canvas.Scale)-2,Y=(int)Math.Floor((o.Y+p.Top)*Canvas.Scale)-2,R=(int)Math.Ceiling((o.X+p.Left+p.Width)*Canvas.Scale)+2,B=(int)Math.Ceiling((o.Y+p.Top+p.Height)*Canvas.Scale)+2});}
  Area ClampArea(Area a){a.X=Math.Max(0,a.X);a.Y=Math.Max(0,a.Y);a.R=Math.Min(Canvas.Width,a.R);a.B=Math.Min(Canvas.Height,a.B);return a;}
  void RememberChunkState(BattleMap map){
   int cols=(map.Columns+ChunkSide-1)/ChunkSide,rows=(map.Rows+ChunkSide-1)/ChunkSide;sceneChunks=new SceneChunk[cols*rows];
   for(int y=0;y<rows;y++)for(int x=0;x<cols;x++){var a=P(new V2(x*ChunkSide,y*ChunkSide));var b=P(new V2(Math.Min(map.Columns,(x+1)*ChunkSide),Math.Min(map.Rows,(y+1)*ChunkSide)));var c=P(new V2(Math.Min(map.Columns,(x+1)*ChunkSide),y*ChunkSide));var d=P(new V2(x*ChunkSide,Math.Min(map.Rows,(y+1)*ChunkSide)));
    sceneChunks[y*cols+x]=new SceneChunk{Bounds=ClampArea(new Area{X=(int)(a.X*Canvas.Scale)-2,R=(int)(b.X*Canvas.Scale)+2,Y=(int)(c.Y*Canvas.Scale)-2,B=(int)(d.Y*Canvas.Scale)+2})};}
   foreach(var item in sceneryItems){var o=item.Decoration??StudioRules.At(map,item.TileId);V2 p=o==null?map.Center(item.TileId):new V2(o.X,o.Y);int x=Math.Max(0,Math.Min(cols-1,(int)Math.Floor(p.X/ChunkSide))),y=Math.Max(0,Math.Min(rows-1,(int)Math.Floor(p.Y/ChunkSide)));var ch=sceneChunks[y*cols+x];ch.Items.Add(item);ch.Bounds.Include(Bounds(map,item));}
   pictureBounds.Clear();if(map.ProceduralPictures!=null)foreach(var p in map.ProceduralPictures)pictureBounds[p]=PictureArea(p);
  }
  protected IEnumerable<Item> VisibleSceneryItems(){var view=Canvas.Gpu as ICanvasViewportGpu;if(view==null||sceneChunks==null)return sceneryItems;
   visibleSet.Clear();VisibleChunks=0;foreach(var c in sceneChunks){var a=c.Bounds;if(!view.Visible(a.X,a.Y,a.R,a.B))continue;VisibleChunks++;foreach(var i in c.Items){var b=sceneryBounds[i.TileId];if(view.Visible(b.X,b.Y,b.R,b.B))visibleSet.Add(i);}}
   visibleItems.Clear();foreach(var i in sceneryItems)if(visibleSet.Contains(i))visibleItems.Add(i);return visibleItems;
  }
#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
  bool PatchChunks(BattleMap map){
   if(cachedMap!=map||cachedSharpScenery!=SharpScenery||basePixels==null||sceneryDepth==null||sceneChunks==null)return false;
   HashSet<int> roots;if(!map.SceneryChanges.Since(map,cachedRevision,out roots))return false;
   var changes=new List<Area>();foreach(int id in roots){Area a;if(sceneryBounds.TryGetValue(id,out a))changes.Add(a);}
   var oldPictures=new HashSet<ProceduralPicture>(pictureBounds.Keys);if(map.ProceduralPictures!=null)foreach(var p in map.ProceduralPictures){if(!oldPictures.Remove(p))changes.Add(PictureArea(p));}foreach(var p in oldPictures)changes.Add(pictureBounds[p]);
   // New debris and removed props change the ordered list; unchanged frames never visit this path.
   BuildSceneryItems(map);foreach(int id in roots)foreach(var item in sceneryItems)if(item.TileId==id)changes.Add(Bounds(map,item));
   var dirty=new List<Area>();LastDirtyChunks=0;
   foreach(var chunk in sceneChunks){bool hit=false;foreach(var a in changes)if(chunk.Bounds.Intersects(a)){hit=true;break;}if(hit)LastDirtyChunks++;}
   // Wake affected chunks, but raster only the changed footprint inside them.
   // Includes new extents outside the old chunk, such as falling wall caps.
   foreach(var a in changes)dirty.Add(a);
   for(int i=0;i<dirty.Count;i++)for(int j=i+1;j<dirty.Count;j++)if(dirty[i].Intersects(dirty[j])){var a=dirty[i];a.Include(dirty[j]);dirty[i]=a;dirty.RemoveAt(j);i=-1;break;}
   activeMap=map;LastSceneryPixels=0;
   try{foreach(var area in dirty){var a=ClampArea(area);if(a.R<=a.X||a.B<=a.Y)continue;PatchRegion(map,a);LastSceneryPixels+=(a.R-a.X)*(a.B-a.Y);}}
   finally{Canvas.ClipLeft=Canvas.ClipTop=0;Canvas.ClipRight=Canvas.ClipBottom=int.MaxValue;Canvas.RecordDepth=false;Canvas.TestDepth=false;}
   PartialSceneryBuilds++;ChunkUpdates+=LastDirtyChunks;cachedRevision=map.Revision;int pixels=LastSceneryPixels;RememberBounds(map);RememberChunkState(map);LastSceneryPixels=pixels;return true;
  }
#endif

#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
  void PatchRegion(BattleMap map,Area a){
   Canvas.ClipLeft=a.X;Canvas.ClipTop=a.Y;Canvas.ClipRight=a.R;Canvas.ClipBottom=a.B;Canvas.TestDepth=false;Canvas.RecordDepth=false;
   CopyRegion(basePixels,Canvas.Pixels,a);for(int y=a.Y;y<a.B;y++){Array.Clear(Canvas.Occlusion,y*Canvas.Width+a.X,a.R-a.X);if(Canvas.RoofOcclusion!=null)Array.Clear(Canvas.RoofOcclusion,y*Canvas.Width+a.X,a.R-a.X);}
   for(int y=0;y<map.Rows;y++)for(int x=0;x<map.Columns;x++){var p=P(new V2(x,y));var b=new Area{X=(int)((p.X-24)*Canvas.Scale),R=(int)((p.X+46)*Canvas.Scale),Y=(int)((p.Y-24)*Canvas.Scale),B=(int)((p.Y+24)*Canvas.Scale)};if(b.Intersects(a))Ground(map,x,y);}
   if(map.TerrainPicture!=null)DrawPictureSet(new[]{map.TerrainPicture});
   if(sharpGround!=null)CopyRegion(Canvas.Pixels,sharpGround,a);
   Canvas.RecordDepth=true;foreach(var item in sceneryItems){if(!Bounds(map,item).Intersects(a))continue;var o=item.Decoration??StudioRules.At(map,item.TileId);if(o!=null?o.Destroyed:map.Tiles[item.TileId]!=Tile.LowCover&&map.Tiles[item.TileId]!=Tile.Wall)continue;DrawSceneryItem(map,item);}
   DrawProceduralPictures(map);Canvas.RecordDepth=false;CopyRegion(Canvas.Pixels,scenery,a);CopyRegion(Canvas.Occlusion,sceneryDepth,a);
   
  }
#endif

 }
}
