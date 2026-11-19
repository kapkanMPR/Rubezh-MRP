from pathlib import Path
import os
import sys
import shutil
import subprocess
import json

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from rubezh_dotnet import find_sdk

root = Path(__file__).resolve().parents[3]
os.chdir(root)
base = root
work = root
extra = Path(__file__).resolve().parent
unity_compile = "--unity-compile" in sys.argv
if "--regression" in sys.argv and not unity_compile:
    subprocess.run(
        [
            sys.executable,
            str(root / "Tools/ProjectCheck/Native213/building_source_check.py"),
        ],
        check=True,
    )
main = (
    "UnityPlayerCompile"
    if unity_compile
    else (
        "NativeMasks213Check"
        if "--masks" in sys.argv
        else (
            "Native214RegressionCheck"
            if "--regression" in sys.argv
            else (
                "Native214Check"
                if "--render-regression" in sys.argv
                else "Optimization220Check"
            )
        )
    )
)
dotnet, sdk, ref, runtime = find_sdk()
src = root / "Tools/ProjectCheck"
out = (
    root
    / "Build/Reports/AI-0.12.220"
    / ("Baseline" if "--baseline" in sys.argv else "Current")
)
out.mkdir(parents=True, exist_ok=True)
(out / "Platform.cs").write_text(
    (src / "Contracts/PlatformContract.cs")
    .read_text()
    .split("namespace UnityEditor")[0]
)
if "--baseline" in sys.argv and not os.environ.get("RUBEZH_BASELINE_ROOT"):
    raise SystemExit(
        "Set RUBEZH_BASELINE_ROOT to an unmodified213 source tree for --baseline."
    )
code = Path(os.environ["RUBEZH_BASELINE_ROOT"]) if "--baseline" in sys.argv else root
prod = (
    list((code / "Assets/Rubezh/Core").glob("*.cs"))
    + list((code / "Assets/Rubezh/Camp").glob("*.cs"))
    + list((code / "Assets/Rubezh/Runtime").rglob("*.cs"))
    + [p for p in (src / "Contracts").glob("*.cs") if p.name != "PlatformContract.cs"]
    + [
        out / "Platform.cs",
        src / "Native211/NativeAssetsContract.cs",
        src / "Native213/Native213Check.cs",
        src / "Native213/NativeEffects213Check.cs",
        src / "Native213/NativeMasks213Check.cs",
        src / "Native213/Building213Check.cs",
    ]
)
prod += [
    src / "Native214/Native214Check.cs",
    src / "Native214/Native214PoseCheck.cs",
    src / "Native214/Native214RegressionCheck.cs",
]
if "--baseline" not in sys.argv:
    prod += [
        src / "Native214/Native214WorldCheck.cs",
        src / "Native214/Native214PrimitiveCheck.cs",
    ]
prod += [extra / "Optimization220Check.cs"]
if unity_compile:
    prod = [
        p
        for p in prod
        if p.name
        not in (
            "Native213Check.cs",
            "NativeEffects213Check.cs",
            "NativeMasks213Check.cs",
            "BaseContract.cs",
            "Building213Check.cs",
            "Native214Check.cs",
            "Native214WorldCheck.cs",
            "Native214PoseCheck.cs",
            "Native214RegressionCheck.cs",
            "Native214PrimitiveCheck.cs",
            "Optimization220Check.cs",
        )
    ] + [src / "Native213/UnityPlayerContract.cs", out / "BasePlayerContract.cs"]
    base_contract = (src / "Contracts/BaseContract.cs").read_text()
    base_contract = base_contract.replace(
        "public class Object {",
        "public class Object {public static T Instantiate<T>(T source) where T:Object=>source;",
    )
    base_contract = base_contract.replace(
        "public static class Application {",
        "public static class Application {public static event Action lowMemory; public static event Action<string,string,LogType> logMessageReceivedThreaded;",
    )
    base_contract = base_contract.replace(
        "public static class Resources {",
        "public static class Resources {public static T[] FindObjectsOfTypeAll<T>()=>new T[0];",
    )
    base_contract = base_contract.replace(
        "public static class SystemInfo {",
        'public static class SystemInfo {public static string deviceModel="contract",operatingSystem="contract",processorType="contract",graphicsDeviceName="contract",graphicsDeviceType="contract";public static int processorCount=1,systemMemorySize=1,graphicsMemorySize=1;',
    )
    (out / "BasePlayerContract.cs").write_text(base_contract)
    platform = (
        (out / "Platform.cs")
        .read_text()
        .replace(
            "public static class Profiler{",
            "public static class Profiler{public static long GetRuntimeMemorySizeLong(UnityEngine.Object value)=>0;",
        )
    )
    (out / "Platform.cs").write_text(platform)
label = (
    main
    + ("Battle" if "--battle" in sys.argv else "")
    + ("Long" if "--long" in sys.argv else "")
)
exe = out / (label + ".dll")
if (
    not (out / "Newtonsoft.Json.dll").exists()
    or (out / "Newtonsoft.Json.dll").read_bytes()
    != (sdk / "Newtonsoft.Json.dll").read_bytes()
):
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
        "-main:" + main,
        "-out:" + str(exe),
        "-r:" + str(sdk / "Newtonsoft.Json.dll"),
    ]
    + ["-r:" + str(f) for f in ref.glob("*.dll")]
    + list(map(str, prod))
)
if unity_compile:
    cmd = [
        (
            x.replace("-define:", "-define:UNITY_5_3_OR_NEWER,")
            if x.startswith("-define:")
            else "-target:library" if x == "-target:exe" else x
        )
        for x in cmd
        if not x.startswith("-main:")
    ]
r = subprocess.run(cmd, capture_output=True, text=True)
(out / (label + "-compile.log")).write_text(r.stdout + r.stderr)
if r.returncode:
    print(r.stdout, r.stderr)
    raise SystemExit(r.returncode)
if unity_compile:
    print(
        "PASS Unity Player conditional compile against portable contracts; no actual Unity assembly validation."
    )
    raise SystemExit(0)
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
with (out / (label + "-run.log")).open("w") as f:
    r = subprocess.run(
        [str(dotnet), str(exe)]
        + (["--battle"] if "--battle" in sys.argv else [])
        + (["--long"] if "--long" in sys.argv else []),
        stdout=f,
        stderr=subprocess.STDOUT,
        env={**os.environ, "RUBEZH_TEST_OUTPUT": str(out)},
    )
print((out / (label + "-run.log")).read_text())
raise SystemExit(r.returncode)
