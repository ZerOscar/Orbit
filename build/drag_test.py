"""拖拽模式端到端测试：Ctrl+Alt 按下拖动 → 菜单出现 → 移到顶部切片 → 松手 → 应执行动作。"""

import ctypes
import os
import subprocess
import time

# 声明 DPI 感知，否则 SetCursorPos 用的是被虚拟化的逻辑坐标，和钩子报告的物理坐标对不上。
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    pass

user32 = ctypes.WinDLL("user32", use_last_error=True)
user32.FindWindowW.restype = ctypes.c_void_p
user32.FindWindowW.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p]

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.environ.get("ORBIT_EXE") or os.path.join(
    ROOT, "src", "Orbit.App", "bin", "Debug", "net9.0", "Orbit.exe")
VK_CTRL, VK_ALT = 0x11, 0x12
LEFTDOWN, LEFTUP = 0x0002, 0x0004


def key(vk, up=False):
    user32.keybd_event(vk, 0, 2 if up else 0, 0)


def mouse(flag):
    user32.mouse_event(flag, 0, 0, 0, 0)


def menu_state():
    hwnd = user32.FindWindowW(None, "Orbit")
    return bool(hwnd) and bool(user32.IsWindowVisible(hwnd))


def main():
    subprocess.run(["taskkill", "/F", "/IM", "Orbit.exe"], capture_output=True)
    time.sleep(1.5)

    log_path = os.path.join(os.environ["APPDATA"], "Orbit", "orbit.log")
    if os.path.exists(log_path):
        os.remove(log_path)

    subprocess.Popen([EXE], env=dict(os.environ, ORBIT_TRACE="1"),
                     stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    time.sleep(5)

    # 光标先放到屏幕中间偏下，避免贴边被钳制影响判断
    point = (ctypes.c_long * 2)()
    user32.GetCursorPos(point)
    origin = (point[0], point[1])
    user32.SetCursorPos(*origin)
    time.sleep(0.2)

    print("1. 按住 Ctrl+Alt 并按下左键")
    key(VK_CTRL)
    key(VK_ALT)
    time.sleep(0.12)
    mouse(LEFTDOWN)
    time.sleep(0.12)

    print("2. 拖动越过阈值")
    for dx in (6, 12, 18, 24):
        user32.SetCursorPos(origin[0] + dx, origin[1])
        time.sleep(0.06)

    time.sleep(0.9)
    print(f"   菜单已打开: {menu_state()}")

    print("3. 把光标移到正上方的切片（距中心一个切片半径）")
    user32.SetCursorPos(origin[0], origin[1] - 94)
    time.sleep(0.5)

    print("4. 松开左键")
    mouse(LEFTUP)
    key(VK_ALT, up=True)
    key(VK_CTRL, up=True)
    time.sleep(1.2)

    print(f"   松手后菜单可见: {menu_state()}")

    print("\n--- 日志 ---")
    if os.path.exists(log_path):
        with open(log_path, encoding="utf-8-sig") as handle:
            for line in handle:
                print("  " + line.rstrip())

    print(f"\n进程存活: {'Orbit.exe' in subprocess.run(['tasklist', '/FI', 'IMAGENAME eq Orbit.exe', '/NH'], capture_output=True, text=True, encoding='gbk', errors='ignore').stdout}")


if __name__ == "__main__":
    main()
