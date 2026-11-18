from pathlib import Path
import subprocess,json,shutil,os
root=Path(__file__).resolve().parents[2];home=Path(os.environ.get('DOTNET_ROOT') or Path(shutil.which('dotnet') or 'dotnet').resolve().parent);sdk=sorted((home/'sdk').glob('8.*'))[-1];ref=sorted((home/'packs/Microsoft.NETCore.App.Ref').glob('8.*'))[-1]/'ref/net8.0';runtime=sorted((home/'shared/Microsoft.NETCore.App').glob('8.*'))[-1].name
p=root;dst=Path(__file__).resolve().parent;exe=dst/'Test.dll'
sources=list((p/'Assets/Rubezh/Core').glob('*.cs'))+list((p/'Assets/Rubezh/Camp').glob('*.cs'))+list((p/'Assets/Rubezh/Runtime').rglob('*.cs'))+list((dst/'Contracts').glob('*.cs'))
base=[str(home/'dotnet'),str(sdk/'Roslyn/bincore/csc.dll'),'-nologo','-langversion:latest','-optimize','-define:RUBEZH_DEVELOPER','-r:'+str(sdk/'Newtonsoft.Json.dll')]+['-r:'+str(f) for f in ref.glob('*.dll')]
lib=dst/'Production.dll'
if not lib.exists() or any(f.stat().st_mtime>lib.stat().st_mtime for f in sources):
 r=subprocess.run(base+['-target:library','-out:'+str(lib)]+list(map(str,sources)),capture_output=True,text=True);(dst/'compile-production.log').write_text(r.stdout+r.stderr);print(r.stdout[-10000:]);
 if r.returncode:raise SystemExit(r.returncode)
r=subprocess.run(base+['-target:exe','-main:Test','-out:'+str(exe),'-r:'+str(lib),str(dst/'Test.cs')],capture_output=True,text=True);(dst/'compile.log').write_text(r.stdout+r.stderr);print(r.stdout[-10000:]);
if r.returncode:raise SystemExit(r.returncode)
shutil.copy2(sdk/'Newtonsoft.Json.dll',dst/'Newtonsoft.Json.dll');exe.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':runtime}}}))
