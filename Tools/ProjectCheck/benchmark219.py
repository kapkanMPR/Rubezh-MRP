from pathlib import Path
import os, subprocess, json, shutil
root=Path(__file__).resolve().parents[2]
home=Path(os.environ['DOTNET_ROOT']); dotnet=home/'dotnet'
sdk=sorted((home/'sdk').glob('8.*'))[-1]
ref=sorted((home/'packs/Microsoft.NETCore.App.Ref').glob('8.*'))[-1]/'ref/net8.0'
runtime=sorted((home/'shared/Microsoft.NETCore.App').glob('8.*'))[-1].name
assembly=root/'Build/Reports/AI-0.12.215/Current/UnityPlayerCompile.dll'
out=root/'Build/Reports/AI-0.12.219/Benchmark';out.mkdir(parents=True,exist_ok=True)
exe=out/'Benchmark219Check.dll'
subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'-nologo','-out:'+str(exe),'-r:'+str(assembly)]+['-r:'+str(p) for p in ref.glob('*.dll')]+[str(root/'Tools/ProjectCheck/Benchmark219Check.cs')],check=True)
shutil.copy2(assembly,out/assembly.name);shutil.copy2(sdk/'Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
exe.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':runtime}}}))
subprocess.run([str(dotnet),str(exe)],cwd=root,check=True)
