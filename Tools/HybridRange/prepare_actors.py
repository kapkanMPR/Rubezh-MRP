"""Bake the real existing battlefield actor recipe for the isolated hybrid range.
Reuses BattleVisualRecipes.DrawSoldier: authored torso/head plus rifle and hands.
No new anatomy or combat simulation. Requires .NET 8 SDK, Pillow, NumPy.
Run with DOTNET_ROOT set if dotnet is not on PATH; --check verifies committed art.
"""
from pathlib import Path
from PIL import Image
import hashlib, io, json, os, shutil, struct, subprocess, sys, tempfile
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Rubezh/Resources/HybridRange'
PAD = 16
SOURCE = 'using System;using System.IO;using System.Linq;using System.Collections.Generic;using SilentOrders.Core;using SilentOrders.Rendering;\nsealed class ActorBake:BattleVisualRecipes{\n static V2 Aim(float degrees){double a=degrees*Math.PI/180;float x=(float)Math.Cos(a)/11f,y=(float)Math.Sin(a)/5f;return new V2(x-y,x+y).Normalized;}\n public void Paint(BattleSimulation battle,Soldier soldier){activeMap=battle.Map;Canvas=new PixelCanvas(64,96,32);SoldierVisualSection=1;DrawSoldier(battle,soldier,false);}\n static void Main(string[] args){\n  Rubezh.Graphics.ConfigurePacks();\n  var required=new HashSet<int>(new[]{8,21,122,123,124,125,7902,7903,7904,7905,7958,7959,7960,7961});\n  foreach(int page in required.Select(id=>Rubezh.SpritePackIndex.Page[id]).Distinct())Rubezh.Graphics.LoadPage(page,File.ReadAllBytes("Assets/Rubezh/Resources/SpritePacks/p"+page.ToString("D4")+".bytes"),required:required);\n  var b=new BattleSimulation(221);b.Fog.Enabled=false;b.Time=1;\n  var origin=Project(new V2(0,0),b.Map);\n  float sum=(32-origin.X)/11f,difference=(80-origin.Y)/5f;\n  var position=new V2((sum-difference)*.5f,(sum+difference)*.5f);\n  var facings=new[]{Aim(35),Aim(0),Aim(-35),Aim(180)};\n  for(int side=0;side<2;side++)for(int dir=0;dir<4;dir++){\n   var s=new Soldier(b.Rules){Id=0,Side=side==0?Side.Green:Side.Red,Role=Role.Rifleman,Look=side==0?new Rubezh.GreenLook{headId=116}:null,Position=position,Facing=facings[dir],WeaponVariant=side==0?0:1,LastFire=-100,Health=100,Deployed=true};\n   var painter=new ActorBake();painter.Paint(b,s);var pixels=painter.Canvas.Pixels;\n   using(var w=new BinaryWriter(File.Create(Path.Combine(args[0],"frame"+(side*4+dir)+".rgba")))){w.Write(painter.Canvas.Width);w.Write(painter.Canvas.Height);foreach(uint pixel in pixels)w.Write(pixel);}\n   Console.WriteLine("Painted side="+side+" dir="+dir+" missing="+Rubezh.Graphics.MissingSpriteCount);\n  }\n  if(Rubezh.Graphics.MissingSpriteCount!=0)throw new Exception("Missing source sprite "+Rubezh.Graphics.LastMissingSprite);\n }\n}\n'

