"""Execute production exterior preparation and migration using temporary files.
PNG encoding and Unity texture imports are contracts, not actual Unity execution.
"""
from pathlib import Path
import os,sys,subprocess,json,shutil,re
root=Path(__file__).resolve().parents[3];here=Path(__file__).resolve().parent
dotnet=Path(os.environ.get('DOTNET_ROOT',''))/'dotnet'
if not dotnet.is_file():dotnet=Path(shutil.which('dotnet') or '/tmp/rubezh-dotnet/dotnet')
home=dotnet.parent
version=lambda p:tuple(map(int,re.findall(r'\d+',p.name)[:3]))
sdk=sorted((home/'sdk').glob('8.*'),key=version)[-1]
ref=sorted((home/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
runtime=sorted((home/'shared/Microsoft.NETCore.App').glob('8.*'),key=version)[-1].name
out=root/'Build/Reports/AI-0.12.222/Exterior';out.mkdir(parents=True,exist_ok=True)
exe=out/'ExteriorCheck.dll'
sources=[root/'Assets/Rubezh/Editor/BuildResourcePreparation.cs',root/'Assets/Rubezh/Editor/NativeExteriorPreparation.cs',root/'Tools/ProjectCheck/MaterialMigration221a/EditorFileContract.cs',here/'Contract.cs',here/'Check.cs']
cmd=[str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'-nologo','-langversion:latest','-define:UNITY_EDITOR','-target:exe','-out:'+str(exe)]+['-r:'+str(p) for p in ref.glob('*.dll')]+list(map(str,sources))
r=subprocess.run(cmd,capture_output=True,text=True);(out/'compile.log').write_text(r.stdout+r.stderr)
if r.returncode:print(r.stdout+r.stderr);raise SystemExit(r.returncode)
exe.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':runtime}}}))
r=subprocess.run([str(dotnet),str(exe)],cwd=root,capture_output=True,text=True);(out/'run.log').write_text(r.stdout+r.stderr);print(r.stdout+r.stderr);raise SystemExit(r.returncode)
