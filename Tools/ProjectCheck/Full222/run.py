"""Two 100-soldier core/presentation stress fixtures. No Unity/GPU timing claims."""
from pathlib import Path
import os,sys,subprocess,json,shutil,re
root=Path(__file__).resolve().parents[3];here=Path(__file__).resolve().parent
dotnet=Path(os.environ.get('DOTNET_ROOT',''))/'dotnet'
if not dotnet.is_file():dotnet=Path(shutil.which('dotnet') or '/tmp/rubezh-dotnet/dotnet')
home=dotnet.parent;version=lambda p:tuple(map(int,re.findall(r'\d+',p.name)[:3]))
sdk=sorted((home/'sdk').glob('8.*'),key=version)[-1]
ref=sorted((home/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
runtime=sorted((home/'shared/Microsoft.NETCore.App').glob('8.*'),key=version)[-1].name
out=root/'Build/Reports/AI-0.12.222/Stress100';out.mkdir(parents=True,exist_ok=True)
src=root/'Tools/ProjectCheck';(out/'Platform.cs').write_text((src/'Contracts/PlatformContract.cs').read_text().split('namespace UnityEditor')[0])
sources=list((root/'Assets/Rubezh/Core').glob('*.cs'))+list((root/'Assets/Rubezh/Camp').glob('*.cs'))+list((root/'Assets/Rubezh/Runtime').rglob('*.cs'))+[p for p in (src/'Contracts').glob('*.cs') if p.name!='PlatformContract.cs']+[out/'Platform.cs',src/'Native211/NativeAssetsContract.cs',here/'StressCheck.cs']
exe=out/'StressCheck.dll';shutil.copy2(sdk/'Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
cmd=[str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'-nologo','-langversion:latest','-optimize','-define:RUBEZH_DEVELOPER,RUBEZH_PICTURE_CONTRACT,RUBEZH_NATIVE_TEST_CONTRACT,RUBEZH_NATIVE_CONTENT_CONTRACT','-target:exe','-main:Full222StressCheck','-out:'+str(exe),'-r:'+str(sdk/'Newtonsoft.Json.dll')]+['-r:'+str(p) for p in ref.glob('*.dll')]+list(map(str,sources))
r=subprocess.run(cmd,capture_output=True,text=True);(out/'compile.log').write_text(r.stdout+r.stderr)
if r.returncode:print(r.stdout+r.stderr);raise SystemExit(r.returncode)
exe.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':runtime}}}))
with (out/'run.log').open('w') as f:r=subprocess.run([str(dotnet),str(exe)],cwd=root,stdout=f,stderr=subprocess.STDOUT)
print((out/'run.log').read_text());raise SystemExit(r.returncode)
