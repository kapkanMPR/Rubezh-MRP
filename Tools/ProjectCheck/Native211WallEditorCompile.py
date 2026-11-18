"""Compile actual wall preparation with repository Unity/Editor API contracts.
Compile-only check; does not substitute for a Unity import/build run.
"""
from pathlib import Path
import os,shutil,subprocess,json
root=Path(__file__).resolve().parents[2];src=root/'Tools/ProjectCheck';out=root/'Build/Reports/AI-0.12.211/WallEditorCompile';out.mkdir(parents=True,exist_ok=True)
dotnet=Path(os.environ.get('DOTNET_ROOT',''))/'dotnet'
if not dotnet.is_file():dotnet=Path(shutil.which('dotnet') or '/workspace/scratch/e935c95194e6/tooling/dotnet/dotnet')
home=dotnet.parent;sdk=sorted((home/'sdk').glob('8.*'))[-1];ref=sorted((home/'packs/Microsoft.NETCore.App.Ref').glob('8.*'))[-1]/'ref/net8.0'
(out/'Platform.cs').write_text((src/'Contracts/PlatformContract.cs').read_text().split('namespace UnityEditor')[0])
(out/'NativeWallMaterialPreparation.cs').write_text((root/'Assets/Rubezh/Editor/NativeWallMaterialPreparation.cs').read_text().replace('#if UNITY_EDITOR','#if RUBEZH_NATIVE_TEST_CONTRACT',1))
prod=list((root/'Assets/Rubezh/Core').glob('*.cs'))+list((root/'Assets/Rubezh/Camp').glob('*.cs'))+list((root/'Assets/Rubezh/Runtime').rglob('*.cs'))+[p for p in (src/'Contracts').glob('*.cs') if p.name!='PlatformContract.cs']+[out/'Platform.cs',out/'NativeWallMaterialPreparation.cs',src/'Native210/NativeAssetsContract.cs',src/'Native208/EditorContract.cs',src/'Native208/NativeEditorContract.cs']
cmd=[str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'-nologo','-langversion:latest','-define:RUBEZH_DEVELOPER,RUBEZH_PICTURE_CONTRACT,RUBEZH_NATIVE_TEST_CONTRACT,RUBEZH_NATIVE_CONTENT_CONTRACT','-target:library','-out:'+str(out/'WallEditor.dll'),'-r:'+str(sdk/'Newtonsoft.Json.dll')]+['-r:'+str(p) for p in ref.glob('*.dll')]+list(map(str,prod))
r=subprocess.run(cmd,capture_output=True,text=True);(out/'compile.log').write_text(r.stdout+r.stderr);print(r.stdout+r.stderr);print('Editor wall helper compile:',r.returncode)
(out/'result.json').write_text(json.dumps({'passed':r.returncode==0,'scope':'NativeWallMaterialPreparation production source + Runtime/Core/Camp with existing Unity Editor contracts','unityEditorExecuted':False},indent=2));raise SystemExit(r.returncode)
