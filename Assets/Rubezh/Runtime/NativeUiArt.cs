using System;
using System.Collections;
using System.Collections.Generic;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public static class NativeUiArt
    {
        public const string Root = "NativeUI";

        [Serializable]
        public sealed class Entry
        {
            public string key;
            public int page,
                x,
                y,
                w,
                h;
        }

        [Serializable]
        public sealed class Catalog
        {
            public int version = 1,
                size = 2048;
            public Entry[] entries;
        }

        static Catalog catalog;
        static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        static readonly Dictionary<int, Texture2D> pages = new Dictionary<int, Texture2D>();

        public static string PortraitKey(
            int weapon,
            bool enemy,
            Rubezh.GreenLook look,
            bool western,
            Role role,
            int identity
        ) => "p" + Rubezh.PawnAtlas.Sprite(look, enemy, role, identity, 0) + "w" + weapon + "x" + (western ? 1 : 0);

        static void Initialize()
        {
            if (catalog != null)
                return;
            var data = Resources.Load<TextAsset>(Root + "/catalog");
            if (data == null)
                throw new InvalidOperationException("Нет атласа интерфейса. Выполните подготовку нативного контента.");
            try
            {
                var next = JsonUtility.FromJson<Catalog>(data.text);
                if (next == null || next.version != 1 || next.entries == null)
                    throw new InvalidOperationException("Повреждён каталог NativeUI");
                entries.Clear();
                foreach (var e in next.entries)
                    entries.Add(e.key, e);
                catalog = next;
            }
            finally
            {
                Resources.UnloadAsset(data);
            }
        }

        public static IEnumerator Preload(Func<bool> cancelled)
        {
            Initialize();
            var ids = new HashSet<int>();
            foreach (var e in catalog.entries)
                ids.Add(e.page);
            foreach (int page in ids)
            {
                if (cancelled())
                    yield break;
                if (pages.ContainsKey(page))
                    continue;
                var request = Resources.LoadAsync<Texture2D>(Root + "/page-" + page.ToString("D2"));
                while (!request.isDone)
                    yield return null;
                var texture = request.asset as Texture2D;
                if (cancelled())
                {
                    if (texture != null)
                        Resources.UnloadAsset(texture);
                    yield break;
                }
                if (texture == null)
                    throw new InvalidOperationException("Нет страницы интерфейса: " + page);
                pages.Add(page, texture);
            }
        }

        public static void Resolve(string key, out Texture2D texture, out Rect uv)
        {
            Initialize();
            Entry e;
            if (!entries.TryGetValue(key, out e))
                throw new InvalidOperationException("Нет подготовленной иконки: " + key);
            if (!pages.TryGetValue(e.page, out texture))
            {
                texture = Resources.Load<Texture2D>(Root + "/page-" + e.page.ToString("D2"));
                if (texture == null)
                    throw new InvalidOperationException("Нет страницы интерфейса: " + e.page);
                pages.Add(e.page, texture);
            }
            float size = catalog.size;
            uv = new Rect(e.x / size, 1 - (e.y + e.h) / size, e.w / size, e.h / size);
        }

        public static void Draw(Rect rect, string key)
        {
            Texture2D texture;
            Rect uv;
            Resolve(key, out texture, out uv);
            GUI.DrawTextureWithTexCoords(rect, texture, uv, true);
        }

        public static void Release()
        {
            foreach (var t in pages.Values)
                if (t != null)
                    Resources.UnloadAsset(t);
            pages.Clear();
            entries.Clear();
            catalog = null;
        }
    }
}
