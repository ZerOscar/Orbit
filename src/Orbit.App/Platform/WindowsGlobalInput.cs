// Windows 全局输入：WH_MOUSE_LL + WH_KEYBOARD_LL，跑在专用线程上带自己的消息循环。
//
// 两条铁律：
// 1. 回调必须在毫秒级返回。系统会静默移除响应过慢的低级钩子，而且不报错。
// 2. 委托必须保持强引用。被 GC 回收后系统回调进野指针，直接崩进程。

using System.Runtime.InteropServices;
using Orbit.Core.Input;

namespace Orbit.App.Platform;

public sealed class WindowsGlobalInput : IGlobalInput
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const uint WmQuit = 0x0012;

    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    private readonly HookProc _mouseProc;
    private readonly HookProc _keyProc;
    private readonly ManualResetEventSlim _ready = new(false);

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _mouseHook;
    private IntPtr _keyHook;
    private volatile bool _running;
    private volatile bool _mouseDown;

    public WindowsGlobalInput()
    {
        // 存成字段，保证生命周期覆盖钩子存活期。
        _mouseProc = MouseCallback;
        _keyProc = KeyboardCallback;
    }

    public event Action<GlobalMouseEvent>? Mouse;
    public event Action<GlobalKeyEvent>? Key;

    public bool IsRunning => _running;

    public string? Error { get; private set; }

    public (int X, int Y) CursorPosition =>
        GetCursorPos(out var point) ? (point.X, point.Y) : (0, 0);

    public bool Start()
    {
        if (_running)
            return true;
        if (!OperatingSystem.IsWindows())
        {
            Error = "全局输入目前只实现了 Windows。";
            return false;
        }

        Error = null;
        _ready.Reset();
        _thread = new Thread(HookThreadMain)
        {
            IsBackground = true,
            Name = "Orbit.InputHook",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            Error = "钩子线程启动超时。";
            return false;
        }
        return _running;
    }

    public void Stop()
    {
        if (!_running)
            return;
        if (_threadId != 0)
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _threadId = 0;
        _running = false;
    }

    private void HookThreadMain()
    {
        _threadId = GetCurrentThreadId();
        var module = GetModuleHandle(null);

        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, module, 0);
        _keyHook = SetWindowsHookEx(WhKeyboardLl, _keyProc, module, 0);

        if (_mouseHook == IntPtr.Zero || _keyHook == IntPtr.Zero)
        {
            Error = $"安装钩子失败（Win32 错误 {Marshal.GetLastWin32Error()}）。可能被安全软件拦截。";
            if (_mouseHook != IntPtr.Zero)
                UnhookWindowsHookEx(_mouseHook);
            if (_keyHook != IntPtr.Zero)
                UnhookWindowsHookEx(_keyHook);
            _mouseHook = _keyHook = IntPtr.Zero;
            _ready.Set();
            return;
        }

        _running = true;
        _ready.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        _running = false;
        UnhookWindowsHookEx(_mouseHook);
        UnhookWindowsHookEx(_keyHook);
        _mouseHook = _keyHook = IntPtr.Zero;
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            var message = (int)wParam;

            if (message == WmMouseMove)
            {
                // 没有按键按下时不报告移动：菜单只在拖拽期间关心光标，否则每秒几十次无谓开销。
                if (_mouseDown)
                    Mouse?.Invoke(new GlobalMouseEvent(GlobalMouseAction.Move, TriggerMouseButton.None, data.Point.X,
                        data.Point.Y, ReadModifiers()));
            }
            else if (TryMapButton(message, out var button, out var action))
            {
                _mouseDown = action == GlobalMouseAction.Down;
                Mouse?.Invoke(new GlobalMouseEvent(action, button, data.Point.X, data.Point.Y, ReadModifiers()));
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = (int)wParam;
            if (message is WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp)
            {
                var data = Marshal.PtrToStructure<KbdllHookStruct>(lParam);
                var down = message is WmKeyDown or WmSysKeyDown;
                Key?.Invoke(new GlobalKeyEvent(down, (int)data.VirtualKey, ReadModifiers()));
            }
        }
        return CallNextHookEx(_keyHook, nCode, wParam, lParam);
    }

    private static bool TryMapButton(int message, out TriggerMouseButton button, out GlobalMouseAction action)
    {
        (button, action) = message switch
        {
            WmLButtonDown => (TriggerMouseButton.Left, GlobalMouseAction.Down),
            WmLButtonUp => (TriggerMouseButton.Left, GlobalMouseAction.Up),
            WmRButtonDown => (TriggerMouseButton.Right, GlobalMouseAction.Down),
            WmRButtonUp => (TriggerMouseButton.Right, GlobalMouseAction.Up),
            WmMButtonDown => (TriggerMouseButton.Middle, GlobalMouseAction.Down),
            WmMButtonUp => (TriggerMouseButton.Middle, GlobalMouseAction.Up),
            _ => (TriggerMouseButton.None, GlobalMouseAction.Move),
        };
        return button != TriggerMouseButton.None;
    }

    private static ModifierKeys ReadModifiers() => VirtualKeys.ModifiersOf(
        IsDown(VirtualKeys.Control), IsDown(VirtualKeys.Menu), IsDown(VirtualKeys.Shift),
        IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin));

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose() => Stop();

    // ───────────── Win32 ─────────────

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public Point Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdllHookStruct
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg msg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
