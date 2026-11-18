Native 0.12.211 portable checks

Requirements: Python 3, .NET SDK 8. Set DOTNET_ROOT to its installation directory,
or put dotnet on PATH. No Unity installation is required for contract tests.

From the project directory:
  python Tools/ProjectCheck/Native211/run_masks.py

The runner compiles CURRENT Core, Camp and all Runtime sources. Paths are relative
to this project; no scratch/baseline source tree is required. API contracts model
Unity interfaces only; this does not execute shaders or measure device FPS.

Reports: Build/Reports/AI-0.12.211-NativeRuntime/masks-compile.log and masks-run.log.
Tests cover 20,000 fog samples, 64,000 night pixels against the actual CPU reference,
20,000 roof samples against Core, GPU-mask lifecycle, no CPU-mask allocations,
and rain geometry reuse/release. Unity Editor/device checks remain required for
shader compilation, platform render-target orientation and final appearance.
