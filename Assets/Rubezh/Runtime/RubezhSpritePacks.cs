using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using SilentOrders.Core;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        BattleSimulation sceneArtBattle;
        NativeBattleAssets nativeAssets;
        void ReleaseSceneGraphics()
        {
            HideUnityTestHud();
            ReleaseBattleOverlayTextures();
            sceneArtBattle = null;
            ReleaseBattlePresentation();
            if (hdBattle != null)
            {
                hdBattle.Dispose();
                hdBattle = null;
            }

            if (texture != null)
            {
                Destroy(texture);
                texture = null;
            }

            nativeAssets?.Dispose(); nativeAssets=null;
            painter = null;
            ExteriorSpriteLoader.Release();BuildingRecipe.ClearMaterials();
        }

        void ResumeBattle(bool unpause = false)
        {
            if (battle == null)
                return;
            var current = battle;
#if UNITY_5_3_OR_NEWER
            // Saved diagnostic-reference operations now use the same native path.
            if(campBattle!=null&&campBattle.mode=="test-reference")campBattle.mode="test";
            unityTestBattle=true;
#endif
            QueueBattleLoad(current, () =>
            {
                campaignMode = current.CampaignLevel > 0;
                if (campaignMode)
                    mission = current.CampaignLevel;
                screen = 2;
                if (unpause)
                    paused = false;
            });
        }

#if !UNITY_5_3_OR_NEWER
        IEnumerator LoadSpritePages(HashSet<int> pages, float from, float to, HashSet<int> looks = null, HashSet<int> required=null)
        {
            Rubezh.Graphics.ConfigurePacks();
            var sorted = new List<int>(pages);
            sorted.Sort();
            int done = 0;
            foreach (int page in sorted)
            {
                if (loadingDisposed)
                    yield break;
                if (!Rubezh.Graphics.HasPage(page, looks, required))
                {
                    var request = Resources.LoadAsync<TextAsset>(Rubezh.SpritePackIndex.Path(page));
                    while (!request.isDone)
                        yield return null;
                    loadingAtlasAsset = request.asset as TextAsset;
                    if (loadingAtlasAsset == null)
                        throw new InvalidOperationException("Нет пакета графики: " + page);
                    byte[] bytes = loadingAtlasAsset.bytes;
                    Task work = null;
                    try
                    {
#if UNITY_WEBGL && !UNITY_EDITOR
      Rubezh.Graphics.LoadPage(page,bytes,()=>loadingDisposed,looks,required);yield return null;
#else
                        work = Task.Run(() => Rubezh.Graphics.LoadPage(page, bytes, () => loadingDisposed, looks, required));
                        while (!work.IsCompleted)
                            yield return null;
                        work.GetAwaiter().GetResult();
#endif
                    }
                    finally
                    {
                        if (work != null)
                            work.ContinueWith(t =>
                            {
                                var ignored = t.Exception;
                            }, TaskContinuationOptions.OnlyOnFaulted);
                        ReleaseLoadingAtlas();
                    }
                }

                loadingTarget = from + (to - from) * (++done / (float)sorted.Count);
            }
        }

