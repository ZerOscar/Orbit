"""抓 Orbit 菜单窗口自身（PrintWindow + PW_RENDERFULLCONTENT），用于检查渲染效果。"""

import ctypes
import struct
import time
import zlib
from ctypes import wintypes

user32 = ctypes.WinDLL("user32", use_last_error=True)
gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
user32.FindWindowW.restype = ctypes.c_void_p
user32.FindWindowW.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p]


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


def write_png(path, w, h, raw):
    rows = bytearray()
    stride = w * 4
    for row in range(h):
        rows.append(0)
        rows.extend(raw[row * stride : (row + 1) * stride])

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    png = (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(bytes(rows), 6))
        + chunk(b"IEND", b"")
    )
    with open(path, "wb") as handle:
        handle.write(png)


def capture(hwnd, path):
    rect = (ctypes.c_long * 4)()
    user32.GetWindowRect(hwnd, rect)
    w, h = rect[2] - rect[0], rect[3] - rect[1]
    if w <= 0 or h <= 0:
        print("窗口尺寸无效", w, h)
        return

    screen = user32.GetDC(0)
    mem = gdi32.CreateCompatibleDC(screen)
    bitmap = gdi32.CreateCompatibleBitmap(screen, w, h)
    gdi32.SelectObject(mem, bitmap)

    ok = user32.PrintWindow(hwnd, mem, 2)  # PW_RENDERFULLCONTENT
    print(f"PrintWindow={ok} size={w}x{h}")

    info = BITMAPINFO()
    info.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    info.bmiHeader.biWidth = w
    info.bmiHeader.biHeight = -h
    info.bmiHeader.biPlanes = 1
    info.bmiHeader.biBitCount = 32
    info.bmiHeader.biCompression = 0
    buffer = ctypes.create_string_buffer(w * h * 4)
    gdi32.GetDIBits(mem, bitmap, 0, h, buffer, ctypes.byref(info), 0)

    gdi32.DeleteObject(bitmap)
    gdi32.DeleteDC(mem)
    user32.ReleaseDC(0, screen)

    raw = bytearray(buffer.raw)
    raw[0::4], raw[2::4] = raw[2::4], raw[0::4]
    for i in range(3, len(raw), 4):
        raw[i] = 255

    nonzero = sum(1 for i in range(0, len(raw), 4) if raw[i] or raw[i + 1] or raw[i + 2])
    print(f"非黑像素: {nonzero} / {w * h}")
    write_png(path, w, h, raw)
    print(f"saved {path}")


if __name__ == "__main__":
    chord()
    time.sleep(1.2)
    hwnd = user32.FindWindowW(None, "Orbit")
    if not hwnd:
        print("找不到 Orbit 窗口")
    else:
        print(f"hwnd=0x{hwnd:X}")
        capture(hwnd, "build/shot-window.png")
    chord()
    time.sleep(0.5)
