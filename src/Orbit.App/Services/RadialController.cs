// 打开、填充、关闭在光标处的环形菜单。两种模式共用同一个窗口：
//
// 点击模式（快捷键）：窗口激活，指针事件自己处理，Esc / 失焦即关。
// 拖拽模式（修饰键 + 拖）：窗口不激活——否则会把源窗口的拖拽打断——
//   所以指针位置只能从全局钩子喂进来，松手时由 CommitDrag 判定落点。
//
// 位置按物理像素算，钳制进当前屏幕的工作区，避免贴边时跑出屏幕。

using Avalonia;
using Orbit.App.Actions;
using Orbit.App.Controls;
using Orbit.App.Views;
using Orbit.Core.Config;
using Orbit.App.Services;

namespace Orbit.App.Services;

public sealed class RadialController : IDisposable
{
    /// <summary>刚打开时的失焦宽限期：激活窗口的过程中会短暂丢一次焦点，不能当成「点了别处」。</summary>
    private static readonly TimeSpan ActivationGrace = TimeSpan.FromMilliseconds(350);

    private readonly ActionRunner _runner = new();
    private OrbitConfig _config;
    private RadialWindow? _window;
    private bool _visible;
    private bool _dragMode;
    private long _shownAt;
    private PixelPoint _position;
    private double _scaling = 1;

    public RadialController(OrbitConfig config) => _config = config;

    /// <summary>动作执行失败时抛给人看的一句话。</summary>
    public event Action<string>? Failed;

    public bool IsVisible => _visible;

    /// <summary>当前承载菜单的窗口（离屏渲染截图用）。</summary>
    internal RadialWindow? Window => _window;

    public string? LastError => _runner.LastError;

    public void Apply(OrbitConfig config)
    {
        _config = config;
        if (_visible)
            LoadItems();
    }

    /// <summary>点击模式：菜单开着就关，否则在光标处打开。</summary>
    public void ToggleAt(int x, int y)
    {
        if (_visible && !_dragMode)
            Close();
        else
            ShowAt(x, y, activate: true);
    }

    /// <summary>拖拽模式：在按下的位置打开，但不抢焦点。</summary>
    public void BeginDrag(int x, int y) => ShowAt(x, y, activate: false);

    public void ShowAt(int x, int y, bool activate)
    {
        if (Environment.GetEnvironmentVariable("ORBIT_TRACE") == "1")
            Log.Write($"[trace] ShowAt {x},{y} activate={activate}");
        var window = EnsureWindow();
        _dragMode = !activate;
        LoadItems();
        Place(window, x, y);

        _visible = true;
        _shownAt = Environment.TickCount64;
        if (!window.IsVisible)
            window.Show();
        Place(window, x, y); // 显示后 DPI 可能变，再定位一次
        if (activate)
            window.Activate();
        window.DonutControl.PlayOpenAnimation();
    }

    /// <summary>拖拽中：把光标位置喂给菜单，决定哪个切片亮着。</summary>
    public void UpdatePointer(int x, int y)
    {
        if (!_visible || !_dragMode || _window is null)
            return;
        _window.DonutControl.SetExternalHighlight(ToLocal(x, y));
    }

    /// <summary>拖拽松手：落在切片上就执行，落在空处就收起。返回是否执行了动作。</summary>
    public bool CommitDrag(int x, int y)
    {
        if (!_visible || !_dragMode || _window is null)
            return false;

        var local = ToLocal(x, y);
        var index = _window.DonutControl.IndexAtExternal(local);
        if (Environment.GetEnvironmentVariable("ORBIT_TRACE") == "1")
            Log.Write($"[trace] CommitDrag 屏幕({x},{y}) 窗口({_position.X},{_position.Y}) 缩放{_scaling} 局部({local.X:0},{local.Y:0}) → 切片 {index}");
        if (index < 0)
        {
            Close();
            return false;
        }
        Invoke(index);
        return true;
    }

    public void Close(bool animate = true)
    {
        if (!_visible || _window is null)
            return;
        _visible = false;
        _dragMode = false;
        var window = _window;
        if (animate)
            window.DonutControl.PlayCloseAnimation(() =>
            {
                if (!_visible)
                    window.Hide();
            });
        else
            window.Hide();
    }

    private RadialWindow EnsureWindow()
    {
        if (_window is not null)
            return _window;

        var window = new RadialWindow();
        window.DonutControl.SegmentActivated += (_, index) => Invoke(index);
        window.DonutControl.CenterActivated += (_, _) => Close();
        window.Deactivated += (_, _) =>
        {
            // 拖拽模式下窗口本来就不激活，失焦没有意义。
            if (_visible && !_dragMode && Environment.TickCount64 - _shownAt > ActivationGrace.TotalMilliseconds)
                Close();
        };
        _window = window;
        return window;
    }

    private void Invoke(int index)
    {
        var visible = _config.Actions.Where(a => a.Enabled).ToList();
        var action = index >= 0 && index < visible.Count ? visible[index] : null;
        // 先收起菜单再执行：启动程序可能耗几百毫秒，菜单挡在前面会显得卡住。
        Close(animate: false);
        if (action is null)
            return;
        Log.Write($"执行动作：{action.Label} ({action.Kind})");
        if (!_runner.Run(action))
            Failed?.Invoke(_runner.LastError ?? $"「{action.Label}」执行失败");
    }

    private void LoadItems()
    {
        if (_window is null)
            return;
        var donut = _window.DonutControl;
        donut.Items = _config.Actions
            .Where(a => a.Enabled)
            .Select(a => new DonutItem
            {
                Label = a.Label,
                Glyph = string.IsNullOrWhiteSpace(a.Glyph) ? null : a.Glyph,
                Reason = a.Hint,
            })
            .ToList();
        donut.OuterRadius = _config.OuterRadius;
        donut.HoleRatio = _config.HoleRatio;
        donut.SliceOpacity = _config.SliceOpacity;
    }

    private Point ToLocal(int x, int y) =>
        new((x - _position.X) / _scaling, (y - _position.Y) / _scaling);

    private void Place(RadialWindow window, int x, int y)
    {
        var cursor = new PixelPoint(x, y);
        var screen = window.Screens.ScreenFromPoint(cursor) ?? window.Screens.Primary;
        var scaling = screen?.Scaling ?? 1.0;

        var side = DonutMenu.SideFor(_config.OuterRadius);
        window.Width = side;
        window.Height = side;

        var sidePx = (int)Math.Ceiling(side * scaling);
        var left = cursor.X - sidePx / 2;
        var top = cursor.Y - sidePx / 2;

        if (screen is { } s)
        {
            var area = s.WorkingArea;
            left = Math.Clamp(left, area.X, Math.Max(area.X, area.Right - sidePx));
            top = Math.Clamp(top, area.Y, Math.Max(area.Y, area.Bottom - sidePx));
        }

        _position = new PixelPoint(left, top);
        _scaling = scaling;
        window.Position = _position;
    }

    public void Dispose()
    {
        _window?.Close();
        _window = null;
    }
}
