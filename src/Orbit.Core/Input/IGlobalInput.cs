// 平台无关的输入事件模型。Windows 实现走 SetWindowsHookEx，将来 macOS 换 CGEventTap 即可。

namespace Orbit.Core.Input;

public enum GlobalMouseAction
{
    Move,
    Down,
    Up,
}

public readonly record struct GlobalMouseEvent(GlobalMouseAction Action, TriggerMouseButton Button, int X, int Y,
    ModifierKeys Modifiers);

public readonly record struct GlobalKeyEvent(bool Down, int VirtualKey, ModifierKeys Modifiers);

/// <summary>全局输入源。实现必须保证回调极轻——系统会静默移除响应过慢的低级钩子。</summary>
public interface IGlobalInput : IDisposable
{
    event Action<GlobalMouseEvent>? Mouse;
    event Action<GlobalKeyEvent>? Key;

    bool IsRunning { get; }

    /// <summary>启动失败时的原因，成功为 null。</summary>
    string? Error { get; }

    bool Start();
    void Stop();

    /// <summary>当前光标位置（物理像素）。用于快捷键触发时决定菜单落在哪里。</summary>
    (int X, int Y) CursorPosition { get; }
}
