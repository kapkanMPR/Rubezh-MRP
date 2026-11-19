"""Portable wall bake/parity check. --write updates native PNGs, never their .meta.
Uses compiled production C# pixel recipe; Pillow only encodes the exact resulting bytes.
Unity Editor runs NativeWallMaterialPreparation for normal content preparation.
"""

from pathlib import Path
import gzip
import struct
import json
import re
import hashlib
import shutil
import subprocess
import tempfile
import sys
import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from rubezh_dotnet import find_sdk

root = Path(__file__).resolve().parents[2]
out = root / "Build/Reports/AI-0.12.211/WallBake"
out.mkdir(parents=True, exist_ok=True)
native = root / "Assets/Rubezh/NativeContent"
core = root / "Assets/Rubezh/Core"
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
source = (root / "Assets/Rubezh/Runtime/Levels/StandaloneWallMaterials.cs").read_text()
start = source.index("  public static void BakePixels")
brace = source.index("{", start)
depth = 1
end = brace + 1
while depth:
    depth += (source[end] == "{") - (source[end] == "}")
    end += 1
method = source[start:end]
ids = set()
for line in (core / "StudioCatalog.Generated.cs").read_text().splitlines():
    if any('d.Add("' + k + '"' in line for k in ["S30-01", "S30-02", "wide-S12-02-x"]):
        ids.update(map(int, re.findall(r"new StudioSprite\((\d+)", line)))
entries = {
    e["id"]: e
    for e in json.loads((native / "catalog.json").read_text())["entries"]
    if e["id"] in ids
}
assert len(entries) == len(ids)
material = (
    root / "Assets/Rubezh/Resources/BuildingMaterialData/c68531d1147509105a3d4aa9.bytes"
)
m = np.asarray(Image.open(material).convert("RGBA"))
mh, mw = m.shape[:2]
work = Path(tempfile.mkdtemp(prefix="wall211-"))
(work / "material.rgba").write_bytes(m.tobytes())
records = []
for page in sorted({e["page"] for e in entries.values()}):
    package = root / f"Assets/Rubezh/Editor/LegacySources/SpritePacks/p{page:04d}.bytes"
    b = gzip.decompress(package.read_bytes())
    pos = 0

    def integer():
        global pos
        v = struct.unpack_from("<i", b, pos)[0]
        pos += 4
        return v

    fmt, count = integer(), integer()
    assert fmt == 0x32475152
    for _ in range(count):
        id, levels = integer(), integer()
        for mip in range(levels):
            w, h = integer(), integer()
            data = b[pos : pos + w * h * 4]
            pos += w * h * 4
            if id not in entries or mip:
                continue
            e = entries[id]
            assert (w, h) == (e["ow"], e["oh"])
            (work / f"{id}.raw").write_bytes(data)
            records.append((id, w, h, sha(package)))
