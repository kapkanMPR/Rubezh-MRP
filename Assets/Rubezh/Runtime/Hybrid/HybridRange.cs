#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TextCore.Text;
using UnityEngine.TextCore.LowLevel;
using Object=UnityEngine.Object;

namespace Rubezh.UnityPort.Hybrid
{
    // A self-contained visual experiment. No campaign simulation, rewards, save edits,
    // screen-space baked geometry, or battle-renderer cache participates in this view.
    internal sealed class HybridRange : IDisposable
    {
        const int Layer=27;
        const float Pitch=35.2643897f;
        static readonly Quaternion ViewRotation=Quaternion.Euler(Pitch,225,0);
        static readonly Vector3 Right=new Vector3(-.70710678f,0,.70710678f);
        static readonly Vector3 GroundUp=new Vector3(-1.22474487f,0,-1.22474487f);
        readonly Action exit;
        readonly Func<string,bool> export;
        readonly List<GameObject> roofs=new List<GameObject>();
        readonly List<Mesh> meshes=new List<Mesh>();
        readonly Dictionary<int,Vector2> pointers=new Dictionary<int,Vector2>();
        readonly List<Sample> samples=new List<Sample>();
        GameObject root;
        Camera camera;
        RenderTexture target;
        HybridMaterials materials;
        HybridActors actors;
        HybridWallState wall;
        Mesh wallMesh;
        MeshFilter wallFilter;
        Material actorMaterial;
        UIDocument document;
        PanelSettings panel;
        FontAsset font;
        Image view;
        VisualElement toolbar;
        Label stats,hint;
        Button roofButton,motionButton,cameraButton,benchButton;
        readonly List<Button> lockedButtons=new List<Button>();
        Vector3 center=new Vector3(18,0,12);
        float size=15,actorTime,clock,statusAt,frameTotal,renderTotal,autoTime;
        int frameCount,staticTriangles,staticRenderers,wallBuilds,holes;
        bool roofsVisible=true,moving=true,autoCamera,suspended,disposed;
        bool benchmark;
        float benchmarkTime;
        Vector3 savedCenter;
        float savedSize;
        int savedCount;
        bool savedMotion,savedAuto;
        string error;
        string lastBenchmarkReport;
        int droppedSamples;

        [Serializable] public sealed class Sample
        {
            public string phase;public float elapsedMs,cameraSubmissionMs;
        }
        [Serializable] sealed class Report
        {
            public string version=RubezhVersion.Current,kind="hybrid-visual-range",utc;
            public string device,gpu,graphicsApi,unity,scope;
            public int columns=36,rows=24,renderWidth,renderHeight,soldiers,staticRenderers,staticTriangles,actorTriangles,wallMeshBuilds,droppedSamples;
            public bool roofsVisible,actorsMoving,completed;
            public string[] phases=new[]{"still","pan","zoom"};
            public string timingNote="elapsedMs is presented application frame interval; cameraSubmissionMs is CPU time inside Camera.Render, not GPU time. Visual actors are not AI combat.";
            public Sample[] frames;
        }

        public HybridRange(Action exit,Func<string,bool> export)
        {
            this.exit=exit;this.export=export;
            try {BuildWorld();BuildUI();Resize();PlaceCamera();actors.Update(0,ViewRotation);camera.Render();}
            catch {Dispose();throw;}
        }

