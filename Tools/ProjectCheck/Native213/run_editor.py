"""Compile actual wall preparation with repository Unity/Editor API contracts.
Compile-only check; does not substitute for a Unity import/build run.
"""

from pathlib import Path
import subprocess
import json
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from rubezh_dotnet import find_sdk

root = Path(__file__).resolve().parents[3]
src = root / "Tools/ProjectCheck"
out = root / "Build/Reports/AI-0.12.213/EditorCompile"
out.mkdir(parents=True, exist_ok=True)
dotnet, sdk, ref, _ = find_sdk()
(out / "Platform.cs").write_text(
    (src / "Contracts/PlatformContract.cs")
    .read_text()
    .split("namespace UnityEditor")[0]
)
names = [
    "NativeWallMaterialPreparation",
    "NativeUiPreparation",
    "NativeSurfaceImport",
    "NativeExteriorPreparation",
    "NativeBuildingPreparation",
    "NativeContentPreparation",
    "NativeContentAudit",
    "NativeMapBuildScope",
    "BuildResourcePreparation",
    "UnityTest20Preparation",
    "TestMapDependencies",
]
for name in names:
    (out / (name + ".cs")).write_text(
        (root / ("Assets/Rubezh/Editor/" + name + ".cs"))
        .read_text()
        .replace("#if UNITY_EDITOR", "#if RUBEZH_NATIVE_TEST_CONTRACT", 1)
    )
editor = (src / "Native208/EditorContract.cs").read_text()
editor = editor.replace(
    "public static class EditorApplication {",
    "public static class EditorApplication {public static event Action delayCall;",
)
editor = editor.replace(
    "public static void Refresh(){}",
    "public static void Refresh(ImportAssetOptions options=0){}",
)
editor = editor.replace(
    "public static class AssetDatabase {",
    'public static class AssetDatabase {public static string[] GetAllAssetPaths()=>new string[0];public static bool IsValidFolder(string p)=>false;public static string GetAssetPath(UnityEngine.Object o)=>"";',
)
editor = editor.replace(
    "public static class EditorUtility {",
    "public static class EditorUtility {public static void DisplayProgressBar(string title,string info,float progress){}",
)
(out / "EditorContract.cs").write_text(editor)
native = (
    (src / "Native208/NativeEditorContract.cs")
    .read_text()
    .replace(
        "namespace Rubezh.UnityPort.Editor{public static class BuildResourcePreparation{public static string Hash(string p)=>p;}}",
        "",
    )
)
native = native.replace(
    "public class AddressableAssetEntry {public string address;}",
    "public class AddressableAssetEntry {public string address,guid;}",
)
native = native.replace(
    "public class AddressableAssetGroup:ScriptableObject{",
    "public class AddressableAssetGroup:ScriptableObject{public System.Collections.Generic.List<AddressableAssetEntry> entries=new System.Collections.Generic.List<AddressableAssetEntry>();",
)
native = native.replace(
    "public const string kLocalBuildPath=",
    "public void RemoveAssetEntry(string guid){}public const string kLocalBuildPath=",
)
native = native.replace(
    "public string address,guid;",
    "public string address,guid;public AddressableAssetGroup parentGroup;",
)
native = native.replace(
    "public void RemoveAssetEntry(string guid){}",
    "public System.Collections.Generic.List<AddressableAssetGroup> groups=new System.Collections.Generic.List<AddressableAssetGroup>();public AddressableAssetEntry FindAssetEntry(string guid)=>null;public void RemoveAssetEntry(string guid){}",
)
native = native.replace(
    "public class BundledAssetGroupSchema:AddressableAssetGroupSchema{",
    "public class BundledAssetGroupSchema:AddressableAssetGroupSchema{public bool IncludeInBuild,IncludeAddressInCatalog;",
)
native = native.replace(
    "public static class SpriteAtlasExtensions {",
    "public static class SpriteAtlasExtensions {public static SpriteAtlasTextureSettings GetTextureSettings(this UnityEngine.U2D.SpriteAtlas a)=>new SpriteAtlasTextureSettings();public static SpriteAtlasPackingSettings GetPackingSettings(this UnityEngine.U2D.SpriteAtlas a)=>new SpriteAtlasPackingSettings();public static UnityEditor.TextureImporterPlatformSettings GetPlatformSettings(this UnityEngine.U2D.SpriteAtlas a,string name)=>new UnityEditor.TextureImporterPlatformSettings();public static bool IsIncludeInBuild(this UnityEngine.U2D.SpriteAtlas a)=>false;",
)
(out / "NativeEditorContract.cs").write_text(native)
prod = (
    list((root / "Assets/Rubezh/Core").glob("*.cs"))
    + list((root / "Assets/Rubezh/Camp").glob("*.cs"))
    + list((root / "Assets/Rubezh/Runtime").rglob("*.cs"))
    + [p for p in (src / "Contracts").glob("*.cs") if p.name != "PlatformContract.cs"]
    + [
        out / "Platform.cs",
        src / "Native210/NativeAssetsContract.cs",
        out / "EditorContract.cs",
        out / "NativeEditorContract.cs",
        Path(__file__).parent / "Editor213Contract.cs",
    ]
    + [out / (name + ".cs") for name in names]
)
cmd = (
    [
        str(dotnet),
        str(sdk / "Roslyn/bincore/csc.dll"),
        "-nologo",
        "-langversion:latest",
        "-define:RUBEZH_DEVELOPER,RUBEZH_PICTURE_CONTRACT,RUBEZH_NATIVE_TEST_CONTRACT,RUBEZH_NATIVE_CONTENT_CONTRACT,RUBEZH_UI_BAKE",
        "-target:library",
        "-out:" + str(out / "WallEditor.dll"),
        "-r:" + str(sdk / "Newtonsoft.Json.dll"),
    ]
    + ["-r:" + str(p) for p in ref.glob("*.dll")]
    + list(map(str, prod))
)
r = subprocess.run(cmd, capture_output=True, text=True)
(out / "compile.log").write_text(r.stdout + r.stderr)
print(r.stdout + r.stderr)
print("Editor wall helper compile:", r.returncode)
(out / "result.json").write_text(
    json.dumps(
        {
            "passed": r.returncode == 0,
            "scope": "Native surface/UI/wall/exterior/building preparation production sources + Runtime/Core/Camp with existing Unity Editor contracts",
            "unityEditorExecuted": False,
        },
        indent=2,
    )
)
raise SystemExit(r.returncode)
