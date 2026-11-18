from pathlib import Path
import os,sys,re,shutil,subprocess,tempfile,json
root=Path(__file__).resolve().parents[3];os.chdir(root)
base=root;work=root;extra=Path(__file__).resolve().parent
home=Path(os.environ.get('DOTNET_ROOT',''))
dotnet=home/('dotnet.exe' if os.name=='nt' else 'dotnet')
if not dotnet.is_file():
 found=shutil.which('dotnet')
 if not found:raise SystemExit('Install .NET SDK 8 and set DOTNET_ROOT or PATH.')
 dotnet=Path(found).resolve()
home=dotnet.parent
version=lambda p:tuple(int(x) for x in re.findall(r'\d+',p.name)[:3])
sdk=sorted((home/'sdk').glob('8.*'),key=version)[-1];ref=sorted((home/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0';runtime=sorted((home/'shared/Microsoft.NETCore.App').glob('8.*'),key=version)[-1].name
src=root/'Tools/ProjectCheck';out=root/'Build/Reports/AI-0.12.211-NativeDirect/Portable';out.mkdir(parents=True,exist_ok=True)
(out/'Platform.cs').write_text((src/'Contracts/PlatformContract.cs').read_text().split('namespace UnityEditor')[0])
prod=list((root/'Assets/Rubezh/Core').glob('*.cs'))+list((root/'Assets/Rubezh/Camp').glob('*.cs'))+list((root/'Assets/Rubezh/Runtime').rglob('*.cs'))+[p for p in (src/'Contracts').glob('*.cs') if p.name!='PlatformContract.cs']+[out/'Platform.cs',extra/'NativeAssetsContract.cs',src/'Native211Check.cs',src/'NativeEffects211Check.cs']
exe=out/'Native211Check.dll';shutil.copy2(sdk/'Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
cmd=[str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'-nologo','-langversion:latest','-optimize','-define:RUBEZH_DEVELOPER,RUBEZH_PICTURE_CONTRACT,RUBEZH_NATIVE_TEST_CONTRACT,RUBEZH_NATIVE_CONTENT_CONTRACT','-target:exe','-main:Native211Check','-out:'+str(exe),'-r:'+str(sdk/'Newtonsoft.Json.dll')]+['-r:'+str(f) for f in ref.glob('*.dll')]+list(map(str,prod))
r=subprocess.run(cmd,capture_output=True,text=True);(out/'compile.log').write_text(r.stdout+r.stderr)
if r.returncode:print(r.stdout,r.stderr);raise SystemExit(r.returncode)
exe.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':runtime}}}))
with (out/'run.log').open('w') as f:r=subprocess.run([str(dotnet),str(exe)],stdout=f,stderr=subprocess.STDOUT)
print((out/'run.log').read_text());raise SystemExit(r.returncode)
