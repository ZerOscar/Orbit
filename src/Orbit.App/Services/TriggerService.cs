// 把全局输入接到状态机上，并把识别出的手势统一投递到 UI 线程。
// 钩子线程只做「转换 + 投递」，任何 UI 工作都不在这里做。

using Avalonia.Threading;
using Orbit.Core.Input;

namespace Orbit.App.Services;

public sealed class GestureEventArgs(TriggerGesture? gesture, int x, int y) : EventArgs
{
    public TriggerGesture? Gesture { get; } = gesture;
    public int X { get; } = x;
    public int Y { get; } = y;
}

public sealed class TriggerService : IDisposable
{
    private readonly IGlobalInput _input;
    private readonly GestureDetector _detector = new();
    private TriggerMouseButton _heldButton = TriggerMouseButton.None;
    private int _lastPostedX = int.MinValue;
    private int _lastPostedY = int.MinValue;

    public TriggerService(IGlobalInput input)
    {
        _input = input;
        _input.Mouse += OnMouse;
        _input.Key += OnKey;

        _detector.DragStarted += (g, x, y) => Post(() => DragStarted?.Invoke(this, new GestureEventArgs(g, x, y)));
        _detector.DragEnded += (x, y) => Post(() => DragEnded?.Invoke(this, new GestureEventArgs(null, x, y)));
        _detector.HoldTriggered += (g, x, y) => Post(() => SelectionGesture?.Invoke(this, new GestureEventArgs(g, x, y)));
        _detector.KeyChordTriggered += OnKeyChord;
        _detector.HoldTimerRequested += (token, milliseconds) =>
            _ = Task.Delay(milliseconds).ContinueWith(_ => _detector.HoldElapsed(token), TaskScheduler.Default);
    }

    /// <summary>
    /// 快捷键的坐标不能用状态机里存的那个：它只在鼠标事件里更新，而鼠标移动只在按住键时才上报，
    /// 所以那个值可能是几分钟前点过的位置（刚启动时干脆是 0,0）。直接向系统要当前光标位置。
    /// </summary>
    private void OnKeyChord(TriggerGesture gesture, int _, int __)
    {
        var (x, y) = _input.CursorPosition;
        Post(() => SelectionGesture?.Invoke(this, new GestureEventArgs(gesture, x, y)));
    }

    /// <summary>修饰键 + 拖拽开始了（此时还不知道会拖来什么）。</summary>
    public event EventHandler<GestureEventArgs>? DragStarted;

    /// <summary>拖拽手势的鼠标键被松开。</summary>
    public event EventHandler<GestureEventArgs>? DragEnded;

    /// <summary>长按或快捷键：直接在光标处打开菜单。</summary>
    public event EventHandler<GestureEventArgs>? SelectionGesture;

    /// <summary>按住鼠标移动时的光标位置（节流后的）。拖拽模式下用来更新菜单高亮。</summary>
    public event EventHandler<GestureEventArgs>? PointerMoved;

    public bool IsRunning => _input.IsRunning;

    public string? Error => _input.Error;

    public void Configure(IReadOnlyList<TriggerGesture> gestures) => _detector.Configure(gestures);

    /// <summary>暂停后不再识别任何手势。</summary>
    public bool Paused
    {
        get => _detector.Paused;
        set => _detector.Paused = value;
    }

    public bool Start() => _input.Start();

    public void Stop() => _input.Stop();

    private void OnMouse(GlobalMouseEvent e)
    {
        switch (e.Action)
        {
            case GlobalMouseAction.Down:
                if (Environment.GetEnvironmentVariable("ORBIT_TRACE") == "1")
                    Log.Write($"[trace] 按下 {e.Button} @{e.X},{e.Y} 修饰 {e.Modifiers}");
                _heldButton = e.Button;
                _detector.MouseDown(e.Button, e.X, e.Y, e.Modifiers);
                break;
            case GlobalMouseAction.Move:
                _detector.MouseMove(e.X, e.Y, e.Modifiers, _heldButton);
                // 移动事件在按住时才来，频率可达 125Hz：位移太小就不必打扰 UI 线程。
                if (Math.Abs(e.X - _lastPostedX) >= 2 || Math.Abs(e.Y - _lastPostedY) >= 2)
                {
                    _lastPostedX = e.X;
                    _lastPostedY = e.Y;
                    Post(() => PointerMoved?.Invoke(this, new GestureEventArgs(null, e.X, e.Y)));
                }
                break;
            case GlobalMouseAction.Up:
                _detector.MouseUp(e.Button, e.X, e.Y);
                _heldButton = TriggerMouseButton.None;
                break;
        }
    }

    private void OnKey(GlobalKeyEvent e)
    {
        if (e.Down)
        {
            if (Environment.GetEnvironmentVariable("ORBIT_TRACE") == "1")
                Log.Write($"[trace] 键 {VirtualKeys.NameOf(e.VirtualKey)} 修饰 {e.Modifiers}");
            _detector.KeyDown(VirtualKeys.NameOf(e.VirtualKey), e.Modifiers);
        }
    }

    private static void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    public void Dispose()
    {
        _input.Mouse -= OnMouse;
        _input.Key -= OnKey;
        _input.Dispose();
    }
}
