"""回归测试：快捷键必须在「当前光标处」打开菜单。

之前的状态机只在鼠标事件里更新坐标，导致快捷键用的是上一次点击的位置
（刚启动时是 0,0）。这个脚本把光标挪到几个点，按快捷键，比对窗口中心。
"""

import ctypes
import sys
import time

# 必须声明 DPI 感知，否则拿到的坐标会被系统按缩放比例换算，判定全错。
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    ctypes.windll.user32.SetProcessDPIAware()

user32 = ctypes.WinDLL("user32", use_last_error=True)
user32.FindWindowW.restype = ctypes.c_void_p
user32.FindWindowW.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p]
user32.IsWindowVisible.argtypes = [ctypes.c_void_p]
user32.GetWindowRect.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_long * 4)]
user32.GetCursorPos.argtypes = [ctypes.POINTER(ctypes.c_long * 2)]

VK_CTRL, VK_ALT, VK_SPACE = 0x11, 0x12, 0x20
KEYUP = 0x0002


def chord():
    user32.keybd_event(VK_CTRL, 0, 0, 0)
    user32.keybd_event(VK_ALT, 0, 0, 0)
    time.sleep(0.06)
    user32.keybd_event(VK_SPACE, 0, 0, 0)
    time.sleep(0.06)
    user32.keybd_event(VK_SPACE, 0, KEYUP, 0)
    user32.keybd_event(VK_ALT, 0, KEYUP, 0)
    user32.keybd_event(VK_CTRL, 0, KEYUP, 0)


def cursor():
    p = (ctypes.c_long * 2)()
    user32.GetCursorPos(p)
    return p[0], p[1]


def window_rect():
    hwnd = user32.FindWindowW(None, "Orbit")
    if not hwnd:
        return None
    rect = (ctypes.c_long * 4)()
    user32.GetWindowRect(hwnd, rect)
    return rect[0], rect[1], rect[2], rect[3]


def work_area():
    rect = (ctypes.c_long * 4)()
    # SPI_GETWORKAREA = 0x0030
    user32.SystemParametersInfoW(0x0030, 0, ctypes.byref(rect), 0)
    return rect[0], rect[1], rect[2], rect[3]


def check(x, y, tolerance=3):
    user32.SetCursorPos(x, y)
    time.sleep(0.15)
    cx, cy = cursor()
    chord()
    time.sleep(0.75)
    rect = window_rect()
    if rect is None:
        print(f"  ({x},{y}) 窗口没出现")
        return False

    side = rect[2] - rect[0]
    wa = work_area()
    left = min(max(cx - side // 2, wa[0]), max(wa[0], wa[2] - side))
    top = min(max(cy - side // 2, wa[1]), max(wa[1], wa[3] - side))
    dx, dy = rect[0] - left, rect[1] - top
    clamped = (left, top) != (cx - side // 2, cy - side // 2)
    ok = abs(dx) <= tolerance and abs(dy) <= tolerance
    print(f"  光标({cx},{cy}) 窗口{rect} 期望左上({left},{top}) "
          f"偏差({dx:+d},{dy:+d}){'  [贴边钳制]' if clamped else ''} {'OK' if ok else '不符'}")
    chord()          # 收起
    time.sleep(0.5)
    return ok


points = [(int(a), int(b)) for a, b in (p.split(",") for p in sys.argv[1:])] or [
    (400, 300), (1200, 700), (700, 950), (60, 60),
]
print("快捷键落点检查：")
results = [check(x, y) for x, y in points]
print("\n结果:", "全部通过" if all(results) else "有偏差")
sys.exit(0 if all(results) else 1)
