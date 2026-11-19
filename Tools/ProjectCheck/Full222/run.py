"""Run two 100-soldier stress fixtures compiled against portable Unity contracts.

python Tools/ProjectCheck/Full222/run.py --verify-trace checks each simulation tick
against the 0.12.222 baseline from commit 2bdb8d06, captured on .NET SDK 8.0.416
on Linux x64. Each line in Trace/ is the digest after one step. Omit the flag to
run the original stress test without computing hashes. A different runtime or
architecture can produce a different floating-point result.
"""

import argparse
from pathlib import Path
import sys
import subprocess
import json
import shutil

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from rubezh_dotnet import find_sdk

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument(
    "--verify-trace",
    action="store_true",
    help="compare all 2,400 simulation ticks to the baseline",
)
args = parser.parse_args()

root = Path(__file__).resolve().parents[3]
here = Path(__file__).resolve().parent
dotnet, sdk, ref, runtime = find_sdk()
out = root / "Build/Reports/AI-0.12.222/Stress100"
out.mkdir(parents=True, exist_ok=True)
src = root / "Tools/ProjectCheck"
(out / "Platform.cs").write_text(
    (src / "Contracts/PlatformContract.cs")
    .read_text()
    .split("namespace UnityEditor")[0]
)
sources = (
    list((root / "Assets/Rubezh/Core").glob("*.cs"))
    + list((root / "Assets/Rubezh/Camp").glob("*.cs"))
    + list((root / "Assets/Rubezh/Runtime").rglob("*.cs"))
    + [p for p in (src / "Contracts").glob("*.cs") if p.name != "PlatformContract.cs"]
    + [
        out / "Platform.cs",
        src / "Native211/NativeAssetsContract.cs",
        here / "StressCheck.cs",
    ]
)
exe = out / "StressCheck.dll"
shutil.copy2(sdk / "Newtonsoft.Json.dll", out / "Newtonsoft.Json.dll")
cmd = (
    [
        str(dotnet),
        str(sdk / "Roslyn/bincore/csc.dll"),
        "-nologo",
        "-langversion:latest",
        "-optimize",
        "-define:RUBEZH_DEVELOPER,RUBEZH_PICTURE_CONTRACT,RUBEZH_NATIVE_TEST_CONTRACT,RUBEZH_NATIVE_CONTENT_CONTRACT",
        "-target:exe",
        "-main:Full222StressCheck",
        "-out:" + str(exe),
        "-r:" + str(sdk / "Newtonsoft.Json.dll"),
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
with (out / "run.log").open("w") as f:
    r = subprocess.run(
        [str(dotnet), str(exe)] + (["--verify-trace"] if args.verify_trace else []),
        cwd=root,
        stdout=f,
        stderr=subprocess.STDOUT,
    )
print((out / "run.log").read_text())
raise SystemExit(r.returncode)
