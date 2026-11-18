Optimization219 retained-render validation

Run with .NET SDK 8:
  DOTNET_ROOT=/path/to/dotnet python Tools/ProjectCheck/Optimization219/run.py

131 checks call the production mask-cache and command-buffer methods against
portable Unity contracts. They cover parked, moving, appearing/disappearing and
fog-hidden vehicles; map revision/view/scenery/target invalidation; exact mesh,
submesh, material and pass sequence; viewport edge retention; cached reuse;
fallback detach/reattach; roof/scenery toggles; and buffer disposal.

The tests isolate retained-cache fields from texture/atlas initialization.
Camera events and command draws are captured by the contract objects. Bounds
for uploaded vertex lists are computed from the actual coordinates.

Related checks:
  Tools/ProjectCheck/Native215/run.py --unity-compile
  Tools/ProjectCheck/benchmark219.py
  Tools/ProjectCheck/Native214/run.py --regression
  Tools/ProjectCheck/Native214/run219_static.py

Limits: these checks do not execute Unity, compile GPU shaders, compare rendered
pixels, or measure Android performance. Device A/B captures are still required.
