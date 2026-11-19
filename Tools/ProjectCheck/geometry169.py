import json
import gzip
import math
import collections
import pathlib

root = pathlib.Path(__file__).resolve().parents[2]
files = list((root / "Assets/Rubezh/Resources/Regenerated").glob("*.bytes")) + list(
    (root / "Assets/Rubezh/Resources/War").glob("*.bytes")
)
counts = collections.Counter()
worst = []
for p in files:
    d = json.loads(gzip.decompress(p.read_bytes()))
    for h in d.get("dynamicBuildings", []):
        counts["buildings"] += 1
        for w in h["walls"]:
            assert 0 < w["height"] <= 10
            for c in w["columns"]:
                for v in c["spans"]:
                    assert all(math.isfinite(v[k]) for k in ("lo", "hi"))
                    assert 0 <= v["lo"] <= v["hi"] <= w["height"] + 0.001, (
                        p,
                        w["id"],
                        v,
                    )
                    counts["wall_spans"] += 1
        cells = {(r["x"], r["y"]): r for r in h["roof"]}
        groups = collections.defaultdict(list)
        before = after = 0
        for (x, y), r in cells.items():
            if r.get("a", 0) <= 0:
                continue
            for direction, line, start, A, B, neighbor in [
                (0, y, x, "a", "b", (x, y - 1)),
                (1, x + 1, y, "b", "c", (x + 1, y)),
                (2, y + 1, x, "d", "c", (x, y + 1)),
                (3, x, y, "a", "d", (x - 1, y)),
            ]:
                if neighbor not in cells:
                    groups[direction, line].append((start, r[A], r[B]))
                    before += 1
        for key, segments in groups.items():
            segments.sort()
            i = 0
            reconstructed = []
            while i < len(segments):
                start, a, b = segments[i]
                i += 1
                end = start + 1
                slope = b - a
                height = b
                while (
                    i < len(segments)
                    and segments[i][0] == end
                    and abs(segments[i][1] - height) < 1e-5
                    and abs(segments[i][2] - segments[i][1] - slope) < 1e-5
                ):
                    height = segments[i][2]
                    end += 1
                    i += 1
                for k in range(start, end):
                    reconstructed.append(
                        (
                            k,
                            a + (height - a) * (k - start) / (end - start),
                            a + (height - a) * (k + 1 - start) / (end - start),
                        )
                    )
                after += 1
            assert len(segments) == len(reconstructed)
            for old, new in zip(segments, reconstructed):
                assert (
                    old[0] == new[0]
                    and max(abs(old[j] - new[j]) for j in (1, 2)) < 0.001
                )
        counts["roof_edges_before"] += before
        counts["roof_edges_after"] += after
        worst.append((before, after, p.name))
counts["levels"] = len(files)
print(dict(counts))
print("largest roofs", sorted(worst, reverse=True)[:5])
output = root / "Validation/Repair169"
output.mkdir(parents=True, exist_ok=True)
(output / "geometry-results.json").write_text(json.dumps(dict(counts), indent=2))
