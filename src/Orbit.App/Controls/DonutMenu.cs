// 环形菜单自绘控件：整个环是一个 Control，没有子控件，为了速度。
// 动画全部是自研弹簧，帧循环带看门狗——请求丢失时不会卡在中间态。

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Orbit.App.Controls;

/// <summary>环上的一个切片。</summary>
public sealed class DonutItem
{
    public string Label { get; init; } = "";
    public string? Glyph { get; init; }
    public bool IsEnabled { get; init; } = true;
    /// <summary>不可用时的原因，显示在标签下面。</summary>
    public string? Reason { get; init; }
}

public sealed class DonutMenu : Control
{
    /// <summary>悬停时切片沿径向外推的距离。</summary>
    private const double PopOut = 6;

    /// <summary>画阴影预留的边距。</summary>
    private const double ShadowMargin = 16;

    /// <summary>切片之间的间隙（像素）。</summary>
    private const double Gap = 3.0;

    /// <summary>开场时每个切片比前一个晚多少秒。</summary>
    private const double OpenStagger = 0.018;

    /// <summary>单个切片的开场时长。</summary>
    private const double OpenDuration = 0.26;

    /// <summary>一帧没来超过这个时间就当帧循环死了。</summary>
    private const long StalledLoopMs = 250;

    /// <summary>帧循环死掉后重试次数，之后直接跳到终态。</summary>
    private const int FrameRetries = 2;

    public static readonly StyledProperty<IReadOnlyList<DonutItem>?> ItemsProperty =
        AvaloniaProperty.Register<DonutMenu, IReadOnlyList<DonutItem>?>(nameof(Items));

    public static readonly StyledProperty<double> OuterRadiusProperty =
        AvaloniaProperty.Register<DonutMenu, double>(nameof(OuterRadius), 130);

    public static readonly StyledProperty<double> HoleRatioProperty =
        AvaloniaProperty.Register<DonutMenu, double>(nameof(HoleRatio), 0.42);

    public static readonly StyledProperty<double> SliceOpacityProperty =
        AvaloniaProperty.Register<DonutMenu, double>(nameof(SliceOpacity), 0.92);

    public static readonly StyledProperty<double> SliceCornerRadiusProperty =
        AvaloniaProperty.Register<DonutMenu, double>(nameof(SliceCornerRadius), 6);

    public static readonly StyledProperty<IBrush?> SliceBrushProperty =
        AvaloniaProperty.Register<DonutMenu, IBrush?>(nameof(SliceBrush), new SolidColorBrush(Color.Parse("#F21E1E27")));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<DonutMenu, IBrush?>(nameof(AccentBrush), new SolidColorBrush(Color.Parse("#7C6CF0")));

    public static readonly StyledProperty<IBrush?> CenterBrushProperty =
        AvaloniaProperty.Register<DonutMenu, IBrush?>(nameof(CenterBrush), new SolidColorBrush(Color.Parse("#F2262632")));

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        AvaloniaProperty.Register<DonutMenu, IBrush?>(nameof(BorderBrush), new SolidColorBrush(Color.Parse("#1AFFFFFF")));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<DonutMenu, IBrush?>(nameof(Foreground), Brushes.White);

    public static readonly StyledProperty<IBrush?> SecondaryForegroundProperty =
        AvaloniaProperty.Register<DonutMenu, IBrush?>(nameof(SecondaryForeground), new SolidColorBrush(Color.Parse("#A0FFFFFF")));

    private Spring[] _hover = [];
    private double[] _open = [];
    private double _openClock = double.MaxValue;
    private bool _closing;
    private Action? _closeCallback;

    private int _activeIndex = -1;
    private int _pressedIndex = -1;

    private int _loop;
    private long _lastTick;
    private bool _animating;
    private TimeSpan? _lastFrame;
    private int _stalls;

    private readonly Dictionary<(int, double, double, double, double, double), StreamGeometry> _wedgeCache = [];
    private readonly Dictionary<(string, double), TextRun> _textCache = [];

    public DonutMenu()
    {
        ClipToBounds = false;
        Focusable = true;
    }

    public IReadOnlyList<DonutItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public double OuterRadius
    {
        get => GetValue(OuterRadiusProperty);
        set => SetValue(OuterRadiusProperty, value);
    }