def render_frames():
    home = Path(os.environ.get('DOTNET_ROOT', ''))
    dotnet = home / ('dotnet.exe' if os.name == 'nt' else 'dotnet')
    if not dotnet.is_file():
        found = shutil.which('dotnet')
        if not found: raise SystemExit('Install .NET 8 SDK and set DOTNET_ROOT or PATH.')
        dotnet = Path(found).resolve(); home = dotnet.parent
    version = lambda p: tuple(int(x) for x in p.name.split('.')[:3])
    sdk = sorted((home / 'sdk').glob('8.*'), key=version)[-1]
    refs = sorted((home / 'packs/Microsoft.NETCore.App.Ref').glob('8.*'), key=version)[-1] / 'ref/net8.0'
    runtime = sorted((home / 'shared/Microsoft.NETCore.App').glob('8.*'), key=version)[-1].name
    sources = sorted((ROOT / 'Assets/Rubezh/Core').glob('*.cs'))
    core_hash = hashlib.sha256()
    for path in sources:
        if path.name in ('BattleRenderer.cs', 'GreenLook.cs', 'PawnAtlas.cs', 'Rubezh.Graphics.cs', 'SpritePacks.cs'):
            core_hash.update(path.name.encode()); core_hash.update(path.read_bytes())
    frames = []; pivots = []
    with tempfile.TemporaryDirectory(prefix='rubezh-hybrid-actor-') as scratch:
        temp = Path(scratch); source = temp / 'Bake.cs'; source.write_text(SOURCE)
        exe = temp / 'Bake.dll'
        command = [str(dotnet), str(sdk/'Roslyn/bincore/csc.dll'), '-nologo', '-langversion:latest', '-optimize', '-target:exe', '-main:ActorBake', '-out:'+str(exe)]
        command += ['-r:'+str(f) for f in refs.glob('*.dll')] + [str(f) for f in sources] + [str(source)]
        compiled = subprocess.run(command, cwd=ROOT, capture_output=True, text=True)
        if compiled.returncode: raise RuntimeError(compiled.stdout + compiled.stderr)
        exe.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': runtime}}}))
        subprocess.run([str(dotnet), str(exe), str(temp)], cwd=ROOT, check=True)
        for i in range(8):
            raw = (temp / f'frame{i}.rgba').read_bytes()
            w, h = struct.unpack('<II', raw[:8])
            canvas = Image.frombytes('RGBA', (w, h), raw[8:])
            rect = canvas.getchannel('A').getbbox()
            if rect is None or rect[0] == 0 or rect[1] == 0 or rect[2] == w or rect[3] == h:
                raise RuntimeError(f'Empty or clipped actor frame: {i}, {rect}')
            rgba = np.array(canvas.crop(rect), dtype=np.uint32)
            alpha = np.maximum(rgba[:, :, 3:4], 1)
            # Original software renderer writes premultiplied pixels; Unity atlas
            # uses straight alpha with ordinary alpha blending and sRGB import.
            rgba[:, :, :3] = np.minimum(255, (rgba[:, :, :3] * 255 + alpha // 2) // alpha)
            frames.append(Image.fromarray(rgba.astype(np.uint8), 'RGBA'))
            pivots.append((1024 - rect[0], rect[3] - 2560))
    return frames, pivots, core_hash.hexdigest()

frames, pivots, core_hash = render_frames()
cell_w = max(im.width for im in frames) + PAD * 2
cell_h = max(im.height for im in frames) + PAD * 2
atlas = Image.new('RGBA', (cell_w * 4, cell_h * 2))
if atlas.width > 4096 or atlas.height > 4096: raise RuntimeError('Actor atlas exceeds import limit')
rects = []
for i, im in enumerate(frames):
    x, y = i % 4 * cell_w + PAD, i // 4 * cell_h + PAD
    atlas.paste(im, (x, y))
    rects.append({'x': x, 'y': atlas.height - y - im.height,
                  'w': im.width, 'h': im.height, 'anchorX': pivots[i][0], 'anchorY': pivots[i][1]})
meta = {'width': atlas.width, 'height': atlas.height, 'frames': rects,
        'pixelsPerBodyHeight': 36 * (197 / 256) * .85 * 32,
        'sourceFrames': ['front', 'right', 'back', 'left'], 'coreSha256': core_hash,
        'note': 'Exact existing DrawSoldier body-section recipe: torso/head + rifle + hands. Current battlefield art has no leg layer. Demonstration mannequins, no combat simulation. Constant body scale; 3D renderer raises any below-ground part of each pose to ground level without rescaling.'}
raw_json = json.dumps(meta, ensure_ascii=False, indent=2) + '\n'
encoded = io.BytesIO(); atlas.save(encoded, format='PNG')
if '--check' in sys.argv:
    assert json.loads((OUT / 'actors-layout.json').read_text()) == meta, 'Atlas metadata drift'
    current = Image.open(OUT / 'actors.png').convert('RGBA')
    assert current.size == atlas.size and current.tobytes() == atlas.tobytes(), 'Atlas render parity drift'
else:
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'actors.png').write_bytes(encoded.getvalue())
    (OUT / 'actors-layout.json').write_text(raw_json)
print(f'PASS {len(frames)} fully composed actor frames, {atlas.width}x{atlas.height}, original recipe at 32x raster; zero missing sprites, exact renderer parity')
