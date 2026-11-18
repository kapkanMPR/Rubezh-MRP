using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Rubezh.UnityPort.Editor
{
    public static class BuildBranding
    {
        public static void Apply()
        {
            // Git updates can arrive before Unity has imported the new PNG assets.
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var icon = LoadIcon("CommanderIcon.png", true);
            var foreground = LoadIcon("CommanderForeground.png", false) ?? icon;
            var background = LoadIcon("CommanderBackground.png", false);
            if (background == null)
            {
                // Persist the fallback so PlayerSettings keeps a valid asset reference.
                const string path = "Assets/Rubezh/Art/CommanderBackground.asset";
                background = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (background == null)
                {
                    background = new Texture2D(2, 2);
                    background.SetPixels(new[] { Color.black, Color.black, Color.black, Color.black });
                    background.Apply();
                    AssetDatabase.CreateAsset(background, path);
                    AssetDatabase.SaveAssets();
                }
            }

            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
#if UNITY_ANDROID
   foreach(var kind in new[]{UnityEditor.Android.AndroidPlatformIconKind.Legacy,UnityEditor.Android.AndroidPlatformIconKind.Round,UnityEditor.Android.AndroidPlatformIconKind.Adaptive}){
    var slots=PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android,kind);
    foreach(var slot in slots){
     var layers=new Texture2D[slot.maxLayerCount];
     for(int i=0;i<layers.Length;i++)layers[i]=kind==UnityEditor.Android.AndroidPlatformIconKind.Adaptive?(i==0?foreground:background):icon;
     slot.SetTextures(layers);
    }
    PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android,kind,slots);
   }
#endif
        }

        static Texture2D LoadIcon(string name, bool required)
        {
            string path = "Assets/Rubezh/Art/" + name;
            if (File.Exists(path))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null)
                    return texture;
                // Recover from a failed texture import while retaining a persistent icon asset.
                var decoded = new Texture2D(2, 2);
                if (ImageConversion.LoadImage(decoded, File.ReadAllBytes(path)))
                {
                    string fallback = Path.ChangeExtension(path, "asset");
                    var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(fallback);
                    if (existing != null)
                    {
                        Object.DestroyImmediate(decoded);
                        return existing;
                    }

                    AssetDatabase.CreateAsset(decoded, fallback);
                    AssetDatabase.SaveAssets();
                    return decoded;
                }

                Object.DestroyImmediate(decoded);
            }

            if (required)
                throw new BuildFailedException("Не удалось загрузить " + path + ". Проверьте Fetch/Pull origin и текущую ветку. Папка проекта: " + Application.dataPath);
            Debug.LogWarning("Не загружен дополнительный слой иконки: " + path + ". Используется резервный вариант.");
            return null;
        }
    }
}
