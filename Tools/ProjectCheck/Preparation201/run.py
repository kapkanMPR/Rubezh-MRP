"""Portable model/resource checks. Unity API contracts do not implement native rendering."""

from pathlib import Path
import os
import sys
import json
import shutil
import subprocess
import tempfile
import yaml

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from rubezh_dotnet import find_sdk

root = Path(__file__).resolve().parents[3]
os.chdir(root)
out = root / "Validation/Current"
out.mkdir(parents=True, exist_ok=True)
subprocess.run([sys.executable, str(root / "Tools/ProjectCheck/audit.py")], check=True)
dotnet, sdk, ref, runtime = find_sdk()
maps = out / "maps"
maps.mkdir(exist_ok=True)


def normalize(v):
    if isinstance(v, list):
        return [normalize(x) for x in v]
    if isinstance(v, dict):
        return {
            {"m_X": "x", "m_Y": "y", "m_Name": "name"}.get(k, k): normalize(x)
            for k, x in v.items()
        }
    return v


for f in (root / "Assets/Rubezh/Resources/Levels").glob("Campaign_*.asset"):
    text = "\n".join(
        x for x in f.read_text().splitlines() if not x.startswith(("%", "---"))
    )
    (maps / (f.stem + ".json")).write_text(
        json.dumps(normalize(yaml.safe_load(text)["MonoBehaviour"]))
    )
source = root / "Tools/ProjectCheck"
extra = Path(__file__).resolve().parent
p = (
    (root / "Assets/Rubezh/Editor/UnityTest20Preparation.cs")
    .read_text()
    .replace("#if UNITY_EDITOR", "#if RUBEZH_NATIVE_TEST_CONTRACT", 1)
)
(extra / "Preparation.cs").write_text(p)
production = (
    list((root / "Assets/Rubezh/Core").glob("*.cs"))
    + list((root / "Assets/Rubezh/Camp").glob("*.cs"))
    + list((root / "Assets/Rubezh/Runtime").rglob("*.cs"))
    + list((source / "Contracts").glob("*.cs"))
)
production = [p for p in production if p.name != "PlatformContract.cs"]
(extra / "Platform.cs").write_text(
    (source / "Contracts/PlatformContract.cs")
    .read_text()
    .split("namespace UnityEditor")[0]
)
production.append(extra / "Platform.cs")
with tempfile.TemporaryDirectory(prefix="rubezh-check-") as d:
    d = Path(d)
    shutil.copy2(sdk / "Newtonsoft.Json.dll", d / "Newtonsoft.Json.dll")
    for name, main, args in [
        ("Preparation201Check", "Preparation201Check", []),
        ("Unity200Check", "Unity200Check", []),
        ("Render199Check", "Render199Check", []),
        ("Cache198Check", "Cache198Check", []),
        ("Art197Check", "Art197Check", []),
        ("Ammo170Check", "Ammo170Check", []),
        ("Repair169Check", "Repair169Check", []),
        ("Packed164Check", "Packed164Check", []),
        ("Wall162Check", "Wall162Check", []),
        ("Roof161Check", "Roof161Check", []),
        ("Visual160Check", "Visual160Check", []),
        ("DiagnosticTest", "DiagnosticTest", []),
        ("RecipeCheck", "RecipeCheck", []),
        ("PawnAtlasCheck", "PawnAtlasCheck", []),
        ("AimTimingCheck", "AimTimingCheck", []),
        ("EquipmentDetailsCheck", "EquipmentDetailsCheck", []),
        ("RareWeaponsCheck", "RareWeaponsCheck", []),
        (
            "AppearanceCheck",
            "Looks096",
            ["Assets/Rubezh/Resources/FieldCamp/catalog.json"],
        ),
        ("Menu122Check", "Menu122Check", []),
        ("Lifetime122Check", "Lifetime122Check", []),
        ("Levels122Check", "Levels122Check", []),
    ]:
        if len(sys.argv) > 1 and name not in sys.argv[1:]:
            continue
        exe = d / (name + ".dll")
        cmd = (
            [
                str(dotnet),
                str(sdk / "Roslyn/bincore/csc.dll"),
                "-nologo",
                "-langversion:latest",
                "-optimize",
                "-define:RUBEZH_DEVELOPER,RUBEZH_PICTURE_CONTRACT,RUBEZH_NATIVE_TEST_CONTRACT"
                + (",UNITY_2020_2_OR_NEWER" if name == "Render199Check" else ""),
                "-target:exe",
                "-main:" + main,
                "-out:" + str(exe),
                "-r:" + str(sdk / "Newtonsoft.Json.dll"),
            ]
            + ["-r:" + str(f) for f in ref.glob("*.dll")]
            + list(map(str, production))
            + (
                [
                    str(extra / "Preparation201Check.cs"),
                    str(extra / "EditorContract.cs"),
                    str(extra / "Preparation.cs"),
                ]
                if name == "Preparation201Check"
                else [str(source / (name + ".cs"))]
            )
        )
        result = subprocess.run(cmd, capture_output=True, text=True)
        (out / (name + "-compile.log")).write_text(result.stdout + result.stderr)
        if result.returncode:
            print(result.stdout, result.stderr)
            raise SystemExit(result.returncode)
        exe.with_suffix(".runtimeconfig.json").write_text(
            json.dumps(
                {
                    "runtimeOptions": {
                        "tfm": "net8.0",
                        "framework": {
                            "name": "Microsoft.NETCore.App",
                            "version": runtime,
                        },
                    }
                }
            )
        )
        with (out / (name + "-run.log")).open("w") as log:
            result = subprocess.run(
                [str(dotnet), str(exe)] + args, stdout=log, stderr=subprocess.STDOUT
            )
        lines = (out / (name + "-run.log")).read_text().splitlines()
        print(name, "\n".join(lines[-4:]), flush=True)
        if result.returncode:
            raise SystemExit(result.returncode)
shutil.rmtree(maps)
print("PASS: portable checks. Run native Unity validation separately.")
