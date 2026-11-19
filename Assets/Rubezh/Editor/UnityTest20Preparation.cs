#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Rubezh.UnityPort.Editor
{
    // Prepared source assets ship with the project. This callback only checks small manifests/file lengths.
    public sealed class UnityTest20Preparation : IPreprocessBuildWithReport
    {
        public static string Folder =>
            Directory.Exists("Assets/Rubezh/Art/LegacyUnityForest")
                ? "Assets/Rubezh/Art/LegacyUnityForest"
                : "Assets/Rubezh/Resources/UnityTestForest";
        public int callbackOrder => -1500;

        [Serializable]
        public sealed class Page
        {
            public int id,
                width,
                height;
            public long bytes;
            public string sha256;
        }

        [Serializable]
        public sealed class Prepared
        {
            public int version;
            public string mapSha256,
                catalogSha256;
            public Page[] pages;
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            NativeContentPreparation.RequirePrepared();
        }

        [MenuItem("Рубеж/Тест Unity/Проверить готовые ресурсы леса")]
        public static void CheckMenu()
        {
            try
            {
                EnsurePrepared();
                Debug.Log("Лес готов: атласы уже входят в проект. Можно запускать тест или собирать APK.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public static void EnsurePrepared()
        {
            string manifest = Folder + "/prepared.json",
                catalog = Folder + "/catalog.json";
            if (!File.Exists(manifest) || !File.Exists(catalog))
                throw new BuildFailedException(
                    "Отсутствуют готовые атласы леса. Распакуйте полный архив 0.12.204. Подготовка в Unity больше не требуется."
                );
            var data = JsonUtility.FromJson<Prepared>(File.ReadAllText(manifest));
            if (data == null || data.version != 1 || data.pages == null || data.pages.Length == 0)
                throw new BuildFailedException("Повреждён манифест готовых атласов.");
            if (
                Hash("Assets/Rubezh/Resources/War/war-01.bytes") != data.mapSha256
                || Hash(catalog) != data.catalogSha256
            )
                throw new BuildFailedException(
                    "Карта или каталог отличаются от готового набора. Требуется новый экспорт ресурсов."
                );
            var ids = data.pages.Select(p => p.id).ToArray();
            if (ids.Distinct().Count() != ids.Length)
                throw new BuildFailedException("Повторные страницы атласа.");
            foreach (var page in data.pages)
            {
                string path = Folder + "/atlas-" + page.id.ToString("D3") + ".png";
                var file = new FileInfo(path);
                if (!file.Exists || file.Length != page.bytes || !File.Exists(path + ".meta"))
                    throw new BuildFailedException("Нет файла готового атласа или он повреждён: " + path);
            }
            var entries = JsonUtility.FromJson<UnityTestAtlasCatalog>(File.ReadAllText(catalog));
            if (
                entries?.entries == null
                || entries.entries.Length == 0
                || entries.entries.Any(e => !ids.Contains(e.page))
            )
                throw new BuildFailedException("Каталог ссылается на отсутствующие страницы.");
        }

        static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var file = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
