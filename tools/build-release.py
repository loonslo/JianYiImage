"""Build the release files: the portable zip and the installer, with SHA-256 files, and check that no build path leaked in.

    py -3.14 tools/build-release.py --version 1.1.0 --out <folder>

Produces, in <folder>:
    JianYiImage-v<version>-win-x64.zip              portable: one top-level folder JianYiImage/
    JianYiImage-v<version>-win-x64-setup.exe        installer with the OCR language pack page
    <each of the two>.sha256

Needs the .NET SDK from global.json, Python with Pillow, and Inno Setup 6 (ISCC.exe). Run it from the tree the release is
built from: the published assemblies must not carry the absolute path of that tree (PathMap) or of the machine's user
profile, and the script checks that before it writes anything to <folder>.

--fast skips the slow installer compression; use it for trying things out, never for a release.
"""

from __future__ import annotations

import argparse
import hashlib
import os
import re
import shutil
import socket
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP_PROJECT = ROOT / "src/JianYiImage.App/JianYiImage.App.csproj"
INSTALLER_SCRIPT = ROOT / "installer/JianYiImage.iss"
FOLDER_IN_ZIP = "JianYiImage"


def find_tool(names: list[str], extra: list[Path]) -> Path:
    for name in names:
        found = shutil.which(name)
        if found:
            return Path(found)
    for candidate in extra:
        if candidate.is_file():
            return candidate
    raise SystemExit(f"cannot find {names[0]}; looked on PATH and in {', '.join(map(str, extra))}")


def local_appdata() -> Path:
    return Path(os.environ.get("LOCALAPPDATA", ""))


def find_dotnet() -> Path:
    """The first dotnet that can actually resolve the SDK global.json asks for.

    PATH often holds a runtime-only host (it answers `dotnet --version` with "no SDK found") while the SDK itself
    lives in a per-user folder, so each candidate is asked instead of trusting the first one found.
    """
    candidates: list[Path] = []
    on_path = shutil.which("dotnet")
    if on_path:
        candidates.append(Path(on_path))
    candidates.append(local_appdata() / "Microsoft/dotnet/dotnet.exe")
    candidates.extend(Path(os.environ.get(k, "")) / "dotnet/dotnet.exe" for k in ("ProgramFiles", "ProgramFiles(x86)"))
    for candidate in candidates:
        if not candidate.is_file():
            continue
        probe = subprocess.run([str(candidate), "--version"], cwd=ROOT, capture_output=True, text=True)
        if probe.returncode == 0:
            return candidate
    raise SystemExit("no dotnet with the SDK required by global.json was found")


def find_iscc() -> Path:
    program_files = [Path(os.environ.get(k, "")) for k in ("ProgramFiles(x86)", "ProgramFiles")]
    return find_tool(
        ["ISCC"],
        [local_appdata() / "Programs/Inno Setup 6/ISCC.exe", *[p / "Inno Setup 6/ISCC.exe" for p in program_files]],
    )


def run(command: list[str], **kwargs) -> None:
    print("+", " ".join(f'"{c}"' if " " in c else c for c in command), flush=True)
    subprocess.run(command, check=True, **kwargs)


def git_revision() -> str:
    result = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT, capture_output=True, text=True)
    return result.stdout.strip() if result.returncode == 0 else ""


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write_sha256(path: Path) -> str:
    value = sha256(path)
    path.with_name(path.name + ".sha256").write_text(f"{value}  {path.name}\n", encoding="utf-8")
    return value


def needles() -> list[tuple[str, bytes]]:
    """Strings that must not appear in anything we ship: where this was built and who built it."""
    texts: dict[str, str] = {
        "build folder": str(ROOT),
        "build folder (forward slashes)": ROOT.as_posix(),
        "build folder (parent)": str(ROOT.parent),
        "user profile": str(Path.home()),
        "host name": socket.gethostname(),
    }
    for key in ("USERNAME", "USER", "LOGNAME", "COMPUTERNAME"):
        value = os.environ.get(key, "")
        if value:
            texts[f"environment {key}"] = value
    identity = {
        key: subprocess.run(["git", "config", "--get", key], capture_output=True, text=True).stdout.strip()
        for key in ("user.email", "user.name")
    }
    # A GitHub noreply identity (the one the public clone is committed under) is public by design: its login also
    # appears in the repository URL the program links to, so it is not something to hunt for in the binaries.
    if not identity["user.email"].endswith("@users.noreply.github.com"):
        for key, value in identity.items():
            if value:
                texts[f"git {key}"] = value
    result: list[tuple[str, bytes]] = []
    for label, text in texts.items():
        # shorter strings (a four letter login, say) turn up by chance in any large binary; the exporter's text scan covers those
        if len(text) < 6:
            continue
        for encoding in ("utf-8", "utf-16-le"):
            result.append((f"{label} ({encoding})", text.lower().encode(encoding)))
    return result


