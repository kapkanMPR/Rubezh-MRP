Native215 roster lifecycle validation

Requirements: Python3 and .NET8 (DOTNET_ROOT or PATH). The full --regression
suite also uses Pillow/numpy to regenerate the actual story20 building fixture.

Commands from any directory:
  python Tools/ProjectCheck/Native215/run.py
  python Tools/ProjectCheck/Native215/run.py --unity-compile
  python Tools/ProjectCheck/Native215/run.py --render-regression
  python Tools/ProjectCheck/Native215/run.py --regression

The default suite tests changes AFTER ObserveTick without advancing the tick:
- add a commander who is reloading;
- replace a Soldier object while preserving its ID;
- replace an ID while preserving roster count;
- remove one actor and remove the whole roster;
- 100 five-actor insertion/removal cycles with reused and new IDs;
- 10 new BattleSimulation instances through the same composer/renderer.

Assertions cover frame/actor/root counts, body composition, no previous-object
interpolation, immediate removal of stale groups and Unity part nodes, and root
deactivation before deferred Destroy. Terminal actor caches/roots/nodes are zero;
three negative non-actor groups and their non-actor nodes are legitimate.

These are API contract tests using CURRENT Core/Camp/Runtime source. They do NOT
execute actual Unity GPU rendering, asset import, deferred Destroy/GC, Addressables
builds or APKs. Bounded dictionary/node counts do not prove native GPU memory release.

The 100-cycle same-simulation test is a stress case, not the current raid workflow.
Actual raid: RubezhRaid.LaunchRaidWave -> BeginNativeTestLoad -> LoadNativeTest ->
FieldCamp.BuildCampSimulation constructs a new BattleSimulation for each wave,
then PrepareRaidWave restores the five fighters' saved state.

Reports: Build/Reports/AI-0.12.215/Current and validation-summary.json.
