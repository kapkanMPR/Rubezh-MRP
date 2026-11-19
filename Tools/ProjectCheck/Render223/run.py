"""Compare retained rendering command submissions using the same map and camera trace.

Run normally against the current checkout. --revision can compile production C#
from a local Git revision while keeping the fixtures and map data identical.
"""

import argparse
import io
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import tempfile

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from rubezh_dotnet import find_sdk


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--revision", help="use renderer source from a local Git commit"
    )
    args = parser.parse_args()
    here = Path(__file__).resolve().parent
    root = here.parents[2]
    checks = here.parent
    dotnet, sdk, refs, runtime = find_sdk()
    with tempfile.TemporaryDirectory(prefix="rubezh-render-") as temp:
        output = Path(temp)
        source = root
        if args.revision:
            commit = subprocess.check_output(
                [
                    "git",
                    "rev-parse",
                    "--verify",
                    "--end-of-options",
                    args.revision + "^{commit}",
                ],
                cwd=root,
                text=True,
            ).strip()
            archive = subprocess.check_output(
                ["git", "archive", commit, "--", ":(glob)Assets/Rubezh/**/*.cs"],
                cwd=root,
            )
            with tarfile.open(fileobj=io.BytesIO(archive)) as files:
                files.extractall(output, filter="data")
            source = output
        platform = output / "Platform.cs"
        platform.write_text(
            (checks / "Contracts/PlatformContract.cs")
            .read_text()
            .split("namespace UnityEditor")[0]
        )
        sources = sorted((source / "Assets/Rubezh/Core").glob("*.cs"))
        sources += sorted((source / "Assets/Rubezh/Camp").glob("*.cs"))
        sources += sorted((source / "Assets/Rubezh/Runtime").rglob("*.cs"))
        sources += [
            p
            for p in (checks / "Contracts").glob("*.cs")
            if p.name != "PlatformContract.cs"
        ]
        sources += [
            platform,
            checks / "Native211/NativeAssetsContract.cs",
            *here.glob("*.cs"),
        ]
        app = output / "CameraSweep.dll"
        json_library = shutil.copy(sdk / "Newtonsoft.Json.dll", output)
        subprocess.run(
            [
                dotnet,
                sdk / "Roslyn/bincore/csc.dll",
                "-nologo",
                "-optimize",
                "-warn:0",
                "-langversion:latest",
                "-define:RUBEZH_DEVELOPER,RUBEZH_PICTURE_CONTRACT,RUBEZH_NATIVE_TEST_CONTRACT,RUBEZH_NATIVE_CONTENT_CONTRACT",
                "-target:exe",
                "-main:CameraSweep",
                "-out:" + str(app),
                "-r:" + str(json_library),
                *["-r:" + str(path) for path in refs.glob("*.dll")],
                *sources,
            ],
            check=True,
            cwd=root,
        )
        app.with_suffix(".runtimeconfig.json").write_text(
            json.dumps(
                {
                    "runtimeOptions": {
                        "tfm": "net8.0",
                        "framework": {
                            "name": "Microsoft.NETCore.App",
                            "version": runtime,
                        },
                    }
                }
            )
        )
        subprocess.run([dotnet, app], cwd=root, check=True)


if __name__ == "__main__":
    main()
