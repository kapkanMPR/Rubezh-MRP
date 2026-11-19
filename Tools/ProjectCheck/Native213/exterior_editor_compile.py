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
out = root / "Build/Reports/AI-0.12.213/ExteriorEditorCompile"
out.mkdir(parents=True, exist_ok=True)
dotnet, sdk, ref, _ = find_sdk()
(out / "Platform.cs").write_text(
    (src / "Contracts/PlatformContract.cs")
    .read_text()
    .split("namespace UnityEditor")[0]
)
(out / "NativeWallMaterialPreparation.cs").write_text(
    (root / "Assets/Rubezh/Editor/NativeWallMaterialPreparation.cs")
    .read_text()
    .replace("#if UNITY_EDITOR", "#if RUBEZH_NATIVE_TEST_CONTRACT", 1)
)
(out / "NativeExteriorPreparation.cs").write_text(
    (root / "Assets/Rubezh/Editor/NativeExteriorPreparation.cs")
    .read_text()
    .replace("#if UNITY_EDITOR", "#if RUBEZH_NATIVE_TEST_CONTRACT", 1)
)
(out / "NativeUiPreparation.cs").write_text(
    (root / "Assets/Rubezh/Editor/NativeUiPreparation.cs")
    .read_text()
    .replace("#if UNITY_EDITOR", "#if RUBEZH_NATIVE_TEST_CONTRACT", 1)
)
(out / "NativeEditorContract.cs").write_text(
    (src / "Native208/NativeEditorContract.cs")
    .read_text()
    .replace(
        "public static class BuildResourcePreparation{",
        'public static class BuildResourcePreparation{public const string Packed="Assets/Rubezh/Editor/LegacySources/BuildingMaterialData";',
    )
)
prod = (
    list((root / "Assets/Rubezh/Core").glob("*.cs"))
    + list((root / "Assets/Rubezh/Camp").glob("*.cs"))
    + list((root / "Assets/Rubezh/Runtime").rglob("*.cs"))
    + [p for p in (src / "Contracts").glob("*.cs") if p.name != "PlatformContract.cs"]
    + [
        out / "Platform.cs",
        out / "NativeWallMaterialPreparation.cs",
        out / "NativeUiPreparation.cs",
        out / "NativeExteriorPreparation.cs",
        src / "Native210/NativeAssetsContract.cs",
        src / "Native208/EditorContract.cs",
        out / "NativeEditorContract.cs",
    ]
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
print("Editor exterior helper compile:", r.returncode)
(out / "result.json").write_text(
    json.dumps(
        {
            "passed": r.returncode == 0,
            "scope": "NativeExteriorPreparation + UI/wall preparation production source + Runtime/Core/Camp with existing Unity Editor contracts",
            "unityEditorExecuted": False,
        },
        indent=2,
    )
)
raise SystemExit(r.returncode)
