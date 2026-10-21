#!/usr/bin/env python3
"""Publish and package the POS Desktop client and optional local API for Linux/Windows x64."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
import tarfile
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / "artifacts"


def run(command: list[str]) -> None:
    print("+", " ".join(command), flush=True)
    subprocess.run(command, cwd=ROOT, check=True)


def publish(project: str, rid: str, output: Path) -> None:
    output.mkdir(parents=True, exist_ok=True)
    command = [
        "dotnet", "publish", project, "-c", "Release", "-r", rid,
        "--self-contained", "true", "-o", str(output),
        "-p:PublishSingleFile=false", "-p:DebugType=None", "-p:DebugSymbols=false",
    ]
    if rid.startswith("win-"):
        command.append("-p:EnableWindowsTargeting=true")
    run(command)


def copy_runtime_contents(source: Path, destination: Path) -> None:
    shutil.copytree(source, destination, dirs_exist_ok=True)


def prepare_package(stage: Path, rid: str, display_name: str) -> Path:
    package = stage / display_name
    package.mkdir(parents=True)
    work = stage / f"publish-{rid}"
    client = work / "client"
    server = work / "server"
    setup = work / "setup"
    publish("src/PosDesktop/PosDesktop.csproj", rid, client)
    publish("src/PosServer/PosServer.csproj", rid, server)
    publish("tools/PosSetup/PosSetup.csproj", rid, setup)
    copy_runtime_contents(client, package)
    copy_runtime_contents(server, package / "LocalApi")
    copy_runtime_contents(setup, package / "Setup")

    shutil.copytree(ROOT / "database", package / "Database")
    shutil.copytree(ROOT / "Configuration", package / "Configuration")
    shutil.copy2(ROOT / "README.md", package / "README.md")
    shutil.copy2(ROOT / "distribution/CROSS-PLATFORM-README.md", package / "CROSS-PLATFORM-README.md")

    if rid == "linux-x64":
        shutil.copy2(ROOT / "distribution/linux/launch-posdesktop.sh", package / "launch-posdesktop.sh")
        shutil.copy2(ROOT / "distribution/linux/install-desktop.sh", package / "install-desktop.sh")
        shutil.copy2(ROOT / "distribution/linux/posdesktop.desktop", package / "posdesktop.desktop")
        for script in (package / "PosDesktop", package / "LocalApi/PosServer", package / "Setup/PosSetup",
                       package / "launch-posdesktop.sh", package / "install-desktop.sh"):
            if script.exists():
                script.chmod(script.stat().st_mode | 0o111)
    else:
        shutil.copy2(ROOT / "distribution/windows/Launch-POS-Desktop.cmd", package / "Launch-POS-Desktop.cmd")
        shutil.copy2(ROOT / "distribution/windows/Launch-POS-Desktop.ps1", package / "Launch-POS-Desktop.ps1")
    return package


def replace_path(source: Path, destination: Path) -> None:
    backup = destination.with_name(destination.name + ".previous")
    if backup.exists():
        if backup.is_dir():
            shutil.rmtree(backup)
        else:
            backup.unlink()
    moved_old = False
    try:
        if destination.exists():
            destination.rename(backup)
            moved_old = True
        source.rename(destination)
    except Exception:
        if moved_old and backup.exists() and not destination.exists():
            backup.rename(destination)
        raise
    if backup.exists():
        if backup.is_dir():
            shutil.rmtree(backup)
        else:
            backup.unlink()


def make_tar(package: Path, output: Path) -> None:
    temporary = output.with_suffix(output.suffix + ".tmp")
    with tarfile.open(temporary, "w:gz", compresslevel=6) as archive:
        archive.add(package, arcname=package.name, recursive=True)
    temporary.replace(output)


def make_zip(package: Path, output: Path) -> None:
    temporary = output.with_suffix(output.suffix + ".tmp")
    with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for entry in sorted(package.rglob("*")):
            if entry.is_file():
                archive.write(entry, entry.relative_to(package.parent))
    temporary.replace(output)


def main() -> int:
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="pos-package-", dir=ARTIFACTS) as temporary:
        stage = Path(temporary)
        linux = prepare_package(stage, "linux-x64", "POS-Desktop-linux-x64")
        windows = prepare_package(stage, "win-x64", "POS-Desktop-win-x64")
        linux_archive = stage / "POS-Desktop-linux-x64.tar.gz"
        windows_archive = stage / "POS-Desktop-win-x64.zip"
        make_tar(linux, linux_archive)
        make_zip(windows, windows_archive)

        outputs = [
            (linux, ARTIFACTS / linux.name),
            (windows, ARTIFACTS / windows.name),
            (linux_archive, ARTIFACTS / linux_archive.name),
            (windows_archive, ARTIFACTS / windows_archive.name),
        ]
        for source, destination in outputs:
            replace_path(source, destination)
    print("Created:")
    for name in ("POS-Desktop-linux-x64", "POS-Desktop-linux-x64.tar.gz",
                 "POS-Desktop-win-x64", "POS-Desktop-win-x64.zip"):
        print(f"  {ARTIFACTS / name}")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except subprocess.CalledProcessError as error:
        print(f"Publish failed with exit code {error.returncode}.", file=sys.stderr)
        sys.exit(error.returncode)
