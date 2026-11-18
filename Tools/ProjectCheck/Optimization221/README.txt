Optimization221 rendering regression checks

Requirements: .NET SDK 8 and Python 3. Run from any directory:
  DOTNET_ROOT=/path/to/dotnet python Tools/ProjectCheck/Optimization221/run.py
  DOTNET_ROOT=/path/to/dotnet python Tools/ProjectCheck/Optimization221/run.py --unity-compile

The main suite executes production methods against portable Unity contracts:
- The static cache renders into a separate full-size ARGB32 Linear texture via
  its own camera before the main render. The main pass receives one ordinary
  fullscreen draw. There are no in-camera CopyTexture operations.
- A changed camera view bypasses the cache and draws geometry directly in the
  main pass. Continued motion adds no cache camera/composite overhead and never
  displays stale cached pixels. The next stable view captures once.
- Unchanged cache skips its camera; geometry, mask, atlas upload,
  material, background, target size and allocation changes invalidate it.
- Cache camera failure or missing composite shader preserves full-scene draw
  fallback. A failed graphics path is latched off, not retried every frame.
- Live roofs remain in the main camera suffix. Static cache camera culling is
  zero, so live actors and effects are excluded from cached pixels.
- A synthetic sprite/solid workload preserves every vertex, vertex color,
  alpha, depth mode, clip range and triangle index while reducing 320 ordered
  material runs to eight. Leading/middle/trailing solid-run compaction leaves
  triangle order unchanged; negative roof/smoke UVs are excluded.
- Ground material palettes bind up to four original textures without copying or
  resampling. Tests check each triangle's UV3 selector resolves the exact original
  texture, preserve all geometry/index/channel data, check actual mesh UV3 upload,
  reject non-ground modes/procedural pages/shared vertices, and exercise the fixed
  256-palette cap plus reuse across rebuilds and reordered source texture lists.
- Merged MAX masks conserve referenced geometry and vertex data. Camera pan
  redraws masks but reuses their meshes; geometry or material changes invalidate
  the appropriate work. Vehicles stay live in the object mask only.
- Existing benchmark capture, pause, paired export and cancellation checks plus
  the new scripted motion path, immutable simulation, JSON identity and state
  restoration.

LIMITATIONS
These contracts record C# calls, data and resource ownership. They cannot
execute shaders, native camera render passes, texture color-space conversion,
Vulkan driver behavior, mobile tile memory or pixels. Their Matrix4x4 methods
also do not implement camera projection. A pass does not prove visual equality,
absence of device flicker, FPS, GPU time or real Unity API assembly compatibility.
The --unity-compile mode checks UNITY_5_3_OR_NEWER branches against portable
contracts, not a Unity Editor installation. Android device verification remains
required. The synthetic material-run ratio is not an estimate of FPS gain.

Optional structural map benchmark:
  DOTNET_ROOT=/path/to/dotnet python Tools/ProjectCheck/Native214/run219_static.py
Uses actual prepared runtime maps when present; otherwise test-only Campaign_01
and Campaign_20 from Fixtures (historical full version 204, not a claim of matching
current device map bytes). RUBEZH_MAP_FIXTURES overrides the fixture directory.
Both baseline and changed source must use identical inputs. Texture pixels are
portable placeholder textures, so reported values are geometry/material/draw
counts only, not rendered images or GPU timings.
