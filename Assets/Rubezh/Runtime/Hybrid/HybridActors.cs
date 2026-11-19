#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rubezh.UnityPort
{
    /// <summary>
    /// Visual test mannequins using the exact existing battlefield head+torso art.
    /// This is not combat simulation. One dynamic mesh/material serves all actors.
    /// Vertical billboards keep their depth at the actor's actual world position.
    /// </summary>
    internal sealed class HybridActors : IDisposable
    {
        [Serializable]
        public sealed class AtlasFrame
        {
            public int x,
                y,
                w,
                h,
                anchorX,
                anchorY;
        }

        [Serializable]
        public sealed class AtlasData
        {
            public int width,
                height;
            public float pixelsPerBodyHeight;
            public AtlasFrame[] frames;
        }

        sealed class DepthOrder : IComparer<int>
        {
            public float[] Depth;

            public int Compare(int a, int b)
            {
                int value = Depth[b].CompareTo(Depth[a]);
                return value == 0 ? a.CompareTo(b) : value;
            }
        }

        readonly GameObject root;
        readonly Mesh mesh;
        readonly Texture2D atlas;
        readonly AtlasData data;
        readonly DepthOrder depthOrder = new DepthOrder();
        readonly Material material;
        Vector3[] positions = Array.Empty<Vector3>(),
            facing = Array.Empty<Vector3>();
        Vector3[] vertices = Array.Empty<Vector3>(),
            normals = Array.Empty<Vector3>();
        Vector2[] uv = Array.Empty<Vector2>();
        Color32[] colors = Array.Empty<Color32>();
        int[] triangles = Array.Empty<int>(),
            order = Array.Empty<int>();
        float[] depths = Array.Empty<float>();
        bool disposed,
            hasFrame;
        float lastTime;
        Vector3 lastRight,
            lastForward;

        public int Count { get; private set; }
        public int TriangleCount => Count * 2;
        public int MeshUpdates { get; private set; }
        public long TextureBytes => (long)data.width * data.height * 4 * 4 / 3;

        public Vector3 Position(int index) => positions[index];

        public HybridActors(Transform parent, Material spriteMaterial, int layer)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (spriteMaterial == null)
                throw new ArgumentNullException(nameof(spriteMaterial));
            material = spriteMaterial;
            TextAsset metadata = null;
            try
            {
                atlas = Resources.Load<Texture2D>("HybridRange/actors");
                metadata = Resources.Load<TextAsset>("HybridRange/actors-layout");
                if (atlas == null || metadata == null)
                    throw new InvalidOperationException("Не найден атлас бойцов гибридного полигона.");
                data = JsonUtility.FromJson<AtlasData>(metadata.text);
                if (
                    data == null
                    || data.frames == null
                    || data.frames.Length != 8
                    || data.width != atlas.width
                    || data.height != atlas.height
                    || !(data.pixelsPerBodyHeight > 0)
                )
                    throw new InvalidOperationException("Повреждён атлас бойцов гибридного полигона.");
                foreach (var f in data.frames)
                    if (
                        f == null
                        || f.w < 1
                        || f.h < 1
                        || f.x < 0
                        || f.y < 0
                        || f.x + f.w > data.width
                        || f.y + f.h > data.height
                    )
                        throw new InvalidOperationException("Кадр бойца выходит за границы атласа.");
                material.mainTexture = atlas;
                root = new GameObject("Hybrid · demonstration soldiers");
                root.layer = layer;
                root.transform.SetParent(parent, false);
                mesh = new Mesh { name = "Hybrid · shared soldier billboards" };
                mesh.MarkDynamic();
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                SetCount(10);
            }
            catch
            {
                Dispose();
                throw;
            }
            finally
            {
                if (metadata != null)
                    Resources.UnloadAsset(metadata);
            }
        }

        public void SetCount(int count)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(HybridActors));
            count = Math.Max(0, Math.Min(100, count));
            if (count == Count && positions.Length == count)
                return;
            Count = count;
            hasFrame = false;
            positions = new Vector3[count];
            facing = new Vector3[count];
            vertices = new Vector3[count * 4];
            normals = new Vector3[count * 4];
            uv = new Vector2[count * 4];
            colors = new Color32[count * 4];
            triangles = new int[count * 6];
            order = new int[count];
            depths = new float[count];
            depthOrder.Depth = depths;
            for (int i = 0; i < count; i++)
            {
                order[i] = i;
                int v = i * 4,
                    t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 2;
                triangles[t + 2] = v + 1;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 3;
                triangles[t + 5] = v + 2;
                for (int n = 0; n < 4; n++)
                    colors[v + n] = new Color32(255, 255, 255, 255);
            }
            // Clear old indices before replacing a possibly smaller vertex buffer.
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.colors32 = colors;
            mesh.normals = normals;
            mesh.triangles = triangles;
        }

        /// <summary>
        /// Pure deterministic route for renderer/occlusion validation. Actor zero
        /// goes through the masonry building's real north doorway at x=27.5.
        /// Actor one walks behind the destructible wall, visible through its breach.
        /// Other actors patrol the unobstructed road. No navigation/AI claims.
        /// </summary>
        public static void SampleRoute(int id, int count, float time, out Vector3 position, out Vector3 direction)
        {
            time = Math.Max(0, time);
            if (id == 0)
            {
                const float near = 12.15f,
                    far = 7.10f,
                    distance = near - far;
                float p = time * .82f % (distance * 2);
                bool inward = p < distance;
                position = new Vector3(27.5f, .035f, inward ? near - p : far + p - distance);
                direction = new Vector3(0, 0, inward ? -1 : 1);
                return;
            }
            if (id == 1)
            {
                const float first = 17.7f,
                    last = 23.2f,
                    distance = last - first;
                float p = time * .9f % (distance * 2);
                bool rightward = p < distance;
                position = new Vector3(rightward ? first + p : last - p + distance, .035f, 15.6f);
                direction = new Vector3(rightward ? 1 : -1, 0, 0);
                return;
            }
            const float left = 2.2f,
                right = 33.8f,
                south = 10.85f,
                north = 12.9f;
            const float width = right - left,
                depth = north - south,
                perimeter = 2 * (width + depth);
            float offset = (id - 2) * perimeter / Math.Max(1, count - 2);
            float along = (offset + time * .9f) % perimeter;
            if (along < width)
            {
                position = new Vector3(left + along, .035f, south);
                direction = new Vector3(1, 0, 0);
            }
            else if ((along -= width) < depth)
            {
                position = new Vector3(right, .035f, south + along);
                direction = new Vector3(0, 0, 1);
            }
            else if ((along -= depth) < width)
            {
                position = new Vector3(right - along, .035f, north);
                direction = new Vector3(-1, 0, 0);
            }
            else
            {
                along -= width;
                position = new Vector3(left, .035f, north - along);
                direction = new Vector3(0, 0, -1);
            }
        }

        public void Update(float time, Quaternion cameraRotation)
        {
            if (disposed || Count == 0)
                return;
            // Caller supplies accumulated movement time, so pause/resume never jumps.
            var cameraRight = cameraRotation * new Vector3(1, 0, 0);
            cameraRight.y = 0;
            float rightLength = (float)Math.Sqrt(cameraRight.x * cameraRight.x + cameraRight.z * cameraRight.z);
            if (rightLength < .001f)
                cameraRight = new Vector3(1, 0, 0);
            else
                cameraRight = cameraRight / rightLength;
            var cameraForward = cameraRotation * new Vector3(0, 0, 1);
            // Pan/zoom and paused mannequins reuse the same world geometry.
            if (hasFrame && time == lastTime && Same(cameraRight, lastRight) && Same(cameraForward, lastForward))
                return;
            lastTime = time;
            lastRight = cameraRight;
            lastForward = cameraForward;
            hasFrame = true;
            var flatForward = new Vector3(cameraForward.x, 0, cameraForward.z);
            float forwardLength = (float)Math.Sqrt(flatForward.x * flatForward.x + flatForward.z * flatForward.z);
            if (forwardLength < .001f)
                flatForward = new Vector3(0, 0, 1);
            else
                flatForward = flatForward / forwardLength;
            for (int i = 0; i < Count; i++)
            {
                SampleRoute(i, Count, time, out positions[i], out facing[i]);
                depths[i] = Dot(positions[i], cameraForward);
                order[i] = i;
            }
            Array.Sort(order, depthOrder);
            for (int slot = 0; slot < Count; slot++)
            {
                int id = order[slot];
                float horizontal = Dot(facing[id], cameraRight),
                    away = Dot(facing[id], flatForward);
                int direction = Math.Abs(horizontal) > Math.Abs(away) ? (horizontal > 0 ? 1 : 3) : (away > 0 ? 2 : 0);
                int side = id < (Count + 1) / 2 ? 0 : 1;
                AtlasFrame f = data.frames[side * 4 + direction];
                const float bodyHeight = 1.8f;
                float unitsPerPixel = bodyHeight / data.pixelsPerBodyHeight;
                var basePosition = positions[id];
                // Original 2D gun poses may extend below the old ground pivot.
                // The prototype keeps one body scale and lifts only that pose's
                // silhouette to the ground, rather than clipping authored pixels.
                float bottomOffset = Math.Max(0, -f.anchorY * unitsPerPixel);
                var left = basePosition - cameraRight * (f.anchorX * unitsPerPixel) + new Vector3(0, bottomOffset, 0);
                var span = cameraRight * (f.w * unitsPerPixel);
                var top = new Vector3(0, f.h * unitsPerPixel, 0);
                int v = slot * 4;
                vertices[v] = left;
                vertices[v + 1] = left + span;
                vertices[v + 2] = left + span + top;
                vertices[v + 3] = left + top;
                float u0 = (float)f.x / data.width,
                    u1 = (float)(f.x + f.w) / data.width;
                float v0 = (float)f.y / data.height,
                    v1 = (float)(f.y + f.h) / data.height;
                uv[v] = new Vector2(u0, v0);
                uv[v + 1] = new Vector2(u1, v0);
                uv[v + 2] = new Vector2(u1, v1);
                uv[v + 3] = new Vector2(u0, v1);
                for (int n = 0; n < 4; n++)
                    normals[v + n] = -flatForward;
            }
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            MeshUpdates++;
        }

        static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

        static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (root != null)
            {
                root.SetActive(false);
                UnityEngine.Object.Destroy(root);
            }
            if (mesh != null)
                UnityEngine.Object.Destroy(mesh);
            // Resource is prototype-only, shared by this single owner; the caller
            // owns and destroys the material, which must not retain a dead texture.
            if (material != null && material.mainTexture == atlas)
                material.mainTexture = null;
            if (atlas != null)
                Resources.UnloadAsset(atlas);
        }
    }
}
#endif
