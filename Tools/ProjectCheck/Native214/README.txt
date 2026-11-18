Native214 retained actor/world-item checks

Requires Python3 and .NET8. DOTNET_ROOT or PATH locates SDK.
run.py --baseline [--battle] [--long] compiles the preserved213 source tree specified
by RUBEZH_BASELINE_ROOT, against the same portable contracts and trace.
run.py [--battle] [--long] compiles current source.
run.py --unity-compile checks actual UNITY_5_3_OR_NEWER branches with API doubles.

Default: 50 actors follow a fixed synthetic moving/firing/wounded1200tick trace.
--battle: actual BattleSimulation.Step for 25versus25 on forest, seed214,200ticks.
--battle --long:2400 actual ticks (120seconds simulation); records retained counts.
Every rendering call checks positions/facing/health/ammo/deployment/time/tick/RNG
against a pre-render snapshot to catch presentation mutations.

These measure actual C# decision/composition code against Unity API contracts.
Timing is host contract-runtime timing, NOT Unity FPS, GPU cost or Android memory.
Asset import, shaders, real Unity transforms and visual parity need Editor/device
verification. Active node growth from legitimate new corpses/casings is distinct
from retained-container leaks; sampled counts alone do not prove absence of leaks.

Current regression suite: run.py --regression (regenerates story20 fixture using
Python Pillow/numpy; no copied Build directory is required).
Recorded baseline comparison: python Tools/ProjectCheck/Native214/compare.py
reads the Baseline/Current report folders and asserts recorded state/sprite parity.
Baseline sources are not embedded: set RUBEZH_BASELINE_ROOT to the separately
preserved unmodified213 source tree. No absolute workspace path is required.

Final observed checks: 1600 primitive silhouette parity cases, 4391 pose/depth/
interpolation checks, 22 ground-item lifecycle cases, 602 inherited regressions,
1,718,583 building invariants. Seven sprite endpoint cases compare to213 golden
poses within0.001 units; solid status markers intentionally use continuous edges.
Pause/resume holds the latest endpoint until a new simulation tick; interpolation
crossing updates sorting without body reconstruction. These remain CPU contract
checks, not actual GPU image comparisons.
