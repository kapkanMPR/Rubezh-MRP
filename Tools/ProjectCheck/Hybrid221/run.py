"""Compile the Player-conditional source and exercise the isolated hybrid range.

Uses portable Unity API contracts and real production mesh generators. This does
not import Unity assets, compile shaders, execute a GPU, or measure device FPS.
"""
from pathlib import Path
import os
import re
import shutil
import subprocess
import sys
import json

root = Path(__file__).resolve().parents[3]
os.chdir(root)
checks = root / 'Tools/ProjectCheck'
out = root / 'Build/Reports/AI-0.12.221-Hybrid/Current'
out.mkdir(parents=True, exist_ok=True)
compile_only = '--unity-compile' in sys.argv
home = Path(os.environ.get('DOTNET_ROOT', ''))
dotnet = home / ('dotnet.exe' if os.name == 'nt' else 'dotnet')
if not dotnet.is_file():
    found = shutil.which('dotnet')
    if not found:
        raise SystemExit('Install .NET SDK 8 and set DOTNET_ROOT or PATH.')
    dotnet = Path(found).resolve()
home = dotnet.parent
version = lambda p: tuple(int(x) for x in re.findall(r'\d+', p.name)[:3])
sdk = sorted((home / 'sdk').glob('8.*'), key=version)[-1]
refs = sorted((home / 'packs/Microsoft.NETCore.App.Ref').glob('8.*'), key=version)[-1] / 'ref/net8.0'
runtime = sorted((home / 'shared/Microsoft.NETCore.App').glob('8.*'), key=version)[-1].name

base = (checks / 'Contracts/BaseContract.cs').read_text()
base = base.replace('public class Object {', 'public class Object {public static T Instantiate<T>(T source) where T:Object=>source;')
base = base.replace('public static class Application {', 'public static class Application {public static event Action lowMemory;public static event Action<string,string,LogType> logMessageReceivedThreaded;')
base = base.replace('public static class Resources {', 'public static class Resources {public static T[] FindObjectsOfTypeAll<T>()=>new T[0];')
base = base.replace('public static class SystemInfo {', 'public static class SystemInfo {public static string deviceModel="contract",operatingSystem="contract",processorType="contract",graphicsDeviceName="contract",graphicsDeviceType="contract";public static int processorCount=1,systemMemorySize=1,graphicsMemorySize=1;')
(out / 'BasePlayerContract.cs').write_text(base)
platform = (checks / 'Contracts/PlatformContract.cs').read_text().split('namespace UnityEditor')[0]
platform = platform.replace('public static class Profiler{', 'public static class Profiler{public static long GetRuntimeMemorySizeLong(UnityEngine.Object value)=>0;')
(out / 'Platform.cs').write_text(platform)

sources = list((root / 'Assets/Rubezh/Core').glob('*.cs'))
sources += list((root / 'Assets/Rubezh/Camp').glob('*.cs'))
sources += list((root / 'Assets/Rubezh/Runtime').rglob('*.cs'))
sources += [p for p in (checks / 'Contracts').glob('*.cs') if p.name not in ('BaseContract.cs', 'PlatformContract.cs')]
sources += [out / 'BasePlayerContract.cs', out / 'Platform.cs', checks / 'Native211/NativeAssetsContract.cs', checks / 'Native213/UnityPlayerContract.cs']
label = 'UnityPlayerCompile' if compile_only else 'Hybrid221Check'
if not compile_only:
    sources.append(Path(__file__).with_name('Hybrid221Check.cs'))
assembly = out / (label + '.dll')
shutil.copy2(sdk / 'Newtonsoft.Json.dll', out / 'Newtonsoft.Json.dll')
command = [str(dotnet), str(sdk / 'Roslyn/bincore/csc.dll'), '-nologo', '-langversion:latest', '-optimize',
           '-define:UNITY_5_3_OR_NEWER,RUBEZH_DEVELOPER,RUBEZH_PICTURE_CONTRACT,RUBEZH_NATIVE_TEST_CONTRACT,RUBEZH_NATIVE_CONTENT_CONTRACT',
           '-target:library' if compile_only else '-target:exe', '-out:' + str(assembly), '-r:' + str(sdk / 'Newtonsoft.Json.dll')]
if not compile_only:
    command.append('-main:Hybrid221Check')
command += ['-r:' + str(path) for path in refs.glob('*.dll')] + list(map(str, sources))
result = subprocess.run(command, capture_output=True, text=True)
(out / (label + '-compile.log')).write_text(result.stdout + result.stderr)
if result.returncode:
    print(result.stdout, result.stderr)
    raise SystemExit(result.returncode)
if compile_only:
    print('PASS Unity Player conditional compile against portable contracts; no actual Unity assembly validation.')
    raise SystemExit(0)
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': runtime}}}))
with (out / (label + '-run.log')).open('w') as log:
    result = subprocess.run([str(dotnet), str(assembly)], stdout=log, stderr=subprocess.STDOUT,
                            env={**os.environ, 'RUBEZH_TEST_OUTPUT': str(out)})
print((out / (label + '-run.log')).read_text())
raise SystemExit(result.returncode)
