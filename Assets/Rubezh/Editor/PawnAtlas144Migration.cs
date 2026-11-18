using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Rubezh.UnityPort.Editor
{
    // A patch copied over an existing project cannot delete old files itself.
    // Remove only exact known legacy layers; preserve any locally edited art.
    [InitializeOnLoad]
    public static class PawnAtlas144Migration
    {
        static PawnAtlas144Migration() { EditorApplication.delayCall += RemoveLegacyLayers; }
        static readonly Dictionary<string, string> Legacy = new Dictionary<string, string>
        {
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-63-back.png", "102f55a19216f399e6cf4009f651f3c5550d3be4ceeaf25ee55edc435db6db63" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-136-left.png", "7886941272e24bc6338046730eba18cce18afe8e851ce99357d56db046fddf33" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-109-back.png", "87204ef398129343c1b07697b1ee443fcbcc769bdd155e1f1ac4c9055de03d9f" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-107-left.png", "28cbf5720f143600a1cafc870cf2bf3fd07ca6e20998e12921e2f82cd8b7972d" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-62-right.png", "51528cb0b1bb808f685b4b5b2a14136f4aac0b0c6d15767157ad6b5219af80b9" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-116-right.png", "0b70640f3a1906a5c527398729d13be6c37743a9505498e3337d916c5b5e9e66" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-62-left.png", "1230847f436491a6d83db7b7795a81090603bb89583dfa8c9a769e12b07773d0" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-116-back.png", "cf41f27fcdc86113e340f0eb1f9e2171e8d0a786ec171b896ce40e3370534747" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-59-back.png", "99d735610ccf1420a2699537ccfc994c3c31f41fc3f0712caf5984767654cab2" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-59-right.png", "f539549adf786d846fff9cfb98f9a97d96423b53e10c13f752dc681c784b0c9d" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-63-right.png", "b841a1da0715a159cc08e9320a956488acc5a2a6f6273b0106c9fe82b7127349" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/body-medium-89-2-right.png", "c2d6df38d9a6dd386896ebeb9a0ee355c86d6b841499b4d7db1f91ed7916e899" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-143-left.png", "bcc07c82ec69851af378ff972a25af8e15caeddfc313b043f4bf6e343071a841" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-69-left.png", "0ff9e28a51390d121c738fe9afbd2d27f040c0e52196719b9c9ff7e633d9d724" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-82-right.png", "3d6811d5191703d70dbc7e0dba12e50441aeb4964c48529fa2e4ead3f2d19090" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-57-left.png", "95a46650f28bd9ec6569dfc42796c928479b72c9f1a30470b64af6a7f16563ad" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-109-left.png", "110c557c6559c8c5dc6018f0cbc7aee468706e828a542e93e036be7fb4c187d4" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-57-back.png", "21c6b381301769648940ba575bf9dce2e087ace1c2b2b42843eff9365e798fef" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-116-left.png", "b6d7b88e61a24092d156d999b5658ff79441963fdaf7e01317fc72ceec5d5ee7" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-131-left.png", "f984ae88b0b0d430f93a65816a2e21e2b5694f00612f24cd06cd30eec6b63929" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-107-right.png", "ec1672d96ce9c0ce356e17943577aacbc990a7f79522fd9da684f5c8666440bc" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-82-left.png", "ded352c3006fd2428b94efd53fa805ec35def43c5fe376f352175e2c01d24943" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-134-right.png", "7a9060ac16bb2a420dd49cf9acf97934e53c3eee8e6bf6ce26e71c55d0e977f7" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/body-medium-89-2-back.png", "9b715021f9c51104a769d7fcb0a86cc26a2745bf17631b9254bbdc437cce185f" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-107-back.png", "ff738851ded00920523531614e501e1460ce85388a18903c386b7ce455cc93b0" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-88-back.png", "3cacb5f769332e456938ccdffc012709145b71d78053f2f359f4eb8a4d4ed936" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-131-right.png", "25b17c7d20066c3ede70a670e7ee144224f712e6e72259d8335ab04c248555bb" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-69-right.png", "d23a977f22c42f6c714d6e6fef7648a25654a51eb707f05691467ac5c31ca06e" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-136-back.png", "02445fcf55d50bd9b3b6a82e58444f6c3cb3b77b2dcea774d7223c85ac18a3f7" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-57-right.png", "91a31a7f9cd84b229410e1847dc53c1ae159e7168d341dc6e905aaf9d67bfad9" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-136-right.png", "a4cc886da732c49a25b3bf88b130d7c3763e3e1368c353b2f116b86c78204557" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-134-back.png", "24281d0109ccfa4d25b45c8f50893f390e318889b19255f3ca9a39ef2421dea4" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-62-back.png", "3c6e85b462c4bf77688b9dbef57d7dfc589023d45a4caa6fad7e5acc732b4c94" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-133-left.png", "c570aeed83874de07c891aa4f64329c17b6728ba341fca9bfce381c4b7818506" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-133-right.png", "1e96ef8e30327c4429dc78c8397ab253e44a6fca675111c9069e5f66e98c0726" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-88-right.png", "0021f71b90c9498fb2687833aab8240cdd23091caca36889587d51dd2c698bb5" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-69-back.png", "bac79d7ecc9372a0b8ececad6acc6a2d4e5bac7e99f438576af8ff7e67ae510b" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-88-left.png", "ee783ffd2053fd6a2a9a7e6b086796bde2a63a16012686cc580fbde8a539d975" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-131-back.png", "782ba383acf5085170beacd06f6105f2dd72bc4adf873dbd81bc35963a5f9939" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/body-medium-89-2-left.png", "4bd6db06cc16ea0e9740b92b6b4d90ac7e963a734e3f46ded99e31ec3cc1c4a6" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-106-right.png", "715f77fe762b8827ced254880ad75c7d2fce6c15506b732a66446256cd98de74" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-59-left.png", "c4edee5632f375051dce6baadb9b4c14bc84354b39a7694b61c2605d7753be7d" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-106-left.png", "6da9931d6eac8402ea48450c7ce3f723ced86fb8d5c11512f7568edbcd8d7803" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-143-right.png", "269dd22c72fd4d28d488d8997f81431f43f3e707762cfcf01d305f2d12b224cd" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-82-back.png", "32cce2dc4a4ee104bf44c768943f500193b68578213e8f5c1e44c46fbb11661c" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-106-back.png", "89824c338d3c64de23161da10b6b87308a58db3d8d52a2831282d2e36de1b93e" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-143-back.png", "908152e9eb57c2ea488c6a534562ba19a8dbee793012584124b273a5feae5b3f" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-63-left.png", "5ab6900f2217d1a97b9bfcae57920cc63876e1023d1183b49b5e4aa9b5b9ebb8" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-133-back.png", "f752fa7a73f50f414e96673a7a9871b5e23f4a72a563da9cae2f6363d40ed362" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-109-right.png", "1c187b0ab261303d4bb21e535eac4ce347f86bd5bc3f400f6810113a7664f395" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/body-medium-89-2-front.png", "2103b2e5b745fa9c6bc6afb3eb76b19f4573e0bd7b57c9d7866185b0c60f33f7" },
            { "Assets/Rubezh/Resources/FieldCamp/Art/appearance/head-134-left.png", "36176438098f439f14f348679ffc6b3dfecd82ed69f70fb80ce90f7745f55858" },
        };
        static void RemoveLegacyLayers()
        {
            if (SessionState.GetBool("Rubezh.PawnAtlas144.Cleaned", false)) return;
            SessionState.SetBool("Rubezh.PawnAtlas144.Cleaned", true);
            int deleted = 0;
            foreach (var item in Legacy)
            {
                if (!File.Exists(item.Key)) continue;
                string hash;
                using (var sha = SHA256.Create())
                using (var input = File.OpenRead(item.Key))
                    hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                if (hash != item.Value)
                {
                    Debug.LogWarning("Рубеж: сохранён изменённый вручную старый слой: " + item.Key);
                    continue;
                }
                if (AssetDatabase.DeleteAsset(item.Key)) deleted++;
            }
            if (deleted > 0) Debug.Log("Рубеж: удалено устаревших отдельных слоёв персонажей: " + deleted);
        }
    }
}
