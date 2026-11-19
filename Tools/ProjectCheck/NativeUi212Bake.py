from pathlib import Path
import subprocess
import json
import os
from PIL import Image
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from rubezh_dotnet import find_sdk

p = Path(__file__).resolve().parents[2]
os.chdir(p)
out = p / "Build/Reports/AI-0.12.212/UiBake"
out.mkdir(parents=True, exist_ok=True)
import shutil

dotnet, sdk, ref, ver = find_sdk()
prod = (
    list((p / "Assets/Rubezh/Core").glob("*.cs"))
    + list((p / "Assets/Rubezh/Camp").glob("*.cs"))
    + list((p / "Assets/Rubezh/Runtime").rglob("*.cs"))
    + list((p / "Tools/ProjectCheck/Contracts").glob("*.cs"))
    + [p / "Tools/ProjectCheck/NativeUi212Bake.cs"]
)
exe = out / "UiBake.dll"
cmd = (
    [
        str(dotnet),
        str(sdk / "Roslyn/bincore/csc.dll"),
        "-nologo",
        "-langversion:latest",
        "-define:RUBEZH_DEVELOPER,RUBEZH_PICTURE_CONTRACT,RUBEZH_UI_BAKE",
        "-target:exe",
        "-main:NativeUi212Bake",
        "-out:" + str(exe),
        "-r:" + str(sdk / "Newtonsoft.Json.dll"),
    ]
    + ["-r:" + str(f) for f in ref.glob("*.dll")]
    + list(map(str, prod))
)
r = subprocess.run(cmd, capture_output=True, text=True)
(out / "compile.log").write_text(r.stdout + r.stderr)
print(r.stdout[-4000:])
r.check_returncode()
shutil.copy2(sdk / "Newtonsoft.Json.dll", out / "Newtonsoft.Json.dll")
exe.with_suffix(".runtimeconfig.json").write_text(
    json.dumps(
        {
            "runtimeOptions": {
                "tfm": "net8.0",
                "framework": {"name": "Microsoft.NETCore.App", "version": ver},
            }
        }
    )
)
raw = out / "raw"
r = subprocess.run([str(dotnet), str(exe), str(raw)], capture_output=True, text=True)
(out / "run.log").write_text(r.stdout + r.stderr)
print(r.stdout, r.stderr)
r.check_returncode()
target = p / "Assets/Rubezh/Resources/NativeUI"
target.mkdir(exist_ok=True)
for f in raw.glob("*.rgba"):
    Image.frombytes("RGBA", (2048, 2048), f.read_bytes()).save(
        target / (f.stem + ".png")
    )
shutil.copy2(raw / "catalog.json", target / "catalog.json")
print("PNG bytes", sum(f.stat().st_size for f in target.glob("*.png")))
