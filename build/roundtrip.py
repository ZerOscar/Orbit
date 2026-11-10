"""一轮完整回归：启动 → 打开菜单 → 抓图 → 关闭 → 检查进程是否存活。"""

import ctypes
import os
import struct
import subprocess
import time
import zlib
from ctypes import wintypes

user32 = ctypes.WinDLL("user32", use_last_error=True)
gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
user32.FindWindowW.restype = ctypes.c_void_p
user32.FindWindowW.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p]

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.environ.get("ORBIT_EXE") or os.path.join(
    ROOT, "src", "Orbit.App", "bin", "Debug", "net9.0", "Orbit.exe")
SHOT = os.path.join(ROOT, "build", "shot-window.png")


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", wintypes.DWORD), ("biWidth", wintypes.LONG), ("biHeight", wintypes.LONG),
        ("biPlanes", wintypes.WORD), ("biBitCount", wintypes.WORD), ("biCompression", wintypes.DWORD),
        ("biSizeImage", wintypes.DWORD), ("biXPelsPerMeter", wintypes.LONG),
        ("biYPelsPerMeter", wintypes.LONG), ("biClrUsed", wintypes.DWORD), ("biClrImportant", wintypes.DWORD),
    ]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [("bmiHeader", BITMAPINFOHEADER), ("bmiColors", wintypes.DWORD * 3)]


def chord():
    for vk in (0x11, 0x12, 0x20):
        user32.keybd_event(vk, 0, 0, 0)
        time.sleep(0.05)
    for vk in (0x20, 0x12, 0x11):
        user32.keybd_event(vk, 0, 2, 0)


def alive():
    out = subprocess.run(
        ["tasklist", "/FI", "IMAGENAME eq Orbit.exe", "/NH"],
        capture_output=True, text=True, encoding="gbk", errors="ignore",
    ).stdout
    return "Orbit.exe" in out


def capture(hwnd, path):
    rect = (ctypes.c_long * 4)()
    user32.GetWindowRect(hwnd, rect)
    w, h = rect[2] - rect[0], rect[3] - rect[1]
    if w <= 0 or h <= 0:
        return False

    screen = user32.GetDC(0)
    mem = gdi32.CreateCompatibleDC(screen)
    bitmap = gdi32.CreateCompatibleBitmap(screen, w, h)
    gdi32.SelectObject(mem, bitmap)
    user32.PrintWindow(hwnd, mem, 2)

    info = BITMAPINFO()
    info.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    info.bmiHeader.biWidth = w
    info.bmiHeader.biHeight = -h
    info.bmiHeader.biPlanes = 1
    info.bmiHeader.biBitCount = 32
    buffer = ctypes.create_string_buffer(w * h * 4)
    gdi32.GetDIBits(mem, bitmap, 0, h, buffer, ctypes.byref(info), 0)
    gdi32.DeleteObject(bitmap)
    gdi32.DeleteDC(mem)
    user32.ReleaseDC(0, screen)

    raw = bytearray(buffer.raw)
    raw[0::4], raw[2::4] = raw[2::4], raw[0::4]
    for i in range(3, len(raw), 4):
        raw[i] = 255

    opaque = sum(1 for i in range(0, len(raw), 4) if raw[i] or raw[i + 1] or raw[i + 2])
    print(f"  画面尺寸 {w}x{h}，非黑像素 {opaque} ({opaque * 100 // (w * h)}%)")

    rows = bytearray()
    for row in range(h):
        rows.append(0)
        rows.extend(raw[row * w * 4 : (row + 1) * w * 4])

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    with open(path, "wb") as handle:
        handle.write(
            b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(rows), 6))
            + chunk(b"IEND", b"")
        )
    return True


def main():
    subprocess.run(["taskkill", "/F", "/IM", "Orbit.exe"], capture_output=True)
    time.sleep(1.5)

    log_path = os.path.join(os.environ["APPDATA"], "Orbit", "orbit.log")
    if os.path.exists(log_path):
        os.remove(log_path)

    env = dict(os.environ, ORBIT_TRACE="1")
    subprocess.Popen([EXE], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    time.sleep(5)

    print(f"1. 启动后进程存活: {alive()}")

    chord()
    time.sleep(1.3)
    hwnd = user32.FindWindowW(None, "Orbit")
    visible = bool(hwnd) and bool(user32.IsWindowVisible(hwnd))
    print(f"2. 菜单已打开: {visible} (hwnd=0x{hwnd:X})" if hwnd else "2. 菜单已打开: False")

    if hwnd and visible:
        if capture(hwnd, SHOT):
            print(f"3. 截图已保存: {SHOT}")

    chord()
    time.sleep(1.5)
    hwnd2 = user32.FindWindowW(None, "Orbit")
    print(f"4. 关闭后窗口可见: {bool(hwnd2) and bool(user32.IsWindowVisible(hwnd2))}")
    print(f"5. 关闭后进程仍存活: {alive()}")

    print("\n--- 日志 ---")
    if os.path.exists(log_path):
        with open(log_path, encoding="utf-8-sig") as handle:
            for line in handle:
                print("  " + line.rstrip())


if __name__ == "__main__":
    main()
