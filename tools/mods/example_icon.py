"""Writes the example mod's icon.png (256x256, Thunderstore's size): a headlamp's cone in the dark. Stdlib only.

    python3 tools/mods/example_icon.py
"""
import math
import pathlib
import struct
import zlib

W = H = 256


def pixel(x, y):
    dx, dy = x - 128, y - 150
    if math.hypot(dx, dy) < 18:
        return (250, 214, 140)  # the lamp
    if y < 150 and abs(dx) < (150 - y) * 0.55:
        return (70, 64, 50)  # its cone
    if y > 200:
        return (38, 30, 26)  # the ballast
    return (16, 18, 24)  # the night


def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)


raw = b"".join(b"\0" + b"".join(bytes(pixel(x, y)) for x in range(W)) for y in range(H))
png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
out = pathlib.Path(__file__).with_name("example") / "icon.png"
out.write_bytes(png)
print(out)