        void BuildWorld()
        {
            root=new GameObject("Рубеж · гибридный полигон 36 × 24");
            materials=new HybridMaterials();
            foreach(var part in HybridLayout.Build())
            {
                var mesh=part.Geometry.CreateMesh(part.Name);meshes.Add(mesh);
                var go=MeshObject(part.Name,mesh,materials.Get(part.Surface));
                if(part.Roof)roofs.Add(go);
                staticTriangles+=part.Geometry.Triangles.Count/3;staticRenderers++;
            }
            wall=new HybridWallState();
            var wallObject=MeshObject("Разрушаемая стена",null,materials.Get(HybridSurface.Brick));
            wallObject.transform.localPosition=new Vector3(17,0,16.5f);
            wallFilter=wallObject.GetComponent<MeshFilter>();RebuildWall();staticRenderers++;
            actorMaterial=new Material(RequireShader("HybridRange/HybridSprite")){name="Полигон · бойцы"};
            actors=new HybridActors(root.transform,actorMaterial,Layer);actors.SetCount(10);
            var camObject=new GameObject("Полигон · камера");camObject.transform.SetParent(root.transform,false);
            camera=camObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;
            camera.cullingMask=1<<Layer;camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.09f,.115f,.11f,1);camera.nearClipPlane=.1f;camera.farClipPlane=180;
            camera.allowHDR=false;camera.allowMSAA=false;
        }
        static Shader RequireShader(string name)
        {
            var shader=Resources.Load<Shader>(name);
            if(shader==null||!shader.isSupported)throw new InvalidOperationException("Не поддерживается шейдер полигона: "+name);
            return shader;
        }
        GameObject MeshObject(string name,Mesh mesh,Material material)
        {
            var go=new GameObject(name);go.layer=Layer;go.transform.SetParent(root.transform,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows=false;return go;
        }
        void RebuildWall()
        {
            var data=wall.BuildMesh();var replacement=data.CreateMesh("Полигон · пролом");
            wallFilter.sharedMesh=replacement;
            if(wallMesh!=null){staticTriangles-=wallMesh.triangles.Length/3;Object.Destroy(wallMesh);}
            wallMesh=replacement;staticTriangles+=data.Triangles.Count/3;wallBuilds++;
        }

        void BuildUI()
        {
            var ui=new GameObject("Полигон · управление");ui.transform.SetParent(root.transform,false);
            document=ui.AddComponent<UIDocument>();panel=ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode=PanelScaleMode.ConstantPixelSize;panel.sortingOrder=305;
            panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FieldCamp/CampTheme");document.panelSettings=panel;
            var content=document.rootVisualElement;content.style.flexGrow=1;
            var sourceFont=Resources.Load<Font>("FieldCamp/Fonts/CampSans");
            if(sourceFont!=null)
            {
                font=FontAsset.CreateFontAsset(sourceFont,90,9,GlyphRenderMode.SDFAA,1024,1024,AtlasPopulationMode.Dynamic,true);
                content.style.unityFontDefinition=FontDefinition.FromSDFFont(font);
            }
            content.style.color=new Color(.91f,.92f,.86f);content.style.fontSize=18;
            view=new Image();view.scaleMode=ScaleMode.StretchToFill;Stretch(view);content.Add(view);
            view.RegisterCallback<PointerDownEvent>(e=>{
                if(benchmark)return;pointers[e.pointerId]=new Vector2(e.position.x,e.position.y);view.CapturePointer(e.pointerId);e.StopPropagation();
            });
            view.RegisterCallback<PointerMoveEvent>(e=>{
                Vector2 before;if(!pointers.TryGetValue(e.pointerId,out before)||benchmark)return;
                var now=new Vector2(e.position.x,e.position.y);
                if(pointers.Count==1)Pan(now-before);
                else foreach(var other in pointers)if(other.Key!=e.pointerId)
                {
                    float oldDistance=(before-other.Value).magnitude,newDistance=(now-other.Value).magnitude;
                    if(oldDistance>5&&newDistance>5)ZoomAt((now+other.Value)*.5f,size*oldDistance/newDistance);break;
                }
                pointers[e.pointerId]=now;e.StopPropagation();
            });
            view.RegisterCallback<PointerUpEvent>(e=>ReleasePointer(e.pointerId));
            view.RegisterCallback<PointerCancelEvent>(e=>ReleasePointer(e.pointerId));
            view.RegisterCallback<WheelEvent>(e=>{if(!benchmark){ZoomAt(e.mousePosition,size*(1+e.delta.y*.045f));e.StopPropagation();}});
            var top=new VisualElement();top.style.position=Position.Absolute;top.style.left=10;top.style.right=10;top.style.top=8;
            toolbar=top;
            top.style.backgroundColor=new Color(.045f,.065f,.065f,.94f);top.style.paddingLeft=10;top.style.paddingRight=10;top.style.paddingTop=6;top.style.paddingBottom=6;content.Add(top);
            var title=new Label("ГИБРИДНЫЙ ПОЛИГОН · 36 × 24 · демонстрационные бойцы, без боя");title.style.fontSize=19;top.Add(title);
            var bar=new VisualElement();bar.style.flexDirection=FlexDirection.Row;bar.style.flexWrap=Wrap.Wrap;top.Add(bar);
            AddButton(bar,"В лагерь",()=>exit(),false);
            AddButton(bar,"Общий вид",()=>{autoCamera=false;center=new Vector3(18,0,12);size=15;});
            AddButton(bar,"К предметам",()=>{autoCamera=false;center=new Vector3(10,0,7);size=4.5f;});
            AddButton(bar,"К стене",()=>{autoCamera=false;center=new Vector3(21,1,16.5f);size=5;});
            AddButton(bar,"−",()=>size=Mathf.Clamp(size*1.2f,3,24));
            AddButton(bar,"+",()=>size=Mathf.Clamp(size/1.2f,3,24));
            roofButton=AddButton(bar,"Крыши: вкл",()=>{roofsVisible=!roofsVisible;foreach(var roof in roofs)roof.SetActive(roofsVisible);});
            AddButton(bar,"10 бойцов",()=>actors.SetCount(10));
            AddButton(bar,"50 бойцов",()=>actors.SetCount(50));
            AddButton(bar,"100 бойцов",()=>actors.SetCount(100));
            motionButton=AddButton(bar,"Движение: вкл",()=>moving=!moving);
            cameraButton=AddButton(bar,"Облёт: выкл",()=>{autoCamera=!autoCamera;autoTime=0;});
            AddButton(bar,"Пробить стену",()=>{
                float x=2.1f+(holes%3)*1.9f;holes++;
                if(wall.Damage(x,1.05f,1.2f)>0)
                {RebuildWall();hint.text="Пролом изменил только меш этой стены. Отверстие сквозное, с внутренними гранями.";}
                else hint.text="Этот участок уже разрушен. Можно восстановить стену.";
            });
            AddButton(bar,"Восстановить",()=>{wall.Reset();holes=0;RebuildWall();});
            benchButton=AddButton(bar,"Тест 30 с",ToggleBenchmark,false);
            AddButton(bar,"Сохранить отчёт",SaveCurrentOrLastReport,true);
            stats=new Label();stats.pickingMode=PickingMode.Ignore;stats.style.fontSize=17;top.Add(stats);
            hint=new Label("Перетаскивание — камера · щипок / колесо — масштаб · «К стене» — проверка толщины и пролома");
            hint.pickingMode=PickingMode.Ignore;hint.style.position=Position.Absolute;hint.style.left=14;hint.style.right=14;hint.style.bottom=10;
            hint.style.backgroundColor=new Color(.045f,.065f,.065f,.94f);hint.style.whiteSpace=WhiteSpace.Normal;hint.style.paddingLeft=8;hint.style.paddingRight=8;content.Add(hint);
        }
        static void Stretch(VisualElement item)
        {item.style.position=Position.Absolute;item.style.left=0;item.style.top=0;item.style.right=0;item.style.bottom=0;}
        Button AddButton(VisualElement bar,string text,Action action,bool lockDuringTest=true)
        {
            var button=new Button(()=>{if(disposed)return;try{action();}catch(Exception e){error=e.Message;hint.text=error;Debug.LogException(e);}}){text=text};
            button.style.fontSize=18;button.style.height=42;button.style.minWidth=text.Length<=1?45:132;
            button.style.marginRight=4;button.style.marginTop=4;bar.Add(button);if(lockDuringTest)lockedButtons.Add(button);return button;
        }
        void ReleasePointer(int id){pointers.Remove(id);view?.ReleasePointer(id);}
        void CancelPointers(){if(view!=null)foreach(int id in pointers.Keys)view.ReleasePointer(id);pointers.Clear();}
        void Pan(Vector2 delta)
        {
            autoCamera=false;float units=2*size/Mathf.Max(1,view.worldBound.height);
            center+=Right*(-delta.x*units)+GroundUp*(delta.y*units);ClampCenter();
        }
        void ZoomAt(Vector2 point,float nextSize)
        {
            autoCamera=false;nextSize=Mathf.Clamp(nextSize,3,24);
            var rect=view.worldBound;float h=Mathf.Max(1,rect.height);
            float dx=point.x-rect.x-rect.width*.5f,dy=rect.y+rect.height*.5f-point.y;
            center+=(Right*dx+GroundUp*dy)*(2*(size-nextSize)/h);size=nextSize;ClampCenter();
        }
        void ClampCenter(){center.x=Mathf.Clamp(center.x,-2,38);center.z=Mathf.Clamp(center.z,-2,26);}
        void Resize()
        {
            var safe=Screen.safeArea;int width=Mathf.Max(1,Mathf.RoundToInt(safe.width));
            panel.scale=Mathf.Max(.5f,Mathf.Min(safe.height/720f,safe.width/1280f));
            float topInset=Mathf.Max(140,toolbar.worldBound.height)+18;
            view.style.top=topInset;view.style.bottom=50;
            int height=Mathf.Max(1,Mathf.RoundToInt(safe.height-(topInset+50)*panel.scale));
            var style=document.rootVisualElement.style;
            style.marginLeft=safe.x/panel.scale;style.marginRight=(Screen.width-safe.xMax)/panel.scale;
            style.marginTop=(Screen.height-safe.yMax)/panel.scale;style.marginBottom=safe.y/panel.scale;
            if(target!=null&&target.width==width&&target.height==height&&target.IsCreated())return;
            if(benchmark){FinishBenchmark(false);hint.text="Тест остановлен: изменилось разрешение окна.";}
            if(target!=null){camera.targetTexture=null;view.image=null;target.Release();Object.Destroy(target);}
            target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Default){name="Полигон · полный размер экрана",filterMode=FilterMode.Bilinear,antiAliasing=1};
            if(!target.Create())throw new InvalidOperationException("Не создана текстура полигона "+width+"×"+height);
            camera.targetTexture=target;camera.aspect=(float)width/height;view.image=target;
        }
        void PlaceCamera()
        {
            camera.transform.rotation=ViewRotation;camera.transform.position=center-ViewRotation*new Vector3(0,0,60);
            camera.orthographicSize=size;
        }
        public void Tick(float delta)
        {
            if(disposed||suspended||error!=null)return;
            try
            {
                Resize();clock+=delta;
                if(benchmark)StepBenchmark(delta);
                else if(autoCamera){autoTime+=delta;center=new Vector3(18+Mathf.Sin(autoTime*.33f)*7,0,12+Mathf.Cos(autoTime*.25f)*4);}
                if(moving)actorTime+=Mathf.Min(delta,.1f);
                actors.Update(actorTime,ViewRotation);PlaceCamera();
                long began=System.Diagnostics.Stopwatch.GetTimestamp();camera.Render();
                float cameraMs=(float)((System.Diagnostics.Stopwatch.GetTimestamp()-began)*1000.0/System.Diagnostics.Stopwatch.Frequency);
                frameCount++;frameTotal+=delta;renderTotal+=cameraMs;
                if(benchmark&&benchmarkTime>=0)
                {
                    if(samples.Count<8192)samples.Add(new Sample{phase=Phase(benchmarkTime),elapsedMs=delta*1000,cameraSubmissionMs=cameraMs});
                    else droppedSamples++;
                }
                if(clock>=statusAt)
                {
                    statusAt=clock+.4f;
                    stats.text=(frameTotal>0?(frameCount/frameTotal).ToString("F1"):"…")+" FPS · Camera.Render CPU "+(renderTotal/Mathf.Max(1,frameCount)).ToString("F2")+" мс · "+target.width+" × "+target.height+" · бойцов "+actors.Count+" · статичных мешей "+staticRenderers+" · перестроений стены "+wallBuilds;
                    frameCount=0;frameTotal=renderTotal=0;
                    roofButton.text=roofsVisible?"Крыши: вкл":"Крыши: выкл";motionButton.text=moving?"Движение: вкл":"Движение: выкл";cameraButton.text=autoCamera?"Облёт: вкл":"Облёт: выкл";
                    benchButton.text=benchmark?(benchmarkTime<0?"Прогрев…":"Стоп · "+Mathf.CeilToInt(30-benchmarkTime)+" с"):"Тест 30 с";
                }
                if(benchmark&&benchmarkTime>=30)FinishBenchmark(true);
            }
            catch(Exception e){error=e.Message;FinishBenchmark(false);hint.text="Ошибка полигона: "+error+". Можно выйти в лагерь.";Debug.LogException(e);}
        }
        static string Phase(float time)=>time<10?"still":time<20?"pan":"zoom";
        void ToggleBenchmark()
        {
            if(benchmark){FinishBenchmark(false);hint.text="Тест остановлен.";return;}
            savedCenter=center;savedSize=size;savedCount=actors.Count;savedMotion=moving;savedAuto=autoCamera;
            samples.Clear();droppedSamples=0;lastBenchmarkReport=null;CancelPointers();benchmarkTime=-1;benchmark=true;autoCamera=false;moving=true;actors.SetCount(100);
            foreach(var button in lockedButtons)button.SetEnabled(false);
            hint.text="Прогрев 1 с, затем 30 секунд: неподвижная камера → движение → масштаб. 100 демонстрационных бойцов. Отчёт сохранится автоматически.";
        }
        void StepBenchmark(float delta)
        {
            benchmarkTime+=delta;center=new Vector3(18,0,12);size=15;
            if(benchmarkTime>=10&&benchmarkTime<20)center=new Vector3(18+Mathf.Sin((benchmarkTime-10)*.6283185f)*7,0,12+Mathf.Sin((benchmarkTime-10)*1.256637f)*3);
            if(benchmarkTime>=20)size=15-9*Mathf.Sin(Mathf.Min(10,benchmarkTime-20)*.3141593f);
        }
        void FinishBenchmark(bool completed)
        {
            if(!benchmark)return;
            // Capture the tested state before restoring the user's view/count.
            if(completed)SaveReport(true);
            benchmark=false;center=savedCenter;size=savedSize;moving=savedMotion;autoCamera=savedAuto;actors.SetCount(savedCount);
            foreach(var button in lockedButtons)button.SetEnabled(true);
        }
        void SaveReport(bool completed)
        {
            var report=new Report{utc=DateTime.UtcNow.ToString("o"),device=SystemInfo.deviceModel,gpu=SystemInfo.graphicsDeviceName,
                graphicsApi=SystemInfo.graphicsDeviceType.ToString(),unity=Application.unityVersion,scope=completed?"30-second automatic camera test":"manual snapshot; frame samples are from the most recent automatic test",
                renderWidth=target.width,renderHeight=target.height,soldiers=actors.Count,staticRenderers=staticRenderers,staticTriangles=staticTriangles,
                actorTriangles=actors.TriangleCount,wallMeshBuilds=wallBuilds,droppedSamples=droppedSamples,roofsVisible=roofsVisible,actorsMoving=moving,completed=completed,frames=completed?samples.ToArray():Array.Empty<Sample>()};
            string json=JsonUtility.ToJson(report,false);if(completed)lastBenchmarkReport=json;
            ExportReport(json);
        }
        void ExportReport(string json)
        {
            bool saved=export(json);hint.text=saved?"Отчёт полигона сохранён.":"Не удалось сохранить отчёт. Повтори сохранение.";
        }
        void SaveCurrentOrLastReport(){if(lastBenchmarkReport!=null)ExportReport(lastBenchmarkReport);else SaveReport(false);}
        public void Suspend(bool value)
        {
            suspended=value;if(value){CancelPointers();if(benchmark){FinishBenchmark(false);hint.text="Тест остановлен: приложение потеряло фокус.";}}
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;CancelPointers();
            if(camera!=null){camera.enabled=false;camera.targetTexture=null;}
            if(root!=null)root.SetActive(false);
            if(view!=null)view.image=null;
            if(target!=null){target.Release();Object.Destroy(target);target=null;}
            actors?.Dispose();materials?.Dispose();
            if(actorMaterial!=null)Object.Destroy(actorMaterial);
            foreach(var mesh in meshes)if(mesh!=null)Object.Destroy(mesh);meshes.Clear();
            if(wallMesh!=null)Object.Destroy(wallMesh);
            if(font!=null){foreach(var tex in font.atlasTextures)if(tex!=null)Object.Destroy(tex);if(font.material!=null)Object.Destroy(font.material);Object.Destroy(font);}
            if(panel!=null)Object.Destroy(panel);if(root!=null)Object.Destroy(root);
        }
    }
}
#endif
