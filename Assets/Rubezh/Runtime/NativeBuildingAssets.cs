using System;
using System.Collections;
using System.Collections.Generic;
using SilentOrders.Core;
using UnityEngine;
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_CONTENT_CONTRACT
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#endif
namespace Rubezh.UnityPort
{
    [Serializable]
    public sealed class NativeBuildingRegion
    {
        public string key;
        public int page,
            x,
            y,
            w,
            h,
            px,
            py;
    }

    [Serializable]
    public sealed class NativeBuildingCatalog
    {
        public int version;
        public int pages;
        public NativeBuildingRegion[] regions;
    }

    public static class NativeBuildingAssets
    {
        public sealed class Page
        {
            public Texture2D Color,
                Depth;
            public readonly List<NativeBuildingRegion> Regions = new List<NativeBuildingRegion>();
        }

        public sealed class Region
        {
            public NativeBuildingRegion Data;
            public Page Page;
        }

        public static int LoadedPageCount { get; private set; }
        static readonly Dictionary<string, List<Region>> entries = new Dictionary<string, List<Region>>();
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_CONTENT_CONTRACT
        static readonly List<AsyncOperationHandle> handles = new List<AsyncOperationHandle>();
#endif

        public static List<Region> Get(string key)
        {
            List<Region> value;
            if (!entries.TryGetValue(key, out value))
                throw new InvalidOperationException("Building texture was not preloaded: " + key);
            return value;
        }

        public static IEnumerator Load(BattleMap map, Func<bool> cancelled)
        {
            Release();
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_CONTENT_CONTRACT
            var catalogs = new SortedSet<string>(StringComparer.Ordinal);
            if (map.ProceduralPictures != null)
                foreach (var p in map.ProceduralPictures)
                    if (!string.IsNullOrEmpty(p.NativeKey))
                        catalogs.Add(p.NativeKey.Split(':')[0]);
            foreach (var key in catalogs)
            {
                var request = Addressables.LoadAssetAsync<TextAsset>("rubezh/native/building/" + key + "/catalog");
                handles.Add(request);
                while (!request.IsDone)
                {
                    if (cancelled())
                        yield break;
                    yield return null;
                }
                if (request.Status != AsyncOperationStatus.Succeeded)
                    throw new InvalidOperationException("Building catalog missing: " + key, request.OperationException);
                var catalog = JsonUtility.FromJson<NativeBuildingCatalog>(request.Result.text);
                if (
                    catalog == null
                    || catalog.version != 1
                    || catalog.regions == null
                    || catalog.pages < 0
                    || catalog.pages > 65536
                    || catalog.regions.Length > 1000000
                )
                    throw new InvalidOperationException("Invalid building catalog: " + key);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var region in catalog.regions)
                {
                    if (
                        region == null
                        || string.IsNullOrEmpty(region.key)
                        || !region.key.StartsWith(key + ":", StringComparison.Ordinal)
                        || region.page < 0
                        || region.page >= catalog.pages
                        || region.x < 1
                        || region.y < 1
                        || region.w < 1
                        || region.h < 1
                        || region.px < 0
                        || region.py < 0
                        || !seen.Add(region.key + ":" + region.px + ":" + region.py)
                    )
                        throw new InvalidOperationException("Invalid or duplicate building region: " + key);
                }
                var pages = new Page[catalog.pages];
                for (int i = 0; i < pages.Length; i++)
                {
                    if (cancelled())
                        yield break;
                    var color = Addressables.LoadAssetAsync<Texture2D>(
                        "rubezh/native/building/" + key + "/" + i + "/color"
                    );
                    var depth = Addressables.LoadAssetAsync<Texture2D>(
                        "rubezh/native/building/" + key + "/" + i + "/depth"
                    );
                    handles.Add(color);
                    handles.Add(depth);
                    while (!color.IsDone || !depth.IsDone)
                    {
                        if (cancelled())
                            yield break;
                        yield return null;
                    }
                    if (
                        color.Status != AsyncOperationStatus.Succeeded
                        || depth.Status != AsyncOperationStatus.Succeeded
                    )
                        throw new InvalidOperationException(
                            "Building textures missing: " + key,
                            color.OperationException ?? depth.OperationException
                        );
                    if (
                        color.Result == null
                        || depth.Result == null
                        || color.Result.width != depth.Result.width
                        || color.Result.height != depth.Result.height
                    )
                        throw new InvalidOperationException("Building colour/depth dimensions mismatch: " + key);
                    pages[i] = new Page { Color = color.Result, Depth = depth.Result };
                    LoadedPageCount++;
                }
                foreach (var r in catalog.regions)
                {
                    var page = pages[r.page];
                    if ((long)r.x + r.w >= page.Color.width || (long)r.y + r.h >= page.Color.height)
                        throw new InvalidOperationException("Building region exceeds page: " + r.key);
                    page.Regions.Add(r);
                    List<Region> list;
                    if (!entries.TryGetValue(r.key, out list))
                        entries[r.key] = list = new List<Region>();
                    list.Add(new Region { Data = r, Page = page });
                }
                Addressables.Release(request);
                handles.Remove(request);
            }
#else
            yield return null;
#endif
        }

        public static void Release()
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_CONTENT_CONTRACT
            foreach (var handle in handles)
                if (handle.IsValid())
                    Addressables.Release(handle);
            handles.Clear();
#endif
            entries.Clear();
            LoadedPageCount = 0;
        }
    }
}
