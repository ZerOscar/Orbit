# 设置页端到端验证：拖动列表第 1 行到第 3 行，点保存，检查配置文件里的顺序是否真的变了。
#
# 复用了之前踩过的两个坑的经验：
#   1. 脚本必须声明 DPI 感知，否则坐标会被系统按缩放虚拟化；
#   2. 窗口坐标一律用 ClientToScreen 换算，不要手算标题栏高度。

import ctypes
import ctypes.wintypes as wt
import json
import os
import subprocess
import sys
import time

try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    pass

user32 = ctypes.WinDLL("user32", use_last_error=True)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.environ.get("ORBIT_EXE") or os.path.join(
    ROOT, "src", "Orbit.App", "bin", "Debug", "net9.0", "Orbit.exe")
CONFIG = os.path.join(os.environ["APPDATA"], "Orbit", "config.json")

MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004


def find_window(title, timeout=10.0):
    end = time.time() + timeout
    while time.time() < end:
        hwnd = user32.FindWindowW(None, title)
        if hwnd:
            return hwnd
        time.sleep(0.2)
    return 0


def client_size(hwnd):
    rect = wt.RECT()
    user32.GetClientRect(hwnd, ctypes.byref(rect))
    return rect.right - rect.left, rect.bottom - rect.top


def client_origin(hwnd):
    """客户区左上角在屏幕上的物理坐标，以及 DPI 缩放。"""
    point = wt.POINT(0, 0)
    user32.ClientToScreen(hwnd, ctypes.byref(point))
    dpi = user32.GetDpiForWindow(hwnd) if hasattr(user32, "GetDpiForWindow") else 96
    scale = dpi / 96.0
    return point.x, point.y, scale


def click(x, y):
    user32.SetCursorPos(int(x), int(y))
    time.sleep(0.06)
    user32.mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    time.sleep(0.06)
    user32.mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)


def drag(x1, y1, x2, y2, steps=14):
    user32.SetCursorPos(int(x1), int(y1))
    time.sleep(0.12)
    user32.mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    time.sleep(0.12)
    for i in range(1, steps + 1):
        x = x1 + (x2 - x1) * i / steps
        y = y1 + (y2 - y1) * i / steps
        user32.SetCursorPos(int(x), int(y))
        time.sleep(0.035)
    time.sleep(0.12)
    user32.mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    time.sleep(0.3)


def read_order():
    with open(CONFIG, encoding="utf-8") as f:
        data = json.load(f)
    return [a["Label"] for a in data["Actions"]]


def main():
    subprocess.run(["taskkill", "/F", "/IM", "Orbit.exe"], capture_output=True)
    time.sleep(0.8)

    env = dict(os.environ, ORBIT_OPEN_SETTINGS="1", ORBIT_TRACE="1")
    subprocess.Popen([EXE], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    hwnd = find_window("Orbit 设置")
    if not hwnd:
        print("FAIL 找不到设置窗口")
        return 1
    time.sleep(1.2)
    print(f"窗口句柄 {hwnd}")

    ox, oy, scale = client_origin(hwnd)
    cw, ch = client_size(hwnd)
    lw, lh = cw / scale, ch / scale
    print(f"客户区原点 ({ox},{oy}) 缩放 {scale} 逻辑尺寸 {lw:.0f}x{lh:.0f}")

    def at(cx, cy):
        return ox + cx * scale, oy + cy * scale

    before = read_order()
    print(f"拖动前：{before}")

    # 列表在右栏：x 取行内容中部，y 取第 1 行和第 3 行中心（行高 40 + 间距 4）
    x1, y1 = at(600, 168)
    x2, y2 = at(600, 256)
    drag(x1, y1, x2, y2)

    # 保存按钮贴右下角：右边距 24，按钮宽 84；底边距 18，按钮高 34。
    sx, sy = at(lw - 24 - 42, lh - 18 - 17)
    click(sx, sy)
    time.sleep(0.9)

    after = read_order()
    print(f"保存后：{after}")

    ok = len(after) == len(before) and after != before and after[2] == before[0]
    print("PASS 顺序已按拖动结果写盘" if ok else "FAIL 顺序没有按预期变化")

    subprocess.run(["taskkill", "/F", "/IM", "Orbit.exe"], capture_output=True)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