    public double HoleRatio
    {
        get => GetValue(HoleRatioProperty);
        set => SetValue(HoleRatioProperty, value);
    }

    public double SliceOpacity
    {
        get => GetValue(SliceOpacityProperty);
        set => SetValue(SliceOpacityProperty, value);
    }

    public double SliceCornerRadius
    {
        get => GetValue(SliceCornerRadiusProperty);
        set => SetValue(SliceCornerRadiusProperty, value);
    }

    public IBrush? SliceBrush
    {
        get => GetValue(SliceBrushProperty);
        set => SetValue(SliceBrushProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public IBrush? CenterBrush
    {
        get => GetValue(CenterBrushProperty);
        set => SetValue(CenterBrushProperty, value);
    }

    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush? SecondaryForeground
    {
        get => GetValue(SecondaryForegroundProperty);
        set => SetValue(SecondaryForegroundProperty, value);
    }

    /// <summary>悬停中的切片，-1 表示没有。</summary>
    public int ActiveIndex => _activeIndex;

    /// <summary>点击或松手落在某个可用切片上。</summary>
    public event EventHandler<int>? SegmentActivated;

    /// <summary>点击了中心孔洞。</summary>
    public event EventHandler? CenterActivated;

    /// <summary>窗口需要的边长，含阴影和悬停外推的余量。</summary>
    public static double SideFor(double outerRadius) => (outerRadius + ShadowMargin + PopOut) * 2;

    private int Count => Items?.Count ?? 0;

    private double InnerRadius => OuterRadius * Math.Clamp(HoleRatio, 0.2, 0.75);

    private Point Center => new(Bounds.Width / 2, Bounds.Height / 2);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty)
        {
            SyncArrays();
            _wedgeCache.Clear();
            InvalidateVisual();
        }
        else if (change.Property == OuterRadiusProperty || change.Property == HoleRatioProperty ||
                 change.Property == SliceCornerRadiusProperty)
        {
            _wedgeCache.Clear();
            InvalidateVisual();
        }
    }

    private void SyncArrays()
    {
        var n = Count;
        if (_hover.Length != n)
            Array.Resize(ref _hover, n);
        if (_open.Length != n)
            _open = new double[n];
    }

    // ───────────── 动画 ─────────────

    public void PlayOpenAnimation()
    {
        SyncArrays();
        Array.Fill(_open, 0);
        _openClock = 0;
        _closing = false;
        _closeCallback = null;
        StartAnimation();
    }

    public void PlayCloseAnimation(Action done)
    {
        _closing = true;
        _closeCallback = done;
        _openClock = 0;
        StartAnimation();
    }

    /// <summary>
    /// 直接以完全展开的终态显示，不播动画。设置页的预览用这个：
    /// 拖动滑块时如果每次重播开场动画，画面会一直闪。
    /// </summary>
    public void SnapOpen()
    {
        SyncArrays();
        Array.Fill(_open, 1);
        _openClock = double.MaxValue;
        _closing = false;
        _closeCallback = null;
        for (var i = 0; i < _hover.Length; i++)
            _hover[i] = new Spring { Value = i == _activeIndex ? 1 : 0 };
        InvalidateVisual();
    }

    private void StartAnimation()
    {
        InvalidateVisual();
        if (_animating && Environment.TickCount64 - _lastTick < StalledLoopMs)
            return;
        _stalls = 0;
        StartLoop();
    }

