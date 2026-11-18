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
version=lambda p:tuple(int(x) for x in re.findall(r'\d+',p.name)[:3]);sdk=sorted((home/'sdk').glob('8.*'),key=version)[-1];ref=sorted((home/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0';runtime=sorted((home/'shared/Microsoft.NETCore.App').glob('8.*'),key=version)[-1].name
(extra/'Platform.cs').write_text((root/'Tools/ProjectCheck/Contracts/PlatformContract.cs').read_text().split('namespace UnityEditor')[0])
files=list((root/'Assets/Rubezh/Core').glob('*.cs'))+list((root/'Assets/Rubezh/Camp').glob('*.cs'))+list((root/'Tools/ProjectCheck/Contracts').glob('*.cs'))
files=[p for p in files if p.name!='PlatformContract.cs']
files+=list((root/'Assets/Rubezh/Runtime').rglob('*.cs'))
files+=[extra/'Platform.cs',extra/'NativeAssetsContract.cs',root/'Tools/ProjectCheck/NativeRender210Check.cs']
with tempfile.TemporaryDirectory() as tmp:
 tmp=Path(tmp);shutil.copy2(sdk/'Newtonsoft.Json.dll',tmp/'Newtonsoft.Json.dll');dll=tmp/'check.dll'
 cmd=[str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'-nologo','-langversion:latest','-optimize','-define:RUBEZH_DEVELOPER,RUBEZH_PICTURE_CONTRACT,RUBEZH_NATIVE_TEST_CONTRACT,RUBEZH_NATIVE_CONTENT_CONTRACT','-target:exe','-main:NativeRender210Check','-out:'+str(dll),'-r:'+str(sdk/'Newtonsoft.Json.dll')]+['-r:'+str(f) for f in ref.glob('*.dll')]+list(map(str,files))
 result=subprocess.run(cmd,capture_output=True,text=True);(root/'Build/Reports/AI-0.12.210-NativeMigration/render-compile.log').write_text(result.stdout+result.stderr)
 if result.returncode:print(result.stdout);raise SystemExit(result.returncode)
 dll.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':runtime}}}))
 result=subprocess.run([str(dotnet),str(dll)]);raise SystemExit(result.returncode)
