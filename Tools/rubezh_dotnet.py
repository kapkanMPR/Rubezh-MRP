"""Discover the .NET 8 compiler, reference pack and runtime used by project checks."""

import os
from pathlib import Path
import re
import shutil
from typing import NamedTuple


class DotnetSdk(NamedTuple):
    executable: Path
    sdk: Path
    references: Path
    runtime_version: str


def latest_net8(directory: Path) -> Path:
    """Compare version components numerically, preferring stable builds."""
    versions = []
    for path in directory.glob("8.*"):
        match = re.fullmatch(r"(8)\.(\d+)\.(\d+)(?:-(.+))?", path.name)
        if match and path.is_dir():
            versions.append((tuple(map(int, match.groups()[:3])), not match[4], path))
    if not versions:
        raise SystemExit(
            f".NET 8 component missing in {directory}. Install the .NET 8 SDK."
        )
    return max(versions)[2]


def find_sdk() -> DotnetSdk:
    executable = "dotnet.exe" if os.name == "nt" else "dotnet"
    configured = os.environ.get("DOTNET_ROOT")
    launcher = Path(configured, executable) if configured else None
    if launcher is None or not launcher.is_file():
        found = shutil.which(executable)
        if not found:
            raise SystemExit("Install the .NET 8 SDK and set DOTNET_ROOT or PATH.")
        launcher = Path(found)
    dotnet = launcher.resolve()
    home = dotnet.parent
    return DotnetSdk(
        dotnet,
        latest_net8(home / "sdk"),
        latest_net8(home / "packs/Microsoft.NETCore.App.Ref") / "ref/net8.0",
        latest_net8(home / "shared/Microsoft.NETCore.App").name,
    )