#endif
        void QueueBattleLoad(BattleSimulation candidate, Action complete)
        {
            PrepareFireLoading();
            loading = true;
            loadingError = null;
            loadingTarget = loadingShown = 0;
            loadingStatus = "Слушаем брифинг";
            StartCoroutine(GuardLoading(LoadBattlePages(candidate, complete)));
        }

        IEnumerator LoadBattlePages(BattleSimulation candidate, Action complete)
        {
            yield return null;
            ReleaseSceneGraphics();
            ExteriorSpriteLoader.Initialize();
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_CONTENT_CONTRACT
            var exteriorLoad=ExteriorSpriteLoader.Preload(()=>loadingDisposed);
            try{while(exteriorLoad.MoveNext())yield return exteriorLoad.Current;}finally{(exteriorLoad as IDisposable)?.Dispose();}
            if(loadingDisposed)yield break;
#endif
            var required=Rubezh.Graphics.SceneSprites(candidate);
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_CONTENT_CONTRACT
            unityTestBattle=true;
            {
                // All regular art is imported/compressed by Unity; no duplicate RGBA unpacking.
                Rubezh.Graphics.ReleaseSceneSprites();
                loadingStatus="Загрузка атласов Unity";nativeAssets=new NativeBattleAssets();
                var load=nativeAssets.Load(required,()=>loadingDisposed);bool ready=false;
                try{while(load.MoveNext())yield return load.Current;ready=!loadingDisposed;}
                finally{(load as IDisposable)?.Dispose();if(!ready){nativeAssets.Dispose();nativeAssets=null;}}
                if(!ready)yield break;
            }
#else
            {
                var pages=Rubezh.Graphics.PagesFor(candidate.Map);
                Rubezh.Graphics.AddLookPages(pages,candidate);
                var looks=Rubezh.Graphics.LookSprites(candidate);
                foreach(int id in required)pages.Add(Rubezh.SpritePackIndex.Page[id]);
                Rubezh.Graphics.KeepPages(pages);Rubezh.Graphics.KeepRequiredSprites(required);
                var work=LoadSpritePages(pages,.05f,.9f,looks,required);
                try{while(work.MoveNext())yield return work.Current;}finally{(work as IDisposable)?.Dispose();}
            }
#endif
            if(loadingDisposed)yield break;
            loadingStatus="Загрузка зданий Unity";
            var buildingLoad=NativeBuildingAssets.Load(candidate.Map,()=>loadingDisposed);
            try{while(buildingLoad.MoveNext())yield return buildingLoad.Current;}finally{(buildingLoad as IDisposable)?.Dispose();}
            if(loadingDisposed)yield break;
            Rubezh.Graphics.ValidateSceneSprites(required);
            var uiLoad=NativeUiArt.Preload(()=>loadingDisposed);
            try{while(uiLoad.MoveNext())yield return uiLoad.Current;}finally{(uiLoad as IDisposable)?.Dispose();}
            if(loadingDisposed)yield break;
            LoadBattleSamples();
            loadingStatus = "Собираем группу";
            painter = new NativeBattleComposer(BattleRasterScale);
            yield return null;
            complete();
            Render();
            sceneArtBattle = candidate;
            loadingTarget = 1;
            loadingStatus = "Выход на рубеж";
            while (loadingShown < 1)
                yield return null;
            var fade = FinishFireLoading();
            while (fade.MoveNext()) yield return fade.Current;
            loading = false;
        }
    }

    // Editor-only source reader; deliberately unavailable in the Player.
#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
    public static class EditorSpritePacks
    {
        public static string SourcePath(int page) {
            string relative=Rubezh.SpritePackIndex.Path(page)+".bytes";
            string moved="Assets/Rubezh/Editor/LegacySources/"+relative;
            return System.IO.File.Exists(moved)?moved:"Assets/Rubezh/Resources/"+relative;
        }
        static TextAsset LoadSource(int page) {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(SourcePath(page));
#else
            return Resources.Load<TextAsset>(Rubezh.SpritePackIndex.Path(page));
#endif
        }
        public static void Ensure(int id)
        {
            Rubezh.Graphics.ConfigurePacks();
            if (Rubezh.Graphics.HasSpritePixels(id))
                return;
            id = Rubezh.SpritePackIndex.Resolve(id);
            if (id < 0 || id >= Rubezh.SpritePackIndex.Count || Rubezh.SpritePackIndex.Page[id] < 0)
                throw new InvalidOperationException("Удалённый спрайт: " + id);
            int page = Rubezh.SpritePackIndex.Page[id];
            var asset = LoadSource(page);
            if (asset == null)
                throw new InvalidOperationException("Нет пакета " + page);
            try
            {
                Rubezh.Graphics.LoadPage(page, asset.bytes);
            }
            finally
            {
                Resources.UnloadAsset(asset);
            }
        }

        public static void Common()
        {
            Rubezh.Graphics.ConfigurePacks();
            foreach (int page in Rubezh.Graphics.CommonPages())
                if (!Rubezh.Graphics.HasPage(page))
                {
                    var asset = LoadSource(page);
                    if (asset == null)
                        throw new InvalidOperationException("Нет пакета " + page);
                    try
                    {
                        Rubezh.Graphics.LoadPage(page, asset.bytes);
                    }
                    finally
                    {
                        Resources.UnloadAsset(asset);
                    }
                }
        }

        public static void All()
        {
            Rubezh.Graphics.ConfigurePacks();
            for (int i = 0; i < Rubezh.SpritePackIndex.Count; i++)
                if (Rubezh.SpritePackIndex.Page[i] >= 0)
                    Ensure(i);
        }
    }
#endif
}
