"""生成 Orbit 的托盘 / 窗口图标：深色圆底 + 紫色环。纯标准库，不依赖 Pillow。"""

import math
import struct
import zlib
from pathlib import Path

OUT = Path(__file__).resolve().parent / "orbit.ico"


def render(size: int) -> bytes:
    """返回 size x size 的 RGBA 像素数据（PNG 格式）。"""

    cx = cy = size / 2.0
    outer = size * 0.44
    inner = size * 0.26
    plate = size * 0.46

    pixels = bytearray()
    for y in range(size):
        pixels.append(0)  # PNG 每行的 filter byte
        for x in range(size):
            px, py = x + 0.5, y + 0.5
            d = math.hypot(px - cx, py - cy)

            # 抗锯齿：用距离的线性过渡当覆盖率，1px 宽。
            plate_a = clamp((plate - d) + 0.5)
            ring_a = clamp((outer - d) + 0.5) * clamp((d - inner) + 0.5)

            if plate_a <= 0:
                pixels.extend((0, 0, 0, 0))
                continue

            base = (26, 26, 32)
            accent = (124, 108, 240)

            # 环越靠近外缘越亮，模拟一点层次。
            t = clamp((d - inner) / max(outer - inner, 1))
            ring_rgb = tuple(
                round(accent[i] * (0.82 + 0.18 * t) + base[i] * (1 - 0.82 - 0.18 * t)) for i in range(3)
            )

            a = plate_a
            rgb = tuple(round(base[i] * (1 - ring_a) + ring_rgb[i] * ring_a) for i in range(3))
            pixels.extend((rgb[0], rgb[1], rgb[2], round(255 * a)))

    raw = bytes(pixels)
    return png_chunk_wrap(size, raw)


def clamp(v: float) -> float:
    return max(0.0, min(1.0, v))


def png_chunk_wrap(size: int, raw: bytes) -> bytes:
    def chunk(tag: bytes, data: bytes) -> bytes:
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", ihdr)
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b"")
    )


def build() -> None:
    sizes = [16, 32, 48, 256]
    images = [render(s) for s in sizes]

    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = b""
    for size, data in zip(sizes, images):
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)

    OUT.write_bytes(header + entries + b"".join(images))
    print(f"wrote {OUT} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    build()