    private void StartLoop()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            _animating = false;
            FinishAnimations();
            return;
        }
        _animating = true;
        _lastFrame = null;
        _lastTick = Environment.TickCount64;
        var loop = ++_loop;
        top.RequestAnimationFrame(t => OnFrame(t, loop));
        Watch(loop);
    }

    private void Watch(int loop) =>
        DispatcherTimer.RunOnce(() =>
        {
            if (loop == _loop)
                CheckLoop();
        }, TimeSpan.FromMilliseconds(StalledLoopMs + 50));

    private void CheckLoop()
    {
        if (!_animating)
            return;
        if (Environment.TickCount64 - _lastTick < StalledLoopMs)
        {
            Watch(_loop);
            return;
        }
        if (++_stalls <= FrameRetries)
        {
            StartLoop();
            return;
        }
        _animating = false;
        _loop++;
        FinishAnimations();
    }

    private void OnFrame(TimeSpan time, int loop)
    {
        if (loop != _loop)
            return;
        _lastTick = Environment.TickCount64;
        var dt = _lastFrame is { } last ? Math.Clamp((time - last).TotalSeconds, 0, 0.05) : 1 / 60.0;
        _lastFrame = time;

        var active = Advance(dt);
        InvalidateVisual();
        if (active && TopLevel.GetTopLevel(this) is { } top)
            top.RequestAnimationFrame(next => OnFrame(next, loop));
        else
            _animating = false;
    }

    /// <summary>推进一步。内部可见，便于测试确定性地步进时间。</summary>
    internal bool Advance(double dt)
    {
        var active = false;

        for (var i = 0; i < _hover.Length; i++)
        {
            var target = i == _activeIndex ? 1 : 0;
            active |= Step(ref _hover[i], target, dt, 320, i == _activeIndex ? 20 : 26);
        }

        if (_openClock < double.MaxValue)
        {
            _openClock += dt;
            var done = true;
            for (var i = 0; i < _open.Length; i++)
            {
                var t = Math.Clamp((_openClock - i * OpenStagger) / OpenDuration, 0, 1);
                _open[i] = _closing ? 1 - EaseInCubic(t) : EaseOutBack(t);
                done &= t >= 1;
            }
            if (done)
            {
                _openClock = double.MaxValue;
                if (_closing)
                    FinishClose();
            }
            else
            {
                active = true;
            }
        }

        return active;
    }

    internal void FinishAnimations()
    {
        for (var i = 0; i < _hover.Length; i++)
            _hover[i] = new Spring { Value = i == _activeIndex ? 1 : 0 };
        Array.Fill(_open, _closing ? 0 : 1);
        if (_openClock < double.MaxValue)
        {
            _openClock = double.MaxValue;
            if (_closing)
                FinishClose();
        }
        InvalidateVisual();
    }

    private void FinishClose()
    {
        _closing = false;
        var callback = _closeCallback;
        _closeCallback = null;
        if (callback is null)
            return;
        // 这一步通常发生在渲染帧回调里，而回调里直接 Hide 窗口会让原生层在绘制途中改窗口状态。
        // 推到下一个消息循环再执行。
        if (Dispatcher.UIThread.CheckAccess())
            Dispatcher.UIThread.Post(callback);
        else
            callback();
    }

    private static double EaseOutBack(double t)
    {
        const double c1 = 1.55;
        const double c3 = c1 + 1;
        var p = t - 1;
        return 1 + c3 * p * p * p + c1 * p * p;
    }

    private static double EaseInCubic(double t) => t * t * t;

    /// <summary>阻尼弹簧。单步积分限长，否则迟帧会让过冲逐帧累积而不是收敛。</summary>
    private struct Spring
    {
        public double Value;
        public double Velocity;
    }

    private const double MaxStep = 1 / 120.0;

    private static bool Step(ref Spring spring, double target, double dt, double stiffness, double damping)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(dt / MaxStep));
        var h = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            var force = stiffness * (target - spring.Value) - damping * spring.Velocity;
            spring.Velocity += force * h;
            spring.Value += spring.Velocity * h;
        }
        var settled = Math.Abs(target - spring.Value) < 0.001 && Math.Abs(spring.Velocity) < 0.01;
        if (settled)
        {
            spring.Value = target;
            spring.Velocity = 0;
        }
        return !settled;
    }

    // ───────────── 交互 ─────────────

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetActive(IndexAtPoint(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetActive(-1);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        var index = IndexAtPoint(e.GetPosition(this));
        SetActive(index);
        _pressedIndex = index;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;
        var index = IndexAtPoint(e.GetPosition(this));

        if (index == _pressedIndex && index >= 0 && index < Count && Items![index].IsEnabled)
            SegmentActivated?.Invoke(this, index);
        else if (index == -1 && _pressedIndex == -1)
            CenterActivated?.Invoke(this, EventArgs.Empty);

        _pressedIndex = -1;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            CenterActivated?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    /// <summary>指针所在的切片，中心孔洞返回 -1。</summary>
    private int IndexAtPoint(Point p)
    {
        var hit = DonutGeometry.HitTest(Center, p, InnerRadius, OuterRadius + PopOut, Count);
        return hit.Kind == DonutHitKind.Segment ? hit.Index : -1;
    }

    /// <summary>
    /// 用外部坐标（窗口内逻辑像素）设置高亮。
    /// 拖拽模式下窗口不激活、也拿不到指针事件，只能靠全局钩子把位置喂进来。
    /// </summary>
    public void SetExternalHighlight(Point point) => SetActive(IndexAtPoint(point));

    /// <summary>外部坐标落在哪个切片上，-1 表示中心或环外。</summary>
    public int IndexAtExternal(Point point) => IndexAtPoint(point);

    /// <summary>外部坐标是否在环之外（用于判断「松手时没选中任何东西」）。</summary>
    public bool IsOutside(Point point) =>
        DonutGeometry.HitTest(Center, point, InnerRadius, OuterRadius + PopOut, Count).Kind == DonutHitKind.Outside;

    private void SetActive(int index)
    {
        if (index == _activeIndex)
            return;
        _activeIndex = index;
        StartAnimation();
    }

    // ───────────── 绘制 ─────────────

    public override void Render(DrawingContext context)
    {
        var center = Center;
        if (Count == 0 && _open.Length == 0)
            return;

        var outer = OuterRadius;
        var inner = InnerRadius;
        var n = Count;
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        var regular = new Typeface(FontFamily.Default);

        DrawShadow(context, center, outer);

        for (var i = 0; i < n; i++)
        {
            var open = i < _open.Length ? _open[i] : 1;
            if (open <= 0.001)
                continue;

            var item = Items![i];
            var hover = i < _hover.Length ? _hover[i].Value : 0;
            var mid = DonutGeometry.MidAngle(i, n);
            var span = DonutGeometry.Span(n);
            var offset = DonutGeometry.PointAt(default, hover * PopOut, mid);

            // 开场：从中心放大 + 轻微扭转；悬停：整体再放大一点并外推。
            var scale = 0.55 + 0.45 * open;
            var twist = (1 - Math.Min(open, 1)) * -0.35;
            var grow = scale * (1 + hover * 0.03);
            var transform =
                Matrix.CreateTranslation(-center.X, -center.Y) *
                Matrix.CreateRotation(twist) *
                Matrix.CreateScale(grow, grow) *
                Matrix.CreateTranslation(center.X + offset.X, center.Y + offset.Y);

            using (context.PushTransform(transform))
            using (context.PushOpacity(Math.Clamp(open, 0, 1)))
            {
                var wedge = GetWedge(center, inner, outer, mid, span);
                context.DrawGeometry(WithOpacity(SliceBrush, SliceOpacity * (item.IsEnabled ? 1 : 0.45)), BorderPen, wedge);

                if (hover > 0.01 && item.IsEnabled)
                    context.DrawGeometry(WithOpacity(AccentBrush, 0.8 * Math.Clamp(hover, 0, 1)), null, wedge);

                DrawLabel(context, item, center, mid, span, typeface, regular);
            }
        }

        DrawCenter(context, center, inner, typeface, regular);
    }

    /// <summary>用径向渐变伪造柔和投影，不做模糊，任何 GPU 上都便宜。</summary>
    private void DrawShadow(DrawingContext context, Point center, double outer)
    {
        var radius = outer + ShadowMargin;
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 0),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), (outer - 16) / radius),
                new GradientStop(Color.FromArgb(52, 0, 0, 0), (outer - 2) / radius),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 1),
            },
        };
        var opening = _open.Length == 0 ? 1 : Math.Clamp(_open.Average(), 0, 1);
        using (context.PushOpacity(opening))
            context.DrawEllipse(brush, null, new Point(center.X, center.Y + 5), radius, radius);
    }

    private void DrawCenter(DrawingContext context, Point center, double inner, Typeface bold, Typeface regular)
    {
        var radius = Math.Max(4, inner - 3);
        context.DrawEllipse(CenterBrush, BorderPen, center, radius, radius);

        var active = _activeIndex >= 0 && _activeIndex < Count ? Items![_activeIndex] : null;
        var title = Text(active?.Label ?? "Orbit", bold, 13, Foreground, radius * 1.5);
        TextRun? sub = active?.Reason is { } reason
            ? Text(reason, regular, 10.5, SecondaryForeground, radius * 1.6)
            : null;

        const double gap = 2;
        var total = title.Ink.Height + (sub is null ? 0 : gap + sub.Value.Ink.Height);
        var top = center.Y - total / 2;
        DrawInkCentered(context, title, center.X, top + title.Ink.Height / 2);
        if (sub is { } small)
            DrawInkCentered(context, small, center.X, top + title.Ink.Height + gap + small.Ink.Height / 2);
    }

    private void DrawLabel(DrawingContext context, DonutItem item, Point center, double mid, double span,
        Typeface bold, Typeface regular)
    {
        var radius = (InnerRadius + OuterRadius) / 2;
        var brush = item.IsEnabled ? Foreground : SecondaryForeground;
        var label = Text(item.Label, bold, 13, brush, Math.Max(20, span * radius - 14));
        var point = DonutGeometry.PointAt(center, radius, mid);

        if (item.Glyph is not { } glyph)
        {
            DrawInkCentered(context, label, point.X, point.Y);
            return;
        }

        // 图标和文字当成一个整体按墨迹居中，间距固定，上下留白对称。
        var icon = Text(glyph, regular, 18, brush, 44);
        const double gap = 2;
        var total = icon.Ink.Height + gap + label.Ink.Height;
        var top = point.Y - total / 2;
        DrawInkCentered(context, icon, point.X, top + icon.Ink.Height / 2);
        DrawInkCentered(context, label, point.X, top + icon.Ink.Height + gap + label.Ink.Height / 2);
    }

    /// <summary>
    /// 一段文字 + 它的墨迹外框。居中一律按墨迹算，不能按 <see cref="FormattedText.Width"/>：
    /// 那个是排版盒宽度，而 DrawText 又会按 TextAlignment 在 MaxTextWidth 里再居中一次，
    /// 直接用 Width 居中等于居中两遍，会稳定偏右 (MaxTextWidth - 墨迹宽) / 2。
    /// </summary>
    private readonly record struct TextRun(FormattedText Formatted, Rect Ink);

    /// <summary>把文字的墨迹中心放在 (centerX, centerY)。</summary>
    private static void DrawInkCentered(DrawingContext context, TextRun run, double centerX, double centerY)
    {
        var ink = run.Ink;
        context.DrawText(run.Formatted,
            new Point(centerX - (ink.X + ink.Width / 2), centerY - (ink.Y + ink.Height / 2)));
    }

    private TextRun Text(string content, Typeface typeface, double size, IBrush? brush, double maxWidth)
    {
        var color = brush is ISolidColorBrush solid ? solid.Color : default;
        var key = ($"{typeface.FontFamily.Name}|{size}|{maxWidth:0.#}|{color}|{content}", size);
        if (_textCache.TryGetValue(key, out var cached))
            return cached;

        var text = new FormattedText(content, System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, typeface, size, brush)
        {
            MaxTextWidth = maxWidth,
            MaxTextHeight = size * 3,
            TextAlignment = TextAlignment.Center,
        };

        // 转成轮廓再取包围盒，得到的才是字形真正落笔的范围（含 TextAlignment 造成的偏移）。
        var run = new TextRun(text, text.BuildGeometry(new Point(0, 0))?.Bounds ?? default);

        if (_textCache.Count > 512)
            _textCache.Clear();
        _textCache[key] = run;
        return run;
    }

    private StreamGeometry GetWedge(Point center, double inner, double outer, double mid, double span)
    {
        var key = (Count, inner, outer, mid, span, SliceCornerRadius);
        if (_wedgeCache.TryGetValue(key, out var cached))
            return cached;

        var path = DonutGeometry.Wedge(center, inner, outer, mid, span, Gap, SliceCornerRadius);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(path.Start, isFilled: true);
            foreach (var segment in path.Segments)
            {
                if (segment.Kind == SegmentKind.Line)
                    ctx.LineTo(segment.To);
                else
                    ctx.ArcTo(segment.To, new Size(segment.Radius, segment.Radius), 0, segment.LargeArc,
                        segment.Clockwise ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
            }
            ctx.EndFigure(isClosed: true);
        }

        if (_wedgeCache.Count > 256)
            _wedgeCache.Clear();
        _wedgeCache[key] = geometry;
        return geometry;
    }

    private IPen? BorderPen => BorderBrush is { } brush ? new Pen(brush, 1) : null;

    private static IBrush? WithOpacity(IBrush? brush, double opacity) =>
        brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, Math.Clamp(opacity, 0, 1)) : brush;
}