(work / "list.txt").write_text("\n".join(f"{id} {w} {h}" for id, w, h, _ in records))
# Compile the exact production method, no reimplementation used to produce assets.
cs = """using System;using System.IO;class Check {\n""" + method + """
static void Main(string[] args){string dir=args[0];int mw=int.Parse(args[1]),mh=int.Parse(args[2]);byte[] raw=File.ReadAllBytes(Path.Combine(dir,"material.rgba"));uint[] m=new uint[mw*mh];Buffer.BlockCopy(raw,0,m,0,raw.Length);
foreach(string line in File.ReadAllLines(Path.Combine(dir,"list.txt"))){var a=line.Split(' ');string path=Path.Combine(dir,a[0]+".raw");byte[] p=File.ReadAllBytes(path);BakePixels(p,int.Parse(a[1]),int.Parse(a[2]),m,mw,mh);File.WriteAllBytes(path+".baked",p);}
}}"""
(work / "Check.cs").write_text(cs)
dotnet, sdk, ref, runtime = find_sdk()
cmd = (
    [
        str(dotnet),
        str(sdk / "Roslyn/bincore/csc.dll"),
        "-nologo",
        "-out:" + str(work / "Check.dll"),
        "-target:exe",
    ]
    + ["-r:" + str(x) for x in ref.glob("*.dll")]
    + [str(work / "Check.cs")]
)
subprocess.run(cmd, check=True, capture_output=True)
(work / "Check.runtimeconfig.json").write_text(
    json.dumps(
        {
            "runtimeOptions": {
                "tfm": "net8.0",
                "framework": {"name": "Microsoft.NETCore.App", "version": runtime},
            }
        }
    )
)
subprocess.run(
    [str(dotnet), str(work / "Check.dll"), str(work), str(mw), str(mh)], check=True
)
manifest = []
total = 0
for id, w, h, packageHash in records:
    a = (
        np.frombuffer((work / f"{id}.raw").read_bytes(), np.uint8)
        .reshape(h, w, 4)
        .copy()
    )
    baked = np.frombuffer((work / f"{id}.raw.baked").read_bytes(), np.uint8).reshape(
        h, w, 4
    )
    # Independent vector reference of pre-211 Graphics.ApplyWallMaterial, explicit float32 operations.
    x = np.arange(w, dtype=np.float32)
    y = np.arange(h, dtype=np.float32)
    u = np.minimum(
        mw - 1,
        (
            (
                np.float32(0.04)
                + np.float32(0.92) * (x + np.float32(0.5)) / np.float32(w)
            )
            * np.float32(mw)
        ).astype(int),
    )
    v = np.minimum(
        mh - 1,
        (
            (
                np.float32(0.06)
                + np.float32(0.88) * (y + np.float32(0.5)) / np.float32(h)
            )
            * np.float32(mh)
        ).astype(int),
    )
    alpha = a[:, :, 3].astype(np.int64)
    light = (
        (
            a[:, :, 0].astype(np.int64) * 77
            + a[:, :, 1].astype(np.int64) * 150
            + a[:, :, 2].astype(np.int64) * 29
        )
        * 255
        // np.maximum(1, alpha)
        // 256
    )
    shade = np.maximum(
        np.float32(0.55),
        np.minimum(
            np.float32(1.15),
            np.float32(0.55) + light.astype(np.float32) / np.float32(230),
        ),
    )
    expected = a.copy()
    for c in range(3):
        value = (
            m[v[:, None], u[None, :], c].astype(np.float32)
            * shade
            * alpha.astype(np.float32)
            / np.float32(255)
        ).astype(int)
        expected[:, :, c] = np.where(alpha == 0, a[:, :, c], np.minimum(alpha, value))
    assert np.array_equal(
        expected, baked
    ), f"Legacy pixel parity failed {id}: {np.max(abs(expected.astype(int)-baked.astype(int)))}"
    assert np.array_equal(a[:, :, 3], baked[:, :, 3])
    e = entries[id]
    crop = baked[e["oy"] : e["oy"] + e["h"], e["ox"] : e["ox"] + e["w"]][::-1]
    png = native / f'Sprites/p{e["page"]:04d}/s{id:05d}.png'
    meta = Path(str(png) + ".meta")
    before = sha(meta)
    if "--write" in sys.argv:
        Image.fromarray(crop).save(png)
    assert np.array_equal(
        np.asarray(Image.open(png).convert("RGBA")), crop
    ), f"PNG not baked: {id}"
    assert before == sha(meta)
    manifest.append({"id": id, "sourceHash": packageHash, "pngHash": sha(png)})
    total += w * h
assert len(records) == len(ids)
manifest = {
    "recipe": "wall-material-211-v1",
    "materialHash": sha(material),
    "entries": sorted(manifest, key=lambda e: e["id"]),
}
if "--write" in sys.argv:
    (native / "wall-materials.json").write_text(json.dumps(manifest, indent=2))
report = {
    "passed": True,
    "sprites": len(records),
    "pixelsCompared": total,
    "exactLegacyPixelParity": True,
    "alphaPreserved": True,
    "metaGuidPreserved": True,
    "pngOrientation": "top-down; exact decoded equality to bottom-up source crop reversed once",
}
(out / "result.json").write_text(json.dumps(report, indent=2))
print(json.dumps(report))
shutil.rmtree(work)
