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
main = "NativeFormation213Check"
dotnet, sdk, ref, runtime = find_sdk()
src = root / "Tools/ProjectCheck"
out = root / "Build/Reports/AI-0.12.213/Portable"
out.mkdir(parents=True, exist_ok=True)
(out / "Platform.cs").write_text(
    (src / "Contracts/PlatformContract.cs")
    .read_text()
    .split("namespace UnityEditor")[0]
)
prod = (
    list((root / "Assets/Rubezh/Core").glob("*.cs"))
    + list((root / "Assets/Rubezh/Camp").glob("*.cs"))
    + list((root / "Assets/Rubezh/Runtime").rglob("*.cs"))
    + [p for p in (src / "Contracts").glob("*.cs") if p.name != "PlatformContract.cs"]
    + [
        out / "Platform.cs",
        src / "Native211/NativeAssetsContract.cs",
        extra / "NativeFormation213Check.cs",
        extra / "NativeEffects213Check.cs",
        src / "NativeMasks211Check.cs",
    ]
)
if unity_compile:
    prod = [
        p
        for p in prod
        if p.name
        not in (
            "Native213Check.cs",
            "NativeEffects213Check.cs",
            "NativeMasks211Check.cs",
            "BaseContract.cs",
        )
    ] + [extra / "UnityPlayerContract.cs", out / "BasePlayerContract.cs"]
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
exe = out / (main + ".dll")
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
(out / (main + "-compile.log")).write_text(r.stdout + r.stderr)
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
with (out / (main + "-run.log")).open("w") as f:
    r = subprocess.run([str(dotnet), str(exe)], stdout=f, stderr=subprocess.STDOUT)
print((out / (main + "-run.log")).read_text())
raise SystemExit(r.returncode)
