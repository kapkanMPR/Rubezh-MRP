"""Copy authored, flat material textures for the hybrid range without resampling.

Run from any directory. Source bytes remain untouched; generated .meta enables
mips and trilinear sampling for shrinking on 3D surfaces. Masonry's existing
painted top/bottom trim is excluded by a shader UV rectangle, not raster editing.
"""
from pathlib import Path
import hashlib, json, shutil
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/Rubezh/Resources/HybridRange"
SOURCES = {
    "surface-wood": "Assets/Rubezh/Resources/ClosureWood.png",
    "surface-roof": "Assets/Rubezh/Resources/RoofSurface.png",
    "surface-rough": "Assets/Rubezh/Art/BuildingMaterialsSource/4816e7b3b4c05e4e0173dc7a.png",
    "surface-brick": "Assets/Rubezh/Art/BuildingMaterialsSource/fcbe705eaef95b9f7183621f.png",
}
def guid(name):
    return hashlib.sha256(("Rubezh-Hybrid221:" + name).encode()).hexdigest()[:32]
def metadata(name):
    return f"""fileFormatVersion: 2
guid: {guid(name)}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: 1
  isReadable: 0
  streamingMipmaps: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 2
    aniso: 4
    mipBias: 0
    wrapU: 0
    wrapV: 0
    wrapW: 0
  nPOTScale: 0
  textureType: 0
  textureShape: 1
  alphaSource: 0
  alphaIsTransparency: 0
  textureCompression: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 100
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
"""
def main():
    OUT.mkdir(parents=True, exist_ok=True)
    provenance = []
    for name, source in SOURCES.items():
        origin = ROOT / source
        target = OUT / (name + ".png")
        shutil.copyfile(origin, target)
        target.with_suffix(".png.meta").write_text(metadata(name), encoding="utf-8")
        digest = hashlib.sha256(target.read_bytes()).hexdigest()
        assert digest == hashlib.sha256(origin.read_bytes()).hexdigest()
        provenance.append({"resource": "HybridRange/" + name, "source": source, "sha256": digest, "resampled": False})
    (Path(__file__).parent / "materials-provenance.json").write_text(json.dumps(provenance, indent=2) + "\n", encoding="utf-8")
    print("Copied 4 authored surface textures unchanged; mipmaps enabled, no resizing.")
if __name__ == "__main__":
    main()
