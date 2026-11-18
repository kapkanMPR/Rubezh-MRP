using System;
using System.Collections.Generic;
using UnityEngine;
namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        // Native actor parts share immutable unit quads. A pose changes Transform and
        // shader parameters, never repacks the sprite into the dynamic vertex buffer.
        struct NativeSpritePose
        {
            public int At;
            public NativeSpriteMesh Geometry;
            public Vector3 Position, Scale;
            public float Angle;
            public Vector4 Data;
        }
        sealed class NativeSpriteMesh
        {
            public Mesh Mesh;
            public int[] Pages;
        }
        bool capturingNativeActors;
        readonly Dictionary<long, NativeSpriteMesh> nativeSpriteMeshes = new Dictionary<long, NativeSpriteMesh>();
        readonly List<NativeSpritePose> nativeSpritePoses = new List<NativeSpritePose>(256);
        public int NativeSpriteInstances => directFrame ? nativeSpriteCount : nativeSpritePoses.Count;
        public int NativeSpriteMeshes => nativeSpriteMeshes.Count;

        bool RetainNativeSprite(int id, int level, Region region, float x, float y,
            float width, float height, float angle, float depth, bool test)
        {
            if (!UnityOwnedScene || !capturingNativeActors || (!nativeDynamic && visualId < 0)) return false;
            long key = ((long)id << 32) | (uint)level;
            NativeSpriteMesh geometry;
            if (!nativeSpriteMeshes.TryGetValue(key, out geometry))
            {
                var texture = atlases[region.Page];
                float u0 = (float)region.X / texture.width, v0 = (float)region.Y / texture.height;
                float u1 = (float)(region.X + region.W) / texture.width, v1 = (float)(region.Y + region.H) / texture.height;
                var quad = new Mesh { name = "Actor sprite " + id };
                quad.vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0), new Vector3(.5f,.5f,0), new Vector3(-.5f,.5f,0) };
                quad.uv = new[] { new Vector2(u0,v0), new Vector2(u1,v0), new Vector2(u1,v1), new Vector2(u0,v1) };
                quad.colors32 = new[] { new Color32(255,255,255,255), new Color32(255,255,255,255), new Color32(255,255,255,255), new Color32(255,255,255,255) };
                quad.triangles = new[] { 0,1,2,0,2,3 };
                quad.RecalculateBounds();
#if UNITY_5_3_OR_NEWER
                quad.UploadMeshData(true); // Immutable sprite geometry needs no CPU-readable copy.
#endif
                geometry = new NativeSpriteMesh { Mesh = quad, Pages = new[] { region.Page } };
                nativeSpriteMeshes.Add(key, geometry);
            }
            var pose = new NativeSpritePose {
                At=indices.Count, Geometry=geometry,
                Position=new Vector3(x,y,0), Scale=new Vector3(width,height,1),
                Angle=angle*180f/(float)Math.PI,
                Data=new Vector4(depth,depth<-.5f?-1:test?1:0,canvas.LayerLeft,canvas.LayerRight)
            };
            if (nativeDynamic) AddDirectSprite(pose, depth, test); else nativeSpritePoses.Add(pose);
            return true;
        }
        void SubmitNativeSprite(NativeSpritePose pose)
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            AddUnityMesh(pose.Geometry.Mesh, pose.Geometry.Pages, "Actor sprite", 28,
                pose.Position, pose.Scale, pose.Angle, pose.Data);
#endif
        }
        void DisposeNativeSprites()
        {
            foreach (var item in nativeSpriteMeshes.Values) UnityEngine.Object.Destroy(item.Mesh);
            nativeSpriteMeshes.Clear();nativeSpritePoses.Clear();
        }
    }
}
