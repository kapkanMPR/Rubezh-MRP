"""Compile the actual pure combat Core and exercise recovered behavior; no Unity API mocks."""

from pathlib import Path
import subprocess
import json
import hashlib
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from rubezh_dotnet import find_sdk

root = Path(__file__).resolve().parents[3]
here = Path(__file__).resolve().parent
out = root / "Build/Reports/AI-0.12.222/CoreRecovery"
out.mkdir(parents=True, exist_ok=True)
dotnet, sdk, ref, runtime = find_sdk()
cleaned_hashes = json.loads((here / "cleanup-sha256.json").read_text(encoding="utf-8"))
for entry in json.loads((here / "provenance.json").read_text(encoding="utf-8")):
    path = entry["path"]
    digest = hashlib.sha256((root / path).read_bytes()).hexdigest()
    if digest == entry["restored204"]:
        print(f"{path}: exact historical source")
    elif digest == cleaned_hashes.get(path):
        print(f"{path}: verified cleanup revision")
    else:
        raise SystemExit(f"Unexpected recovered Core mutation: {path}")
exe = out / "CoreRecovery222Check.dll"
cmd = (
    [
        str(dotnet),
        str(sdk / "Roslyn/bincore/csc.dll"),
        "-nologo",
        "-langversion:latest",
        "-optimize",
        "-target:exe",
        "-main:CoreRecovery222Check",
        "-out:" + str(exe),
    ]
    + ["-r:" + str(p) for p in ref.glob("*.dll")]
    + [str(p) for p in (root / "Assets/Rubezh/Core").glob("*.cs")]
    + [str(here / "Check.cs")]
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
r = subprocess.run([str(dotnet), str(exe)], capture_output=True, text=True, cwd=root)
(out / "run.log").write_text(r.stdout + r.stderr)
print(r.stdout + r.stderr)
raise SystemExit(r.returncode)
