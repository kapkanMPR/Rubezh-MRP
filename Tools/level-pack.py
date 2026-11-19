"""Lossless level storage. Usage: python level-pack.py input.json output.bytes
To edit a packed level: python level-pack.py input.bytes output.json
No precision or geometry is discarded. Runtime ignores legacy terrainVisual.
"""

import sys
import json
import gzip
from pathlib import Path

source, target = map(Path, sys.argv[1:3])
raw = source.read_bytes()
if raw[:2] == b"\x1f\x8b":
    raw = gzip.decompress(raw)
data = json.loads(raw)
if target.suffix == ".bytes":
    data.pop("terrainVisual", None)
    target.write_bytes(
        gzip.compress(
            json.dumps(data, ensure_ascii=False, separators=(",", ":")).encode(),
            compresslevel=9,
            mtime=0,
        )
    )
else:
    target.write_text(
        json.dumps(data, ensure_ascii=False, separators=(",", ":")), encoding="utf-8"
    )
print(target, target.stat().st_size)
