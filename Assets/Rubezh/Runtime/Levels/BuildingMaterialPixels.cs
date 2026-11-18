using System;
using UnityEngine;
namespace Rubezh.UnityPort
{
    // Encoded PNG/JPEG bytes avoid shipping an uncompressed Unity texture per material.
    public static class BuildingMaterialPixels
    {
        public sealed class Data { public int width,height; public Color32[] pixels; }
        public static Data Read(string id)
        {
            if(id==null||id.Length!=24)throw new ArgumentException("Invalid building material ID");
            foreach(char c in id)if(!Uri.IsHexDigit(c))throw new ArgumentException("Invalid building material ID");
            #if UNITY_EDITOR
            var asset=UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Rubezh/Editor/LegacySources/BuildingMaterialData/"+id+".bytes");
#elif UNITY_5_3_OR_NEWER
            throw new InvalidOperationException("Building material pixels are Editor-only; prepare native content.");
#else
            var asset=Resources.Load<TextAsset>("BuildingMaterialData/"+id);
#endif
#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
            if(asset==null)throw new InvalidOperationException("Missing packed building material: "+id);
            Texture2D texture=null;
            try
            {
                texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                if(!ImageConversion.LoadImage(texture,asset.bytes))throw new InvalidOperationException("Invalid building material image: "+id);
                return new Data{width=texture.width,height=texture.height,pixels=texture.GetPixels32()};
            }
            finally
            {
                #if !UNITY_EDITOR
                Resources.UnloadAsset(asset);
#endif
                if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);
            }
#endif
        }
    }
}
