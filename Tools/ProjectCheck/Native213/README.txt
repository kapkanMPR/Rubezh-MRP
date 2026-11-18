Native 213 portable regression checks

Requirements: Python 3 (Pillow and numpy for actual building source validation) and .NET SDK 8. Set DOTNET_ROOT or put dotnet on PATH.
Run from any directory:
  python Tools/ProjectCheck/Native213/run.py
  python Tools/ProjectCheck/Native213/run.py --masks

Sources under Core, Camp, Runtime are compiled from the CURRENT project.
Unity API contracts provide data/lifecycle test doubles only. These checks do not
compile Unity shaders, import assets, build Addressables/APK, execute actual GPU
rendering, or measure device FPS. Unity Editor/device verification is still needed.

The first suite verifies map addressing/70-map sprite coverage, content leases,
release/cancellation, native frame retention, simulated renderer lifetime, and
that presentation does not mutate combat soldiers, map health, time or RNG.
The second suite verifies fog/roof formulas against Core references and
GPU-mask/rain resource lifetime through test doubles.

Logs: Build/Reports/AI-0.12.213/Portable/<suite>-compile.log and <suite>-run.log.
A passing compile alone is not a passing run. Exit code is nonzero on failure.

Player conditional API contract compile: run.py --unity-compile
Editor preparation API contract compile: run_editor.py
These are not substitutes for compilation inside Unity.

Main run regenerates actual story20 metadata fixtures with building_source_check.py.
It validates 1737 source layers and native pixel-free loading plus region packing;
this is not actual Unity texture import or Addressables build. Source rendering
for 70-map sprite coverage omits procedural pictures and is complemented by the
separate full story20 building metadata fixture. GPU appearance is not compared.

Added assertions: actor reuse across idle ticks, selected health/stance invalidation,
hide/reveal group identity, renderer recreation, native damage masks and no CPU
image upload, imported exterior ownership, all five graphics tiers including fog,
retained fighter inspection/settings/FPS/reset cancellation/loading/resource release.
Formation suite has its own runner: formation_check.py.
Editor suite covers preparation/audit/scope helpers using contract APIs and transformed
outer UNITY_EDITOR guards. It cannot prove actual Editor importer behavior.
