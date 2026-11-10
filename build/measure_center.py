"""测量环形菜单快照里文字/图标的实际居中偏差。

用法: python measure_center.py before.png after.png

对每个切片，统计落在该扇区内的亮像素（文字墨迹）包围盒中心，
和扇形径向中线上的理论点比较，输出偏差像素。中心标题同理。
"""

import math
import sys

from analyze_png import read_png

INNER_RATIO = 0.42
OUTER = 132.0


def ink(x, y, w, px):
    i = (y * w + x) * 4
    r, g, b, a = px[i], px[i + 1], px[i + 2], px[i + 3]
    if a < 180:
        return False
    return (r * 299 + g * 587 + b * 114) // 1000 > 150


def bbox(points):
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    return min(xs), max(xs), min(ys), max(ys)


def measure(path):
    w, h, px = read_png(path)
    cx = cy = w / 2.0
    inner = OUTER * INNER_RATIO
    radius = (inner + OUTER) / 2

    title_pts = [
        (x, y)
        for y in range(h)
        for x in range(w)
        if math.hypot(x - cx, y - cy) < inner - 6 and ink(x, y, w, px)
    ]
    if not title_pts:
        raise SystemExit(f"{path}: 没找到中心标题")
    x0, x1, y0, y1 = bbox(title_pts)
    title = ((x0 + x1) / 2, (y0 + y1) / 2)

    slices = []
    n = 8
    for i in range(n):
        mid = i * 2 * math.pi / n
        pts = []
        for y in range(h):
            for x in range(w):
                d = math.hypot(x - cx, y - cy)
                if not (inner + 2 < d < OUTER - 2):
                    continue
                if not ink(x, y, w, px):
                    continue
                a = math.atan2(x - cx, cy - y)
                if a < 0:
                    a += 2 * math.pi
                if min(abs(a - mid), 2 * math.pi - abs(a - mid)) < 0.30:
                    pts.append((x, y))
        if not pts:
            slices.append((i, None))
            continue
        x0, x1, y0, y1 = bbox(pts)
        expect_x = cx + radius * math.sin(mid)
        expect_y = cy - radius * math.cos(mid)
        slices.append((i, ((x0 + x1) / 2 - expect_x, (y0 + y1) / 2 - expect_y)))

    return title, slices


def main(paths):
    for path in paths:
        title, slices = measure(path)
        print(f"\n{path}")
        print(f"  中心标题偏移 ({title[0] - 154:+.1f}, {title[1] - 154:+.1f})")
        worst = 0.0
        for i, delta in slices:
            if delta is None:
                print(f"  片{i}: 没有墨迹")
                continue
            worst = max(worst, abs(delta[0]), abs(delta[1]))
            print(f"  片{i}: 偏移 ({delta[0]:+.1f}, {delta[1]:+.1f})")
        print(f"  最大偏差 {worst:.1f} px")


if __name__ == "__main__":
    main(sys.argv[1:])
