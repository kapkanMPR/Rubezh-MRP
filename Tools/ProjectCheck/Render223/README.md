# Campaign 20 camera submission check

From the Unity project root with Python 3.12+ and the .NET 8 SDK available, run the same trace against the current renderer and the previous complete release.

```sh
python3 Tools/ProjectCheck/Render223/run.py
python3 Tools/ProjectCheck/Render223/run.py --revision 85a5addcd3899343148c1acf96804a26c1f2c62f
```

The fixture loads the checked-in 136 × 80 mission with its original 15 soldiers. The simulation stays paused, fog is disabled and the render target is 960 × 540. The trace includes a cold frame, eight idle frames, 48 integer-pixel pans, 24 fractional-pixel pans, 36 continuously changing zoom levels, and nine stationary frames at the final zoom. The benchmark checks that rendering leaves the mission tick and map revision unchanged, resolves the sprite data, reuses the stationary color cache, and keeps static GameObjects off the command-buffer path when the renderer reports that metric.

`commands` counts explicit DrawMesh entries in the command buffers attached when each camera renders. `main`, `masks` and `cache` divide the total by destination. `groundColor` and `buildingColor` describe draws within the color passes. Ordinary actor MeshRenderer draws are submitted by Unity itself and do not appear in these counters.

Production code generates geometry and command buffers. The Unity API contracts supply texture identities and dimensions from asset metadata. Building pieces use the source PNG header dimensions and the production atlas packer with the Editor's tile and page settings. Atlas origins for ordinary sprites are synthetic. This measures command submissions; measuring frame time, final pixels and Android driver behavior requires the Unity player.
