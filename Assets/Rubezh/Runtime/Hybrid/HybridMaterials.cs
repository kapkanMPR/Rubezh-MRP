using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubezh.UnityPort.Hybrid
{
    /// <summary>Eight shared opaque materials for the isolated 3D range.</summary>
    /// <remarks>Only resources in HybridRange are owned here. Existing battle assets
    /// and the application's lighting configuration are never changed.</remarks>
    public sealed class HybridMaterials : IDisposable
    {
        readonly Dictionary<HybridSurface, Material> materials = new Dictionary<HybridSurface, Material>();
        readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        Material template;
        bool disposed;

        public HybridMaterials()
        {
            try
            {
                template = Resources.Load<Material>("HybridRange/SurfaceTemplate");
                if (template == null || template.shader == null)
                    throw new InvalidOperationException(
                        "Не найден материал гибридного полигона: HybridRange/SurfaceTemplate"
                    );
                if (!template.shader.isSupported)
                    throw new InvalidOperationException(
                        "Устройство не поддерживает материал поверхностей гибридного полигона."
                    );
                Create(HybridSurface.Ground, "surface-rough", new Color(.66f, .73f, .58f, 1), .24f, .24f);
                Create(HybridSurface.Road, "surface-rough", new Color(.69f, .67f, .61f, 1), .45f, .45f);
                Create(HybridSurface.Concrete, "surface-rough", new Color(1.08f, 1.08f, 1.04f, 1), .6f, .6f);
                Create(
                    HybridSurface.Brick,
                    "surface-brick",
                    new Color(1.08f, 1.05f, 1.02f, 1),
                    .55f,
                    .36f,
                    new Vector4(0, .055f, 1, .89f)
                );
                Create(HybridSurface.Wood, "surface-wood", new Color(1.05f, 1.02f, .98f, 1), .65f, .5f);
                Create(HybridSurface.Metal, "surface-rough", new Color(.57f, .64f, .60f, 1), 1.6f, 1.6f);
                Create(HybridSurface.Roof, "surface-roof", new Color(.93f, .98f, 1f, 1), .45f, .3f);
                // Opaque smoked glazing is optional decoration, never used to fill test apertures.
                Create(HybridSurface.Glass, "surface-rough", new Color(.23f, .31f, .34f, 1), .25f, .25f);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        void Create(HybridSurface surface, string textureName, Color color, float tileX, float tileY)
        {
            Create(surface, textureName, color, tileX, tileY, new Vector4(0, 0, 1, 1));
        }

        void Create(HybridSurface surface, string textureName, Color color, float tileX, float tileY, Vector4 region)
        {
            Texture2D texture;
            if (!textures.TryGetValue(textureName, out texture))
            {
                texture = Resources.Load<Texture2D>("HybridRange/" + textureName);
                if (texture == null)
                    throw new InvalidOperationException("Не найдена поверхность гибридного полигона: " + textureName);
                textures.Add(textureName, texture);
            }
            // Register immediately so an exception in property setup cannot leak a material.
            var material = new Material(template) { name = "Hybrid · " + surface };
            materials.Add(surface, material);
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", color);
            material.SetVector("_TileScale", new Vector4(tileX, tileY, 0, 0));
            material.SetVector("_UVRect", region);
        }

        public Material Get(HybridSurface surface)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(HybridMaterials));
            Material material;
            if (!materials.TryGetValue(surface, out material))
                throw new ArgumentOutOfRangeException(nameof(surface));
            return material;
        }

        public int MaterialCount => materials.Count;
        public int TextureCount => textures.Count;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            foreach (var material in materials.Values)
                if (material != null)
                    UnityEngine.Object.Destroy(material);
            materials.Clear();
            foreach (var texture in textures.Values)
                if (texture != null)
                    Resources.UnloadAsset(texture);
            textures.Clear();
            if (template != null)
                Resources.UnloadAsset(template);
            template = null;
        }
    }
}
