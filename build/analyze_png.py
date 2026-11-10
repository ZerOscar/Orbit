"""不依赖 Pillow 的 PNG 读取 + 简单分析：找亮像素的分布，判断视觉问题来源。"""

import struct
import sys
import zlib


def read_png(path):
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", "不是 PNG"
    pos, width, height, idat = 8, 0, 0, b""
    while pos < len(data):
        length = struct.unpack(">I", data[pos : pos + 4])[0]
        tag = data[pos + 4 : pos + 8]
        payload = data[pos + 8 : pos + 8 + length]
        if tag == b"IHDR":
            width, height, depth, color = struct.unpack(">IIBB", payload[:10])
            assert depth == 8 and color == 6, f"只支持 8 位 RGBA，实际 {depth}/{color}"
        elif tag == b"IDAT":
            idat += payload
        pos += 12 + length

    raw = zlib.decompress(idat)
    stride = width * 4
    out = bytearray(height * stride)
    prev = bytearray(stride)
    offset = 0
    for row in range(height):
        filter_type = raw[offset]
        offset += 1
        line = bytearray(raw[offset : offset + stride])
        offset += stride
        if filter_type == 0:
            pass
        elif filter_type == 1:
            for i in range(4, stride):
                line[i] = (line[i] + line[i - 4]) & 0xFF
        elif filter_type == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif filter_type == 3:
            for i in range(stride):
                left = line[i - 4] if i >= 4 else 0
                line[i] = (line[i] + ((left + prev[i]) >> 1)) & 0xFF
        elif filter_type == 4:
            for i in range(stride):
                a = line[i - 4] if i >= 4 else 0
                b = prev[i]
                c = prev[i - 4] if i >= 4 else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pred = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pred) & 0xFF
        out[row * stride : (row + 1) * stride] = line
        prev = line
    return width, height, out


def main(path):
    w, h, px = read_png(path)
    print(f"尺寸 {w}x{h}")

    opaque = bright = 0
    alpha_hist = {}
    for i in range(0, len(px), 4):
        a = px[i + 3]
        alpha_hist[a] = alpha_hist.get(a, 0) + 1
        if a > 200:
            opaque += 1
            if px[i] > 150 and px[i + 1] > 150 and px[i + 2] > 150:
                bright += 1

    print(f"不透明像素 {opaque}，其中亮色 {bright}")
    top = sorted(alpha_hist.items(), key=lambda kv: -kv[1])[:5]
    print("alpha 分布 top5:", top)

    # 亮像素按半径分布，看它们是不是集中在某个环带上
    cx = cy = w / 2
    buckets = {}
    samples = []
    for y in range(h):
        for x in range(w):
            i = (y * w + x) * 4
            if px[i + 3] > 200 and px[i] > 150 and px[i + 1] > 150 and px[i + 2] > 150:
                r = int(((x - cx) ** 2 + (y - cy) ** 2) ** 0.5)
                buckets[r] = buckets.get(r, 0) + 1
                if len(samples) < 12 and r < 90:
                    samples.append((x, y, r, px[i], px[i + 1], px[i + 2]))

    print("亮像素半径分布(前 10):", sorted(buckets.items(), key=lambda kv: -kv[1])[:10])
    print("内圈亮像素样例:", samples)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "build/render3.png")
