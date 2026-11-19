"""Exact portable counterpart of NativeExteriorPreparation; no resampling."""

from pathlib import Path
import struct
import json
import hashlib
from PIL import Image

root = Path(__file__).resolve().parents[3]
source = root / "Assets/Rubezh/Resources/Exterior"
if not source.exists():
    source = root / "Assets/Rubezh/Editor/LegacySources/Exterior"
target = root / "Assets/Rubezh/Resources/NativeExterior"
target.mkdir(parents=True, exist_ok=True)
size = 2048
pad = 2
page = 0
x = y = pad
row = 0
im = Image.new("RGBA", (size, size))
entries = []
checked = 0
for file in sorted(source.glob("*.bytes"), key=lambda p: p.name):
    data = file.read_bytes()
    w, h, left, top, width, height = struct.unpack("<iiffff", data[:24])
    assert 0 < w <= 256 and 0 < h <= 256 and len(data) == 24 + w * h * 4
    if x + w + pad > size:
        x = pad
        y += row + pad * 2
        row = 0
    if y + h + pad > size:
        im.transpose(Image.Transpose.FLIP_TOP_BOTTOM).save(
            target / f"page-{page:02d}.png"
        )
        page += 1
        im = Image.new("RGBA", (size, size))
        x = y = pad
        row = 0
    rgba = bytearray(data[24:])
    for i in range(0, len(rgba), 4):
        a = rgba[i + 3]
        for ch in range(3):
            rgba[i + ch] = rgba[i + ch] * a // 255
    src = Image.frombytes("RGBA", (w, h), bytes(rgba))
    im.paste(src, (x, y))
    for dx in range(1, pad + 1):
        im.paste(src.crop((0, 0, 1, h)), (x - dx, y))
        im.paste(src.crop((w - 1, 0, w, h)), (x + w - 1 + dx, y))
    for dy in range(1, pad + 1):
        im.paste(im.crop((x - pad, y, x + w + pad, y + 1)), (x - pad, y - dy))
        im.paste(
            im.crop((x - pad, y + h - 1, x + w + pad, y + h)), (x - pad, y + h - 1 + dy)
        )
    entries.append(
        dict(
            sourceSha256=hashlib.sha256(data).hexdigest(),
            key=file.stem,
            page=page,
            x=x,
            y=y,
            w=w,
            h=h,
            left=left,
            top=top,
            width=width,
            height=height,
        )
    )
    x += w + pad * 2
    row = max(row, h)
im.transpose(Image.Transpose.FLIP_TOP_BOTTOM).save(target / f"page-{page:02d}.png")
(target / "catalog.json").write_text(
    json.dumps(
        dict(version=1, size=size, entries=entries), ensure_ascii=False, indent=2
    )
)
# Verify exact premultiplied source bytes + Unity bottom-up UV convention after PNG roundtrip.
for e in entries:
    image = Image.open(target / f"page-{e['page']:02d}.png").transpose(
        Image.Transpose.FLIP_TOP_BOTTOM
    )
    got = image.crop((e["x"], e["y"], e["x"] + e["w"], e["y"] + e["h"])).tobytes()
    data = (source / (e["key"] + ".bytes")).read_bytes()[24:]
    expected = bytearray(data)
    for i in range(0, len(expected), 4):
        for ch in range(3):
            expected[i + ch] = expected[i + ch] * expected[i + 3] // 255
    assert got == expected, e["key"]
    checked += e["w"] * e["h"]
report = dict(
    passed=True,
    sprites=len(entries),
    pages=page + 1,
    pixelsCompared=checked,
    sourceBytes=sum(f.stat().st_size for f in source.glob("*.bytes")),
    pngBytes=sum(f.stat().st_size for f in target.glob("*.png")),
    gpuCompressedBytes=(page + 1) * size * size,
)
out = root / "Build/Reports/AI-0.12.213/Exterior"
out.mkdir(parents=True, exist_ok=True)
(out / "parity.json").write_text(json.dumps(report, indent=2))
print(json.dumps(report))
