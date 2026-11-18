"""Compile the actual pure combat Core and exercise recovered behavior; no Unity API mocks."""
from pathlib import Path
import os,re,shutil,subprocess,json,hashlib
root=Path(__file__).resolve().parents[3]
here=Path(__file__).resolve().parent
out=root/'Build/Reports/AI-0.12.222/CoreRecovery';out.mkdir(parents=True,exist_ok=True)
dotnet=Path(os.environ.get('DOTNET_ROOT',''))/('dotnet.exe' if os.name=='nt' else 'dotnet')
if not dotnet.is_file():
    found=shutil.which('dotnet')
    if not found:raise SystemExit('Install .NET SDK8 and set DOTNET_ROOT.')
    dotnet=Path(found).resolve()
home=dotnet.parent
version=lambda p:tuple(int(x) for x in re.findall(r'\d+',p.name)[:3])
sdk=sorted((home/'sdk').glob('8.*'),key=version)[-1]
ref=sorted((home/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
runtime=sorted((home/'shared/Microsoft.NETCore.App').glob('8.*'),key=version)[-1].name
for entry in json.loads((here/'provenance.json').read_text()):
    digest=hashlib.sha256((root/entry['path']).read_bytes()).hexdigest()
    if digest!=entry['restored204']:raise SystemExit('Unexpected recovered Core mutation: '+entry['path'])
exe=out/'CoreRecovery222Check.dll'
cmd=[str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'-nologo','-langversion:latest','-optimize','-target:exe','-main:CoreRecovery222Check','-out:'+str(exe)]+['-r:'+str(p) for p in ref.glob('*.dll')]+[str(p) for p in (root/'Assets/Rubezh/Core').glob('*.cs')]+[str(here/'Check.cs')]
r=subprocess.run(cmd,capture_output=True,text=True);(out/'compile.log').write_text(r.stdout+r.stderr)
if r.returncode:print(r.stdout+r.stderr);raise SystemExit(r.returncode)
exe.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':runtime}}}))
r=subprocess.run([str(dotnet),str(exe)],capture_output=True,text=True,cwd=root)
(out/'run.log').write_text(r.stdout+r.stderr);print(r.stdout+r.stderr);raise SystemExit(r.returncode)
