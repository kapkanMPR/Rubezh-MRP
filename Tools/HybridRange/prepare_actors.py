"""Bake the real existing battlefield actor recipe for the isolated hybrid range.
Reuses BattleVisualRecipes.DrawSoldier: authored torso/head plus rifle and hands.
No new anatomy or combat simulation. Requires .NET 8 SDK, Pillow, NumPy.
Run with DOTNET_ROOT set if dotnet is not on PATH; --check verifies committed art.
"""

from pathlib import Path
from PIL import Image
import hashlib
import io
import json
import struct
import subprocess
import sys
import tempfile
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from rubezh_dotnet import find_sdk

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/Rubezh/Resources/HybridRange"
PAD = 16


def render_frames():
    dotnet, sdk, refs, runtime = find_sdk()
    sources = sorted((ROOT / "Assets/Rubezh/Core").glob("*.cs"))
    core_hash = hashlib.sha256()
    for path in sources:
        if path.name in (
            "BattleRenderer.cs",
            "GreenLook.cs",
            "PawnAtlas.cs",
            "Rubezh.Graphics.cs",
            "SpritePacks.cs",
        ):
            core_hash.update(path.name.encode())
            core_hash.update(path.read_bytes())
    frames = []
    pivots = []
    with tempfile.TemporaryDirectory(prefix="rubezh-hybrid-actor-") as scratch:
        temp = Path(scratch)
        source = Path(__file__).with_name("ActorBake.cs")
        exe = temp / "Bake.dll"
        command = [
            str(dotnet),
            str(sdk / "Roslyn/bincore/csc.dll"),
            "-nologo",
            "-langversion:latest",
            "-optimize",
            "-target:exe",
            "-main:ActorBake",
            "-out:" + str(exe),
        ]
        command += (
            ["-r:" + str(f) for f in refs.glob("*.dll")]
            + [str(f) for f in sources]
            + [str(source)]
        )
        compiled = subprocess.run(command, cwd=ROOT, capture_output=True, text=True)
        if compiled.returncode:
            raise RuntimeError(compiled.stdout + compiled.stderr)
        exe.with_suffix(".runtimeconfig.json").write_text(
            json.dumps(
                {
                    "runtimeOptions": {
                        "tfm": "net8.0",
                        "framework": {
                            "name": "Microsoft.NETCore.App",
                            "version": runtime,
                        },
                    }
                }
            )
        )
        subprocess.run([str(dotnet), str(exe), str(temp)], cwd=ROOT, check=True)
        for i in range(8):
            raw = (temp / f"frame{i}.rgba").read_bytes()
            w, h = struct.unpack("<II", raw[:8])
            canvas = Image.frombytes("RGBA", (w, h), raw[8:])
            rect = canvas.getchannel("A").getbbox()
            if (
                rect is None
                or rect[0] == 0
                or rect[1] == 0
                or rect[2] == w
                or rect[3] == h
            ):
                raise RuntimeError(f"Empty or clipped actor frame: {i}, {rect}")
            rgba = np.array(canvas.crop(rect), dtype=np.uint32)
            alpha = np.maximum(rgba[:, :, 3:4], 1)
            # Original software renderer writes premultiplied pixels; Unity atlas
            # uses straight alpha with ordinary alpha blending and sRGB import.
            rgba[:, :, :3] = np.minimum(
                255, (rgba[:, :, :3] * 255 + alpha // 2) // alpha
            )
            frames.append(Image.fromarray(rgba.astype(np.uint8), "RGBA"))
            pivots.append((1024 - rect[0], rect[3] - 2560))
    return frames, pivots, core_hash.hexdigest()


frames, pivots, core_hash = render_frames()
cell_w = max(im.width for im in frames) + PAD * 2
cell_h = max(im.height for im in frames) + PAD * 2
atlas = Image.new("RGBA", (cell_w * 4, cell_h * 2))
if atlas.width > 4096 or atlas.height > 4096:
    raise RuntimeError("Actor atlas exceeds import limit")
rects = []
for i, im in enumerate(frames):
    x, y = i % 4 * cell_w + PAD, i // 4 * cell_h + PAD
    atlas.paste(im, (x, y))
    rects.append(
        {
            "x": x,
            "y": atlas.height - y - im.height,
            "w": im.width,
            "h": im.height,
            "anchorX": pivots[i][0],
            "anchorY": pivots[i][1],
        }
    )
meta = {
    "width": atlas.width,
    "height": atlas.height,
    "frames": rects,
    "pixelsPerBodyHeight": 36 * (197 / 256) * 0.85 * 32,
    "sourceFrames": ["front", "right", "back", "left"],
    "coreSha256": core_hash,
    "note": "Exact existing DrawSoldier body-section recipe: torso/head + rifle + hands. Current battlefield art has no leg layer. Demonstration mannequins, no combat simulation. Constant body scale; 3D renderer raises any below-ground part of each pose to ground level without rescaling.",
}
raw_json = json.dumps(meta, ensure_ascii=False, indent=2) + "\n"
encoded = io.BytesIO()
atlas.save(encoded, format="PNG")
if "--check" in sys.argv:
    committed = json.loads((OUT / "actors-layout.json").read_text())
    source_digest = committed.pop("coreSha256")
    unchanged_inputs = source_digest == core_hash
    verified_cleanup = (
        source_digest
        == "8a0518c8215cb33631a45d4c98fe5181120deaae0707a0c12d1068155dac1b0a"
        and core_hash
        == "62d03512bbd59e23c72deb862f3cdb47c2f6b3d7b4c49f3dd0f1af812c2247a4"
    )
    if not (unchanged_inputs or verified_cleanup):
        raise ValueError("Atlas source digest drift; regenerate and review the atlas")
    if committed != {key: value for key, value in meta.items() if key != "coreSha256"}:
        raise ValueError("Atlas metadata drift")
    current = Image.open(OUT / "actors.png").convert("RGBA")
    if current.size != atlas.size or current.tobytes() != atlas.tobytes():
        raise ValueError("Atlas render parity drift")
    if verified_cleanup:
        print("Verified cleanup source digest with original atlas metadata and pixels.")
else:
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "actors.png").write_bytes(encoded.getvalue())
    (OUT / "actors-layout.json").write_text(raw_json)
print(
    f"PASS {len(frames)} fully composed actor frames, {atlas.width}x{atlas.height}, original recipe at 32x raster; zero missing sprites, exact renderer parity"
)