def scan_for_leaks(folder: Path) -> list[str]:
    patterns = needles()
    findings: list[str] = []
    files = [p for p in folder.rglob("*") if p.is_file()]
    for path in files:
        data = path.read_bytes().lower()
        for label, needle in patterns:
            if needle in data:
                findings.append(f"{path.relative_to(folder)}: {label}")
    print(f"leak scan: {len(files)} files, {len(patterns)} patterns, {len(findings)} finding(s)")
    return findings


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--version", required=True, help="for example 1.1.0")
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--fast", action="store_true", help="quick installer compression (not for releases)")
    args = parser.parse_args()
    if not re.fullmatch(r"\d+\.\d+\.\d+", args.version):
        parser.error("--version must look like 1.2.3")

    dotnet, iscc = find_dotnet(), find_iscc()
    out = args.out.resolve()
    work = out / "_work"
    if ROOT in out.parents or out == ROOT:
        raise SystemExit("--out must be outside the source tree")
    if work.exists():
        shutil.rmtree(work)
    work.mkdir(parents=True)
    publish = work / "publish"

    revision = git_revision()
    publish_command = [
        str(dotnet), "publish", str(APP_PROJECT), "-c", "Release", "-r", "win-x64", "--self-contained", "true",
        "-o", str(publish), f"-p:Version={args.version}", "-p:DebugType=none", "-p:DebugSymbols=false",
        # an absolute build path embedded in the dlls is personal information once the file is public
        f"-p:PathMap={ROOT}{os.sep}=/_/",
    ]
    if revision:
        publish_command.append(f"-p:SourceRevisionId={revision}")
    run(publish_command)

    # Both the portable archive and installer must carry the distribution notices.
    for notice in ("LICENSE", "THIRD_PARTY_NOTICES.md"):
        shutil.copy2(ROOT / notice, publish / notice)

    findings = scan_for_leaks(publish)
    if findings:
        print("the published files contain build or personal information; nothing was written to --out:")
        for finding in findings:
            print("  ", finding)
        return 2

    out.mkdir(parents=True, exist_ok=True)

    zip_path = out / f"JianYiImage-v{args.version}-win-x64.zip"
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path in sorted(p for p in publish.rglob("*") if p.is_file()):
            archive.write(path, f"{FOLDER_IN_ZIP}/{path.relative_to(publish).as_posix()}")
    print("wrote", zip_path.name, f"{zip_path.stat().st_size / 2**20:.1f} MiB")

    wizard = work / "wizard"
    run([sys.executable, str(ROOT / "tools/make-installer-images.py"), "--out", str(wizard)])

    major, minor, patch = args.version.split(".")
    iscc_command = [
        str(iscc), "/Q", f"/DVersion={args.version}", f"/DVersionNumeric={major}.{minor}.{patch}.0",
        f"/DPublishDir={publish}", f"/DWizardImageDir={wizard}", f"/O{out}", str(INSTALLER_SCRIPT),
    ]
    if args.fast:
        iscc_command.insert(2, "/DFastBuild")
    run(iscc_command, env={**os.environ, "MSYS2_ARG_CONV_EXCL": "*"})
    setup_path = out / f"JianYiImage-v{args.version}-win-x64-setup.exe"
    if not setup_path.exists():
        raise SystemExit(f"the installer was not produced: {setup_path}")
    print("wrote", setup_path.name, f"{setup_path.stat().st_size / 2**20:.1f} MiB")

    for path in (zip_path, setup_path):
        print(f"{write_sha256(path)}  {path.name}")
    shutil.rmtree(work)
    return 0


if __name__ == "__main__":
    sys.exit(main())
