"""Static release guards. This does not compile shaders or run Unity imports."""

from pathlib import Path
import hashlib
import json
import re

root = Path(__file__).resolve().parents[3]
out = root / "Build/Reports/Recovery222"
out.mkdir(parents=True, exist_ok=True)
errors = []
shaders = sorted(
    list((root / "Assets").rglob("*.shader")) + list((root / "Assets").rglob("*.cginc"))
)
reserved = {
    "point",
    "line",
    "triangle",
    "lineadj",
    "triangleadj",
    "class",
    "namespace",
    "template",
    "interface",
    "register",
    "cbuffer",
    "technique",
    "pass",
    "sample",
    "linear",
    "centroid",
    "nointerpolation",
    "noperspective",
    "precise",
}
for path in shaders:
    source = re.sub(
        r'/\*.*?\*/|//[^\n]*|"(?:\\.|[^"\\])*"', "", path.read_text(), flags=re.S
    )
    for match in re.finditer(
        r"\b(?:float|half|fixed|int|uint|bool)(?:[1-4](?:x[1-4])?)?\s+(\w+)\b", source
    ):
        if match[1] in reserved:
            errors.append(
                f"{path.relative_to(root)} declares reserved HLSL name {match[1]}"
            )

version_source = (root / "Assets/Rubezh/Runtime/RubezhLoading.cs").read_text()
version = re.search(r'Current\s*=\s*"([^"]+)"', version_source)[1]
code = int(re.search(r"AndroidCode\s*=\s*(\d+)", version_source)[1])
settings = (root / "ProjectSettings/ProjectSettings.asset").read_text()
if re.search(r"^  bundleVersion: (.+)$", settings, re.M)[1] != version:
    errors.append("PlayerSettings bundleVersion differs from RubezhVersion")
if int(re.search(r"^  AndroidBundleVersionCode: (\d+)$", settings, re.M)[1]) != code:
    errors.append("Android bundleVersionCode differs from RubezhVersion")

shape = root / "Assets/Rubezh/Editor/NativeAtlasShapeFix.cs"
if not shape.exists():
    errors.append("Native atlas Texture2D repair is absent")
for name in ("NativeUiPreparation.cs", "NativeExteriorPreparation.cs"):
    source = (root / "Assets/Rubezh/Editor" / name).read_text()
    if not re.search(
        r"\bi\s*\.\s*textureShape\s*=\s*TextureImporterShape\s*\.\s*Texture2D\s*;",
        source,
    ):
        errors.append(name + " does not set Texture2D shape")

report = {
    "version": version,
    "androidCode": code,
    "shaderSourcesInspected": len(shaders),
    "passed": not errors,
    "errors": errors,
    "scope": "Static HLSL reserved-name and release configuration guards only; no Unity shader compilation or APK execution.",
    "sha256": {
        p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
        for p in shaders
    },
}
(out / "build-source-audit.json").write_text(
    json.dumps(report, ensure_ascii=False, indent=2)
)
print(
    json.dumps(
        {k: v for k, v in report.items() if k != "sha256"}, ensure_ascii=False, indent=2
    )
)
raise SystemExit(bool(errors))
