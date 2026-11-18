#if UNITY_EDITOR
using System;using System.Collections.Generic;using UnityEngine;using UnityEditor;using SilentOrders.Core;
namespace Rubezh.UnityPort.Editor {
 public sealed partial class RubezhLevelEditorWindow {
  string studioSelected="S28-01",studioSearch="";bool studioEquipment,studioArchived,studioMirrored;int studioGroup,studioMaterial;
  readonly Dictionary<int,Texture2D> studioTextures=new Dictionary<int,Texture2D>();
  StudioMap previewStudio;
  Texture2D StudioTexture(int id){Texture2D t;if(studioTextures.TryGetValue(id,out t))return t;
   EditorSpritePacks.Ensure(id);
   int w,h;byte[] pixels=Rubezh.Graphics.SpriteData(id,0,out w,out h);byte[] flipped=new byte[pixels.Length];for(int y=0;y<h;y++)Array.Copy(pixels,y*w*4,flipped,(h-y-1)*w*4,w*4);
   t=new Texture2D(w,h,TextureFormat.RGBA32,false);t.LoadRawTextureData(flipped);t.Apply();t.filterMode=FilterMode.Point;studioTextures[id]=t;return t;
  }
  void ClearStudioTextures(){foreach(var t in studioTextures.Values)DestroyImmediate(t);studioTextures.Clear();}
  void DrawStudioPalette(){
   studioEquipment=GUILayout.Toolbar(studioEquipment?1:0,new[]{"Объекты","Оружие / медицина"})==1;
   studioSearch=EditorGUILayout.TextField("Поиск",studioSearch);studioArchived=EditorGUILayout.Toggle("Архивные варианты",studioArchived);
   var groups=new List<string>{"Все группы"};foreach(var a in StudioCatalog.Assets.Values)if(a.Archived==studioArchived&&(!string.IsNullOrEmpty(a.EquipmentKind))==studioEquipment&&!groups.Contains(a.Group))groups.Add(a.Group);
   studioGroup=Math.Min(studioGroup,groups.Count-1);int next=EditorGUILayout.Popup("Группа",studioGroup,groups.ToArray());if(next!=studioGroup){studioGroup=next;studioMaterial=0;}
   var materials=new List<string>{"Все материалы"};foreach(var a in StudioCatalog.Assets.Values)if(a.Archived==studioArchived&&(!string.IsNullOrEmpty(a.EquipmentKind))==studioEquipment&&(studioGroup==0||a.Group==groups[studioGroup])&&!materials.Contains(a.DisplayMaterial))materials.Add(a.DisplayMaterial);
   studioMaterial=Math.Min(studioMaterial,materials.Count-1);studioMaterial=EditorGUILayout.Popup("Материал",studioMaterial,materials.ToArray());
   var chosen=StudioCatalog.Find(studioSelected,studioMirrored);if(chosen!=null){EditorGUILayout.HelpBox(chosen.Name+" · "+chosen.Width+"×"+chosen.Height+"\n"+(!string.IsNullOrEmpty(chosen.EquipmentKind)?"Подбираемое снаряжение · один полный магазин / единица · без запаса":chosen.Decor?"Декор · "+chosen.DecorHits+" попадания · без защиты · авторазмещение на опоре":chosen.Ground?"Покрытие":chosen.NoCover?"Препятствие без защиты · 4 попадания / любой взрыв":!chosen.Blocking?"Проход, без защиты":chosen.MediumMask!=0?"Пролом: 65% в одной клетке; целая часть: 75%":chosen.Medium?"Среднее укрытие · 65%":chosen.High?"Высокое укрытие · 75%":chosen.Partial?"Частичное · 50%, прострел 20%":"Низкое укрытие · 50%")+(chosen.Indestructible?" · неразрушимое":""),MessageType.None);
    studioMirrored=EditorGUILayout.Toggle("Отразить текущую текстуру",studioMirrored);
    if(chosen.Partner!=null&&GUILayout.Button("Другая проекция"))studioSelected=chosen.Partner;
   }
   foreach(var a in StudioCatalog.Assets.Values){if((!string.IsNullOrEmpty(a.EquipmentKind))!=studioEquipment||a.Archived!=studioArchived||studioGroup!=0&&a.Group!=groups[studioGroup]||studioMaterial!=0&&a.DisplayMaterial!=materials[studioMaterial]||studioSearch.Length>0&&(a.Name+" "+a.Id).IndexOf(studioSearch,StringComparison.OrdinalIgnoreCase)<0)continue;
    var sprite=a.Sprites[0];if(GUILayout.Button(new GUIContent(a.Name+"  "+a.Width+"×"+a.Height,StudioTexture(sprite.Id)),GUILayout.Height(46))){studioSelected=a.Id;brush=BrushMode.Object;}
   }
  }
  void PrepareStudioPreview(){previewStudio=new StudioMap(level.columns*level.rows);foreach(var g in level.ground)if(g!=null&&level.Inside(g.x,g.y))previewStudio.Ground[g.y*level.columns+g.x]=StudioCatalog.Find(g.assetId,g.mirrored);
   foreach(var o in level.objects){if(o==null)continue;var a=StudioCatalog.Find(o.assetId,o.mirrored);if(a==null)continue;var p=new StudioPlaced{Uid=o.uid,ParentUid=o.parentUid,OffsetX=o.offsetX,OffsetY=o.offsetY,HitsLeft=a.DecorHits,Asset=a,X=o.x,Y=o.y,Angle=o.rotationDegrees,ScaleX=LevelObjectCell.SafeScale(o.scaleX),ScaleY=LevelObjectCell.SafeScale(o.scaleY),Root=o.y*level.columns+o.x};if(a.Decor){previewStudio.Decorations.Add(p);continue;}previewStudio.Objects.Add(p);for(int y=o.y;y<o.y+a.Height;y++)for(int x=o.x;x<o.x+a.Width;x++)if(level.Inside(x,y))previewStudio.Cells[y*level.columns+x]=p;}DecorRules.Bind(previewStudio,level.columns);previewStudio.Connect();
  }
  StudioPlaced PreviewObject(LevelObjectCell o){var a=StudioCatalog.Find(o.assetId,o.mirrored);return a!=null&&a.Decor?previewStudio.Decorations.Find(d=>d.Uid==o.uid):previewStudio.Cells[o.y*level.columns+o.x];}
  float PreviewDepth(LevelObjectCell o){var p=PreviewObject(o);return p==null?100+o.y-o.x:p.Asset.Decor?DecorRules.Depth(p):100+p.Y+p.Asset.Height-p.X;}
  void DrawStudioSprite(StudioSprite s,Vector2 origin,float scale){var saved=GUI.matrix;if(s.W<0){GUIUtility.ScaleAroundPivot(new Vector2(-1,1),new Vector2(origin.x+s.X*scale,origin.y+s.Y*scale));s.W=-s.W;}GUI.DrawTexture(new Rect(origin.x+(s.X-s.W/2)*scale,origin.y+(s.Y-s.H/2)*scale,s.W*scale,s.H*scale),StudioTexture(s.Id),ScaleMode.StretchToFill,true);GUI.matrix=saved;}
  void DrawStudioObject(StudioPlaced p,float hx,float hy,Vector2 center){var origin=Project(p.X-.5f,p.Y-.5f,level,hx,hy,center);var sprite=StudioCatalog.Sprite(p);if(p.Asset.Decor){float factor=DecorRules.Scale(p);sprite.X=11+(sprite.X-11)*factor+11*(p.OffsetX+p.OffsetY);sprite.Y=sprite.Y*factor-DecorRules.Lift(p)+5*(p.OffsetY-p.OffsetX);sprite.W*=factor;sprite.H*=factor;}if(p.Asset.Legacy){var matrix=GUI.matrix;GUIUtility.RotateAroundPivot(p.Asset.Mirrored?-p.Angle:p.Angle,new Vector2(origin.x+hx,origin.y));sprite.X=11+(sprite.X-11)*p.ScaleX;sprite.Y*=p.ScaleY;sprite.W*=p.ScaleX;sprite.H*=p.ScaleY;DrawStudioSprite(sprite,origin,hx/11f);GUI.matrix=matrix;}else DrawStudioSprite(sprite,origin,hx/11f);foreach(var cap in StudioCatalog.CapSprites(p))DrawStudioSprite(cap,origin,hx/11f);}
 }
}
#endif

