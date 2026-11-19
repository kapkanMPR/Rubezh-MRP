"""Decode actual authored story20 PNGs with Pillow and emit native-metadata fixture.
This validates source dimensions/channels and core native-map loading, not Unity imports.
"""

from pathlib import Path
import base64
import gzip
import hashlib
import io
import json
import math
import numpy as np
from PIL import Image

root = Path(__file__).resolve().parents[3]
out = root / "Build/Reports/AI-0.12.213/Buildings"
out.mkdir(parents=True, exist_ok=True)
source = root / "Assets/Rubezh/Resources/Regenerated/Campaign_20.bytes"
original = source.read_bytes()
data = json.loads(gzip.decompress(original))
key = hashlib.sha256(original).hexdigest()
rows = []
decoded_bytes = 0
for building in data["dynamicBuildings"]:
    for layer in building["layers"]:
        v = layer["picture"]
        assert not v.get("recipe"), "story20 fixture assumes authoredPNG"
        c = Image.open(io.BytesIO(base64.b64decode(v["png"]))).convert("RGBA")
        d = Image.open(io.BytesIO(base64.b64decode(v["depthPng"]))).convert("RGBA")
        assert c.size == d.size
        pw = min(
            c.width, max(1, math.ceil(float(np.float32(v["width"]) * np.float32(4.8))))
        )
        ph = min(
            c.height,
            max(1, math.ceil(float(np.float32(v["height"]) * np.float32(4.8)))),
        )
        xs = np.minimum(
            c.width - 1,
            (
                (np.arange(pw, dtype=np.float32) + np.float32(0.5))
                * np.float32(c.width)
                / np.float32(pw)
            ).astype(np.int64),
        )
        ys = np.minimum(
            c.height - 1,
            (
                (np.arange(ph, dtype=np.float32) + np.float32(0.5))
                * np.float32(c.height)
                / np.float32(ph)
            ).astype(np.int64),
        )
        rgba = np.array(c)[ys[:, None], xs[None, :]]
        dep = np.array(d)[ys[:, None], xs[None, :]]
        depth = (
            dep[:, :, 0].astype(np.uint16) + dep[:, :, 1].astype(np.uint16) * 256
        ).astype(np.float32) / np.float32(64)
        assert np.isfinite(depth).all()
        native = key + ":" + str(len(rows))
        rows.append(
            {
                "key": native,
                "sourceWidth": c.width,
                "sourceHeight": c.height,
                "width": pw,
                "height": ph,
                "rgbaSha256": hashlib.sha256(rgba.tobytes()).hexdigest(),
                "depthSha256": hashlib.sha256(depth.tobytes()).hexdigest(),
                "material": (
                    building["walls"][layer["wall"]]["material"]
                    if layer["wall"] >= 0
                    else "static"
                ),
            }
        )
        decoded_bytes += pw * ph * 8
        v.update(
            nativeKey=native,
            pixelWidth=pw,
            pixelHeight=ph,
            png=None,
            depthPng=None,
            recipe=None,
            clipTable=None,
        )
        if len(rows) % 200 == 0:
            print("validated source layers", len(rows), flush=True)
# Same metadata transformation as Editor preparation; preserve geometry/combat/roofs verbatim.
data["terrainVisual"] = None
data["proceduralVisuals"] = None
(out / "story20-native-fixture.json").write_text(
    json.dumps(data, separators=(",", ":"))
)
report = {
    "status": "PASS",
    "sourceSha256": hashlib.sha256(original).hexdigest(),
    "layers": len(rows),
    "legacyDecodedPixelDepthBytes": decoded_bytes,
    "nativeCpuPixelDepthBytes": 0,
    "fixtureBytes": (out / "story20-native-fixture.json").stat().st_size,
    "materials": sorted(set(r["material"] for r in rows)),
    "note": "Pillow decoding and native metadata fixture only; not Unity texture import or GPU visual test",
    "entries": rows,
}
(out / "story20-source-parity.json").write_text(json.dumps(report, indent=2))
print(json.dumps({k: v for k, v in report.items() if k != "entries"}, indent=2))
