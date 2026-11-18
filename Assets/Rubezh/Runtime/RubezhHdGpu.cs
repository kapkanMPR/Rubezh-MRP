using System;
using System.Collections.Generic;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rubezh.UnityPort
{
    // Native scene owns rendering. Shared presentation helpers update retained poses;
    // the deterministic simulation has no Unity dependency.
    public sealed partial class HdGpuRenderer : INativeComposition, IVectorCanvasGpu, IVisualGroupGpu, ISmokePuffGpu, IReusableSceneGpu, IStaticSceneGpu, IGroundChunkGpu, IProceduralGpu, IRoofVisibilityGpu, ICanvasGpu, IRoofGpu, IRoofPatchGpu, IShapedRoofGpu, IClosureGpu, ICanvasViewportGpu, IDisposable
    {
        readonly PixelCanvas canvas;
        NativeBattleComposer nativeComposer;
        public int NativeActorRebuilds=>nativeComposer?.ActorRebuilds??0;
        public int NativeActorReuseHits=>nativeComposer?.ActorReuseHits??0;
        readonly List<Material> materials = new List<Material>();
        readonly List<Texture2D> atlases = new List<Texture2D>();
        readonly Shader shader;
        readonly Material backgroundMaterial;
        readonly CommandBuffer commands;
        readonly Mesh backgroundMesh;
        Texture2D frameProbe;
        bool validateFrame = true;
        readonly Mesh mesh;
        Texture2D scenery, occlusion, foregroundOcclusion;
        RenderTexture target, foregroundRaw, foreground;
        public Texture ForegroundTexture => foreground;
        object cachedMap;
        int cachedRevision = int.MinValue;
        struct Region
        {
            public int Page, X, Y, W, H, OX, OY, OW, OH;
        }

        struct Entry
        {
            public int Id, Level, Mip, W, H, OX, OY, OW, OH;
        }

        Region[][] regions;
        List<Vector3> vertices = new List<Vector3>(16384);
        List<Vector2> uvs = new List<Vector2>(16384);
        List<Vector2> clips = new List<Vector2>(16384);
        List<Vector2> depths = new List<Vector2>(16384);
        List<Color32> colors = new List<Color32>(16384);
        List<int> indices = new List<int>(24576);
        AtlasLayout.DrawRuns runs = new AtlasLayout.DrawRuns();
        public Texture Texture
        {
            get
            {
                return target;
            }
        }

        public int MapUploads { get; private set; }

        public int QuadCount
        {
            get
            {
                return vertices.Count / 4;
            }
        }

        public Rect FrameUV { get; private set; }

        readonly bool ultra, primitive;
        int spriteAtlasPages;
        Rect visiblePixels;
        public HdGpuRenderer(PixelCanvas source, bool ultra = false, bool primitive = false, bool unityScene = false)
        {
            UnitySceneMode=unityScene;
            this.ultra = ultra;
            this.primitive = primitive;
            canvas = source;

            shader = Resources.Load<Shader>("RubezhHdGpu");
            if (shader == null || !shader.isSupported || !SystemInfo.SupportsTextureFormat(TextureFormat.RFloat))
                throw new NotSupportedException("HD GPU: shader or RFloat textures unavailable.");
            backgroundMaterial = new Material(shader);
            if(!unityScene){
#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
            commands = new CommandBuffer
            {
                name = "Rubezh isolated HD frame"
            };
            backgroundMesh = new Mesh
            {
                name = "Rubezh HD background"
            };
            backgroundMesh.vertices = new[]
            {
                new Vector3(0, 0, 0),
                new Vector3(canvas.Width, 0, 0),
                new Vector3(canvas.Width, canvas.Height, 0),
                new Vector3(0, canvas.Height, 0)
            };
            backgroundMesh.uv = new[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };
            backgroundMesh.triangles = new[]
            {
                0,
                1,
                2,
                0,
                2,
                3
            };
            mesh = new Mesh
            {
                name = "Rubezh HD ordered quads",
                indexFormat = IndexFormat.UInt32
            };
            mesh.MarkDynamic();
#else
                throw new InvalidOperationException("Only native scene rendering is supported.");
#endif
            }
            try
            {
                BuildAtlas();
                spriteAtlasPages=atlases.Count;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public long AtlasTransparentBytesSaved,SpriteAtlasBytes;
        public static int[] AlphaCrop(byte[] rgba,int w,int h){
            int l=w,t=h,r=-1,b=-1;
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(rgba[(y*w+x)*4+3]!=0){l=Math.Min(l,x);r=Math.Max(r,x);t=Math.Min(t,y);b=Math.Max(b,y);}
            if(r<0)return new[]{0,0,1,1};
            l=Math.Max(0,l-2);t=Math.Max(0,t-2);r=Math.Min(w-1,r+2);b=Math.Min(h-1,b+2);
            return new[]{l,t,r-l+1,b-t+1};
        }
        void BuildAtlas()
        {
            using (var sample = new SilentOrders.Core.CpuSample("Rubezh.GPU.BuildAtlas"))
            {
                int count = Rubezh.Graphics.SpriteCount;
                if(UnitySceneMode){regions=new Region[count][];LoadUnityAtlasCatalog();for(int id=0;id<count;id++)TryUnitySprite(id);return;}
#if UNITY_5_3_OR_NEWER && !UNITY_EDITOR
                throw new InvalidOperationException("Runtime atlas packing has been removed. Prepare native content in the editor.");
#else
                var dynamicIds = new HashSet<int>();
                for (int id = 0; id < Math.Min(count, StudioCatalog.SpriteStart); id++)
                {
                    // PersonSprite uses these legacy green bodies only in primitive mode. Red bodies remain.
                    bool oldGreen = id >= Rubezh.Graphics.LoadoutSoldierStart && id < Rubezh.Graphics.LoadoutSoldierStart + 72 && (id - Rubezh.Graphics.LoadoutSoldierStart) % 8 < 4;
                    if (primitive || !oldGreen)
                        dynamicIds.Add(id);
                }

                for (int id = 41; id <= DebrisCatalog.Count; id++)
                {
                    int sprite = DebrisCatalog.Sprite(id);
                    if (sprite >= 0 && sprite < count)
                        dynamicIds.Add(sprite);
                }

                foreach (int loose in Rubezh.Graphics.LooseIds)
                    if (Rubezh.Graphics.HasSprite(loose))
                        dynamicIds.Add(loose);
                dynamicIds.Add(Rubezh.WeaponAppearance.RareAk);
                dynamicIds.Add(Rubezh.WeaponAppearance.EpicAk);
                dynamicIds.Add(Rubezh.Graphics.RedBeltSprite);
                dynamicIds.Add(Rubezh.Graphics.RedSniperMagSprite);
                for (int id = 6825; id <= 6830; id++)
                    dynamicIds.Add(id);
                for (int id = Rubezh.GreenLook.BakedStart; id < count; id++)
                    if (Rubezh.Graphics.HasSprite(id))
                        dynamicIds.Add(id);
                if (ultra)
                    for (int id = 0; id < count; id++)
                        if (Rubezh.Graphics.HasSprite(id)) dynamicIds.Add(id);
                regions = new Region[count][];
                if(UnitySceneMode)LoadUnityAtlasCatalog();
                var entries = new List<Entry>();
                for (int id = 0; id < count; id++)
                {
                    if(UnitySceneMode && TryUnitySprite(id))continue;
                    if (!dynamicIds.Contains(id) || !Rubezh.Graphics.HasSprite(id) ||
                        (Rubezh.Graphics.PackedMode && Rubezh.SpritePackIndex.Resolve(id) != id))
                        continue;
                    int first = 0, levels = Rubezh.Graphics.LevelCount(id);
                    int limit = id == Rubezh.Graphics.VehicleSprite(true) || id == Rubezh.Graphics.VehicleSprite(false) ? 512 : 128;
                    if (ultra)
                        limit = int.MaxValue;
                    while (first + 1 < levels)
                    {
                        int w, h;
                        Rubezh.Graphics.SpriteData(id, first, out w, out h);
                        if (w <= limit && h <= limit)
                            break;
                        first++;
                    }

                    if (ultra)
                        levels = first + 1; // one original per sprite; no duplicate reduced copies
                    regions[id] = new Region[levels - first];
                    for (int mip = first; mip < levels; mip++)
                    {
                        int w, h;
                        var pixels=Rubezh.Graphics.SpriteData(id,mip,out w,out h);
                        var crop=AlphaCrop(pixels,w,h);
                        entries.Add(new Entry { Id=id,Level=mip-first,Mip=mip,W=crop[2],H=crop[3],OX=crop[0],OY=crop[1],OW=w,OH=h });
                        AtlasTransparentBytesSaved+=(long)(w*h-crop[2]*crop[3])*4;
                    }
                }

                var sizes = new List<AtlasLayout.Size>();
                foreach (var e in entries)
                    sizes.Add(new AtlasLayout.Size(e.W, e.H));
                var layout = AtlasLayout.Pack(sizes, SystemInfo.maxTextureSize);
                int importedPageCount=atlases.Count;
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    var r = layout.Items[i];
                    regions[e.Id][e.Level] = new Region
                    {
                        Page = r.Page+importedPageCount,
                        X = r.X,
                        Y = r.Y,
                        W = r.W,
                        H = r.H, OX=e.OX, OY=e.OY, OW=e.OW, OH=e.OH
                    };
                }

                for (int page = 0; page < layout.Pages.Length; page++)
                {
                    var size = layout.Pages[page];
                    byte[] packed = new byte[size.W * size.H * 4];
                    // Every page has a white tile, so adjacent primitive quads need no page switch.
                    for (int yy = 0; yy < 4; yy++)
                        for (int xx = 0; xx < 4; xx++)
                            for (int c = 0; c < 4; c++)
                                packed[(yy * size.W + xx) * 4 + c] = 255;
                    foreach (var e in entries)
                    {
                        var r = regions[e.Id][e.Level];
                        if (r.Page != page+importedPageCount)
                            continue;
                        int w, h;
                        var data = Rubezh.Graphics.SpriteData(e.Id, e.Mip, out w, out h);
                        if(e.W!=w || e.H!=h){var cropped=new byte[e.W*e.H*4];for(int row=0;row<e.H;row++)Array.Copy(data,((row+e.OY)*w+e.OX)*4,cropped,row*e.W*4,e.W*4);data=cropped;}
                        AtlasLayout.CopyExtruded(data, packed, size, new AtlasLayout.Placement { Page = page, X = r.X, Y = r.Y, W = r.W, H = r.H });
                    }

                    AtlasExport?.Invoke(page,packed,size.W,size.H);
                    var atlas = new Texture2D(size.W, size.H, TextureFormat.RGBA32, false, true);
                    atlas.name="Rubezh trimmed sprite atlas "+page;
                    SpriteAtlasBytes+=(long)size.W*size.H*4;
                    atlases.Add(atlas);
                    atlas.filterMode = primitive ? FilterMode.Point : FilterMode.Bilinear;
                    atlas.wrapMode = TextureWrapMode.Clamp;
                    atlas.LoadRawTextureData(packed);
                    atlas.Apply(false, true);
                    var material = new Material(shader);
                    materials.Add(material);
                    material.SetTexture("_MainTex", atlas);
                    material.SetVector("_CanvasSize", new Vector4(canvas.Width, canvas.Height, 0, 0));
                }
#endif
            }
        }

        public void Background(uint[] pixels,float[] depth,object map,int revision)
        {throw new InvalidOperationException("CPU background upload is not available in the native renderer.");}


        public int SmokePuffs {get;private set;}
        public bool SmokePuff(float x,float y,float scale,int phase,int density,bool dark,float depth) {
            if(DisableSmoke)return true;
            int n=vertices.Count;byte shade=(byte)(dark?132:161);
            Quad(x,y,x+46*scale,y,x+46*scale,y+30*scale,x,y+30*scale,0,0,1,1,
                new Color32(shade,(byte)(dark?145:170),(byte)(dark?141:164),(byte)(density*63)),depth,true,0);
            for(int i=n;i<depths.Count;i++)depths[i]=new Vector2(depth,-2-phase);
            if(vertices.Count>n)SmokePuffs++;return true;
        }
        public bool DisableSmoke, DisableRoofs, DisableScenery;
        public void Rect(float x, float y, float w, float h, uint color, float depth, bool test)
        {
            if (w <= 0 || h <= 0)
                return;
            if (RetainNativeRect(x,y,w,h,color,depth,test)) return;
            var tint = new Color32((byte)color, (byte)(color >> 8), (byte)(color >> 16), (byte)(color >> 24));
            // Native colour primitives do not need a white texture. Keep the current
            // material run and mark the vertices as solid, preserving painter order.
            // Switching to page zero between every terrain sprite otherwise creates
            // thousands of tiny submeshes while the camera is moving.
            bool solid = nativeAtlasMode && UnityOwnedScene;
            int page = solid ? runs.CurrentPage : nativeAtlasMode ? 0 : runs.CurrentPage < spriteAtlasPages ? runs.CurrentPage : 0;
            var atlas = atlases[page];
            float u = solid ? -1 : 1.5f / atlas.width, v = solid ? -1 : 1.5f / atlas.height;
            Quad(x, y, x + w, y, x + w, y + h, x, y + h, u, v, u, v, tint, depth, test, page);
        }

        public bool Visible(float l,float t,float r,float b)=>!ultra || !(r<visiblePixels.xMin||l>visiblePixels.xMax||b<visiblePixels.yMin||t>visiblePixels.yMax);

        public void RoofDiamond(float x, float y, float hw, float hh)
        {
            bool solid = nativeAtlasMode && UnityOwnedScene;
            int page = solid ? runs.CurrentPage : nativeAtlasMode ? 0 : runs.CurrentPage < spriteAtlasPages ? runs.CurrentPage : 0;
            var atlas = atlases[page];
            float u = solid ? -1 : 1.5f / atlas.width, v = solid ? -1 : 1.5f / atlas.height;
            Quad(x, y - hh, x + hw, y, x, y + hh, x - hw, y, u, v, u, v, new Color32(16, 17, 18, 26), 10000, false, page);
        }

        public void Sprite(int id, float x, float y, int width, int height, float angle, float depth, bool test)
        {
            if (Rubezh.Graphics.PackedMode) id = Rubezh.SpritePackIndex.Resolve(id);
            if (id < 0 || id >= regions.Length || regions[id] == null)
            {
                if (Rubezh.Graphics.ReportMissingSprite(id))
                    Debug.LogError("Missing GPU sprite (frame continues): " + id);
                return;
            }
            float w = width * canvas.Scale, h = height * canvas.Scale;
            int level = 0;
            while (level + 1 < regions[id].Length && regions[id][level + 1].OW >= Math.Abs(w) * Rubezh.Graphics.DetailScale(id) && regions[id][level + 1].OH >= Math.Abs(h) * Rubezh.Graphics.DetailScale(id))
                level++;
            var r = regions[id][level];
            var atlas = atlases[r.Page];
            float cx = (float)Math.Floor(x * canvas.Scale), cy = (float)Math.Floor(y * canvas.Scale);
            cx += (float)Math.Round((x * canvas.Scale - cx) * 4) * .25f;
            cy += (float)Math.Round((y * canvas.Scale - cy) * 4) * .25f;
            if (ultra)
            {
                cx = x * canvas.Scale;
                cy = y * canvas.Scale;
            }
            else
                angle = (float)(Math.Round(angle * 1024 / (Math.PI * 2)) * Math.PI * 2 / 1024);
            float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
            float offsetX=((r.OX+r.W*.5f)/r.OW-.5f)*w,offsetY=((r.OY+r.H*.5f)/r.OH-.5f)*h;
            cx+=offsetX*cos-offsetY*sin;cy+=offsetX*sin+offsetY*cos;
            w*=r.W/(float)r.OW;h*=r.H/(float)r.OH;
            float ax = w * .5f * cos, ay = w * .5f * sin, bx = -h * .5f * sin, by = h * .5f * cos;
            // Software samples at integer pixel coordinates; GPU samples at pixel centres.
            cx += .5f;
            cy += .5f;
            if (RetainNativeSprite(id, level, r, cx, cy, w, h, angle, depth, test)) return;
            Quad(cx - ax - bx, cy - ay - by, cx + ax - bx, cy + ay - by, cx + ax + bx, cy + ay + by, cx - ax + bx, cy - ay + by, (float)r.X / atlas.width, (float)r.Y / atlas.height, (float)(r.X + r.W) / atlas.width, (float)(r.Y + r.H) / atlas.height, new Color32(255, 255, 255, 255), depth, test, r.Page);
        }

        void Quad(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, float u0, float v0, float u1, float v1, Color32 tint, float depth, bool test, int page)
        {
            if (ultra && !buildingGroundChunk && (Math.Max(Math.Max(x0,x1),Math.Max(x2,x3)) < visiblePixels.xMin ||
                Math.Min(Math.Min(x0,x1),Math.Min(x2,x3)) > visiblePixels.xMax ||
                Math.Max(Math.Max(y0,y1),Math.Max(y2,y3)) < visiblePixels.yMin ||
                Math.Min(Math.Min(y0,y1),Math.Min(y2,y3)) > visiblePixels.yMax)) return;
            runs.AddQuad(page);
            int n = vertices.Count;
            vertices.Add(new Vector3(x0, y0, 0));
            vertices.Add(new Vector3(x1, y1, 0));
            vertices.Add(new Vector3(x2, y2, 0));
            vertices.Add(new Vector3(x3, y3, 0));
            uvs.Add(new Vector2(u0, v0));
            uvs.Add(new Vector2(u1, v0));
            uvs.Add(new Vector2(u1, v1));
            uvs.Add(new Vector2(u0, v1));
            for (int i = 0; i < 4; i++)
            {
                colors.Add(tint);
                depths.Add(new Vector2(depth, depth < -.5f ? -1 : test ? 1 : 0));
                clips.Add(new Vector2(canvas.LayerLeft, canvas.LayerRight));
            }

            indices.Add(n);
            indices.Add(n + 1);
            indices.Add(n + 2);
            indices.Add(n);
            indices.Add(n + 2);
            indices.Add(n + 3);
        }

        public double ComposeMs,SubmitMs;
        public int RenderWidth=>target==null?0:target.width;
        public int RenderHeight=>target==null?0:target.height;
        BattleSimulation retainedBattle;
        int retainedRosterRevision=-1, retainedTick=-1, retainedRevision=-1, retainedSelection=-2;
        float retainedTime;
        bool retainedDebug, retainedFog, retainedHardcore, retainedSmoke, retainedRoofs, retainedScenery;
        public int NativeCompositionHits {get;private set;}
        bool CanReuseNativeComposition(BattleSimulation battle,bool debug,int selected,bool foregroundOnly)
        {
            return UnityOwnedScene&&!foregroundOnly&&nativeComposer!=null&&nativeComposer.UsesRenderer(this)&&retainedRosterRevision==nativeComposer.RosterRevision&&ReferenceEquals(retainedBattle,battle)&&
                retainedTick==battle.TickNumber&&retainedTime==battle.Time&&retainedRevision==battle.Map.Revision&&
                retainedSelection==selected&&retainedDebug==debug&&retainedFog==battle.Fog.Enabled&&retainedHardcore==battle.Fog.Hardcore&&
                retainedSmoke==DisableSmoke&&retainedRoofs==DisableRoofs&&retainedScenery==DisableScenery;
        }
        void RetainNativeComposition(BattleSimulation battle,bool debug,int selected,bool foregroundOnly)
        {
            if(!UnityOwnedScene||foregroundOnly){retainedBattle=null;return;}
            retainedRosterRevision=nativeComposer.RosterRevision;retainedBattle=battle;retainedTick=battle.TickNumber;retainedTime=battle.Time;retainedRevision=battle.Map.Revision;
            retainedSelection=selected;retainedDebug=debug;retainedFog=battle.Fog.Enabled;retainedHardcore=battle.Fog.Hardcore;
            retainedSmoke=DisableSmoke;retainedRoofs=DisableRoofs;retainedScenery=DisableScenery;
        }
        public void InvalidateNativeComposition(){retainedBattle=null;}
        public void Render(BattleVisualRecipes painter, BattleSimulation battle, bool debug, int selected, int width, int height, Rect? viewport = null, bool foregroundOnly = false)
        {
            if(painter is NativeBattleComposer supplied && !ReferenceEquals(nativeComposer,supplied)){
                nativeComposer?.ForgetRenderer(this);nativeComposer=supplied;InvalidateNativeComposition();
            }
            if(nativeComposer==null)nativeComposer=new NativeBattleComposer(canvas);
            nativeComposer.ObserveTick(battle);
            // Canvas already carries the quality scale; a second upscale wastes pixels.
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            if (target == null || target.width != width || target.height != height || !target.IsCreated())
            {
                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.Destroy(target);
                }

                target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                target.filterMode = primitive ? FilterMode.Point : FilterMode.Bilinear;
                target.wrapMode = TextureWrapMode.Clamp;
                if (!target.Create())
                    throw new InvalidOperationException("Cannot create HD render target.");
                validateFrame = true;
            }

            var visible = viewport ?? new Rect(0,0,1,1);
            if (UnityOwnedScene)
            {
                // Build the complete forest geometry once. Unity cameras now cull the retained meshes.
                // Destruction still invalidates the corresponding source geometry through Map.Revision.
                depthCoverage = new Rect(0, 0, 1, 1);
                ReleaseNativeDepth();
            }

            var cullView = NativeScene ? depthCoverage : visible;
            visiblePixels = new Rect(cullView.x * canvas.Width, cullView.y * canvas.Height, cullView.width * canvas.Width, cullView.height * canvas.Height);
            bool reuseComposition=CanReuseNativeComposition(battle,debug,selected,foregroundOnly);
            if(reuseComposition){ComposeMs=0;NativeCompositionHits++;}
            else {
            if (!NativeScene) groundDraws.Clear();
            pictureFrame++;
            SmokePuffs=0;
            ResetVisualGroups();
            vertices.Clear();
            uvs.Clear();
            depths.Clear();
            clips.Clear();
            colors.Clear();
            indices.Clear();
            runs.Clear();
            var composeWatch=System.Diagnostics.Stopwatch.StartNew();
            var previousSink = canvas.Gpu;
            try
            {
                canvas.Gpu = this;
                capturingNativeActors=UnityOwnedScene&&!foregroundOnly;
                if(!UnityOwnedScene || foregroundOnly)throw new InvalidOperationException("Only the retained native battle path is supported.");
                nativeComposer.Compose(battle,this,debug,selected);
            }
            finally
            {
                canvas.Gpu = previousSink;
                capturingNativeActors=false;
            }

            ComposeMs=composeWatch.Elapsed.TotalMilliseconds;
                RetainNativeComposition(battle,debug,selected,foregroundOnly);
            }
            // Explicit shader matrix: no dependency on the camera, IMGUI matrix or SRP globals.
            var uv = viewport ?? new Rect(0, 0, 1, 1);
            FrameUV = uv;
            RenderUnityCamera(battle,uv,width,height);
        }

        // GPU-only visibility composite reuses the same retained actors; no recipe/CPU pass.
        public void RenderForeground(BattleVisualRecipes painter,BattleSimulation battle,int selected)
        {if(!RenderUnityForeground())throw new InvalidOperationException("Native foreground camera unavailable.");}

        static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEngine.Object.DestroyImmediate(value); return; }
#endif
            UnityEngine.Object.Destroy(value);
        }

        bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            InvalidateNativeComposition();
            DisposeUnityCamera();
            DisposeGroundChunks();
            DisposeNativeScene();
            ReleasePictures();
            ReleaseRoof();
            if (target != null)
            {
                target.Release();
                DestroyOwned(target);
                target = null;
            }

            if (foregroundRaw != null) { foregroundRaw.Release(); DestroyOwned(foregroundRaw); }
            if (foreground != null) { foreground.Release(); DestroyOwned(foreground); }
            foreach (var atlas in atlases) {
                if(addressableTextures.Contains(atlas))continue;
                if(importedUnityTextures.Contains(atlas))Resources.UnloadAsset(atlas);else DestroyOwned(atlas);
            }
            importedUnityTextures.Clear();addressableTextures.Clear();nativeTexturePages.Clear();unityPages.Clear();unityEntries.Clear();
            regions=null;foregroundRaw=foreground=null;
            atlases.Clear();
            foreach (var material in materials)
                DestroyOwned(material);
            materials.Clear();
            groundMaterialPages.Clear();groundMaterialPalettes.Clear();
            DestroyOwned(scenery);
            DestroyOwned(occlusion);
            DestroyOwned(foregroundOcclusion);
            DestroyOwned(mesh);
            DestroyOwned(backgroundMesh);
            DestroyOwned(frameProbe);
            DestroyOwned(backgroundMaterial);
            if (commands != null)
                commands.Release();
        }
    }
}
