#!/usr/bin/env python3
"""Range-read global-metadata.dat and arm64 libil2cpp.so from the official APK.

Usage: python3 tools/protocol-sync/fetch.py [version] [--output DIR]
Without a version, the current one is read from the official vars.js.
"""
import argparse
import hashlib
import importlib.util
from pathlib import Path
import re
import tempfile
import urllib.request

# RemoteApk (HTTP Range + CRC check) lives in the emoticon tool; its top level is stdlib-only.
_spec = importlib.util.spec_from_file_location("update_chat_emoticons", Path(__file__).resolve().parents[1] / "update-chat-emoticons.py")
_module = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_module)
RemoteApk = _module.RemoteApk


def latest_version():
    with urllib.request.urlopen("https://mememori-game.com/apps/vars.js", timeout=30) as response:
        match = re.search(r"/apps/mementomori_(\d+\.\d+\.\d+)\.apk", response.read().decode("utf-8", "replace"))
    if not match:
        raise ValueError("vars.js did not provide a game version")
    return match.group(1)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("version", nargs="?", help="three-part game version, e.g. 5.1.1 (default: latest from vars.js)")
    parser.add_argument("--output", type=Path, help="output directory (default: <temp>/mementomori-<version>)")
    args = parser.parse_args()
    version = args.version or latest_version()
    output = args.output or Path(tempfile.gettempdir(), f"mementomori-{version}")
    output.mkdir(parents=True, exist_ok=True)
    apk = RemoteApk(version)
    names = [n for n in apk.zip.namelist() if n.endswith("global-metadata.dat") or n == "lib/arm64-v8a/libil2cpp.so"]
    if len(names) != 2:
        raise ValueError(f"Expected metadata and arm64 libil2cpp.so, found {names}")
    for name in names:
        data = apk.read(name)
        (output / Path(name).name).write_bytes(data)
        print(name, len(data), hashlib.sha256(data).hexdigest())
    print(f"{version} -> {output}")


if __name__ == "__main__":
    main()
