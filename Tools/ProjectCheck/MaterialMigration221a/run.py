"""Run production resource preparation against temporary files with Editor API contracts."""

from pathlib import Path
import json
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from rubezh_dotnet import find_sdk

root = Path(__file__).resolve().parents[3]
here = Path(__file__).resolve().parent
dotnet, sdk, ref, runtime = find_sdk()
out = root / "Build/Reports/MaterialMigration221a"
out.mkdir(parents=True, exist_ok=True)
reproduce = "--reproduce" in sys.argv
source = (
    Path(sys.argv[-1]).resolve()
    if reproduce
    else root / "Assets/Rubezh/Editor/BuildResourcePreparation.cs"
)
name = "Original221" if reproduce else "Migration221a"
exe = out / (name + ".dll")
cmd = (
    [
        str(dotnet),
        str(sdk / "Roslyn/bincore/csc.dll"),
        "-nologo",
        "-langversion:latest",
        "-target:exe",
        "-out:" + str(exe),
    ]
    + ["-r:" + str(p) for p in ref.glob("*.dll")]
    + [
        str(source),
        str(here / "EditorFileContract.cs"),
        str(here / "MigrationCheck.cs"),
    ]
)
r = subprocess.run(cmd, capture_output=True, text=True)
(out / (name + "-compile.log")).write_text(r.stdout + r.stderr)
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
r = subprocess.run(
    [str(dotnet), str(exe)] + (["--reproduce"] if reproduce else []),
    capture_output=True,
    text=True,
    cwd=root,
)
(out / (name + "-run.log")).write_text(r.stdout + r.stderr)
print(r.stdout + r.stderr)
raise SystemExit(r.returncode)
