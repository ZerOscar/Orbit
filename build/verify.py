"""端到端验证：发送 Ctrl+Alt+Space，检查环形菜单窗口是否真的出现又消失。"""

import ctypes
import time

user32 = ctypes.WinDLL("user32", use_last_error=True)
user32.FindWindowW.restype = ctypes.c_void_p
user32.FindWindowW.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p]
user32.IsWindowVisible.argtypes = [ctypes.c_void_p]
user32.GetWindowRect.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_long * 4)]

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


def state(label):
    hwnd = user32.FindWindowW(None, "Orbit")
    if not hwnd:
        print(f"{label}: 窗口不存在")
        return None
    visible = bool(user32.IsWindowVisible(hwnd))
    rect = (ctypes.c_long * 4)()
    user32.GetWindowRect(hwnd, rect)
    print(f"{label}: hwnd=0x{hwnd:X} visible={visible} rect=({rect[0]},{rect[1]},{rect[2]},{rect[3]})")
    return hwnd, visible


before = state("打开前")
chord()
time.sleep(1.0)
opened = state("按一次快捷键后")
chord()
time.sleep(1.2)
closed = state("再按一次后")

ok = opened is not None and opened[1]
print("\n结果:", "通过 — 菜单可以正常打开" if ok else "失败 — 菜单没有出现")
