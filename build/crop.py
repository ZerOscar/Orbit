"""裁剪 + 最近邻放大，用来看清局部细节。"""

import struct
import sys
import zlib

from analyze_png import read_png


def write_png(path, w, h, rgba):
    rows = bytearray()
    for y in range(h):
        rows.append(0)
        rows.extend(rgba[y * w * 4 : (y + 1) * w * 4])

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(
            b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(rows), 6))
            + chunk(b"IEND", b"")
        )


def main(src, dst, x0, y0, size, scale):
    w, h, px = read_png(src)
    x0 = max(0, min(x0, w - 1))
    y0 = max(0, min(y0, h - 1))
    cw = min(size, w - x0)
    ch = min(size, h - y0)

    out_w, out_h = cw * scale, ch * scale
    out = bytearray(out_w * out_h * 4)
    for y in range(out_h):
        sy = y0 + y // scale
        for x in range(out_w):
            sx = x0 + x // scale
            si = (sy * w + sx) * 4
            di = (y * out_w + x) * 4
            r, g, b, a = px[si], px[si + 1], px[si + 2], px[si + 3]
            if a < 255:
                # 透明区域铺到白底上，方便看清边缘
                r = (r * a + 255 * (255 - a)) // 255
                g = (g * a + 255 * (255 - a)) // 255
                b = (b * a + 255 * (255 - a)) // 255
                a = 255
            out[di : di + 4] = bytes((r, g, b, a))
    write_png(dst, out_w, out_h, out)
    print(f"{dst} ({out_w}x{out_h})")


if __name__ == "__main__":
    args = sys.argv[1:]
    if len(args) >= 6:
        main(args[0], args[1], int(args[2]), int(args[3]), int(args[4]), int(args[5]))
    else:
        main("build/render3.png", "build/crop.png", 100, 100, 110, 4)
