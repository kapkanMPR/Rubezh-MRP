Optimization220 exact static pixel cache validation

Run with .NET SDK 8:
  DOTNET_ROOT=/path/to/dotnet python Tools/ProjectCheck/Optimization220/run.py
  DOTNET_ROOT=/path/to/dotnet python Tools/ProjectCheck/Optimization220/run.py --unity-compile

462 production-method contract checks cover:
- Cache miss: original ordered meshes render into the original target, then a
  raw texture copy captures pixels before actors/effects/roofs.
- Cache hit: only raw texture copy runs for static scenery; roofs stay live.
- Failed rendering does not validate the captured cache.
- Geometry/view, object masks, building-mask content, material replacement,
  page uploads, regional uploads, background color, output replacement/resize,
  and lost RenderTexture allocations invalidate cached pixels.
- Cache preserves target dimensions, ARGB32 linear format and single sampling.
- Unsupported copies, sRGB, MSAA, and incompatible formats use original draws.
- Unsupported hardware does not rebuild the geometry command buffer per frame.
- Disposal detaches command buffers and releases the cache target.

The suite records actual production CommandBuffer call sequences using portable
Unity contracts. It does not execute Unity or the GPU. In particular Android
Vulkan CopyTexture scheduling inside the camera pass, device output pixels and
performance require the device A/B test; this suite makes no FPS claim.
