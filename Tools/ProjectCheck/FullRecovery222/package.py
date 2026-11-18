"""Package/verify recovered source project; this does NOT invoke Unity or build an APK."""
import argparse
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import zipfile

VERSION = "AI-0.12.222-RecoveredFull"
PREFIX = "Rubezh-0.12.222-RecoveredFull/"
MANIFEST = "MANIFEST-222.json"
OMIT_PARTS = {".git", "__pycache__", "bin", "obj", "Library", "Temp", "Logs"}


def sha(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def included(path, root):
    rel = path.relative_to(root)
    if not path.is_file() or any(p in OMIT_PARTS for p in rel.parts):
        return False
    if rel.as_posix() == MANIFEST:
        return False
    if rel.parts[0] == "Build":
        if path.suffix in {".dll", ".exe", ".pdb"} or path.name.endswith((".runtimeconfig.json", ".deps.json")):
            return False
    return not path.name.endswith((".pyc", ".zip"))


def verify(root):
    manifest = json.loads((root / MANIFEST).read_text(encoding="utf-8"))
    errors = []
    for item in manifest["files"]:
        path = root / item["path"]
        if not path.is_file():
            errors.append("missing: " + item["path"])
        elif path.stat().st_size != item["bytes"] or sha(path) != item["sha256"]:
            errors.append("changed: " + item["path"])
    result = {"verified": not errors, "files": len(manifest["files"]), "errors": errors}
    print(json.dumps(result, ensure_ascii=False, indent=2), flush=True)
    return 0 if not errors else 1


def source_copy(rel):
    if not rel.startswith("Assets/"):
        return True
    if "/Editor/LegacySources/" in rel and rel.endswith(".bytes"):
        return False
    if "/Resources/HybridRange/" in rel:
        return True
    return Path(rel).suffix.lower() in {
        ".cs", ".meta", ".shader", ".cginc", ".hlsl", ".compute", ".asmdef", ".asmref",
        ".json", ".txt", ".md", ".uxml", ".uss", ".xml", ".yaml", ".yml", ".csv",
        ".unity", ".mat", ".asset", ".bytes", ".inputactions", ".sh", ".ps1", ".py"
    }


def write_archive(root, path, names, expected):
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as z:
        for rel in sorted(names):
            z.write(root / rel, PREFIX + rel)
    checked = 0
    with zipfile.ZipFile(path) as z:
        infos = z.infolist()
        if len({entry.filename for entry in infos}) != len(infos):
            raise RuntimeError("Duplicate ZIP entry " + path.name)
        for entry in infos:
            rel = entry.filename[len(PREFIX):]
            h = hashlib.sha256()
            with z.open(entry) as stream:
                for block in iter(lambda: stream.read(1024 * 1024), b""):
                    h.update(block)
            if h.hexdigest() != expected[rel]:
                raise RuntimeError("ZIP SHA256 mismatch " + rel)
            checked += 1
    result = {"filename": path.name, "bytes": path.stat().st_size, "sha256": sha(path), "verifiedEntries": checked}
    if result["bytes"] >= 512 * 1024 * 1024:
        raise RuntimeError("Archive exceeds transfer limit: " + path.name)
    print(json.dumps(result), flush=True)
    return result


def package(root, out):
    out.mkdir(parents=True, exist_ok=True)
    paths = sorted(p for p in root.rglob("*") if included(p, root))
    records = [{"path": p.relative_to(root).as_posix(), "bytes": p.stat().st_size, "sha256": sha(p)} for p in paths]
    manifest = {
        "version": VERSION, "androidVersionCode": 323, "unityEditor": "6000.3.16f1",
        "createdUtc": datetime.now(timezone.utc).isoformat(),
        "artifactKind": "Complete source project; NOT a compiled APK",
        "unityEditorExecuted": False, "androidBuildExecuted": False,
        "derivedNativeContent": "Requires real Unity Editor preparation; source inputs included",
        "files": records,
        "manifestSelfExcluded": True,
    }
    (root / MANIFEST).write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    expected = {item["path"]: item["sha256"] for item in records}
    expected[MANIFEST] = sha(root / MANIFEST)
    sizes = {item["path"]: item["bytes"] for item in records}
    sizes[MANIFEST] = (root / MANIFEST).stat().st_size
    # Assets and their .meta travel together. All four ordinary ZIPs are required.
    groups = {}
    for rel in expected:
        key = rel[:-5] if rel.endswith(".meta") else rel
        groups.setdefault(key, []).append(rel)
    parts = [[] for _ in range(4)]
    totals = [0] * 4
    for key, names in sorted(groups.items(), key=lambda pair: (-sum(sizes[x] for x in pair[1]), pair[0])):
        i = min(range(4), key=lambda index: totals[index])
        parts[i].extend(names)
        totals[i] += sum(sizes[x] for x in names)
    all_entries = [name for names in parts for name in names]
    if len(all_entries) != len(set(all_entries)) or set(all_entries) != set(expected):
        raise RuntimeError("Incomplete or overlapping partition")
    jobs = [(out / f"Rubezh-0.12.222-FULL-part{i+1}-of4.zip", names) for i, names in enumerate(parts)]
    jobs.append((out / "Rubezh-222-Source-and-Checks.zip", [rel for rel in expected if source_copy(rel)]))
    with ThreadPoolExecutor(max_workers=4) as pool:
        futures = [pool.submit(write_archive, root, path, names, expected) for path, names in jobs]
        results = [future.result() for future in futures]
    report = {
        "version": VERSION, "fileCount": len(expected), "uncompressedBytes": sum(sizes.values()),
        "fullArchiveUnionVerified": True, "allEntriesSha256Verified": True,
        "fullArchives": results[:4], "sourceCheckpoint": results[4],
        "note": "Extract all four FULL ZIPs to the same directory. Source checkpoint is not a full project."
    }
    (out / "Rubezh-222-Archive-Verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    (out / "Rubezh-222-SHA256.txt").write_text("".join(f'{r["sha256"]}  {r["filename"]}\n' for r in results), encoding="utf-8")
    print(json.dumps({"finished": True, "files": len(expected), "archives": len(results)}), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", type=Path)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[3])
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    if args.verify:
        raise SystemExit(verify(args.verify.resolve()))
    if args.out is None:
        parser.error("--out is required for packaging")
    package(args.root.resolve(), args.out.resolve())
