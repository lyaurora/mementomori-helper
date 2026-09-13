#!/usr/bin/env python3
"""Export the official chat atlas without downloading the entire APK.

Maintenance only: pip install UnityPy Pillow
Usage: python tools/update-chat-emoticons.py 4.22.0
"""
import argparse
import base64
import io
import json
from pathlib import Path
import re
import struct
import tempfile
import urllib.request
import zipfile
import zlib


class RemoteApk:
    def __init__(self, version):
        if not re.fullmatch(r"\d+\.\d+\.\d+", version):
            raise ValueError("Expected a three-part game version")
        self.url = f"https://mememori-game.com/apps/mementomori_{version}.apk"
        with urllib.request.urlopen(urllib.request.Request(self.url, method="HEAD"), timeout=30) as response:
            size = int(response.headers["Content-Length"])
        tail = self.range(size - 65536, size - 1)
        end = tail.rfind(b"PK\x05\x06")
        if end < 0:
            raise ValueError("APK has no ZIP directory")
        record = struct.unpack_from("<4s4H2IH", tail, end)
        length, self.central_start = record[5:7]
        # ponytail: current APK is ZIP32; add ZIP64 parsing if the client exceeds 4 GiB.
        if 0xFFFFFFFF in (length, self.central_start):
            raise ValueError("ZIP64 APK needs an updated extractor")
        central = self.range(self.central_start, self.central_start + length - 1)
        self.zip = zipfile.ZipFile(io.BytesIO(central + tail[end:]))

    def range(self, start, end):
        request = urllib.request.Request(self.url, headers={"Range": f"bytes={start}-{end}"})
        with urllib.request.urlopen(request, timeout=60) as response:
            if response.status != 206 or not response.headers.get("Content-Range", "").startswith(f"bytes {start}-{end}/"):
                raise ValueError("Server did not honor the requested APK range")
            data = response.read()
        if len(data) != end - start + 1:
            raise ValueError("Truncated APK range")
        return data

    def read(self, name):
        info = self.zip.getinfo(name)
        offset = info.header_offset + self.central_start
        header = struct.unpack("<4s5H3I2H", self.range(offset, offset + 29))
        if header[0] != b"PK\x03\x04":
            raise ValueError("Invalid ZIP entry")
        start = offset + 30 + header[-2] + header[-1]
        data = self.range(start, start + info.compress_size - 1)
        if info.compress_type == zipfile.ZIP_DEFLATED:
            data = zlib.decompress(data, -15)
        elif info.compress_type != zipfile.ZIP_STORED:
            raise ValueError("Unsupported APK compression")
        if len(data) != info.file_size or zlib.crc32(data) != info.CRC:
            raise ValueError(f"APK CRC check failed: {name}")
        return data


def bundles_for(catalog, asset_paths):
    raw = base64.b64decode(catalog["m_EntryDataString"])
    count = struct.unpack_from("<i", raw)[0]
    if len(raw) != 4 + count * 28:
        raise ValueError("Unsupported Addressables entry format")
    entries = [struct.unpack_from("<7i", raw, 4 + i * 28) for i in range(count)]
    raw = base64.b64decode(catalog["m_BucketDataString"])
    buckets, offset = [], 4
    for _ in range(struct.unpack_from("<i", raw)[0]):
        _, count = struct.unpack_from("<2i", raw, offset)
        offset += 8
        buckets.append(struct.unpack_from(f"<{count}i", raw, offset))
        offset += count * 4
    ids = catalog["m_InternalIds"]
    pending = [i for i, entry in enumerate(entries) if ids[entry[0]] in asset_paths]
    if {ids[entries[i][0]] for i in pending} != asset_paths:
        raise ValueError("Chat assets are missing from the official catalog")
    provider = catalog["m_ProviderIds"].index("Ortega.Common.OrtegaAssestBundleProvider")
    seen, names = set(), set()
    while pending:
        index = pending.pop()
        if index in seen:
            continue
        seen.add(index)
        entry = entries[index]
        if entry[1] == provider:
            names.add(Path(ids[entry[0]]).name)
        if entry[2] >= 0:
            pending.extend(buckets[entry[2]])
    return sorted(names)


def main():
    import UnityPy
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("version")
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "MementoMori.BlazorShared/wwwroot/chat")
    args = parser.parse_args()
    apk = RemoteApk(args.version)
    catalog = json.loads(apk.read("assets/aa/catalog.json"))
    names = bundles_for(catalog, {
        "Assets/AddressableLocalAssets/Materials/Chat/EmotIcon.png",
        "Assets/AddressableLocalAssets/Materials/Chat/EmoticonAtlas.asset",
        "Assets/AddressableLocalAssets/Prefabs/Common/CommonChatReactionPopupViewController.prefab",
    })
    image, sprites = None, None
    reactions = {}
    with tempfile.TemporaryDirectory(prefix="mementomori-emoticons-") as directory:
        for name in names:
            Path(directory, name).write_bytes(apk.read(f"assets/aa/Android/{name}"))
        for obj in UnityPy.load(directory).objects:
            if obj.type.name == "Texture2D":
                texture = obj.read()
                if texture.m_Name == "EmotIcon":
                    image = texture.image.convert("RGBA")
            elif obj.type.name == "MonoBehaviour":
                data = obj.read_typetree()
                if data.get("m_Name") == "EmoticonAtlas":
                    sprites = data["EmoticonSpriteInfos"]
            elif obj.type.name == "Sprite":
                sprite = obj.read()
                if re.fullmatch(r"icon_chat_reaction_[1-4]", sprite.m_Name):
                    reactions[int(sprite.m_Name[-1])] = sprite.image.convert("RGBA")
    if image is None or not sprites or len(reactions) != 4:
        raise ValueError("Could not decode chat sprites")
    frames = {}
    for sprite in sprites:
        rect = sprite["Rect"]
        x, y = round(rect["x"] * image.width), round((1 - rect["y"] - rect["height"]) * image.height)
        width, height = round(rect["width"] * image.width), round(rect["height"] * image.height)
        if not (0 <= x < x + width <= image.width and 0 <= y < y + height <= image.height):
            raise ValueError("Sprite rectangle is outside its atlas")
        frames[str(sprite["Id"])] = [x, y, width, height]
    args.output.mkdir(parents=True, exist_ok=True)
    image.save(args.output / f"emoticons-{args.version}.webp", "WEBP", lossless=True, method=6)
    for reaction, icon in reactions.items():
        icon.save(args.output / f"reaction-{reaction}-{args.version}.webp", "WEBP", lossless=True, method=6)
    metadata = {"Version": args.version, "Width": image.width, "Height": image.height, "Sprites": frames}
    temporary = args.output / "emoticons.json.tmp"
    temporary.write_text(json.dumps(metadata, separators=(",", ":")) + "\n", encoding="utf-8")
    temporary.replace(args.output / "emoticons.json")
    print(f"Exported {len(frames)} stickers and 4 reaction icons from {len(names)} CRC-verified bundles ({args.version})")


if __name__ == "__main__":
    main()
