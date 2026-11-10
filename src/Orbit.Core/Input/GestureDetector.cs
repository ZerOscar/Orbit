// 纯状态机：把「鼠标按下 / 移动 / 抬起、按键」这些原始事件翻译成「打开菜单」的语义手势。
// 它不认识任何操作系统 API，输入只有坐标和修饰键，所以可以完全确定性地单测。
//
// 拖拽：按下时修饰键匹配 → 装填 → 移动超过阈值 → DragStarted → 抬起 → DragEnded
// 长按：按下时修饰键匹配 → 装填 → 停留够久且没移动 → HoldTriggered
// 两个可以共用同一次按下：动就是拖拽（长按取消），不动就是长按。
// 快捷键：按下某个键 + 修饰键精确匹配 → KeyChordTriggered（用最后一次已知光标位置）

namespace Orbit.Core.Input;

public sealed class GestureDetector
{
    private readonly object _lock = new();
    private IReadOnlyList<TriggerGesture> _gestures = [];
    private Armed? _armed;
    private bool _dragActive;
    private bool _lateRejected;
    private int _lastX, _lastY;
    private long _tokenSeed;

    /// <summary>一次按下之后，在拖拽或长按真正发生之前保持的状态。</summary>
    private sealed record Armed(TriggerGesture? Drag, TriggerGesture? Hold, int X, int Y, long Token);

    /// <summary>拖拽阈值的兜底值（像素），当手势配置为 0 = 系统默认时用。</summary>
    public int SystemDragThreshold { get; set; } = 4;

    /// <summary>暂停时什么手势都不识别（托盘的「暂停」）。</summary>
    public bool Paused { get; set; }

    public event Action<TriggerGesture, int, int>? DragStarted;
    public event Action<int, int>? DragEnded;
    public event Action<TriggerGesture, int, int>? HoldTriggered;
    public event Action<TriggerGesture, int, int>? KeyChordTriggered;

    /// <summary>长按进入装填状态时触发，调用方负责在延时后回调 <see cref="HoldElapsed"/>。</summary>
    public event Action<long, int>? HoldTimerRequested;

    public void Configure(IEnumerable<TriggerGesture> gestures)
    {
        lock (_lock)
        {
            _gestures = gestures.Where(g => g.Enabled).Select(g => g.Clone()).ToList();
            _armed = null;
            _dragActive = false;
            _lateRejected = false;
        }
    }

    public void MouseDown(TriggerMouseButton button, int x, int y, ModifierKeys modifiers)
    {
        TriggerGesture? hold = null;
        long token = 0;
        lock (_lock)
        {
            (_lastX, _lastY) = (x, y);
            _armed = null;
            _lateRejected = false;
            if (Paused)
                return;

            var drag = FirstInScope(TriggerKind.Drag, button, modifiers);
            hold = FirstInScope(TriggerKind.Hold, button, modifiers);
            if (drag is null && hold is null)
                return;

            token = ++_tokenSeed;
            _armed = new Armed(drag, hold, x, y, token);
        }

        if (hold is not null)
            HoldTimerRequested?.Invoke(token, hold.HoldMilliseconds);
    }

    /// <param name="heldButton">当前按住的鼠标键，用于识别「先开始拖、再按修饰键」。</param>
    public void MouseMove(int x, int y, ModifierKeys modifiers, TriggerMouseButton heldButton = TriggerMouseButton.None)
    {
        TriggerGesture? fire;
        lock (_lock)
        {
            (_lastX, _lastY) = (x, y);
            fire = _armed is null ? DetectLateModifier(modifiers, heldButton) : DetectDragPastThreshold(x, y);
            if (fire is not null)
                _dragActive = true;
        }

        if (fire is not null)
            DragStarted?.Invoke(fire, x, y);
    }

    public void MouseUp(TriggerMouseButton button, int x, int y)
    {
        bool ended;
        lock (_lock)
        {
            (_lastX, _lastY) = (x, y);
            _armed = null;
            ended = _dragActive;
            _dragActive = false;
            _lateRejected = false;
        }
        if (ended)
            DragEnded?.Invoke(x, y);
    }

    public void HoldElapsed(long token)
    {
        TriggerGesture? fire = null;
        int x = 0, y = 0;
        lock (_lock)
        {
            if (_armed is { Hold: { } hold } armed && armed.Token == token)
            {
                fire = hold;
                (x, y) = (armed.X, armed.Y);
                _armed = null;
                // 长按已经触发，之后即使指针移动也不再开始共享同一修饰键的拖拽。
                _lateRejected = true;
            }
        }
        if (fire is not null)
            HoldTriggered?.Invoke(fire, x, y);
    }

    public void KeyDown(string key, ModifierKeys modifiers)
    {
        TriggerGesture? fire;
        int x, y;
        lock (_lock)
        {
            if (Paused)
                return;
            (x, y) = (_lastX, _lastY);
            fire = _gestures.FirstOrDefault(g =>
                g.Kind == TriggerKind.KeyChord &&
                string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase) &&
                g.Modifiers == modifiers);
        }
        if (fire is not null)
            KeyChordTriggered?.Invoke(fire, x, y);
    }

    /// <summary>同一个键和修饰键上的第一个手势。调用方必须持有锁。</summary>
    private TriggerGesture? FirstInScope(TriggerKind kind, TriggerMouseButton button, ModifierKeys modifiers) =>
        _gestures.FirstOrDefault(g => g.Kind == kind && g.Button == button && g.Modifiers == modifiers);

    private TriggerGesture? DetectDragPastThreshold(int x, int y)
    {
        var armed = _armed!;
        // 动了就不再是长按。
        if (armed.Hold is { } hold && Moved(hold, armed, x, y))
            armed = armed with { Hold = null };

        if (armed.Drag is { } drag && Moved(drag, armed, x, y))
        {
            _armed = null;
            return drag;
        }

        _armed = armed.Drag is null && armed.Hold is null ? null : armed;
        return null;
    }

    private bool Moved(TriggerGesture gesture, Armed armed, int x, int y)
    {
        var threshold = gesture.DragThreshold > 0 ? gesture.DragThreshold : SystemDragThreshold;
        return Math.Abs(x - armed.X) > threshold || Math.Abs(y - armed.Y) > threshold;
    }

    /// <summary>
    /// 修饰键是在拖拽已经开始之后才按下的（很常见：先拖起来，再按 Ctrl）。
    /// 把它当成「刚刚越过阈值的拖拽」处理。
    /// </summary>
    private TriggerGesture? DetectLateModifier(ModifierKeys modifiers, TriggerMouseButton heldButton)
    {
        if (_dragActive || _lateRejected || Paused || heldButton == TriggerMouseButton.None ||
            modifiers == ModifierKeys.None)
            return null;

        return _gestures.FirstOrDefault(g =>
            g.Kind == TriggerKind.Drag && g.Button == heldButton && g.Modifiers == modifiers);
    }
}
