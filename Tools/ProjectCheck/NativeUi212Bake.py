from pathlib import Path
import subprocess,json,os
from PIL import Image
p=Path(__file__).resolve().parents[2];os.chdir(p)
out=p/'Build/Reports/AI-0.12.212/UiBake';out.mkdir(parents=True,exist_ok=True)
import shutil
home=Path(os.environ.get('DOTNET_ROOT') or Path(shutil.which('dotnet') or '/workspace/scratch/e935c95194e6/tooling/dotnet/dotnet').parent);sdk=sorted((home/'sdk').glob('8.*'))[-1];ref=sorted((home/'packs/Microsoft.NETCore.App.Ref').glob('8.*'))[-1]/'ref/net8.0';ver=sorted((home/'shared/Microsoft.NETCore.App').glob('8.*'))[-1].name
prod=list((p/'Assets/Rubezh/Core').glob('*.cs'))+list((p/'Assets/Rubezh/Camp').glob('*.cs'))+list((p/'Assets/Rubezh/Runtime').rglob('*.cs'))+list((p/'Tools/ProjectCheck/Contracts').glob('*.cs'))+[p/'Tools/ProjectCheck/NativeUi212Bake.cs']
exe=out/'UiBake.dll';cmd=[str(home/'dotnet'),str(sdk/'Roslyn/bincore/csc.dll'),'-nologo','-langversion:latest','-define:RUBEZH_DEVELOPER,RUBEZH_PICTURE_CONTRACT,RUBEZH_UI_BAKE','-target:exe','-main:NativeUi212Bake','-out:'+str(exe),'-r:'+str(sdk/'Newtonsoft.Json.dll')]+['-r:'+str(f) for f in ref.glob('*.dll')]+list(map(str,prod));r=subprocess.run(cmd,capture_output=True,text=True);(out/'compile.log').write_text(r.stdout+r.stderr);print(r.stdout[-4000:]);r.check_returncode()
import shutil
shutil.copy2(sdk/'Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');exe.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':ver}}}))
raw=out/'raw';r=subprocess.run([str(home/'dotnet'),str(exe),str(raw)],capture_output=True,text=True);(out/'run.log').write_text(r.stdout+r.stderr);print(r.stdout,r.stderr);r.check_returncode()
target=p/'Assets/Rubezh/Resources/NativeUI';target.mkdir(exist_ok=True)
for f in raw.glob('*.rgba'):Image.frombytes('RGBA',(2048,2048),f.read_bytes()).save(target/(f.stem+'.png'))
shutil.copy2(raw/'catalog.json',target/'catalog.json')
print('PNG bytes',sum(f.stat().st_size for f in target.glob('*.png')))
