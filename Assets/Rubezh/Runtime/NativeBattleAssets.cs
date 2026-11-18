using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_CONTENT_CONTRACT
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.U2D;
#endif
namespace Rubezh.UnityPort
{
    // A lease belongs to a single battle. Addressables owns imported textures; never Destroy/UnloadAsset them.
    public sealed class NativeBattleAssets : IDisposable
    {
        public static NativeBattleAssets Active { get; private set; }
        readonly Dictionary<int,UnityTestAtlasEntry> resolved=new Dictionary<int,UnityTestAtlasEntry>();
        readonly Dictionary<int,UnityTestAtlasEntry> entries=new Dictionary<int,UnityTestAtlasEntry>();
        public int LoadedAtlases { get; private set; }
        bool disposed;
        public GameObject VisualPrefab {get; private set;}
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_CONTENT_CONTRACT
        readonly List<AsyncOperationHandle> handles=new List<AsyncOperationHandle>();
        readonly Dictionary<int,SpriteAtlas> atlases=new Dictionary<int,SpriteAtlas>();
        readonly Dictionary<int,Sprite> sprites=new Dictionary<int,Sprite>();
#endif
        public IEnumerator Load(HashSet<int> required, Func<bool> cancelled)
        {
            if(Active!=null)throw new InvalidOperationException("Previous native battle resources were not released");
            Active=this;
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_CONTENT_CONTRACT
            var catalog=Addressables.LoadAssetAsync<TextAsset>("rubezh/native/catalog");handles.Add(catalog);
            while(!catalog.IsDone){if(cancelled())yield break;yield return null;}
            if(catalog.Status!=AsyncOperationStatus.Succeeded)throw new InvalidOperationException("Не подготовлен контент Unity: Рубеж → Тест Unity → Подготовить нативный контент",catalog.OperationException);
            var data=JsonUtility.FromJson<UnityTestAtlasCatalog>(catalog.Result.text);
            if(data==null||data.version!=2||data.entries==null)throw new InvalidOperationException("Invalid native sprite catalog");
            foreach(var e in data.entries)entries[e.id]=e;
            // The catalog TextAsset is no longer needed once its compact records are parsed.
            Addressables.Release(catalog);handles.Remove(catalog);
            var prefab=Addressables.LoadAssetAsync<GameObject>("rubezh/native/visual");handles.Add(prefab);
            while(!prefab.IsDone){if(cancelled())yield break;yield return null;}
            if(prefab.Status!=AsyncOperationStatus.Succeeded)throw new InvalidOperationException("Не загружен префаб боевого объекта",prefab.OperationException);
            VisualPrefab=prefab.Result;
            var pages=new SortedSet<int>();
            foreach(int id in required){UnityTestAtlasEntry e;if(entries.TryGetValue(Rubezh.SpritePackIndex.Resolve(id),out e))pages.Add(e.page);}
            foreach(int page in pages)
            {
                if(cancelled())yield break;
                var request=Addressables.LoadAssetAsync<SpriteAtlas>("rubezh/native/atlas/"+page);handles.Add(request);
                while(!request.IsDone){if(cancelled())yield break;yield return null;}
                if(request.Status!=AsyncOperationStatus.Succeeded)throw new InvalidOperationException("Не загружен атлас Unity "+page,request.OperationException);
                atlases.Add(page,request.Result);LoadedAtlases++;
            }
            foreach(int raw in required){UnityTestAtlasEntry e;if(entries.TryGetValue(Rubezh.SpritePackIndex.Resolve(raw),out e))Rubezh.Graphics.RegisterNativeSprite(raw,e.ow,e.oh);}
#else
            yield return null;
#endif
        }
        public bool TrySprite(int id,out Texture2D texture,out UnityTestAtlasEntry region)
        {
            texture=null;region=null;
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_CONTENT_CONTRACT
            UnityTestAtlasEntry e;SpriteAtlas atlas;
            id=Rubezh.SpritePackIndex.Resolve(id);
            if(disposed||!entries.TryGetValue(id,out e)||!atlases.TryGetValue(e.page,out atlas))return false;
            Sprite sprite;if(!sprites.TryGetValue(id,out sprite)){
                sprite=atlas.GetSprite("s"+id.ToString("D5"));
                if(sprite==null)throw new InvalidOperationException("Sprite missing from Unity atlas: "+id);
                sprites.Add(id,sprite);
            }
            texture=sprite.texture;if(resolved.TryGetValue(id,out region))return true;
            var rect=sprite.textureRect;
            region=new UnityTestAtlasEntry{id=id,page=e.page,x=(int)rect.x,y=(int)rect.y,w=(int)rect.width,h=(int)rect.height,ox=e.ox,oy=e.oy,ow=e.ow,oh=e.oh};
            if(region.w!=e.w||region.h!=e.h)throw new InvalidOperationException("Atlas resized sprite "+id+"; disable atlas downscaling");
            resolved[id]=region;return true;
#else
            return false;
#endif
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            NativeBuildingAssets.Release();
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_CONTENT_CONTRACT
            foreach(var sprite in sprites.Values)UnityEngine.Object.Destroy(sprite);sprites.Clear();
            foreach(var handle in handles)if(handle.IsValid())Addressables.Release(handle);handles.Clear();atlases.Clear();
#endif
            entries.Clear();resolved.Clear();VisualPrefab=null;LoadedAtlases=0;if(ReferenceEquals(Active,this))Active=null;
        }
    }
}
