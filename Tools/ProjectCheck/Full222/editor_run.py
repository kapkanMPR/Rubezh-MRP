"""Compile current production content preparation and run atlas-shape regressions under portable API contracts."""

from pathlib import Path
import sys
import subprocess
import json

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from rubezh_dotnet import find_sdk

root = Path(__file__).resolve().parents[3]
here = Path(__file__).resolve().parent
dotnet, sdk, ref, runtime = find_sdk()
out = root / "Build/Reports/AI-0.12.222/Editor"
out.mkdir(parents=True, exist_ok=True)
sources = [
    root / "Assets/Rubezh/Editor" / p
    for p in [
        "BuildResourcePreparation.cs",
        "NativeUiPreparation.cs",
        "NativeExteriorPreparation.cs",
        "NativeAtlasShapeFix.cs",
    ]
] + [here / "EditorContract.cs", here / "EditorCheck.cs"]
exe = out / "EditorCheck.dll"
cmd = (
    [
        str(dotnet),
        str(sdk / "Roslyn/bincore/csc.dll"),
        "-nologo",
        "-langversion:latest",
        "-define:UNITY_EDITOR",
        "-target:exe",
        "-main:Full222EditorCheck",
        "-out:" + str(exe),
    ]
    + ["-r:" + str(p) for p in ref.glob("*.dll")]
    + list(map(str, sources))
)
r = subprocess.run(cmd, capture_output=True, text=True)
(out / "compile.log").write_text(r.stdout + r.stderr)
if r.returncode:
    print(r.stdout + r.stderr)
    raise SystemExit(r.returncode)
exe.with_suffix(".runtimeconfig.json").write_text(
    json.dumps(
        {
            "runtimeOptions": {
                "tfm": "net8.0",
                "framework": {"name": "Microsoft.NETCore.App", "version": runtime},
            }
        }
    )
)
r = subprocess.run([str(dotnet), str(exe)], cwd=root, capture_output=True, text=True)
(out / "run.log").write_text(r.stdout + r.stderr)
print(r.stdout + r.stderr)
raise SystemExit(r.returncode)
