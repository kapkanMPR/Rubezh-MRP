from pathlib import Path
import gzip
import struct
import json
import re
import hashlib

r = Path(__file__).resolve().parents[2]
a = r / "Assets/Rubezh"
s = (a / "Core/SpritePackIndex.Generated.cs").read_text()
owner = list(map(int, re.search(r"Page\s*=\s*\{([^}]+)", s)[1].split(",")))
alias = {int(i): int(j) for i, j in re.findall(r"case (\d+):\s*return (\d+);", s)}
expected = json.loads((r / "Tools/ProjectCheck/payload-sha256.json").read_text())
found = {}
pages = set()
for p in (a / "Editor/LegacySources/SpritePacks").glob("*.bytes"):
    page = int(p.stem[1:])
    pages.add(page)
    raw = gzip.decompress(p.read_bytes())
    magic, n = struct.unpack_from("<ii", raw)
    assert magic in (0x50475152, 0x32475152) and 1 <= n <= 32
    o = 8
    for _ in range(n):
        start = o
        i, levels = struct.unpack_from("<ii", raw, o)
        o += 8
        assert 1 <= levels <= 32 and owner[i] == page and i not in found
        for _ in range(levels):
            w, h = struct.unpack_from("<ii", raw, o)
            assert 0 < w <= 8192 and 0 < h <= 8192
            o += 8 + w * h * 4
        assert o <= len(raw)
        digest = hashlib.sha256(raw[start:o]).hexdigest()
        assert expected[str(i)] == digest
        found[i] = digest
    assert o == len(raw)
assert set(found) == set(map(int, expected))
retired = set(range(130, 138)) | set(range(139, 211)) | set(range(6831, 7459))
assert not (set(found) & retired), "retired pawn payload remains"
assert not (a / "Art/LoadoutSoldiers").exists(), "retired source atlas remains"
for i, p in enumerate(owner):
    if p >= 0:
        assert i in found and p in pages
for i, j in alias.items():
    assert j in found and j not in alias, (i, j)
refs = set()
for catalog in (a / "Core").glob("*.Generated.cs"):
    refs.update(map(int, re.findall(r"new StudioSprite\((\d+)", catalog.read_text())))
# Runtime drops must survive atlas cleanup, even when absent at battle start.
refs.update(
    [
        219,
        220,
        221,
        222,
        7459,
        7460,
        7861,
        126,
        127,
        6824,
        6825,
        6826,
        6827,
        6828,
        6829,
        6830,
    ]
)
for i in refs:
    assert alias.get(i, i) in found, i
art = a / "Resources/FieldCamp/Art"
cat = json.loads((art.parent / "catalog.json").read_text())
for item in cat["items"]:
    assert (art / item["img"]).exists(), item
active = {57, 59, 62, 63, 69, 82, 88, 106, 107, 109, 116, 131, 133, 134, 136, 143}
for h in active:
    assert (art / f"appearance/head-{h}-front.png").exists()
    for f in ["front", "back", "left", "right"]:
        assert (art / f"pawns/green-{h}-{f}.png").exists()
assert len(list((art / "appearance").glob("head-*.png"))) == 16
assert not list((art / "appearance").glob("body-*.png"))
assert len(list((a / "Art/PawnAtlases").glob("*.png"))) == 28
print(
    "PASS",
    len(found),
    "verified sprite payloads,",
    len(pages),
    "pages,",
    len(alias),
    "aliases,",
    len(refs),
    "catalog references; 28 whole pawn atlases",
)
# Serialized references and the single production scene.
guids = {}
for p in (r / "Assets").rglob("*.meta"):
    assert p.with_suffix("").exists(), ("orphan meta", p)
    m = re.search(r"^guid: (\w+)", p.read_text(), re.M)
    if m:
        assert m[1] not in guids, ("duplicate guid", p, guids.get(m[1]))
        guids[m[1]] = p
for base in [r / "Assets", r / "ProjectSettings"]:
    for p in base.rglob("*"):
        if p.suffix not in {
            ".asset",
            ".unity",
            ".prefab",
            ".mat",
            ".uss",
            ".tss",
            ".uxml",
            ".controller",
        }:
            continue
        for g in re.findall(r"guid:\s*([0-9a-f]{32})", p.read_text(errors="replace")):
            assert g in guids or g.startswith("0000000000000000"), (
                "missing reference",
                p,
                g,
            )
assert (
    "Assets/Scenes/Battle.unity"
    in (r / "ProjectSettings/EditorBuildSettings.asset").read_text()
)
print("PASS serialized Unity references and build scene;", len(guids), "unique GUIDs")
# Unity main objects must keep their file names; display titles are separate.
for p in (a / "Resources/Levels").glob("*.asset"):
    m = re.search(r"^  m_Name: (.*)$", p.read_text(), re.M)
    assert m and m[1] == p.stem, ("Unity main object name mismatch", p)
# Validate PCM headers and complete data, avoiding inconsistent Ogg end granules.
import wave

tracks = list((a / "Resources/Music").glob("track*.wav"))
assert len(tracks) == 9
for p in tracks:
    with wave.open(str(p), "rb") as w:
        assert (
            len(w.readframes(w.getnframes()))
            == w.getnframes() * w.getnchannels() * w.getsampwidth()
        ), p
assert not list((a / "Resources/Music").glob("track*.ogg"))
print("PASS level object names and 9 complete PCM music streams")
