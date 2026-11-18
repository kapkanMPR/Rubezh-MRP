using System;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
using System.Collections.Generic;
using UnityEngine.Rendering;
#endif
namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer : INativeEffectsGpu
    {
        public bool OwnsBattleEffects => UnityOwnedScene;
        public int NativeEffectNodes { get; private set; }
        public int NativeEffectPoolSize { get; private set; }
        public int NativeEffectMeshBuilds { get; private set; }
        public int NativeEffectUpdates { get; private set; }
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
        sealed class EffectNode
        {
            public GameObject Object; public MeshRenderer Renderer; public MeshFilter Filter;
            public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
            public int LastFrame; public bool Active;
        }
        readonly List<EffectNode> effectNodes = new List<EffectNode>();
        readonly Dictionary<ArcProjectile, V2> projectileOrigins = new Dictionary<ArcProjectile, V2>();
        readonly List<ArcProjectile> expiredProjectiles = new List<ArcProjectile>();
        Mesh effectQuad, blastRing; Material effectMaterial, nativeCraterMaterial; Texture2D nativeCraterTexture; int nativeCraterOrder, usedEffectNodes, effectFrame;
        BattleSimulation effectsBattle; float effectsTime,effectsScale; int effectsTick,effectsRevision,effectsCraterOrder,effectsCount;
        bool effectsFog,effectsHardcore,effectsSmoke;
        static readonly int EffectColor = Shader.PropertyToID("_Color"), EffectData = Shader.PropertyToID("_Effect"),
            EffectDepth = Shader.PropertyToID("_EffectDepth"), EffectUV = Shader.PropertyToID("_UVRect"), EffectTexture = Shader.PropertyToID("_MainTex");
        void EnsureEffects()
        {
            if (effectMaterial != null) return;
            // Serialized material keeps the runtime-instancing variant reachable during shader stripping.
            var template = Resources.Load<Material>("RubezhNativeEffectMaterial");
            if (template == null || template.shader == null || !template.shader.isSupported) throw new InvalidOperationException("Native effect material unavailable");
            effectMaterial = new Material(template) { name = "Rubezh pooled battle effects", enableInstancing = true };
            effectQuad = new Mesh { name = "Shared effect quad" };
            effectQuad.vertices = new[] {new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};
            effectQuad.uv = new[] {new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};
            effectQuad.triangles = new[] {0,1,2,0,2,3}; effectQuad.RecalculateBounds();effectQuad.UploadMeshData(true);NativeEffectMeshBuilds++;
        }
        Mesh BlastGeometry()
        {
            if(blastRing!=null)return blastRing;
            // 28 ember quads are uploaded once. The vertex shader expands the ring by simulation age.
            var vertices=new Vector3[112];var uv=new Vector2[112];var direction=new Vector2[112];var triangles=new int[168];
            for(int i=0;i<28;i++)
            {
                double angle=i*Math.PI*2/28;var radial=new Vector2((float)Math.Cos(angle),(float)Math.Sin(angle));int v=i*4,t=i*6;
                vertices[v]=new Vector3(-1.5f,-1,0);vertices[v+1]=new Vector3(1.5f,-1,0);vertices[v+2]=new Vector3(1.5f,1,0);vertices[v+3]=new Vector3(-1.5f,1,0);
                uv[v]=new Vector2(0,0);uv[v+1]=new Vector2(1,0);uv[v+2]=new Vector2(1,1);uv[v+3]=new Vector2(0,1);
                for(int j=0;j<4;j++)direction[v+j]=radial;
                triangles[t]=v;triangles[t+1]=v+1;triangles[t+2]=v+2;triangles[t+3]=v;triangles[t+4]=v+2;triangles[t+5]=v+3;
            }
            blastRing=new Mesh{name="Immutable blast ember ring"};blastRing.vertices=vertices;blastRing.uv=uv;blastRing.uv2=direction;blastRing.triangles=triangles;
            blastRing.bounds=new Bounds(Vector3.zero,new Vector3(4096,2048,1));blastRing.UploadMeshData(true);NativeEffectMeshBuilds++;
            return blastRing;
        }
        // Ordinary effects use one immutable quad. Only Transform and MaterialPropertyBlock change.
        // Simulation time drives their appearance, including pause, replay, 2x and 4x speed.
        void Effect(V2 center, float width, float height, float angle, Color color, float depth,
            int kind=0, float phase=0, float density=0, float endDepth=float.NaN, Texture texture=null, Vector4? uv=null, int order=int.MinValue)
        {
            if (width <= 0 || height <= 0) return;
            float scale = canvas.Scale;
            float radius = (kind==4?phase*2+8:Math.Max(width,height))*scale;
            if (!Visible(center.X*scale-radius,center.Y*scale-radius,center.X*scale+radius,center.Y*scale+radius)) return;
            EffectNode node;
            if (usedEffectNodes == effectNodes.Count)
            {
                var go = new GameObject("Pooled battle effect");go.layer=25;go.transform.SetParent(unityRoot.transform,false);
                var filter=go.AddComponent<MeshFilter>();filter.sharedMesh=effectQuad;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=effectMaterial;
                renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
                node=new EffectNode{Object=go,Renderer=renderer,Filter=filter};effectNodes.Add(node);
            } else node=effectNodes[usedEffectNodes];
            node.Filter.sharedMesh=kind==4?BlastGeometry():effectQuad;
            node.Object.transform.localPosition=new Vector3(center.X*scale,center.Y*scale,0);
            node.Object.transform.localRotation=Quaternion.Euler(0,0,angle);
            node.Object.transform.localScale=new Vector3(width*scale,height*scale,1);
            node.Renderer.sortingOrder=order!=int.MinValue?order:20000+Math.Min(usedEffectNodes,9999);
            node.Properties.Clear();node.Properties.SetColor(EffectColor,color);
            node.Properties.SetVector(EffectData,new Vector4(kind,phase,density,0));
            node.Properties.SetVector(EffectDepth,new Vector4(depth,float.IsNaN(endDepth)?depth:endDepth,0,0));
            node.Properties.SetVector(EffectUV,uv??new Vector4(0,0,1,1));
            node.Renderer.sharedMaterial=kind==5?nativeCraterMaterial:effectMaterial;
            if(texture!=null&&kind!=5)node.Properties.SetTexture(EffectTexture,texture);
            node.Renderer.SetPropertyBlock(node.Properties);
            if(!node.Active){node.Object.SetActive(true);node.Active=true;}
            node.LastFrame=effectFrame;usedEffectNodes++;
        }
        static Color Tint(int r,int g,int b) => new Color(r/255f,g/255f,b/255f,1);
        static float EffectZ(V2 p,float offset=0) => 256+p.Y-p.X+offset;
        static V2 EffectPoint(BattleSimulation battle,V2 p) => BattleVisualRecipes.Project(p,battle.Map);
        void EffectLine(V2 from,V2 to,Color color,float z0,float z1,float width=1)
        {
            V2 delta=to-from;float length=delta.Length;
            if(length<.001f)return;
            Effect((from+to)*.5f,length,width,(float)(Math.Atan2(delta.Y,delta.X)*180/Math.PI),color,z0,endDepth:z1);
        }
        void EffectSprite(int id,V2 point,float width,float height,float depth)
        {
            Texture2D texture;UnityTestAtlasEntry region;
            if(NativeBattleAssets.Active==null||!NativeBattleAssets.Active.TrySprite(id,out texture,out region))
                throw new InvalidOperationException("Native projectile sprite not loaded: "+id);
            point+=new V2(((region.ox+region.w*.5f)/region.ow-.5f)*width,((region.oy+region.h*.5f)/region.oh-.5f)*height);
            Effect(point,width*region.w/region.ow,height*region.h/region.oh,0,Color.white,depth,kind:3,texture:texture,
                uv:new Vector4((float)region.x/texture.width,(float)region.y/texture.height,(float)region.w/texture.width,(float)region.h/texture.height));
        }
        void UpdateNativeEffects(BattleSimulation battle)
        {
            EnsureEffects();
            // Camera-only renders reuse every node; a resized render target still rebinds its mask.
            effectMaterial.SetTexture("_Occlusion",unityMask);
            if(nativeCraterMaterial!=null)nativeCraterMaterial.SetTexture("_Occlusion",unityMask);
            int count=battle.Traces.Count+battle.Ballistics.Sparks.Count+battle.Projectiles.Count+battle.Blasts.Count+battle.Smoke.Count+battle.Craters.Count;
            if(effectsBattle==battle&&effectsTime==battle.Time&&effectsTick==battle.TickNumber&&effectsRevision==battle.Map.Revision&&
                effectsFog==battle.Fog.Enabled&&effectsHardcore==battle.Fog.Hardcore&&effectsSmoke==DisableSmoke&&effectsCraterOrder==nativeCraterOrder&&effectsScale==canvas.Scale&&effectsCount==count)return;
            effectsBattle=null;effectsTime=battle.Time;effectsTick=battle.TickNumber;effectsRevision=battle.Map.Revision;
            effectsFog=battle.Fog.Enabled;effectsHardcore=battle.Fog.Hardcore;effectsSmoke=DisableSmoke;effectsCraterOrder=nativeCraterOrder;effectsScale=canvas.Scale;effectsCount=count;
            NativeEffectUpdates++;effectFrame++;usedEffectNodes=0;
            DrawNativeCraters(battle);
            foreach(var trace in battle.Traces)
            {
                if(battle.Fog.Enabled&&(!battle.Fog.Revealed(trace.From)||!battle.Fog.Revealed(trace.To)))continue;
                V2 tail=trace.Ballistic?trace.Tail:trace.From;
                V2 muzzle=EffectPoint(battle,tail),tip=EffectPoint(battle,trace.To);
                tip.Y-=trace.Ballistic?trace.ToHeight*9:9;
                if(trace.Ballistic)muzzle.Y-=trace.TailHeight*9;
                else if(trace.ShooterId>=0&&trace.ShooterId<battle.Soldiers.Count)
                {
                    var shooter=battle.Soldiers[trace.ShooterId];
                    BattleVisualRecipes.WeaponPose(battle,shooter,trace.Rocket,out var center,out muzzle,out var aim,out var weapon,out var left);
                    V2 foot=EffectPoint(battle,shooter.Position+shooter.Exchange.Visual);muzzle=foot+(muzzle-foot)*.85f;
                } else muzzle.Y-=11;
                bool segment=!trace.Ballistic||V2.Distance(trace.From,trace.To)>=.001f;
                if(segment)
                {
                    EffectLine(muzzle,tip,trace.Side==Side.Green?Tint(242,218,144):Tint(244,157,100),EffectZ(tail),EffectZ(trace.To));
                    if(trace.Fragment)EffectLine(muzzle+new V2(0,1),tip+new V2(0,1),Tint(242,218,144),EffectZ(tail),EffectZ(trace.To));
                }
                if(!trace.Fragment&&(!trace.Ballistic||!trace.Ricochet&&battle.Time-trace.Born<=.075f))
                    Effect(trace.Ballistic?EffectPoint(battle,trace.From)+trace.MuzzleOffset:muzzle,3,3,0,Tint(255,242,182),EffectZ(trace.From,.06f));
                if(trace.Ballistic&&segment)Effect(tip,1,1,0,Tint(255,245,202),EffectZ(trace.To,.06f));
                if(trace.Hit)EffectLine(tip-new V2(2,2),tip+new V2(2,2),Tint(249,227,185),EffectZ(trace.To,.12f),EffectZ(trace.To,.12f));
            }
            foreach(var spark in battle.Ballistics.Sparks)
            {
                float height;V2 world=Ballistics.SparkPosition(spark,battle.Time,out height);
                if(!battle.Fog.Revealed(world))continue;
                V2 point=EffectPoint(battle,world);point.Y-=height*9;
                EffectLine(point,point+new V2(spark.Direction.X*3,-spark.Direction.Y*3),spark.Dust?Tint(170,150,112):Tint(255,211,119),EffectZ(world,.08f),EffectZ(world,.08f));
            }
            foreach(var projectile in battle.Projectiles)DrawNativeProjectile(battle,projectile);
            foreach(var blast in battle.Blasts)
            {
                if(!battle.Fog.Revealed(blast.Position))continue;
                V2 point=EffectPoint(battle,blast.Position);point.Y-=blast.Height*24/2.8f;
                float age=battle.Time-blast.Born,z=EffectZ(blast.Position,.12f);
                int radius=(int)((8+age*28)*blast.Radius/3.3f);
                Color tint=age<.35f?(blast.Kind==2?Tint(255,146,65):Tint(255,216,123)):Tint(179,139,98);
                Effect(point+new V2(1.5f,-4),1,1,0,tint,z,kind:4,phase:radius);
                if(blast.Mortar&&age<.28f)
                {
                    int flash=(int)(28*(1-age/.35f));
                    Effect(point-new V2(0,8),flash*2,Math.Max(4,flash/2)*2,0,Tint(255,240,183),z,kind:1);
                    Effect(point-new V2(0,8),flash,Math.Max(3,flash/3)*2,0,Tint(255,255,240),z,kind:1);
                }
                if(age<.2f)
                {
                    Effect(point-new V2(0,6),22,16,0,Tint(250,222,144),z,kind:1);
                    Effect(point-new V2(0,10.5f),4,23,0,Tint(255,243,190),z);
                }
            }
            SmokePuffs=0;
            if(!DisableSmoke)foreach(var smoke in battle.Smoke)
            {
                if(!battle.Fog.SmokeVisible(battle,smoke))continue;
                V2 point=EffectPoint(battle,smoke.Position);
                float fade=Util.Clamp((smoke.Until-battle.Time)/3,0,1)*Util.Clamp((battle.Time-smoke.Born)/1.8f,0,1);
                int phase=(int)(battle.Time*2);
                for(int i=0;i<5;i++)
                {
                    int before=usedEffectNodes;
                    Effect(new V2((int)point.X+(i-2)*10+1,(int)point.Y-9-i%2*6+1),46,30,0,i%2==0?Tint(161,170,164):Tint(132,145,141),EffectZ(smoke.Position,.12f),kind:2,phase:((i*3+phase)%7+7)%7,density:(int)(4*fade));
                    if(usedEffectNodes>before)SmokePuffs++;
                }
            }
            for(int i=usedEffectNodes;i<effectNodes.Count;i++)if(effectNodes[i].Active){effectNodes[i].Object.SetActive(false);effectNodes[i].Active=false;}
            while(effectNodes.Count>usedEffectNodes&&effectFrame-effectNodes[effectNodes.Count-1].LastFrame>120)
            {UnityEngine.Object.Destroy(effectNodes[effectNodes.Count-1].Object);effectNodes.RemoveAt(effectNodes.Count-1);}
            expiredProjectiles.Clear();foreach(var p in projectileOrigins.Keys)if(!battle.Projectiles.Contains(p))expiredProjectiles.Add(p);
            foreach(var p in expiredProjectiles)projectileOrigins.Remove(p);
            NativeEffectNodes=usedEffectNodes;NativeEffectPoolSize=effectNodes.Count;effectsBattle=battle;
        }
        void DrawNativeCraters(BattleSimulation battle)
        {
            if(battle.Craters.Count==0)return;
            if(nativeCraterTexture==null)nativeCraterTexture=Resources.Load<Texture2D>("Effects/terrain-crater");
            if(nativeCraterTexture==null)throw new InvalidOperationException("Native crater texture unavailable");
            if(nativeCraterMaterial==null){nativeCraterMaterial=new Material(effectMaterial){name="Native crater instances",enableInstancing=true};nativeCraterMaterial.SetTexture("_MainTex",nativeCraterTexture);}
            nativeCraterMaterial.SetTexture("_Occlusion",unityMask);
            foreach(var crater in battle.Craters)
            {
                if(!battle.Fog.Revealed(crater.Position))continue;
                float width=crater.Cells*22,height=crater.Cells*10,z=EffectZ(crater.Position,.015f);
                // Source texture has conventional Unity bottom-up UVs and straight alpha.
                Effect(EffectPoint(battle,crater.Position),width,height,0,Color.white,z-height/10,
                    kind:5,endDepth:z+height/10,texture:nativeCraterTexture,
                    uv:crater.Mirror?new Vector4(1,1,-1,-1):new Vector4(0,1,1,-1),order:nativeCraterOrder);
            }
        }
        void DrawNativeProjectile(BattleSimulation battle,ArcProjectile projectile)
        {
            if(!battle.Fog.Revealed(projectile.Position))return;
            V2 foot=EffectPoint(battle,projectile.Position),from=EffectPoint(battle,projectile.From),offset;
            float progress=Util.Clamp((battle.Time-projectile.Born)/projectile.Duration,0,1),z=EffectZ(projectile.Position,.12f);
            if(projectile.HasMuzzleOffset)offset=projectile.MuzzleOffset;
            else if(!projectileOrigins.TryGetValue(projectile,out offset))
            {
                offset=new V2(0,-11);
                if(projectile.ShooterId>=0&&projectile.ShooterId<battle.Soldiers.Count)
                {
                    var shooter=battle.Soldiers[projectile.ShooterId];
                    BattleVisualRecipes.WeaponPose(battle,shooter,projectile.Kind>=3,out var center,out var muzzle,out var aim,out var weapon,out var left);
                    offset=((projectile.Kind>=3)?muzzle:projectile.Kind==2?muzzle+new V2(0,3):center)-EffectPoint(battle,shooter.Position);offset*=.85f;
                }
                projectileOrigins.Add(projectile,offset); // View state only: never mutate simulation projectile.
            }
            V2 point=projectile.Kind>=3?foot+offset*(1-progress)+new V2(0,-9*progress):foot+offset*(1-progress)+new V2(0,-(projectile.Height-(projectile.Kind==2?1.55f:1.35f)*(1-progress))*9);
            if(projectile.Landed)point=foot;
            Effect(foot,6,2,0,Tint(40,48,39),EffectZ(projectile.Position,-.2f),kind:1);
            if(projectile.Kind>=3)
            {
                V2 direction=(EffectPoint(battle,projectile.To)+new V2(0,-9)-(from+offset)).Normalized;
                EffectLine(point-direction*10,point-direction*4,Tint(236,153,73),z,z);
                EffectLine(point-direction*4,point,Tint(248,226,173),z,z);
                Effect(point,1,1,0,Tint(255,247,205),z);return;
            }
            bool green=projectile.ShooterId>=0&&projectile.ShooterId<battle.Soldiers.Count&&battle.Soldiers[projectile.ShooterId].Side==Side.Green;
            if(green&&(projectile.Kind==0||projectile.Kind==1&&HandGrenades.Type(projectile)>=3))
                EffectSprite(projectile.Kind==0?6825:6826+HandGrenades.Type(projectile)-3,point,projectile.Kind==0?3:4,6,z);
            else if(projectile.Kind==2&&green)EffectSprite(Rubezh.Graphics.VogSprite,point,3,6,z);
            else if(projectile.Kind==1)EffectSprite(Rubezh.Graphics.GrenadeSprite,point,7,5,z);
            else {Effect(point,6,4,0,projectile.Kind==2?Tint(239,197,97):Tint(148,172,94),z,kind:1);Effect(point+new V2(1,-1),1,1,0,Tint(252,235,175),z);}
        }
        void DisposeNativeEffects()
        {
            foreach(var node in effectNodes)UnityEngine.Object.Destroy(node.Object);effectNodes.Clear();
            projectileOrigins.Clear();expiredProjectiles.Clear();
            if(effectMaterial!=null)UnityEngine.Object.Destroy(effectMaterial);if(effectQuad!=null)UnityEngine.Object.Destroy(effectQuad);if(blastRing!=null)UnityEngine.Object.Destroy(blastRing);blastRing=null;
            if(nativeCraterMaterial!=null)UnityEngine.Object.Destroy(nativeCraterMaterial);nativeCraterMaterial=null;
            if(nativeCraterTexture!=null)Resources.UnloadAsset(nativeCraterTexture);
            effectsBattle=null;nativeCraterTexture=null;effectMaterial=null;effectQuad=null;NativeEffectNodes=NativeEffectPoolSize=0;
        }
#endif
    }
}
